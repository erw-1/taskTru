using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using static taskTru.NativeMethods;

namespace taskTru;

public sealed partial class MainForm : Form, IMessageFilter
{
    private const int MaximumTrayWindows = 8;
    private const int MaximumTrayTitleLength = 48;
    private const int WindowMessageMouseMove = 0x0200;
    private const int WindowMessageLeftButtonDown = 0x0201;
    private const int WindowMessageLeftButtonUp = 0x0202;
    private const int WindowMessageMouseWheel = 0x020A;
    private const int DragScrollThreshold = 5;
    private const int HeaderHeight = 32;
    private const int RowHeight = 40;
    private const int MaximumInitialRows = 5;
    private const int MainWindowMinimumWidth = 680;
    private const int MainWindowMinimumHeight = 112;
    private const int WindowHorizontalInset = 10;
    private const int WindowStateColumnWidth = 90;
    private const int WindowOpacityColumnWidth = 132;
    private const int WindowActionsColumnWidth = 136;
    private const int WindowCropColumnWidth = 76;
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "taskTru";
    private const int VideoScanBatchSize = 5;
    private static readonly string[] PassiveVideoProcesses =
    [
        "brave", "chrome", "firefox", "msedge", "opera", "vivaldi",
        "vlc", "mpv", "mpc-hc", "mpc-be", "potplayer", "wmplayer"
    ];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "taskTru",
        "settings.json");
    private readonly Icon _applicationIcon = LoadApplicationIcon();
    private readonly Icon _trayBaseIcon;
    private readonly Icon _activeTrayIcon;
    private readonly Dictionary<nint, CropSession> _crops = [];
    private readonly Dictionary<nint, WindowRow> _rows = [];
    private readonly Dictionary<nint, CachedVideoBounds> _videoBounds = [];
    private readonly Panel _content;
    private readonly Panel _windowList;
    private readonly RoundedViewport _windowViewport;
    private readonly DarkScrollBar _windowScroll;
    private readonly Label _emptyListLabel;
    private readonly Label _ignoredCount;
    private readonly RoundedActionButton _revealIgnored;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _windowsTrayItem;
    private readonly ToolTip _toolTip = UiToolTips.Create(6000);
    private readonly WinEventProc _foregroundChanged;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _stateSaveTimer;
    private readonly System.Windows.Forms.Timer? _foregroundTimer;
    private readonly CancellationTokenSource _videoDetectionCancellation = new();
    private readonly RegisteredWaitHandle? _activationRegistration;
    private readonly WindowStateStore _stateStore = new();
    private readonly bool _startMinimized;

    private TableLayoutPanel? _header;
    private ResetActionButton? _headerRefresh;
    private nint _foregroundHook;
    private AppSettings _settings = ReadSettings();
    private bool _disposed;
    private bool _allowExit;
    private bool _selectionOverlayActive;
    private bool _initialPopulationComplete;
    private bool _initialSizeApplied;
    private bool _startupVisibilityApplied;
    private bool _revealIgnoredTasks;
    private bool _refreshing;
    private bool _videoScanRunning;
    private bool _startupVideoScanQueued;
    private int _videoScanCursor;
    private long _lastFreshVideoScanTick;
    private bool _videoRetryArmed;
    private readonly Dictionary<nint, long> _singleVideoScanTicks = [];
    private readonly System.Windows.Forms.Timer _uiaCleanupTimer;
    private readonly System.Windows.Forms.Timer _videoRetryTimer;
    private bool? _appliedLightMode;
    private int? _appliedActiveWindowCount;
    private int _layoutDpi = UiScale.DefaultDpi;
    private bool _dragScrollCandidate;
    private bool _dragScrolling;
    private Point _dragScrollStart;
    private int _dragScrollStartValue;

    public MainForm(
        bool startMinimized = false,
        EventWaitHandle? activationEvent = null)
    {
        _startMinimized = startMinimized;
        _trayBaseIcon = new(_applicationIcon, SystemInformation.SmallIconSize);
        _activeTrayIcon = CreateActiveTrayIcon(_trayBaseIcon);
        ConfigureForm();

        (_windowViewport, _windowList, _windowScroll) = CreateWindowList();
        _emptyListLabel = new()
        {
            Text = "No task windows to show.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.DisabledText,
            BackColor = UiTheme.AppBackground,
            Margin = Padding.Empty,
            Visible = false
        };
        _windowViewport.Resize += (_, _) => UpdateWindowListLayout();
        _windowScroll.ValueChanged += (_, _) => UpdateWindowListLayout();
        (_ignoredCount, _revealIgnored) = CreateIgnoredControls();
        _content = CreateContent(CreateWindowListContainer());
        Controls.Add(_content);

        Icon = _applicationIcon;
        (_trayIcon, _windowsTrayItem) = CreateTrayIcon();
        _refreshTimer = new()
        {
            Interval = RefreshIntervalMilliseconds,
            Enabled = !_settings.ManualTaskRefresh
        };
        _refreshTimer.Tick += (_, _) => ReconcileWindows();
        _stateSaveTimer = new()
        {
            Interval = 400
        };
        _stateSaveTimer.Tick += (_, _) =>
        {
            _stateSaveTimer.Stop();
            _stateStore.Flush();
        };
        // UI Automation scans allocate COM wrappers en masse; one debounced collect
        // after scans settle keeps multi-day RAM flat without per-scan GC storms.
        _uiaCleanupTimer = new()
        {
            Interval = 3000
        };
        _uiaCleanupTimer.Tick += (_, _) =>
        {
            _uiaCleanupTimer.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        };
        // Browsers build their accessibility tree lazily: the first UI Automation
        // query can come back empty while later ones succeed. One delayed retry
        // per scan wave catches those cold misses without ever looping.
        _videoRetryTimer = new()
        {
            Interval = 8000
        };
        _videoRetryTimer.Tick += (_, _) =>
        {
            _videoRetryTimer.Stop();
            if (_settings.ScanForVideoContent)
            {
                QueueVideoScan(
                    [.. _rows.Values.Select(row => row.Window)],
                    force: true);
            }
        };
        _activationRegistration =
            activationEvent is null
                ? null
                : ThreadPool.RegisterWaitForSingleObject(
                    activationEvent,
                    OnActivationSignaled,
                    state: null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);

        ReconcileWindows();
        Application.AddMessageFilter(this);
        ApplyTrayState(force: true);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _foregroundChanged = OnForegroundWindowChanged;
        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            _foregroundChanged,
            0,
            0,
            WinEventOutOfContext);
        if (_foregroundHook == 0)
        {
            _foregroundTimer = new()
            {
                Interval = 1000,
                Enabled = true
            };
            _foregroundTimer.Tick += (_, _) =>
                CaptureForegroundWindow(GetForegroundWindow());
        }
        DpiChanged += OnMainFormDpiChanged;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startupVisibilityApplied)
            return;

        _startupVisibilityApplied = true;
        if (_startMinimized)
            BeginInvoke((Action)HideToTray);

        BeginInvoke((Action)QueueStartupVideoScan);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.Apply(Handle);
        RegisterGlobalShortcuts();
        ScaleLayoutForDpi(DeviceDpi);
        ApplyInitialSize(_rows.Count);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterGlobalShortcuts();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowMessageHotKey
            && _settings.EnableKeybinds)
        {
            var action =
                (ShortcutAction)message.WParam.ToInt32();
            BeginInvoke((Action)(() =>
                HandleGlobalShortcut(action)));
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized)
            BeginInvoke((Action)HideToTray);
    }

    protected override void OnFormClosing(
        FormClosingEventArgs eventArgs)
    {
        if (!_allowExit
            && eventArgs.CloseReason
                == CloseReason.UserClosing
            && _settings.CloseButtonMinimizesToTray)
        {
            eventArgs.Cancel = true;
            HideToTray();
            return;
        }

        if (eventArgs.CloseReason == CloseReason.UserClosing
            && !ConfirmExitWithActiveTasks())
        {
            eventArgs.Cancel = true;
            return;
        }

        _refreshTimer.Stop();
        _stateSaveTimer.Stop();
        _foregroundTimer?.Stop();
        _videoDetectionCancellation.Cancel();
        if (_settings.RestoreTasksOnExit)
            RestoreManagedWindows();
        base.OnFormClosing(eventArgs);
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (HandleDragScrollMessage(ref message))
            return true;

        if (message.Msg != WindowMessageMouseWheel
            || !_windowViewport.IsHandleCreated
            || !_windowViewport.RectangleToScreen(
                    _windowViewport.ClientRectangle)
                .Contains(MousePosition))
        {
            return false;
        }

        int delta = unchecked(
            (short)(((long)message.WParam >> 16) & 0xFFFF));
        if (delta == 0)
            return false;

        _windowScroll.Value -= Math.Sign(delta) * GetTypicalRowHeight() * 3;
        return true;
    }

    private bool HandleDragScrollMessage(ref Message message)
    {
        switch (message.Msg)
        {
            case WindowMessageLeftButtonDown:
                _dragScrollCandidate =
                    _windowScroll.Maximum > 0
                    && IsPointerInWindowViewport()
                    && !IsDragScrollInteractiveTarget(
                        Control.FromHandle(message.HWnd));
                _dragScrolling = false;
                if (_dragScrollCandidate)
                {
                    _dragScrollStart = MousePosition;
                    _dragScrollStartValue = _windowScroll.Value;
                }

                return false;
            case WindowMessageMouseMove:
                if (!_dragScrollCandidate
                    || (Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    return false;
                }

                int delta = MousePosition.Y - _dragScrollStart.Y;
                if (!_dragScrolling
                    && Math.Abs(delta) < UiScale.ToDevice(DragScrollThreshold, _layoutDpi))
                {
                    return false;
                }

                if (!_dragScrolling)
                {
                    _dragScrolling = true;
                    _windowViewport.Capture = true;
                }

                _windowScroll.Value = _dragScrollStartValue - delta;
                return true;
            case WindowMessageLeftButtonUp:
                bool handled = _dragScrolling;
                _dragScrollCandidate = false;
                _dragScrolling = false;
                if (handled)
                {
                    _windowViewport.Capture = false;
                    Activate();
                }

                return handled;
            default:
                return false;
        }
    }

    private bool IsPointerInWindowViewport() =>
        _windowViewport.IsHandleCreated
        && _windowViewport.RectangleToScreen(
                _windowViewport.ClientRectangle)
            .Contains(MousePosition);

    private static bool IsDragScrollInteractiveTarget(Control? control)
    {
        while (control is not null)
        {
            if (control is CheckBox
                or RoundedSlider
                or RoundedActionButton
                or IconActionButton
                or DarkScrollBar)
            {
                return true;
            }

            control = control.Parent;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Application.RemoveMessageFilter(this);
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            DpiChanged -= OnMainFormDpiChanged;
            if (_foregroundHook != 0)
                _ = UnhookWinEvent(_foregroundHook);

            StopAllCrops();
            _activationRegistration?.Unregister(null);
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _stateSaveTimer.Stop();
            _stateStore.Flush();
            _stateSaveTimer.Dispose();
            _videoDetectionCancellation.Cancel();
            _videoDetectionCancellation.Dispose();
            _uiaCleanupTimer.Stop();
            _uiaCleanupTimer.Dispose();
            _videoRetryTimer.Stop();
            _videoRetryTimer.Dispose();
            _foregroundTimer?.Dispose();
            foreach (nint handle in _rows.Keys.ToArray())
                RemoveRow(handle);
            _refreshTimer.Dispose();
            _toolTip.Dispose();
            Icon = null;
            _activeTrayIcon.Dispose();
            _trayBaseIcon.Dispose();
            _applicationIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadApplicationIcon()
    {
        using Icon? icon =
            Icon.ExtractAssociatedIcon(
                Application.ExecutablePath);
        return icon is null
            ? (Icon)SystemIcons.Application.Clone()
            : (Icon)icon.Clone();
    }

    private static Icon CreateActiveTrayIcon(Icon baseIcon)
    {
        // A tiny badge covers active/inactive state without a second icon asset.
        using Bitmap bitmap = baseIcon.ToBitmap();
        using Graphics graphics = Graphics.FromImage(bitmap);
        int size = Math.Max(8, bitmap.Width / 3);
        using var fill = new SolidBrush(UiTheme.Accent);
        using var border = new Pen(Color.White, Math.Max(1, bitmap.Width / 32));
        var bounds = new Rectangle(
            1,
            1,
            size,
            size);
        graphics.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(fill, bounds);
        graphics.DrawEllipse(border, bounds);

        nint handle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    private void ConfigureForm()
    {
        Text = "taskTru";
        BackColor = UiTheme.AppBackground;
        ForeColor = Color.White;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new(780, HeaderHeight + RowHeight);
        MinimumSize = new(MainWindowMinimumWidth, MainWindowMinimumHeight);
    }

    private Panel CreateContent(Control listArea)
    {
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = new(
                UiTheme.ScrollBarWidth,
                0,
                0,
                UiTheme.ScrollBarWidth)
        };
        var headerHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = HeaderHeight,
            Padding = new(
                WindowHorizontalInset,
                0,
                WindowHorizontalInset,
                0),
            BackColor = Color.Transparent
        };
        headerHost.Controls.Add(CreateHeader());
        listArea.Dock = DockStyle.Fill;
        content.Controls.Add(listArea);
        content.Controls.Add(headerHost);
        return content;
    }

    private (RoundedViewport Viewport, Panel List, DarkScrollBar Scroll) CreateWindowList()
    {
        var viewport = new RoundedViewport
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Margin = Padding.Empty
        };
        var list = new Panel
        {
            BackColor = UiTheme.AppBackground,
            Location = Point.Empty,
            Margin = Padding.Empty
        };
        var scroll = new DarkScrollBar
        {
            Dock = DockStyle.Right,
            Width = UiTheme.ScrollBarWidth,
            Margin = Padding.Empty
        };
        viewport.Content = list;
        return (viewport, list, scroll);
    }

    private Panel CreateWindowListContainer()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Margin = Padding.Empty
        };
        container.Controls.Add(_emptyListLabel);
        container.Controls.Add(_windowViewport);
        container.Controls.Add(_windowScroll);
        return container;
    }

    private TableLayoutPanel CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            ColumnCount = 6,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };

        _header = header;
        ConfigureWindowColumns(
            header,
            _layoutDpi,
            _settings.ScanForVideoContent);
        header.RowStyles.Add(new(
            SizeType.Percent,
            100));

        var ignoredStatus = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        ignoredStatus.Controls.Add(_ignoredCount);
        ignoredStatus.Controls.Add(_revealIgnored);
        header.Controls.Add(ignoredStatus, 0, 0);

        header.Controls.Add(CreateHeaderLabel("Click-through", ContentAlignment.MiddleCenter), 1, 0);
        header.Controls.Add(CreateHeaderLabel("Lock on top", ContentAlignment.MiddleCenter), 2, 0);
        header.Controls.Add(CreateHeaderLabel("Opacity", ContentAlignment.MiddleCenter), 3, 0);

        var actionHeader = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        actionHeader.Controls.Add(CreateSettingsButton());
        actionHeader.Controls.Add(CreateHeaderRefreshButton());
        header.Controls.Add(actionHeader, 4, 0);
        return header;
    }

    private static void ConfigureWindowColumns(
        TableLayoutPanel layout,
        int dpi,
        bool scanForVideoContent)
    {
        layout.ColumnStyles.Add(new(
            SizeType.Percent,
            100));
        foreach (int width in new[]
                 {
                     WindowStateColumnWidth,
                     WindowStateColumnWidth,
                     WindowOpacityColumnWidth,
                     WindowRow.ActionColumnWidth(scanForVideoContent),
                     UiTheme.ScrollBarWidth
                 })
        {
            layout.ColumnStyles.Add(new(
                SizeType.Absolute,
                UiScale.ToDevice(width, dpi)));
        }
    }

    private void UpdateVideoActionLayout()
    {
        if (_header is not null)
        {
            _header.ColumnStyles[4].Width = UiScale.ToDevice(
                WindowRow.ActionColumnWidth(_settings.ScanForVideoContent),
                _layoutDpi);
        }

        foreach (WindowRow row in _rows.Values)
            row.SetVideoFeatureEnabled(_settings.ScanForVideoContent);
    }

    private void OnActivationSignaled(
        object? state,
        bool timedOut)
    {
        if (timedOut
            || _disposed
            || IsDisposed
            || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)RestoreFromTray);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static Label CreateHeaderLabel(
        string text,
        ContentAlignment alignment) =>
        new()
        {
            Text = text,
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = alignment,
            Margin = Padding.Empty,
            Font = new Font(
                "Segoe UI",
                8.25f,
                FontStyle.Bold)
        };

    private Control CreateSettingsButton()
    {
        var button = new GearActionButton
        {
            Dock = DockStyle.Right,
            Width = 28,
            Margin = new(4, 4, 4, 4)
        };
        button.Click += (_, _) => OpenSettings();
        return button;
    }

    private Control CreateHeaderRefreshButton()
    {
        _headerRefresh = new ResetActionButton
        {
            Dock = DockStyle.Right,
            Width = 28,
            Margin = new(4, 4, 4, 4),
            Visible = _settings.ManualTaskRefresh,
            AccessibleName = "Refresh task list"
        };
        _toolTip.SetToolTip(
            _headerRefresh,
            "Refresh the task list now.");
        _headerRefresh.Click += (_, _) =>
        {
            ReconcileWindows();
            RequestFreshVideoScan();
        };
        return _headerRefresh;
    }

    private (Label Count, RoundedActionButton Reveal) CreateIgnoredControls()
    {
        var count = new Label
        {
            Visible = false,
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            Margin = new(2, 9, 4, 0),
            Font = new("Segoe UI", 8.25f)
        };
        var reveal = new RoundedActionButton
        {
            Visible = false,
            Text = "Reveal",
            Size = new(48, 22),
            Margin = new(0, 5, 0, 5),
            ForeColor = UiTheme.SecondaryText,
            Subtle = true,
            AccessibleName = "Reveal ignored tasks"
        };
        reveal.Click += (_, _) =>
        {
            _revealIgnoredTasks = !_revealIgnoredTasks;
            ReconcileWindows();
        };

        return (count, reveal);
    }

    private void OnForegroundWindowChanged(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || window == 0 || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() =>
                    CaptureForegroundWindow(window)));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        CaptureForegroundWindow(window);
    }

    private void CaptureForegroundWindow(nint handle)
    {
        if (handle == 0 || handle == Handle)
            return;

        if (TryGetSourceHandle(handle, out nint sourceHandle))
            handle = sourceHandle;

        if (_rows.ContainsKey(handle))
            return;

        if (WindowManager.TryGetWindowInfo(
                handle,
                out WindowInfo? window))
        {
            RepairParkedSiblingWindow(
                window,
                SystemInformation.VirtualScreen);
        }
    }

    private bool TryGetSourceHandle(nint cropHandle, out nint sourceHandle)
    {
        foreach ((nint handle, CropSession session) in _crops)
        {
            if (session.Crop.OwnsOverlayWindow(cropHandle))
            {
                sourceHandle = handle;
                return true;
            }
        }

        sourceHandle = 0;
        return false;
    }

    private void ApplyTrayWindowState(
        WindowInfo window,
        WindowState state,
        bool restoreFocusWhenClickThroughEnds = true)
    {
        CropSession? focusSession = null;
        if (restoreFocusWhenClickThroughEnds
            && _crops.TryGetValue(
                window.Handle,
                out CropSession? session)
            && session.State.ClickThrough
            && !state.ClickThrough)
        {
            focusSession = session;
        }

        ApplyWindowState(window.Handle, state);
        SaveWindowState(window, GetStoredWindowState(window, state));
        if (_rows.TryGetValue(window.Handle, out WindowRow? row))
        {
            row.SetState(state);
            row.SetTaskActive(IsWindowTaskActive(window.Handle));
        }

        focusSession?.Crop.RestoreShortcutFocus();
    }

    private bool IsTrayWindowManaged(WindowInfo window) =>
        _stateStore.Get(window.StateKey) is { IsDefault: false }
        || WindowManager.IsTracked(window.Handle);

    private WindowState GetWindowState(WindowInfo window) =>
        _rows.TryGetValue(window.Handle, out WindowRow? row)
            ? row.State
            : _stateStore.Get(window.StateKey)
            ?? WindowManager.ReadState(window.Handle);

    private WindowState GetActiveWindowState(WindowInfo window) =>
        _crops.TryGetValue(window.Handle, out CropSession? session)
            ? session.State
            : GetWindowState(window);

    private WindowState GetStoredWindowState(
        WindowInfo window,
        WindowState state) =>
        _crops.TryGetValue(window.Handle, out CropSession? session)
            ? GetStoredCropState(session, state)
            : state;

    private static WindowState GetStoredCropState(
        CropSession session,
        WindowState state)
    {
        WindowState stored =
            state with { ClickThrough = session.InitialState.ClickThrough };
        if (session.AutoLockedTopMost)
            stored = stored with { TopMost = session.InitialState.TopMost };

        return CleanUntrackedCropState(session, stored);
    }

    private static WindowState CleanUntrackedCropState(
        CropSession session,
        WindowState state) =>
        session.SourceWasTracked
            ? state
            : state with
            {
                ClickThrough = false,
                TopMost = session.InitialState.TopMost
                    ? false
                    : state.TopMost
            };

    private WaitCursorScope ShowWaitCursor() =>
        new(this);

    private void OpenSettings()
    {
        SettingsForm dialog;
        using (ShowWaitCursor())
        {
            RestoreFromTray();
            dialog = new SettingsForm(_settings);
        }

        using (dialog)
        {
            UnregisterGlobalShortcuts();
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                RegisterGlobalShortcuts();
                return;
            }

            _settings = dialog.Settings.Normalize();
            _refreshTimer.Interval = RefreshIntervalMilliseconds;
            _refreshTimer.Enabled = !_settings.ManualTaskRefresh;
            if (_headerRefresh is not null)
                _headerRefresh.Visible = _settings.ManualTaskRefresh;
            foreach (WindowRow row in _rows.Values)
            {
                row.SetShowOpacityPercentage(
                    _settings.ShowOpacityPercentage);
                row.SetCompactRows(_settings.CompactTaskRows);
                row.SetFavoriteVisible(_settings.EnableFavoriteTasks);
                row.SetIgnoredVisible(_settings.EnableIgnoredTasks);
            }
            foreach (CropSession session in _crops.Values)
                session.Crop.SetShowOpacityPercentage(
                    _settings.ShowOpacityPercentage);
            UpdateVideoActionLayout();
            UpdateWindowListLayout();

            if (!_settings.EnableIgnoredTasks)
                _revealIgnoredTasks = false;

            WriteSettings(_settings);
            ApplyStartupSetting(_settings);
            RegisterGlobalShortcuts(showErrors: true);
            ReconcileWindows();
        }
    }

    private void ReconcileWindows()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        int visibleWindowCount = 0;
        _windowList.SuspendLayout();
        try
        {
            List<WindowInfo> allWindows =
                IncludeCroppedWindows(
                    WindowManager.Enumerate(
                        Handle));
            RepairParkedSiblingWindows(allWindows);
            List<WindowInfo> windows =
                FilterAndSortWindows(allWindows);
            visibleWindowCount = windows.Count;
            UpdateIgnoredStatus(allWindows);
            RemoveMissingRows(windows);

            for (int index = 0; index < windows.Count; index++)
            {
                WindowInfo window = windows[index];
                if (_rows.TryGetValue(
                        window.Handle,
                        out WindowRow? existingRow))
                {
                    if (existingRow.UpdateWindow(window, index)
                        && _initialPopulationComplete)
                    {
                        if (_settings.EnableUpdateFlash)
                            existingRow.FlashTitleAccent();

                        // Title changes invalidate cached video bounds (navigation,
                        // new media); rescan so the play button comes right back.
                        QueueSingleVideoScan(window.Handle);
                    }

                    existingRow.SetFavoriteActive(
                        _settings.IsFavorite(
                            window.ProcessName));
                    existingRow.SetFavoriteVisible(
                        _settings.EnableFavoriteTasks);
                    existingRow.SetIgnoredActive(
                        _settings.IsIgnored(
                            window.ProcessName));
                    existingRow.SetIgnoredVisible(
                        _settings.EnableIgnoredTasks);
                    existingRow.SetVideoFeatureEnabled(
                        _settings.ScanForVideoContent);
                    existingRow.SetCropActive(
                        IsCropped(window.Handle));
                    existingRow.SetVideoDetected(
                        _settings.ScanForVideoContent
                        && TryGetCachedVideoBounds(window, out _));
                    continue;
                }

                AddRow(window, index);
            }

            ApplyRowOrder(windows);
            ApplyTrayState();
            QueueVideoScan(windows);
        }
        finally
        {
            _windowList.ResumeLayout(performLayout: true);
            UpdateWindowListLayout();
            _refreshing = false;
            _initialPopulationComplete = true;
            ApplyInitialSize(visibleWindowCount);
            _emptyListLabel.Visible = visibleWindowCount == 0;
            if (_emptyListLabel.Visible)
                _emptyListLabel.BringToFront();
        }
    }

    private void AddRow(WindowInfo window, int index)
    {
        WindowState? savedState =
            _stateStore.Get(window.StateKey);
        WindowState state =
            savedState
            ?? WindowManager.ReadState(window.Handle);
        if (savedState is not null)
            WindowManager.ApplyStoredState(
                window.Handle,
                savedState);

        bool isCropped =
            IsCropped(window.Handle);
        var row = new WindowRow(
            window,
            state,
            index,
            isCropped,
            _settings.ScanForVideoContent
            && TryGetCachedVideoBounds(window, out _),
            IsWindowTaskActive(window.Handle),
            _settings.IsFavorite(window.ProcessName),
            _settings.IsIgnored(window.ProcessName),
            _settings.EnableFavoriteTasks,
            _settings.EnableIgnoredTasks,
            _settings.ShowOpacityPercentage,
            _settings.ScanForVideoContent,
            _settings.CompactTaskRows,
            _layoutDpi,
            OnRowStateChanged,
            OnCropRequested,
            OnVideoRequested,
            OnResetRequested,
            OnFavoriteRequested,
            OnIgnoreRequested,
            OnFocusRequested);
        _rows.Add(window.Handle, row);
        _windowList.Controls.Add(row);
        if (_initialPopulationComplete)
        {
            if (_settings.EnableUpdateFlash)
                row.FlashAccent();

            QueueSingleVideoScan(window.Handle);
        }
    }

    private void ApplyInitialSize(int windowCount)
    {
        if (_initialSizeApplied
            || !IsHandleCreated
            || !_initialPopulationComplete)
            return;

        _initialSizeApplied = true;
        int rows = Math.Clamp(
            windowCount,
            1,
            MaximumInitialRows);
        int rowHeight = GetTypicalRowHeight();
        int visibleContentHeight = _rows.Count == 0
            ? rowHeight
            : _rows.Values
                .Take(rows)
                .Sum(row => row.Height);
        int height = UiScale.ToDevice(HeaderHeight, DeviceDpi)
            + visibleContentHeight
            + UiScale.ToDevice(UiTheme.ScrollBarWidth, DeviceDpi);
        Rectangle workingArea =
            Screen.FromControl(this).WorkingArea;
        ClientSize = new(
            Math.Max(
                UiScale.ToDevice(MainWindowMinimumWidth, DeviceDpi),
                Math.Min(UiScale.ToDevice(780, DeviceDpi), workingArea.Width)),
            Math.Max(
                UiScale.ToDevice(MainWindowMinimumHeight, DeviceDpi),
                Math.Min(height, workingArea.Height - 80)));
        UpdateWindowListLayout();
    }

    private void OnMainFormDpiChanged(
        object? sender,
        DpiChangedEventArgs e)
    {
        ScaleLayoutForDpi(e.DeviceDpiNew);
        BeginInvoke((Action)UpdateWindowListLayout);
    }

    private void ScaleLayoutForDpi(int dpi)
    {
        dpi = Math.Max(UiScale.DefaultDpi, dpi);
        if (dpi == _layoutDpi)
            return;

        float scale = dpi / (float)_layoutDpi;
        SuspendLayout();
        _content.Scale(new SizeF(scale, scale));
        _layoutDpi = dpi;
        MinimumSize = new(
            UiScale.ToDevice(MainWindowMinimumWidth, dpi),
            UiScale.ToDevice(MainWindowMinimumHeight, dpi));
        UpdateVideoActionLayout();
        ResumeLayout(performLayout: true);
    }

    private int GetTypicalRowHeight() =>
        _rows.Values.FirstOrDefault()?.Height
        ?? UiScale.ToDevice(
            WindowRow.LogicalRowHeight(_settings.CompactTaskRows),
            _layoutDpi);

    private void UpdateWindowListLayout()
    {
        if (_windowViewport.IsDisposed)
            return;

        int viewportHeight = Math.Max(0, _windowViewport.ClientSize.Height);
        int contentHeight = _rows.Values.Sum(row => row.Height);
        int largeChange = Math.Max(1, viewportHeight);
        int maximum = Math.Max(0, contentHeight - viewportHeight);
        Rectangle oldBounds = _windowList.Bounds;
        int oldLargeChange = _windowScroll.LargeChange;
        int oldMaximum = _windowScroll.Maximum;
        _windowScroll.LargeChange = largeChange;
        _windowScroll.Maximum = maximum;

        var bounds = new Rectangle(
            0,
            -_windowScroll.Value,
            Math.Max(0, _windowViewport.ClientSize.Width),
            contentHeight);
        if (_windowList.Bounds != bounds)
            _windowList.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);

        if (oldBounds != _windowList.Bounds
            || oldLargeChange != _windowScroll.LargeChange
            || oldMaximum != _windowScroll.Maximum)
        {
            _windowViewport.UpdateBoundary();
        }
    }

    private void RemoveMissingRows(IReadOnlyCollection<WindowInfo> windows)
    {
        var currentHandles = windows
            .Select(window => window.Handle)
            .ToHashSet();
        foreach (nint staleHandle in _rows.Keys
                     .Where(handle =>
                         !currentHandles.Contains(handle))
                     .ToArray())
        {
            RemoveRow(staleHandle);
        }
    }

    private void ApplyRowOrder(List<WindowInfo> windows)
    {
        for (int index = windows.Count - 1, controlIndex = 0;
             index >= 0;
             index--, controlIndex++)
        {
            WindowRow row = _rows[windows[index].Handle];
            if (_windowList.Controls.GetChildIndex(row) != controlIndex)
                _windowList.Controls.SetChildIndex(row, controlIndex);
        }
    }

    private void RemoveRow(nint handle)
    {
        if (!_rows.Remove(handle, out WindowRow? row))
            return;

        _videoBounds.Remove(handle);
        _singleVideoScanTicks.Remove(handle);
        _windowList.Controls.Remove(row);
        row.Dispose();
    }

    private void OnRowStateChanged(WindowRow row)
    {
        WindowState state = row.State;
        ApplyWindowState(
            row.Window.Handle,
            state);
        SaveWindowState(row.Window, GetStoredWindowState(row.Window, state));
        row.SetTaskActive(
            IsWindowTaskActive(
                row.Window.Handle));
    }

    private void OnCropRequested(WindowRow row)
    {
        if (IsCropped(row.Window.Handle))
        {
            StopCrop(row.Window.Handle);
            return;
        }

        _ = StartCrop(
            row.Window,
            row.State);
    }

    private void OnVideoRequested(WindowRow row)
    {
        if (_settings.ScanForVideoContent)
            StartVideoCrop(row.Window, row.State);
    }

    private void OnResetRequested(WindowRow row) =>
        ResetWindow(row.Window);

    private void OnFavoriteRequested(WindowRow row)
    {
        if (!_settings.EnableFavoriteTasks)
            return;

        _settings = _settings
            .SetFavorite(
                row.Window.ProcessName,
                !_settings.IsFavorite(row.Window.ProcessName))
            .Normalize();
        WriteSettings(_settings);
        ReconcileWindows();
    }

    private void OnIgnoreRequested(WindowRow row)
    {
        if (!_settings.EnableIgnoredTasks)
            return;

        _settings = _settings
            .SetIgnored(
                row.Window.ProcessName,
                !_settings.IsIgnored(row.Window.ProcessName))
            .Normalize();
        WriteSettings(_settings);
        ReconcileWindows();
    }

    private void OnFocusRequested(WindowRow row)
    {
        row.FlashTitleAccent();
        if (_crops.TryGetValue(
                row.Window.Handle,
                out CropSession? session))
        {
            session.Crop.RestoreShortcutFocus();
            return;
        }

        if (!IsWindow(row.Window.Handle))
            return;

        if (IsIconic(row.Window.Handle))
            ShowWindow(row.Window.Handle, ShowWindowCommand.Restore);

        BringWindowToTop(row.Window.Handle);
        SetForegroundWindow(row.Window.Handle);
    }

    private bool IsCropped(nint sourceHandle) =>
        _crops.ContainsKey(sourceHandle);

    private List<WindowInfo> IncludeCroppedWindows(List<WindowInfo> windows)
    {
        foreach ((nint handle, CropSession session) in _crops)
        {
            if (IsWindow(handle)
                && !windows.Any(window => window.Handle == handle))
            {
                windows.Add(session.Window);
            }
        }

        return windows;
    }

    private List<WindowInfo> FilterAndSortWindows(List<WindowInfo> windows)
    {
        if (!_settings.EnableFavoriteTasks
            && !_settings.EnableIgnoredTasks)
        {
            return windows;
        }

        return [.. windows
            .Select((window, index) => new
            {
                Window = window,
                Index = index,
                Favorite = _settings.IsFavorite(window.ProcessName),
                Ignored = _settings.IsIgnored(window.ProcessName)
            })
            .Where(item => _revealIgnoredTasks || !item.Ignored)
            .OrderByDescending(item => item.Favorite)
            .ThenBy(item => item.Index)
            .Select(item => item.Window)];
    }

    private void UpdateIgnoredStatus(List<WindowInfo> windows)
    {
        if (!_settings.EnableIgnoredTasks)
        {
            _ignoredCount.Visible = false;
            _revealIgnored.Visible = false;
            _revealIgnoredTasks = false;
            return;
        }

        int ignored = windows.Count(window =>
            _settings.IsIgnored(window.ProcessName));
        _ignoredCount.Visible = ignored > 0;
        _revealIgnored.Visible = ignored > 0;
        if (ignored == 0)
        {
            _revealIgnoredTasks = false;
            return;
        }

        _ignoredCount.Text = $"{ignored} "
            + (ignored == 1
                ? "task ignored"
                : "tasks ignored");
        _revealIgnored.Text =
            _revealIgnoredTasks
                ? "Hide"
                : "Reveal";
        _revealIgnored.AccessibleName =
            _revealIgnoredTasks
                ? "Hide ignored tasks"
                : "Reveal ignored tasks";
        _toolTip.SetToolTip(
            _revealIgnored,
            _revealIgnoredTasks
                ? "Hide tasks from ignored executables again."
                : "Temporarily show tasks hidden by the ignored executable list.");
    }

    private async Task<bool> StartCrop(
        WindowInfo window,
        WindowState state,
        Rectangle? initialSelection = null,
        bool autoLockedTopMost = false,
        bool? sourceWasTracked = null)
    {
        if (!IsWindow(window.Handle))
            return false;

        if (IsIconic(window.Handle))
            ShowWindow(window.Handle, ShowWindowCommand.Restore);

        // Only one selection overlay may exist: the overlay claims the global
        // Enter/Esc hotkeys, so a second overlay could never register them and
        // both would stop responding to Enter.
        if (_selectionOverlayActive)
            return false;

        SetForegroundWindow(window.Handle);
        Rectangle selectedBounds;
        _selectionOverlayActive = true;
        try
        {
            Rectangle? autoVideoBounds =
                await TryGetFreshVideoBounds(window);
            if (!IsWindow(window.Handle))
                return false;

            using var overlay = new CropSelectionForm(
                window.Handle,
                initialSelection,
                autoVideoBounds)
            {
                UseWaitCursor = true
            };
            if (overlay.ShowDialog() != DialogResult.OK)
                return false;

            selectedBounds = overlay.SelectedBounds;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _selectionOverlayActive = false;
        }

        ThumbnailCropForm crop;
        using (ShowWaitCursor())
        {
            try
            {
                crop = new(
                    window.Handle,
                    window.Title,
                    selectedBounds,
                    state);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            RegisterCrop(
                window,
                state,
                crop,
                autoLockedTopMost,
                sourceWasTracked);
        }
        return true;
    }

    // Fresh coordinates beat the passive cache: the player may have moved or
    // resized since the last scan (e.g. clicking play reflows the page).
    private void RegisterCrop(
        WindowInfo window,
        WindowState state,
        ThumbnailCropForm crop,
        bool autoLockedTopMost = false,
        bool? sourceWasTracked = null)
    {
        bool autoLocked =
            autoLockedTopMost
            || (_settings.AutoLockTopMostOnCrop && !state.TopMost);
        WindowState cropState = autoLocked
            ? state with { TopMost = true }
            : state;
        crop.RecropRequested += (_, _) => RecropWindow(window);
        crop.OpacityChangeRequested += opacity =>
            ApplyTrayWindowState(
                window,
                GetActiveWindowState(window) with
                {
                    Opacity = opacity
                });
        crop.ClickThroughRequested += (_, _) =>
            ApplyTrayWindowState(
                window,
                GetActiveWindowState(window) with
                {
                    ClickThrough = true
                });
        crop.SetShowOpacityPercentage(_settings.ShowOpacityPercentage);
        crop.ApplyState(cropState);
        if (_rows.TryGetValue(window.Handle, out WindowRow? row))
            row.SetState(cropState);

        bool wasSourceTracked =
            sourceWasTracked ?? WindowManager.IsTracked(window.Handle);
        WindowManager.Track(window.Handle);

        var session = new CropSession(
            window,
            crop,
            state,
            autoLocked,
            wasSourceTracked,
            GetWindowBounds(window.Handle))
        {
            State = cropState
        };
        crop.FormClosing += (_, _) =>
        {
            if (!session.SuppressSourceRestore)
                OnCropStateChanged(window.Handle, false);
        };
        crop.FormClosed += (_, _) =>
        {
            _crops.Remove(window.Handle);
            WindowState finalState =
                GetStoredCropState(session, session.State);
            if (session.SuppressSourceRestore)
                return;

            if (IsWindow(window.Handle))
            {
                WindowManager.ApplyStoredState(
                    window.Handle,
                    finalState);
            }

            if (!session.SourceWasTracked
                && finalState == session.InitialState)
            {
                WindowManager.Forget(window.Handle);
            }

            SaveWindowState(window, finalState);
            OnCropStateChanged(
                window.Handle,
                false,
                finalState);
            // The restored window is scannable again; bring its play button back.
            QueueSingleVideoScan(window.Handle);
        };

        _crops.Add(window.Handle, session);
        OnCropStateChanged(window.Handle, true);
        crop.Show();
        if (!state.ClickThrough)
            crop.RestoreShortcutFocus();
    }

    private async void RecropWindow(WindowInfo window)
    {
        if (!_crops.TryGetValue(
                window.Handle,
                out CropSession? session))
        {
            return;
        }

        WindowState state = GetStoredCropState(session, session.State);
        Rectangle selection = session.Crop.SourceSelectionBounds;
        Rectangle cropBounds = session.Crop.Bounds;
        bool sourceWasTracked = session.SourceWasTracked;
        session.SuppressSourceRestore = true;
        session.Crop.Close();
        if (IsWindow(window.Handle))
        {
            // Recrop must snapshot the real source window, not the previous
            // crop's layered/click-through parking state.
            WindowManager.Reset(window.Handle);
            ThumbnailCropForm.ClearNativeCropResidue(window.Handle);
        }
        if (await StartCrop(
                window,
                state,
                selection,
                session.AutoLockedTopMost,
                sourceWasTracked))
        {
            return;
        }

        if (!IsWindow(window.Handle))
            return;

        try
        {
            var crop = new ThumbnailCropForm(
                window.Handle,
                window.Title,
                selection,
                state)
            {
                Bounds = cropBounds
            };
            RegisterCrop(
                window,
                state,
                crop,
                session.AutoLockedTopMost,
                sourceWasTracked);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void StopCrop(nint sourceHandle)
    {
        if (_crops.TryGetValue(
                sourceHandle,
                out CropSession? session))
        {
            session.Crop.Close();
        }
    }

    private void StopAllCrops()
    {
        foreach (CropSession session in _crops.Values.ToArray())
            session.Crop.Close();

        _crops.Clear();
    }

    private void ApplyWindowState(nint sourceHandle, WindowState state)
    {
        if (_crops.TryGetValue(sourceHandle, out CropSession? session))
        {
            session.State = state;
            session.Crop.ApplyState(state);
            ApplyTrayState();
            return;
        }

        WindowManager.ApplyStoredState(sourceHandle, state);
        ApplyTrayState();
    }

    private void OnCropStateChanged(
        nint handle,
        bool isCropped,
        WindowState? state = null)
    {
        if (_rows.TryGetValue(
                handle,
                out WindowRow? row))
        {
            if (!isCropped && state is not null)
                row.SetState(state);

            row.SetCropActive(isCropped);
            row.SetTaskActive(
                IsWindowTaskActive(
                    handle));
        }

        ApplyTrayState();
    }

    private void RepairParkedSiblingWindows(List<WindowInfo> windows)
    {
        if (_crops.Count == 0)
            return;

        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        foreach (WindowInfo window in windows)
            RepairParkedSiblingWindow(window, virtualScreen);
    }

    // New top-level windows of a cropped process (popups, new browser windows)
    // can spawn at their parked parent's off-screen position and look "lost";
    // pull them back to where the cropped source originally lived.
    private void RepairParkedSiblingWindow(
        WindowInfo window,
        Rectangle virtualScreen)
    {
        if (_crops.Count == 0)
            return;

        if (_crops.ContainsKey(window.Handle)
            || !GetWindowRect(
                window.Handle,
                out NativeRect rect))
        {
            return;
        }

        Rectangle bounds = ToRectangle(rect);
        if (!IsAtParkingEdge(bounds, virtualScreen))
            return;

        CropSession? source = _crops.Values.FirstOrDefault(
            session =>
                !session.SourceBounds.IsEmpty
                && string.Equals(
                    session.Window.ProcessName,
                    window.ProcessName,
                    StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return;

        Rectangle restored = FitToVirtualScreen(
            new(
                source.SourceBounds.Location,
                bounds.Size),
            virtualScreen);
        _ = SetWindowPos(
            window.Handle,
            0,
            restored.Left,
            restored.Top,
            restored.Width,
            restored.Height,
            WindowPositionFlags.NoActivate
                | WindowPositionFlags.NoZOrder);
    }

    private static Rectangle GetWindowBounds(nint handle) =>
        GetWindowRect(handle, out NativeRect rect)
            ? ToRectangle(rect)
            : Rectangle.Empty;

    private static Rectangle ToRectangle(NativeRect rect) =>
        Rectangle.FromLTRB(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);

    private static bool IsAtParkingEdge(Rectangle bounds, Rectangle virtualScreen) =>
        bounds.Left >= virtualScreen.Right - 2
        && Math.Abs(bounds.Top - virtualScreen.Top) <= 8;

    private static Rectangle FitToVirtualScreen(Rectangle bounds, Rectangle virtualScreen)
    {
        int width = Math.Clamp(bounds.Width, 1, Math.Max(1, virtualScreen.Width));
        int height = Math.Clamp(bounds.Height, 1, Math.Max(1, virtualScreen.Height));
        return new(
            Math.Clamp(bounds.Left, virtualScreen.Left, virtualScreen.Right - width),
            Math.Clamp(bounds.Top, virtualScreen.Top, virtualScreen.Bottom - height),
            width,
            height);
    }

    private void RestoreManagedWindows()
    {
        _stateSaveTimer.Stop();
        StopAllCrops();
        foreach (nint handle in WindowManager.TrackedHandles)
            WindowManager.Reset(handle);

        _stateStore.Clear();
        ApplyTrayState();
    }

    private void RestoreAllWindows()
    {
        RestoreManagedWindows();

        foreach (WindowRow row in _rows.Values)
        {
            row.SetState(WindowManager.ReadState(
                row.Window.Handle));
            row.SetTaskActive(false);
        }
    }

    private void ResetWindow(WindowInfo window)
    {
        WindowState reset = new();
        StopCrop(window.Handle);
        if (IsWindow(window.Handle))
            WindowManager.ApplyStoredState(window.Handle, reset);
        WindowManager.Forget(window.Handle);
        SaveWindowState(window, reset);

        if (_rows.TryGetValue(
                window.Handle,
                out WindowRow? row))
        {
            row.SetState(reset);
            row.SetTaskActive(false);
        }

        ApplyTrayState();
    }

    private void RegisterGlobalShortcuts(bool showErrors = false)
    {
        if (!IsHandleCreated)
            return;

        UnregisterGlobalShortcuts();
        if (!_settings.EnableKeybinds)
            return;

        var failed = new List<ShortcutDefinition>();
        foreach (ShortcutDefinition shortcut in Shortcuts.All)
        {
            ShortcutGesture gesture =
                _settings.GetShortcut(shortcut.Action);
            bool registered = RegisterHotKey(
                Handle,
                (int)shortcut.Action,
                gesture.Modifiers | HotKeyModifiers.NoRepeat,
                (uint)gesture.Key);
            if (!registered)
                failed.Add(shortcut);
        }

        if (showErrors && failed.Count > 0)
        {
            string details = string.Join(
                Environment.NewLine,
                failed.Select(shortcut =>
                    $"{shortcut.ActionName}: "
                    + Shortcuts.Format(
                        _settings.GetShortcut(
                            shortcut.Action))));
            MessageBox.Show(
                this,
                "Windows or another application is already using these shortcuts:"
                    + Environment.NewLine
                    + Environment.NewLine
                    + details,
                "Shortcut unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UnregisterGlobalShortcuts()
    {
        if (!IsHandleCreated)
            return;

        foreach (ShortcutDefinition shortcut in Shortcuts.All)
            _ = UnregisterHotKey(Handle, (int)shortcut.Action);
    }

    private void HandleGlobalShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ShowTaskTru:
                RestoreFromTray();
                return;
            case ShortcutAction.RestoreAll:
                RestoreAllWindows();
                return;
            case ShortcutAction.Uncrop:
                if (TryGetHotKeyTarget(
                        out WindowInfo? cropped,
                        out _)
                    && IsCropped(cropped.Handle))
                {
                    StopCrop(cropped.Handle);
                }

                return;
            case ShortcutAction.Interact:
                CropSession? interacting =
                    _crops.Values.FirstOrDefault(session => session.Crop.IsInteracting);
                if (interacting is not null)
                {
                    interacting.Crop.ToggleInteractions();
                    return;
                }

                if (TryGetHotKeyTarget(
                        out WindowInfo? interactTarget,
                        out _)
                    && _crops.TryGetValue(
                        interactTarget.Handle,
                        out CropSession? session))
                {
                    session.Crop.ToggleInteractions();
                }

                return;
        }

        if (!TryGetHotKeyTarget(
                out WindowInfo? window,
                out WindowState state))
        {
            return;
        }

        switch (action)
        {
            case ShortcutAction.ClickThrough:
                ApplyShortcutWindowState(
                    window,
                    state with
                    {
                        ClickThrough = !state.ClickThrough
                    });
                break;
            case ShortcutAction.TopMost:
                ApplyShortcutWindowState(
                    window,
                    state with
                    {
                        TopMost = !state.TopMost
                    });
                break;
            case ShortcutAction.OpacityUp:
                ApplyShortcutWindowState(
                    window,
                    state with
                    {
                        Opacity = Math.Min(
                            100,
                            state.Opacity + 5)
                    });
                break;
            case ShortcutAction.OpacityDown:
                ApplyShortcutWindowState(
                    window,
                    state with
                    {
                        Opacity = Math.Max(
                            5,
                            state.Opacity - 5)
                    });
                break;
            case ShortcutAction.Crop:
                if (IsCropped(window.Handle))
                    RecropWindow(window);
                else
                    _ = StartCrop(window, state);
                break;
            case ShortcutAction.VideoMode:
                if (_settings.ScanForVideoContent)
                    StartVideoCrop(window, state);
                break;
        }
    }

    private void ApplyShortcutWindowState(WindowInfo window, WindowState state)
    {
        ApplyTrayWindowState(
            window,
            state,
            restoreFocusWhenClickThroughEnds: false);
        if (_crops.TryGetValue(window.Handle, out CropSession? session))
            session.Crop.RestoreShortcutFocus();
    }

    private bool TryGetHotKeyTarget(
        out WindowInfo window,
        out WindowState state)
    {
        nint handle = GetForegroundWindow();
        if (TryGetSourceHandle(handle, out nint sourceHandle))
            handle = sourceHandle;

        if (!WindowManager.TryGetWindowInfo(
                handle,
                out window))
        {
            state = new();
            return false;
        }

        state = GetWindowState(window);
        return true;
    }

    private bool IsWindowTaskActive(nint handle) =>
        IsCropped(handle)
        || WindowManager.HasActiveChanges(handle);

    private bool ConfirmExitWithActiveTasks()
    {
        int activeTaskCount = ActiveTaskCount();
        if (!_settings.ConfirmExitWithActiveTasks
            || activeTaskCount == 0)
        {
            return true;
        }

        string taskText = activeTaskCount == 1
            ? "1 active task"
            : $"{activeTaskCount} active tasks";
        using var dialog = new ExitConfirmDialog(taskText);
        DialogResult result = dialog.ShowDialog(this);
        if (dialog.NeverAskAgain)
        {
            _settings = (_settings with
            {
                ConfirmExitWithActiveTasks = false
            }).Normalize();
            WriteSettings(_settings);
        }

        return result == DialogResult.Yes;
    }

    private void SaveWindowState(WindowInfo window, WindowState state)
    {
        _stateStore.Set(window.StateKey, state);
        _stateSaveTimer.Stop();
        _stateSaveTimer.Start();
    }

    private static AppSettings ReadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath))
                    ?.Normalize()
                    ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private int RefreshIntervalMilliseconds =>
        _settings.RefreshFrequencySeconds * 1000;

    private static void WriteSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(SettingsPath)!);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch
        {
            // Settings should never block window control.
        }
    }

    private static void ApplyStartupSetting(AppSettings settings)
    {
        try
        {
            using RegistryKey key =
                Registry.CurrentUser.CreateSubKey(
                    RunKeyPath,
                    writable: true);
            if (!settings.StartWithWindows)
            {
                key.DeleteValue(
                    StartupValueName,
                    throwOnMissingValue: false);
                return;
            }

            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                string command = $"\"{executable}\"";
                if (settings.StartMinimizedToTray)
                    command += " --minimized";

                key.SetValue(
                    StartupValueName,
                    command);
            }
        }
        catch
        {
            // Startup registration can be unavailable by policy.
        }
    }

    private sealed record CropSession(
        WindowInfo Window,
        ThumbnailCropForm Crop,
        WindowState InitialState,
        bool AutoLockedTopMost,
        bool SourceWasTracked,
        Rectangle SourceBounds)
    {
        public WindowState State { get; set; } = InitialState;
        public bool SuppressSourceRestore { get; set; }
    }

    private sealed class WaitCursorScope : IDisposable
    {
        private readonly Control _owner;
        private readonly bool _previousUseWaitCursor;
        private readonly Cursor? _previousCursor;

        public WaitCursorScope(Control owner)
        {
            _owner = owner;
            _previousUseWaitCursor = owner.UseWaitCursor;
            _previousCursor = Cursor.Current;
            owner.UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
        }

        public void Dispose()
        {
            if (!_owner.IsDisposed)
                _owner.UseWaitCursor = _previousUseWaitCursor;

            Cursor.Current = _previousCursor ?? Cursors.Default;
        }
    }
}
