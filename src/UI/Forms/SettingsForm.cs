using System.Diagnostics;
using System.Drawing;

namespace taskTru;

internal sealed class SettingsForm : Form
{
    private const int LogicalShortcutRowHeight = 25;
    private const int LogicalShortcutChromeHeight = 129;
    private const int LogicalFooterHeight = 52;
    private static readonly Color SecondaryText = UiTheme.SecondaryText;
    private static readonly Color SupportAccent = Color.FromArgb(0xA1, 0x5E, 0x77);

    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startMinimized;
    private readonly CheckBox _closeToTray;
    private readonly CheckBox _restoreTasksOnExit;
    private readonly CheckBox _confirmExitWithActiveTasks;
    private readonly CheckBox _autoLockTopMostOnCrop;
    private readonly CheckBox _showOpacity;
    private readonly CheckBox _updateFlash;
    private readonly CheckBox _scanForVideoContent;
    private readonly CheckBox _manualTaskRefresh;
    private readonly CheckBox _compactTaskRows;
    private readonly CheckBox _keybinds;
    private readonly CheckBox _favoriteTasks;
    private readonly CheckBox _ignoredTasks;
    private readonly NumericUpDown _refreshFrequency;
    private Label _refreshEveryLabel = null!;
    private Label _refreshSecondsLabel = null!;
    private readonly CheckedListBox _favoriteList;
    private readonly CheckedListBox _ignoredList;
    private readonly ToolTip _toolTip = UiToolTips.Create(6000);
    private readonly Dictionary<ShortcutAction, ShortcutCaptureButton> _shortcutInputs = [];
    private TableLayoutPanel _layout = null!;
    private Panel _settingsViewport = null!;
    private TableLayoutPanel _settingsGrid = null!;
    private TableLayoutPanel _leftSections = null!;
    private TableLayoutPanel _manageLists = null!;
    private RoundedPanel _shortcutSection = null!;
    private RoundedPanel _taskSection = null!;
    private DarkScrollBar _settingsScroll = null!;
    private Label _shortcutStatus = null!;
    private Label _favoriteTitle = null!;
    private Label _ignoredTitle = null!;
    private Label _favoriteEmpty = null!;
    private Label _ignoredEmpty = null!;
    private Label _taskDescription = null!;
    private RoundedPanel _favoriteSurface = null!;
    private RoundedPanel _ignoredSurface = null!;
    private Control _favoriteListArea = null!;
    private Control _ignoredListArea = null!;
    private RoundedActionButton _resetAllButton = null!;
    private RoundedActionButton _supportButton = null!;
    private bool _dpiLayoutReady;
    private bool _layingOutSettings;
    private bool _initialSizeApplied;
    private int _layoutDpi = UiScale.DefaultDpi;

    public AppSettings Settings { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Settings = settings.Normalize();
        SuspendLayout();

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = UiTheme.AppBackground;
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        Opacity = 0;
        ClientSize = new(760, 760);
        Font = new("Segoe UI", 8.25f);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
            true);
        DpiChanged += OnDpiChanged;

        _startWithWindows = CreateCheckBox(
            "Start with Windows",
            Settings.StartWithWindows,
            "Launch taskTru when you sign in to Windows.");
        _startMinimized = CreateCheckBox(
            "Start minimized to tray",
            Settings.StartMinimizedToTray,
            "Launch taskTru directly in the system tray.");
        _startMinimized.Margin = new(24, 2, 0, 4);
        _startMinimized.Enabled = Settings.StartWithWindows;
        _startWithWindows.CheckedChanged += OnStartWithWindowsChanged;
        _closeToTray = CreateCheckBox(
            "Close button minimizes to tray",
            Settings.CloseButtonMinimizesToTray,
            "Keep taskTru running in the tray when its window is closed.");
        _restoreTasksOnExit = CreateCheckBox(
            "Restore tasks when taskTru exits",
            Settings.RestoreTasksOnExit,
            "Return managed windows to their original state when taskTru shuts down.");
        _confirmExitWithActiveTasks = CreateCheckBox(
            "Confirm before exiting with active tasks",
            Settings.ConfirmExitWithActiveTasks,
            "Ask before exiting while taskTru is managing windows.");
        _autoLockTopMostOnCrop = CreateCheckBox(
            "Auto lock on top when cropping",
            Settings.AutoLockTopMostOnCrop,
            "Keep cropped tasks above other windows until they are uncropped.");
        _showOpacity = CreateCheckBox(
            "Show opacity percentage",
            Settings.ShowOpacityPercentage,
            "Show the numeric opacity value beside each row slider.");
        _updateFlash = CreateCheckBox(
            "Flash newly detected windows",
            Settings.EnableUpdateFlash,
            "Briefly highlight rows when new windows appear.");
        _scanForVideoContent = CreateCheckBox(
            "Scan for video content",
            Settings.ScanForVideoContent,
            "Detect video players and show the auto video crop button.");
        _manualTaskRefresh = CreateCheckBox(
            "Manual task list refresh",
            Settings.ManualTaskRefresh,
            "Stop the automatic task list refresh; use the refresh button next to settings instead.");
        _manualTaskRefresh.CheckedChanged += (_, _) => UpdateRefreshRowState();
        _scanForVideoContent.Margin = new(0, 10, 0, 4);
        _compactTaskRows = CreateCheckBox(
            "Compact task rows",
            Settings.CompactTaskRows,
            "Use shorter rows in the main task list.");
        _keybinds = CreateCheckBox(
            "Enable global shortcuts",
            Settings.EnableKeybinds,
            "Allow configured shortcuts to work while taskTru is in the background.");
        _keybinds.CheckedChanged += (_, _) => UpdateShortcutInputState();
        _favoriteTasks = CreateCheckBox(
            "Favorite",
            Settings.EnableFavoriteTasks,
            "Show favorite buttons and sort favorite executables above other tasks.");
        _ignoredTasks = CreateCheckBox(
            "Ignore list",
            Settings.EnableIgnoredTasks,
            "Show ignore buttons and hide tasks from the ignore list.");
        _favoriteTasks.CheckedChanged += (_, _) => UpdateTaskSystemState();
        _ignoredTasks.CheckedChanged += (_, _) => UpdateTaskSystemState();
        _refreshFrequency = new()
        {
            Minimum = 1,
            Maximum = 60,
            Value = Settings.RefreshFrequencySeconds,
            Width = 58,
            Height = 24,
            BackColor = UiTheme.ButtonBackground,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center,
            Margin = new(7, 0, 7, 0)
        };
        _toolTip.SetToolTip(
            _refreshFrequency,
            "How often taskTru refreshes the task list.");
        _favoriteList = CreateExecutableList(Settings.FavoriteExecutables);
        _ignoredList = CreateExecutableList(Settings.IgnoredExecutables);

        _layout = CreateLayout();
        Controls.Add(_layout);
        UpdateShortcutInputState();
        UpdateTaskSystemState();
        UpdateListSummaries();
        UpdateRefreshRowState();
        ResumeLayout(performLayout: false);
    }

    protected override void OnShown(EventArgs e)
    {
        FitToWorkingArea();
        Opacity = 1;
        base.OnShown(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.Apply(Handle);
        ScaleLayoutForDpi(DeviceDpi);
        ApplyInitialSize();
        _dpiLayoutReady = true;
        LayoutSettingsGrid();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DpiChanged -= OnDpiChanged;
            _settingsViewport.Resize -= OnSettingsViewportResize;
            _settingsScroll.ValueChanged -= OnSettingsScrollChanged;
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_settingsScroll.Visible
            && _settingsViewport.RectangleToScreen(_settingsViewport.ClientRectangle)
                .Contains(MousePosition))
        {
            _settingsScroll.Value -= Math.Sign(e.Delta)
                * UiScale.ToDevice(48, DeviceDpi);
            return;
        }

        base.OnMouseWheel(e);
    }

    private TableLayoutPanel CreateLayout()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Padding = new(14, 14, 14, 12),
            BackColor = UiTheme.AppBackground
        };
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, LogicalFooterHeight));

        _settingsViewport = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = UiTheme.AppBackground
        };
        _settingsGrid = CreateSettingsGrid();
        _settingsScroll = new()
        {
            Dock = DockStyle.Right,
            Visible = false
        };
        _settingsScroll.ValueChanged += OnSettingsScrollChanged;
        _toolTip.SetToolTip(_settingsScroll, "Scroll through settings.");
        _settingsViewport.Controls.Add(_settingsGrid);
        _settingsViewport.Controls.Add(_settingsScroll);
        _settingsViewport.Resize += OnSettingsViewportResize;

        layout.Controls.Add(_settingsViewport, 0, 0);
        layout.Controls.Add(CreateFooter(), 0, 1);
        return layout;
    }

    private TableLayoutPanel CreateSettingsGrid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new(SizeType.Percent, 47));
        grid.ColumnStyles.Add(new(SizeType.Percent, 53));

        _leftSections = new()
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Margin = new(0, 0, 6, 0),
            BackColor = Color.Transparent
        };
        _leftSections.RowStyles.Add(new(SizeType.Percent, 42));
        _leftSections.RowStyles.Add(new(SizeType.Percent, 27));
        _leftSections.RowStyles.Add(new(SizeType.Percent, 31));
        _leftSections.Controls.Add(CreateStartupSection(), 0, 0);
        _leftSections.Controls.Add(CreateInterfaceSection(), 0, 1);
        _leftSections.Controls.Add(CreatePerformanceSection(), 0, 2);

        _shortcutSection = CreateShortcutSection();
        _taskSection = CreateTaskSection();
        grid.Controls.Add(_leftSections, 0, 0);
        grid.Controls.Add(_shortcutSection, 1, 0);
        grid.Controls.Add(_taskSection, 0, 1);
        grid.SetColumnSpan(_taskSection, 2);
        return grid;
    }

    private RoundedPanel CreateStartupSection()
    {
        var content = CreateSectionContent(7);
        content.Controls.Add(CreateSectionTitle("App behavior"));
        content.Controls.Add(_startWithWindows);
        content.Controls.Add(_startMinimized);
        content.Controls.Add(_closeToTray);
        content.Controls.Add(_restoreTasksOnExit);
        content.Controls.Add(_confirmExitWithActiveTasks);
        content.Controls.Add(_autoLockTopMostOnCrop);
        return CreateSection(content, new(0, 0, 0, 6));
    }

    private RoundedPanel CreateInterfaceSection()
    {
        var content = CreateSectionContent(4);
        content.Controls.Add(CreateSectionTitle("Interface"));
        content.Controls.Add(_compactTaskRows);
        content.Controls.Add(_showOpacity);
        content.Controls.Add(_updateFlash);
        return CreateSection(content, new(0, 6, 0, 6));
    }

    private RoundedPanel CreatePerformanceSection()
    {
        var content = CreateSectionContent(4);
        content.Controls.Add(CreateSectionTitle("Performance"));
        content.Controls.Add(_manualTaskRefresh);
        content.Controls.Add(CreateRefreshRow());
        content.Controls.Add(_scanForVideoContent);
        return CreateSection(content, new(0, 6, 0, 0));
    }

    private void UpdateRefreshRowState()
    {
        if (_refreshEveryLabel is null || _refreshSecondsLabel is null)
            return;

        bool automatic = !_manualTaskRefresh.Checked;
        _refreshFrequency.Enabled = automatic;
        _refreshEveryLabel.ForeColor = automatic
            ? Color.White
            : UiTheme.DisabledText;
        _refreshSecondsLabel.ForeColor = automatic
            ? SecondaryText
            : UiTheme.DisabledText;
    }

    private RoundedPanel CreateShortcutSection()
    {
        var content = CreateSectionContent(5);
        content.RowStyles.Add(new(SizeType.Absolute, 26));
        content.RowStyles.Add(new(SizeType.Absolute, 28));
        content.RowStyles.Add(new(SizeType.Absolute, 30));
        // The table stretches over the leftover section height so shortcut rows
        // breathe instead of leaving a dead gap at the bottom.
        content.RowStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 30));
        content.Controls.Add(CreateSectionTitle("Keyboard shortcuts"));
        content.Controls.Add(_keybinds);
        content.Controls.Add(CreateShortcutHeader());
        content.Controls.Add(CreateShortcutTable());
        content.Controls.Add(CreateShortcutFooter());
        return CreateSection(content, new(6, 0, 0, 0));
    }

    private RoundedPanel CreateTaskSection()
    {
        var content = CreateSectionContent(4);
        content.RowStyles.Add(new(SizeType.Absolute, 26));
        content.RowStyles.Add(new(SizeType.Absolute, 28));
        content.RowStyles.Add(new(SizeType.Absolute, 31));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        content.Controls.Add(CreateSectionTitle("Task sorting"), 0, 0);
        _taskDescription = new()
        {
            Text = "Favorites stay at the top of the list. Ignored executables are hidden from task lists.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = SecondaryText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        content.Controls.Add(_taskDescription, 0, 1);
        content.Controls.Add(CreateTaskSortingRow(), 0, 2);
        content.Controls.Add(CreateManageLists(), 0, 3);
        return CreateSection(content, Padding.Empty);
    }

    private static TableLayoutPanel CreateSectionContent(int rowCount)
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = rowCount,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        return content;
    }

    private static RoundedPanel CreateSection(Control content, Padding margin)
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new(13, 11, 13, 10),
            FillColor = UiTheme.RowPrimary,
            BorderColor = Color.FromArgb(62, 62, 62)
        };
        panel.Controls.Add(content);
        return panel;
    }

    private static Label CreateSectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.White,
        Font = new("Segoe UI", 9.5f, FontStyle.Bold),
        Margin = new(0, 0, 0, 7)
    };

    private FlowLayoutPanel CreateRefreshRow()
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new(0, 5, 0, 0)
        };
        _refreshEveryLabel = new()
        {
            Text = "Refresh task list every",
            AutoSize = true,
            ForeColor = Color.White,
            Margin = new(0, 4, 0, 0)
        };
        row.Controls.Add(_refreshEveryLabel);
        row.Controls.Add(_refreshFrequency);
        _refreshSecondsLabel = new()
        {
            Text = "seconds",
            AutoSize = true,
            ForeColor = SecondaryText,
            Margin = new(0, 4, 0, 0)
        };
        row.Controls.Add(_refreshSecondsLabel);
        return row;
    }

    private static TableLayoutPanel CreateShortcutHeader()
    {
        var header = CreateShortcutGrid();
        header.AutoSize = false;
        header.Dock = DockStyle.Fill;
        header.RowStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(CreateShortcutLabel("Action", bold: true), 0, 0);
        header.Controls.Add(CreateShortcutLabel("Shortcut", bold: true), 1, 0);
        return header;
    }

    private TableLayoutPanel CreateShortcutTable()
    {
        var table = CreateShortcutGrid();
        table.RowCount = Shortcuts.All.Length;
        table.AutoSize = false;
        table.Dock = DockStyle.Fill;
        table.Margin = Padding.Empty;

        for (int index = 0; index < Shortcuts.All.Length; index++)
        {
            ShortcutDefinition shortcut = Shortcuts.All[index];
            table.RowStyles.Add(new(
                SizeType.Percent,
                100f / Shortcuts.All.Length));
            table.Controls.Add(CreateShortcutLabel(shortcut.ActionName), 0, index);
            var input = new ShortcutCaptureButton(Settings.GetShortcut(shortcut.Action))
            {
                Dock = DockStyle.Fill,
                Margin = new(3, 2, 0, 2),
                Font = new("Consolas", 8f)
            };
            input.GestureChanging += OnShortcutGestureChanging;
            _shortcutInputs.Add(shortcut.Action, input);
            _toolTip.SetToolTip(
                input,
                $"Change the shortcut for {shortcut.ActionName}. Use Ctrl, Alt, or Shift with a key, or use an F-key alone; Esc cancels.");
            table.Controls.Add(input, 1, index);
        }

        return table;
    }

    private static TableLayoutPanel CreateShortcutGrid()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new(SizeType.Percent, 46));
        table.ColumnStyles.Add(new(SizeType.Percent, 54));
        return table;
    }

    private static Label CreateShortcutLabel(string text, bool bold = false) => new()
    {
        Text = text,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = bold ? Color.Gainsboro : SecondaryText,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new("Segoe UI", 8.25f, bold ? FontStyle.Bold : FontStyle.Regular),
        Padding = bold ? new(0, 2, 0, 0) : Padding.Empty,
        Margin = Padding.Empty
    };

    private Control CreateShortcutFooter()
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new(0, 4, 0, 0),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.Absolute, 104));
        var reset = CreateButton("Reset defaults");
        reset.Size = new(104, 24);
        reset.Anchor = AnchorStyles.Right;
        reset.Margin = Padding.Empty;
        reset.Click += (_, _) => ResetShortcuts();
        _shortcutStatus = new()
        {
            Text = "Click a shortcut, then press new keys.",
            ForeColor = SecondaryText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new(0, 2, 8, 0)
        };
        row.Controls.Add(_shortcutStatus, 0, 0);
        row.Controls.Add(reset, 1, 0);
        return row;
    }

    private FlowLayoutPanel CreateTaskSortingRow()
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.Controls.Add(new Label
        {
            Text = "Task list controls",
            AutoSize = true,
            ForeColor = SecondaryText,
            Margin = new(0, 6, 12, 0)
        });
        _favoriteTasks.Margin = new(0, 4, 12, 0);
        _ignoredTasks.Margin = new(0, 4, 0, 0);
        row.Controls.Add(_favoriteTasks);
        row.Controls.Add(_ignoredTasks);
        return row;
    }

    private TableLayoutPanel CreateManageLists()
    {
        _manageLists = new()
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        _manageLists.ColumnStyles.Add(new(SizeType.Percent, 50));
        _manageLists.ColumnStyles.Add(new(SizeType.Percent, 50));

        _favoriteListArea = CreateListArea(
            "Manage favorites",
            "No favorite executables.",
            _favoriteList,
            out _favoriteTitle,
            out _favoriteEmpty,
            out _favoriteSurface);
        _ignoredListArea = CreateListArea(
            "Manage ignore list",
            "No ignored executables.",
            _ignoredList,
            out _ignoredTitle,
            out _ignoredEmpty,
            out _ignoredSurface);
        _favoriteListArea.Margin = new(0, 0, 6, 0);
        _ignoredListArea.Margin = new(6, 0, 0, 0);
        _manageLists.Controls.Add(_favoriteListArea, 0, 0);
        _manageLists.Controls.Add(_ignoredListArea, 1, 0);
        return _manageLists;
    }

    private bool ConfigureManageListsLayout(int availableWidth)
    {
        bool stacked = availableWidth < UiScale.ToDevice(620, _layoutDpi);
        _manageLists.SuspendLayout();
        try
        {
            int gap = UiScale.ToDevice(12, _layoutDpi);
            _manageLists.ColumnStyles.Clear();
            _manageLists.RowStyles.Clear();
            _manageLists.ColumnCount = stacked ? 1 : 2;
            _manageLists.RowCount = stacked ? 2 : 1;
            if (stacked)
            {
                _manageLists.ColumnStyles.Add(new(SizeType.Percent, 100));
                _manageLists.RowStyles.Add(new(SizeType.Percent, 50));
                _manageLists.RowStyles.Add(new(SizeType.Percent, 50));
                _manageLists.SetCellPosition(_favoriteListArea, new(0, 0));
                _manageLists.SetCellPosition(_ignoredListArea, new(0, 1));
                _favoriteListArea.Margin = new(0, 0, 0, gap / 2);
                _ignoredListArea.Margin = new(0, gap / 2, 0, 0);
            }
            else
            {
                _manageLists.ColumnStyles.Add(new(SizeType.Percent, 50));
                _manageLists.ColumnStyles.Add(new(SizeType.Percent, 50));
                _manageLists.RowStyles.Add(new(SizeType.Percent, 100));
                _manageLists.SetCellPosition(_favoriteListArea, new(0, 0));
                _manageLists.SetCellPosition(_ignoredListArea, new(1, 0));
                _favoriteListArea.Margin = new(0, 0, gap / 2, 0);
                _ignoredListArea.Margin = new(gap / 2, 0, 0, 0);
            }
        }
        finally
        {
            _manageLists.ResumeLayout(performLayout: true);
        }

        return stacked;
    }

    private Control CreateListArea(
        string title,
        string emptyText,
        CheckedListBox list,
        out Label titleLabel,
        out Label emptyLabel,
        out RoundedPanel surface)
    {
        var area = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        area.RowStyles.Add(new(SizeType.Absolute, 28));
        area.RowStyles.Add(new(SizeType.Percent, 100));
        titleLabel = new()
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = SecondaryText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        surface = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new(7, 6, 7, 6),
            FillColor = UiTheme.InputBackground,
            BorderColor = UiTheme.Border
        };
        emptyLabel = new()
        {
            Text = emptyText,
            Dock = DockStyle.Fill,
            ForeColor = SecondaryText,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = Padding.Empty,
            Visible = list.Items.Count == 0
        };
        surface.Controls.Add(new DarkListHost(list));
        surface.Controls.Add(emptyLabel);
        emptyLabel.BringToFront();
        area.Controls.Add(titleLabel, 0, 0);
        area.Controls.Add(surface, 0, 1);
        return area;
    }

    // Hosts a CheckedListBox with the app's DarkScrollBar on the list background;
    // the native scrollbar is pushed past the clip edge so it never shows.
    private sealed class DarkListHost : Panel
    {
        private readonly CheckedListBox _list;
        private readonly DarkScrollBar _scroll;
        private readonly Panel _clip;
        private bool _syncing;

        public DarkListHost(CheckedListBox list)
        {
            _list = list;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            BackColor = UiTheme.InputBackground;
            _scroll = new()
            {
                Dock = DockStyle.Right,
                Width = UiTheme.ScrollBarWidth,
                BackColor = UiTheme.InputBackground
            };
            _clip = new()
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackColor = UiTheme.InputBackground
            };
            _clip.Controls.Add(_list);
            Controls.Add(_clip);
            Controls.Add(_scroll);
            _clip.Resize += (_, _) => UpdateScroll();
            _scroll.ValueChanged += (_, _) => SyncFromScroll();
            _list.MouseWheel += (_, _) => QueueSyncFromList();
            _list.SelectedIndexChanged += (_, _) => SyncFromList();
        }

        public void UpdateScroll()
        {
            int overhang =
                SystemInformation.VerticalScrollBarWidth + 4;
            _list.SetBounds(
                0,
                0,
                Math.Max(1, _clip.ClientSize.Width + overhang),
                Math.Max(1, _clip.ClientSize.Height));
            int itemHeight = Math.Max(1, _list.ItemHeight);
            int viewHeight = Math.Max(1, _clip.ClientSize.Height);
            _scroll.LargeChange = viewHeight;
            _scroll.Maximum = Math.Max(
                0,
                _list.Items.Count * itemHeight - viewHeight);
            SyncFromList();
        }

        private void QueueSyncFromList()
        {
            if (IsHandleCreated)
                BeginInvoke(SyncFromList);
        }

        private void SyncFromList()
        {
            if (_syncing)
                return;

            _syncing = true;
            _scroll.Value = _list.TopIndex * Math.Max(1, _list.ItemHeight);
            _syncing = false;
        }

        private void SyncFromScroll()
        {
            if (_syncing || _list.Items.Count == 0)
                return;

            _syncing = true;
            _list.TopIndex = Math.Clamp(
                _scroll.Value / Math.Max(1, _list.ItemHeight),
                0,
                _list.Items.Count - 1);
            _syncing = false;
        }
    }

    private CheckedListBox CreateExecutableList(IEnumerable<string> executables)
    {
        var list = new CheckedListBox
        {
            CheckOnClick = true,
            IntegralHeight = false,
            HorizontalScrollbar = false,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.InputBackground,
            ForeColor = Color.White,
            Margin = Padding.Empty
        };
        list.BeginUpdate();
        try
        {
            foreach (string executable in executables)
            {
                list.Items.Add(
                    executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? executable
                        : $"{executable}.exe",
                    isChecked: true);
            }
        }
        finally
        {
            list.EndUpdate();
        }

        list.ItemCheck += (_, _) => BeginInvoke((Action)UpdateListSummaries);
        _toolTip.SetToolTip(
            list,
            "Uncheck an executable to remove it from this list when settings are saved.");
        return list;
    }

    private Panel CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new(0, 10, 0, 0),
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new(SizeType.Percent, 32));
        footer.ColumnStyles.Add(new(SizeType.Percent, 40));
        footer.ColumnStyles.Add(new(SizeType.Percent, 28));

        _resetAllButton = CreateButton("Reset all settings");
        _resetAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _resetAllButton.Subtle = true;
        _resetAllButton.ForeColor = SecondaryText;
        _resetAllButton.Margin = Padding.Empty;
        _resetAllButton.Click += (_, _) => ResetAll();
        _supportButton = CreateSupportButton();
        ApplyFooterButtonSizes(_layoutDpi);
        var save = CreateButton("Save");
        var cancel = CreateButton("Cancel");
        cancel.Margin = new(0, 0, 8, 0);
        save.Margin = Padding.Empty;
        save.BorderColor = UiTheme.Accent;
        save.BorderWidth = 2;
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        AcceptButton = save;
        CancelButton = cancel;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        footer.Controls.Add(_resetAllButton, 0, 0);
        footer.Controls.Add(_supportButton, 1, 0);
        footer.Controls.Add(actions, 2, 0);
        return footer;
    }

    private RoundedActionButton CreateSupportButton()
    {
        var support = CreateButton("Buy me a coffee <3");
        support.Anchor = AnchorStyles.Top;
        support.BorderColor = SupportAccent;
        support.Click += (_, _) => OpenSupportLink();
        _toolTip.SetToolTip(support, "Support taskTru on Ko-fi.");
        return support;
    }

    private static void OpenSupportLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/erwanvinot")
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static RoundedActionButton CreateButton(string text) => new()
    {
        Text = text,
        Size = new(76, 28),
        Margin = new(7, 0, 0, 0)
    };

    private void ApplyFooterButtonSizes(int dpi)
    {
        if (_resetAllButton is null || _supportButton is null)
            return;

        ApplyFooterButtonSize(
            _resetAllButton,
            24,
            dpi,
            64);
        ApplyFooterButtonSize(
            _supportButton,
            28,
            dpi,
            34);
    }

    private static void ApplyFooterButtonSize(
        RoundedActionButton button,
        int logicalHeight,
        int dpi,
        int logicalHorizontalPadding)
    {
        int width = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width
            + UiScale.ToDevice(logicalHorizontalPadding, dpi);
        button.Size = new(width, UiScale.ToDevice(logicalHeight, dpi));
        button.MinimumSize = button.Size;
    }

    private CheckBox CreateCheckBox(string text, bool isChecked, string toolTip)
    {
        var checkBox = new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Margin = new(0, 3, 0, 4)
        };
        _toolTip.SetToolTip(checkBox, toolTip);
        return checkBox;
    }

    private void OnStartWithWindowsChanged(object? sender, EventArgs e)
    {
        _startMinimized.Enabled = _startWithWindows.Checked;
        if (!_startMinimized.Enabled)
            _startMinimized.Checked = false;
    }

    private void OnShortcutGestureChanging(object? sender, ShortcutGestureChangingEventArgs e)
    {
        if (sender is not ShortcutCaptureButton input)
            return;

        KeyValuePair<ShortcutAction, ShortcutCaptureButton> duplicate =
            _shortcutInputs.FirstOrDefault(pair =>
                pair.Value != input
                && pair.Value.Gesture == e.Gesture);
        if (duplicate.Value is not null)
        {
            e.Cancel = true;
            string actionName = Shortcuts.All
                .First(shortcut => shortcut.Action == duplicate.Key)
                .ActionName;
            _shortcutStatus.Text =
                $"{Shortcuts.Format(e.Gesture)} is already used by {actionName}.";
            _shortcutStatus.ForeColor = Color.FromArgb(255, 150, 150);
            return;
        }

        _shortcutStatus.Text = $"Shortcut changed to {Shortcuts.Format(e.Gesture)}.";
        _shortcutStatus.ForeColor = Color.FromArgb(150, 210, 255);
    }

    private void ResetShortcuts()
    {
        foreach (ShortcutDefinition shortcut in Shortcuts.All)
            _shortcutInputs[shortcut.Action].SetGesture(shortcut.DefaultGesture);

        _shortcutStatus.Text = "Default shortcuts restored.";
        _shortcutStatus.ForeColor = Color.FromArgb(150, 210, 255);
    }

    private void ResetAll()
    {
        var defaults = new AppSettings();
        _startWithWindows.Checked = defaults.StartWithWindows;
        _startMinimized.Checked = defaults.StartMinimizedToTray;
        _closeToTray.Checked = defaults.CloseButtonMinimizesToTray;
        _restoreTasksOnExit.Checked = defaults.RestoreTasksOnExit;
        _confirmExitWithActiveTasks.Checked = defaults.ConfirmExitWithActiveTasks;
        _autoLockTopMostOnCrop.Checked = defaults.AutoLockTopMostOnCrop;
        _showOpacity.Checked = defaults.ShowOpacityPercentage;
        _updateFlash.Checked = defaults.EnableUpdateFlash;
        _scanForVideoContent.Checked = defaults.ScanForVideoContent;
        _manualTaskRefresh.Checked = defaults.ManualTaskRefresh;
        _compactTaskRows.Checked = defaults.CompactTaskRows;
        _keybinds.Checked = defaults.EnableKeybinds;
        _favoriteTasks.Checked = defaults.EnableFavoriteTasks;
        _ignoredTasks.Checked = defaults.EnableIgnoredTasks;
        _refreshFrequency.Value = defaults.RefreshFrequencySeconds;
        ResetShortcuts();
        _favoriteList.Items.Clear();
        _ignoredList.Items.Clear();
        UpdateTaskSystemState();
        UpdateListSummaries();
    }

    private void UpdateShortcutInputState()
    {
        foreach (ShortcutCaptureButton input in _shortcutInputs.Values)
            input.Enabled = _keybinds.Checked;
    }

    private void UpdateTaskSystemState()
    {
        if (_favoriteListArea is null || _ignoredListArea is null)
            return;

        _favoriteListArea.Enabled = _favoriteTasks.Checked;
        _ignoredListArea.Enabled = _ignoredTasks.Checked;
        UpdateListAppearance(
            _favoriteSurface,
            _favoriteTitle,
            _favoriteEmpty,
            _favoriteList,
            _favoriteTasks.Checked);
        UpdateListAppearance(
            _ignoredSurface,
            _ignoredTitle,
            _ignoredEmpty,
            _ignoredList,
            _ignoredTasks.Checked);
        bool enabled = _favoriteTasks.Checked || _ignoredTasks.Checked;
        _taskDescription.ForeColor = enabled ? SecondaryText : UiTheme.DisabledText;
    }

    private static void UpdateListAppearance(
        RoundedPanel surface,
        Label title,
        Label empty,
        CheckedListBox list,
        bool enabled)
    {
        surface.FillColor = enabled
            ? UiTheme.InputBackground
            : UiTheme.DisabledBackground;
        surface.BorderColor = enabled
            ? UiTheme.Border
            : UiTheme.DisabledBorder;
        title.ForeColor = enabled
            ? SecondaryText
            : UiTheme.DisabledText;
        empty.ForeColor = title.ForeColor;
        list.BackColor = surface.FillColor;
        list.ForeColor = enabled
            ? Color.White
            : UiTheme.DisabledText;
    }

    private void UpdateListSummaries()
    {
        if (_favoriteTitle is null || _ignoredTitle is null)
            return;

        _favoriteTitle.Text =
            $"Manage favorites ({_favoriteList.CheckedItems.Count})";
        _ignoredTitle.Text =
            $"Manage ignore list ({_ignoredList.CheckedItems.Count})";
        _favoriteEmpty.Visible = _favoriteList.Items.Count == 0;
        _ignoredEmpty.Visible = _ignoredList.Items.Count == 0;
        RefreshListScroll(_favoriteList);
        RefreshListScroll(_ignoredList);
    }

    private static void RefreshListScroll(CheckedListBox list) =>
        (list.Parent?.Parent as DarkListHost)?.UpdateScroll();

    private void SaveAndClose()
    {
        Settings = (Settings with
        {
            StartWithWindows = _startWithWindows.Checked,
            StartMinimizedToTray = _startMinimized.Checked,
            CloseButtonMinimizesToTray = _closeToTray.Checked,
            RestoreTasksOnExit = _restoreTasksOnExit.Checked,
            ConfirmExitWithActiveTasks = _confirmExitWithActiveTasks.Checked,
            AutoLockTopMostOnCrop = _autoLockTopMostOnCrop.Checked,
            ShowOpacityPercentage = _showOpacity.Checked,
            EnableUpdateFlash = _updateFlash.Checked,
            ScanForVideoContent = _scanForVideoContent.Checked,
            ManualTaskRefresh = _manualTaskRefresh.Checked,
            CompactTaskRows = _compactTaskRows.Checked,
            EnableKeybinds = _keybinds.Checked,
            KeyboardShortcuts = _shortcutInputs.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value.Gesture),
            EnableFavoriteTasks = _favoriteTasks.Checked,
            EnableIgnoredTasks = _ignoredTasks.Checked,
            FavoriteExecutables = GetCheckedExecutables(_favoriteList),
            IgnoredExecutables = GetCheckedExecutables(_ignoredList),
            RefreshFrequencySeconds = (int)_refreshFrequency.Value
        }).Normalize();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string[] GetCheckedExecutables(CheckedListBox list) =>
        [.. list.CheckedItems.Cast<string>()];

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        ScaleLayoutForDpi(e.DeviceDpiNew);
        BeginInvoke((Action)(() =>
        {
            FitToWorkingArea();
            LayoutSettingsGrid();
        }));
    }

    private void ApplyInitialSize()
    {
        if (_initialSizeApplied || !IsHandleCreated)
            return;

        _initialSizeApplied = true;
        ClientSize = UiScale.ToDevice(new Size(760, 760), DeviceDpi);
    }

    private void ScaleLayoutForDpi(int dpi)
    {
        dpi = Math.Max(UiScale.DefaultDpi, dpi);
        if (dpi == _layoutDpi)
        {
            ApplyFooterButtonSizes(dpi);
            return;
        }

        float scale = dpi / (float)_layoutDpi;
        SuspendLayout();
        _layout.Scale(new SizeF(scale, scale));
        _layoutDpi = dpi;
        _layout.RowStyles[1].Height = UiScale.ToDevice(LogicalFooterHeight, dpi);
        ApplyFooterButtonSizes(dpi);
        ResumeLayout(performLayout: true);
    }

    private void OnSettingsViewportResize(object? sender, EventArgs e) =>
        LayoutSettingsGrid();

    private void OnSettingsScrollChanged(object? sender, EventArgs e)
    {
        if (!_layingOutSettings)
            _settingsGrid.Top = -_settingsScroll.Value;
    }

    private void LayoutSettingsGrid()
    {
        if (!_dpiLayoutReady
            || _layingOutSettings
            || _settingsViewport.ClientSize.Width <= 0)
        {
            return;
        }

        _layingOutSettings = true;
        _settingsGrid.SuspendLayout();
        try
        {
            bool stacked =
                _settingsViewport.ClientSize.Width < UiScale.ToDevice(680, _layoutDpi);
            int gap = UiScale.ToDevice(6, _layoutDpi);
            int leftHeight = UiScale.ToDevice(stacked ? 480 : 470, _layoutDpi);
            int shortcutHeight = UiScale.ToDevice(
                LogicalShortcutChromeHeight + LogicalShortcutRowHeight * Shortcuts.All.Length,
                _layoutDpi);
            int availableWidth = Math.Max(
                1,
                _settingsViewport.ClientSize.Width
                    - (_settingsScroll.Visible ? _settingsScroll.Width : 0));
            bool taskListsStacked = ConfigureManageListsLayout(availableWidth);
            int taskHeight = UiScale.ToDevice(taskListsStacked ? 380 : 206, _layoutDpi);
            int contentHeight;

            _settingsGrid.ColumnStyles.Clear();
            _settingsGrid.RowStyles.Clear();
            _settingsGrid.ColumnCount = stacked ? 1 : 2;
            _settingsGrid.RowCount = stacked ? 3 : 2;

            if (stacked)
            {
                _settingsGrid.ColumnStyles.Add(new(SizeType.Percent, 100));
                _settingsGrid.RowStyles.Add(new(SizeType.Absolute, leftHeight));
                _settingsGrid.RowStyles.Add(new(SizeType.Absolute, shortcutHeight));
                _settingsGrid.RowStyles.Add(new(SizeType.Absolute, taskHeight));
                _settingsGrid.SetCellPosition(_leftSections, new(0, 0));
                _settingsGrid.SetCellPosition(_shortcutSection, new(0, 1));
                _settingsGrid.SetCellPosition(_taskSection, new(0, 2));
                _settingsGrid.SetColumnSpan(_taskSection, 1);
                _leftSections.Dock = DockStyle.Fill;
                _leftSections.Margin = new(0, 0, 0, gap);
                _shortcutSection.Margin = new(0, gap, 0, gap);
                _taskSection.Margin = new(0, gap, 0, 0);
                contentHeight = leftHeight + shortcutHeight + taskHeight;
            }
            else
            {
                _settingsGrid.ColumnStyles.Add(new(SizeType.Percent, 47));
                _settingsGrid.ColumnStyles.Add(new(SizeType.Percent, 53));
                _settingsGrid.RowStyles.Add(new(SizeType.Absolute, Math.Max(leftHeight, shortcutHeight)));
                _settingsGrid.RowStyles.Add(new(SizeType.Absolute, taskHeight));
                _settingsGrid.SetCellPosition(_leftSections, new(0, 0));
                _settingsGrid.SetCellPosition(_shortcutSection, new(1, 0));
                _settingsGrid.SetCellPosition(_taskSection, new(0, 1));
                _settingsGrid.SetColumnSpan(_taskSection, 2);
                _leftSections.Dock = DockStyle.Fill;
                _leftSections.Margin = new(0, 0, gap, 0);
                _shortcutSection.Margin = new(gap, 0, 0, 0);
                _taskSection.Margin = new(0, gap * 2, 0, 0);
                contentHeight = Math.Max(leftHeight, shortcutHeight) + taskHeight;
            }

            _settingsScroll.LargeChange = Math.Max(1, _settingsViewport.ClientSize.Height);
            _settingsScroll.Maximum = Math.Max(0, contentHeight - _settingsViewport.ClientSize.Height);
            _settingsScroll.Visible = _settingsScroll.Maximum > 0;
            int width = Math.Max(
                1,
                _settingsViewport.ClientSize.Width
                    - (_settingsScroll.Visible ? _settingsScroll.Width : 0));
            _settingsGrid.SetBounds(
                0,
                -_settingsScroll.Value,
                width,
                Math.Max(contentHeight, _settingsViewport.ClientSize.Height));
            _settingsScroll.BringToFront();
        }
        finally
        {
            _settingsGrid.ResumeLayout(performLayout: true);
            _layingOutSettings = false;
        }
    }

    private void FitToWorkingArea()
    {
        if (!IsHandleCreated)
            return;

        Rectangle workingArea = Screen.FromHandle(Handle).WorkingArea;
        int margin = UiScale.ToDevice(12, DeviceDpi);
        int maximumWidth = Math.Max(1, workingArea.Width - margin * 2);
        int maximumHeight = Math.Max(1, workingArea.Height - margin * 2);
        Size = new(
            Math.Min(Width, maximumWidth),
            Math.Min(Height, maximumHeight));
        Location = new(
            Math.Clamp(
                Left,
                workingArea.Left + margin,
                Math.Max(workingArea.Left + margin, workingArea.Right - Width - margin)),
            Math.Clamp(
                Top,
                workingArea.Top + margin,
                Math.Max(workingArea.Top + margin, workingArea.Bottom - Height - margin)));
    }
}
