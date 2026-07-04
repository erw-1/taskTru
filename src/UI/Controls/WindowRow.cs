using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace taskTru;

internal sealed class WindowRow
    : TableLayoutPanel
{
    private const int RowHeight = 40;
    private const int CompactRowHeight = 32;
    private const int WindowHorizontalInset = 10;
    private const int WindowStateColumnWidth = 90;
    private const int WindowOpacityColumnWidth = 132;
    private const int WindowOpacityValueWidth = 42;
    private const int WindowActionsColumnWidth = 136;
    private const int WindowCropColumnWidth = 76;
    private const int WindowActionSize = 28;
    private const int FlashDurationMs = 600;
    private const int FlashTimerIntervalMs = 30;
    internal const int WindowIconSize = 20;
    private const int WindowIdentityGap = 8;
    private const uint AbortIfHung = 0x0002;

    private readonly CheckBox _clickThrough;
    private readonly CheckBox _topMost;
    private readonly RoundedSlider _opacity;
    private readonly Label _opacityValue;
    private readonly TableLayoutPanel _opacityCell;
    private readonly RoundedActionButton _crop;
    private readonly VideoActionButton _video;
    private readonly ResetActionButton _reset;
    private readonly TaskListToggleButton _favorite;
    private readonly TaskListToggleButton _ignore;
    private readonly WindowIdentityCell _windowCell;
    private readonly TableLayoutPanel _identityArea;
    private readonly ToolTip _toolTip = UiToolTips.Create(5000);
    private readonly Action<WindowRow> _stateChanged;
    private readonly Action<WindowRow> _cropRequested;
    private readonly Action<WindowRow> _videoRequested;
    private readonly Action<WindowRow> _resetRequested;
    private readonly Action<WindowRow> _favoriteRequested;
    private readonly Action<WindowRow> _ignoreRequested;
    private readonly Action<WindowRow> _focusRequested;
    private readonly int _initialDpi;
    private System.Windows.Forms.Timer? _flashTimer;
    private readonly Stopwatch _flashClock = new();

    private Color _background;
    private bool _flashRow;
    private bool _flashTitle;
    private bool _isCropped;
    private bool _videoDetected;
    private bool _videoFeatureEnabled;
    private bool _favoriteVisible = true;
    private bool _ignoreVisible = true;
    private bool _compactRows;
    private bool _suppressStateChanged;

    public WindowInfo Window { get; private set; }

    public WindowState State => new(
        _clickThrough.Checked,
        _topMost.Checked,
        _opacity.Value);

    public WindowRow(
        WindowInfo window,
        WindowState state,
        int index,
        bool isCropped,
        bool videoDetected,
        bool isTaskActive,
        bool isFavorite,
        bool isIgnored,
        bool favoriteTasksEnabled,
        bool ignoredTasksEnabled,
        bool showOpacityPercentage,
        bool videoFeatureEnabled,
        bool compactRows,
        int dpi,
        Action<WindowRow> stateChanged,
        Action<WindowRow> cropRequested,
        Action<WindowRow> videoRequested,
        Action<WindowRow> resetRequested,
        Action<WindowRow> favoriteRequested,
        Action<WindowRow> ignoreRequested,
        Action<WindowRow> focusRequested)
    {
        Window = window;
        _isCropped = isCropped;
        _videoDetected = videoDetected;
        _videoFeatureEnabled = videoFeatureEnabled;
        _compactRows = compactRows;
        _stateChanged = stateChanged;
        _cropRequested = cropRequested;
        _videoRequested = videoRequested;
        _resetRequested = resetRequested;
        _favoriteRequested = favoriteRequested;
        _ignoreRequested = ignoreRequested;
        _focusRequested = focusRequested;
        _initialDpi = Math.Max(UiScale.DefaultDpi, dpi);
        _background = RowBackground(index);
        BackColor = _background;

        ColumnCount = 5;
        RowCount = 1;
        Height = Scale(LogicalRowHeight(_compactRows));
        Dock = DockStyle.Top;
        Padding = RowPadding();
        Margin = Padding.Empty;
        ConfigureWindowColumns(
            this,
            _initialDpi,
            _videoFeatureEnabled);
        RowStyles.Add(new(SizeType.Percent, 100));

        _windowCell = new WindowIdentityCell(
            window.Title,
            TryGetWindowIcon(window.Handle, _initialDpi))
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        _windowCell.Click += (_, _) => _focusRequested(this);
        _favorite = new(TaskListToggleKind.Favorite)
        {
            Dock = DockStyle.Fill,
            Margin = new(Scale(2), 0, Scale(2), 0)
        };
        _toolTip.SetToolTip(_favorite, FavoriteToolTipText);
        _favorite.Click += (_, _) => _favoriteRequested(this);
        _ignore = new(TaskListToggleKind.Ignore)
        {
            Dock = DockStyle.Fill,
            Margin = new(Scale(2), 0, Scale(2), 0)
        };
        _toolTip.SetToolTip(_ignore, IgnoreToolTipText);
        _ignore.Click += (_, _) => _ignoreRequested(this);
        _identityArea = CreateIdentityArea();
        Controls.Add(_identityArea, 0, 0);
        SetFavoriteActive(isFavorite);
        SetFavoriteVisible(favoriteTasksEnabled);
        SetIgnoredActive(isIgnored);
        SetIgnoredVisible(ignoredTasksEnabled);
        UpdateTaskToggleColumns();

        _clickThrough = CreateCheckBox(
            state.ClickThrough,
            _initialDpi);
        _toolTip.SetToolTip(
            _clickThrough,
            "Let pointer input pass through this window.");
        _topMost = CreateCheckBox(
            state.TopMost,
            _initialDpi);
        _toolTip.SetToolTip(
            _topMost,
            "Keep this window above other windows.");
        Controls.Add(_clickThrough, 1, 0);
        Controls.Add(_topMost, 2, 0);

        int opacity = Math.Clamp(state.Opacity, 0, 100);
        _opacity = new RoundedSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = opacity,
            Height = Scale(_compactRows ? 18 : 22),
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new(
                Scale(WindowIdentityGap),
                0,
                Scale(WindowIdentityGap),
                0)
        };
        _toolTip.SetToolTip(
            _opacity,
            "Adjust this window's opacity.");
        _opacityValue = new Label
        {
            Text = $"{opacity}%",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Visible = showOpacityPercentage
        };
        _toolTip.SetToolTip(
            _opacityValue,
            "Current window opacity.");
        _opacity.Dock = DockStyle.Fill;
        _opacityCell = CreateOpacityCell();
        Controls.Add(_opacityCell, 3, 0);
        SetShowOpacityPercentage(showOpacityPercentage);

        _crop = new RoundedActionButton
        {
            Text = _isCropped
                ? "Uncrop"
                : "Crop",
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            Margin = CropMargin()
        };
        _toolTip.SetToolTip(_crop, CropToolTipText);

        _video = CreateWindowAction<VideoActionButton>();
        _toolTip.SetToolTip(_video, "Attempt to crop directly to the detected video.");
        _reset = CreateWindowAction<ResetActionButton>();
        _toolTip.SetToolTip(
            _reset,
            "Restore this window to its state before taskTru changed it.");
        _video.Click += (_, _) => _videoRequested(this);
        _reset.Visible = isTaskActive;
        UpdateVideoButton();
        Controls.Add(CreateActionArea(), 4, 0);

        _clickThrough.CheckedChanged += (_, _) => OnStateChanged();
        _topMost.CheckedChanged += (_, _) => OnStateChanged();
        _opacity.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = $"{_opacity.Value}%";
            OnStateChanged();
        };
        _crop.Click += (_, _) => _cropRequested(this);
        _reset.Click += (_, _) => _resetRequested(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flashTimer?.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    public bool UpdateWindow(WindowInfo window, int index)
    {
        bool titleChanged = Window.Title != window.Title;
        Window = window;
        if (titleChanged)
        {
            _windowCell.Text = window.Title;
            _windowCell.Invalidate();
        }

        SetIndex(index);
        return titleChanged;
    }

    public void SetState(WindowState state)
    {
        int opacity = Math.Clamp(state.Opacity, 0, 100);
        if (_clickThrough.Checked == state.ClickThrough
            && _topMost.Checked == state.TopMost
            && _opacity.Value == opacity)
        {
            return;
        }

        try
        {
            _suppressStateChanged = true;
            if (_clickThrough.Checked != state.ClickThrough)
                _clickThrough.Checked = state.ClickThrough;
            if (_topMost.Checked != state.TopMost)
                _topMost.Checked = state.TopMost;
            if (_opacity.Value != opacity)
                _opacity.Value = opacity;
        }
        finally
        {
            _suppressStateChanged = false;
        }
    }

    public void SetShowOpacityPercentage(bool visible)
    {
        int width = visible
            ? Scale(WindowOpacityValueWidth)
            : 0;
        if (_opacityValue.Visible == visible
            && _opacityCell.ColumnStyles[1].Width == width)
        {
            return;
        }

        _opacityValue.Visible = visible;
        _opacityCell.ColumnStyles[1].Width = width;
    }

    public void SetCompactRows(bool compactRows)
    {
        if (_compactRows == compactRows)
            return;

        _compactRows = compactRows;
        Height = Scale(LogicalRowHeight(_compactRows));
        Padding = RowPadding();
        _opacity.Height = Scale(_compactRows ? 18 : 22);
        _crop.Margin = CropMargin();
        _video.Margin = WindowActionMargin();
        _reset.Margin = WindowActionMargin();
    }

    public void SetCropActive(bool isCropped)
    {
        string text = isCropped
            ? "Uncrop"
            : "Crop";
        if (_isCropped == isCropped
            && _crop.Text == text)
        {
            return;
        }

        _isCropped = isCropped;
        _crop.Text = text;
        _toolTip.SetToolTip(_crop, CropToolTipText);
        UpdateVideoButton();
        _crop.Invalidate();
    }

    public void SetVideoDetected(bool detected)
    {
        if (_videoDetected == detected)
            return;

        _videoDetected = detected;
        UpdateVideoButton();
    }

    public void SetVideoFeatureEnabled(bool enabled)
    {
        bool changed = _videoFeatureEnabled != enabled;
        int width = Scale(ActionColumnWidth(enabled));
        bool widthChanged = ColumnStyles[4].Width != width;
        if (!changed && !widthChanged)
            return;

        _videoFeatureEnabled = enabled;
        ColumnStyles[4].Width = width;
        if (changed)
            UpdateVideoButton();
    }

    public void SetTaskActive(bool isActive)
    {
        if (_reset.Visible == isActive)
            return;

        _reset.Visible = isActive;
    }

    public void SetFavoriteActive(bool active)
    {
        if (_favorite.Active == active)
            return;

        _favorite.Active = active;
        _toolTip.SetToolTip(_favorite, FavoriteToolTipText);
    }

    public void SetFavoriteVisible(bool visible)
    {
        if (_favoriteVisible == visible)
            return;

        _favoriteVisible = visible;
        _favorite.Visible = visible;
        UpdateTaskToggleColumns();
    }

    public void SetIgnoredActive(bool active)
    {
        if (_ignore.Active == active)
            return;

        _ignore.Active = active;
        _toolTip.SetToolTip(_ignore, IgnoreToolTipText);
    }

    public void SetIgnoredVisible(bool visible)
    {
        if (_ignoreVisible == visible)
            return;

        _ignoreVisible = visible;
        _ignore.Visible = visible;
        UpdateTaskToggleColumns();
    }

    public void FlashAccent()
    {
        _flashRow = true;
        StartFlash();
    }

    public void FlashTitleAccent()
    {
        _flashTitle = true;
        StartFlash();
    }

    private void StartFlash()
    {
        if (_flashTimer is null)
        {
            _flashTimer = new()
            {
                Interval = FlashTimerIntervalMs
            };
            _flashTimer.Tick += (_, _) => UpdateFlash();
        }

        _flashClock.Restart();
        UpdateFlash();
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void UpdateFlash()
    {
        double progress = _flashClock.Elapsed.TotalMilliseconds / FlashDurationMs;
        if (progress >= 1)
        {
            _flashTimer?.Stop();
            _flashClock.Reset();
            _flashRow = _flashTitle = false;
            SetRowBackColor(_background);
            SetTitleColor(Color.White);
            return;
        }

        float amount = (float)(progress < 0.5
            ? progress * 2
            : (1 - progress) * 2);
        SetRowBackColor(
            _flashRow
                ? ColorMath.Blend(_background, UiTheme.AccentFlash, amount * 0.32f)
                : _background);
        SetTitleColor(
            _flashTitle
                ? ColorMath.Blend(Color.White, UiTheme.AccentFlash, amount)
                : Color.White);
    }

    private void OnStateChanged()
    {
        if (!_suppressStateChanged)
            _stateChanged(this);
    }

    private void SetIndex(int index)
    {
        Color background = RowBackground(index);
        if (_background == background)
            return;

        _background = background;
        if (_flashTimer?.Enabled != true)
            SetRowBackColor(_background);
    }

    private void SetRowBackColor(Color color)
    {
        if (BackColor == color)
            return;

        BackColor = color;
        if (Parent?.Parent is RoundedViewport viewport)
            viewport.UpdateBoundary();
    }

    private void SetTitleColor(Color color)
    {
        _windowCell.ForeColor = color;
        _windowCell.Invalidate();
    }

    private static Color RowBackground(int index) =>
        index % 2 == 0
            ? UiTheme.RowPrimary
            : UiTheme.RowAlternate;

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RefreshWindowIcon();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshWindowIcon();
    }

    private static CheckBox CreateCheckBox(
        bool isChecked,
        int dpi) => new()
        {
            Checked = isChecked,
            AutoSize = false,
            Size = UiScale.ToDevice(new Size(18, 18), dpi),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.None,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            CheckAlign = ContentAlignment.MiddleCenter
        };

    private T CreateWindowAction<T>()
        where T : IconActionButton, new() =>
        new()
        {
            Size = new(Scale(WindowActionSize), Scale(WindowActionSize)),
            Margin = WindowActionMargin()
        };

    private Padding RowPadding() => new(
        Scale(WindowHorizontalInset),
        Scale(_compactRows ? 1 : 2),
        Scale(WindowHorizontalInset),
        Scale(_compactRows ? 1 : 2));

    private Padding CropMargin() => new(
        Scale(4),
        Scale(_compactRows ? 2 : 5),
        Scale(4),
        Scale(_compactRows ? 2 : 5));

    private Padding WindowActionMargin() => new(
        0,
        Scale(_compactRows ? 2 : 4),
        0,
        Scale(_compactRows ? 2 : 4));

    private TableLayoutPanel CreateActionArea()
    {
        var area = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        area.ColumnStyles.Add(new(
            SizeType.Absolute,
            Scale(WindowCropColumnWidth)));
        area.ColumnStyles.Add(new(
            SizeType.Percent,
            100));
        area.RowStyles.Add(new(
            SizeType.Percent,
            100));
        area.Controls.Add(_crop, 0, 0);

        var icons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new(Scale(2), 0, 0, 0),
            BackColor = Color.Transparent
        };
        icons.Controls.Add(_video);
        icons.Controls.Add(_reset);
        area.Controls.Add(icons, 1, 0);
        return area;
    }

    private void UpdateVideoButton()
    {
        bool visible =
            _videoFeatureEnabled
            && _videoDetected
            && !_isCropped;
        if (_video.Visible == visible && _video.Enabled == visible)
            return;

        _video.Visible = visible;
        _video.Enabled = visible;
    }

    private string CropToolTipText =>
        _isCropped
            ? "Close the cropped view and restore the full window."
            : "Select a region to show in a cropped window.";

    private string FavoriteToolTipText =>
        _favorite.Active
            ? $"Remove {ExecutableDisplayName()} from favorites."
            : $"Favorite every {ExecutableDisplayName()} task.";

    private string IgnoreToolTipText =>
        _ignore.Active
            ? $"Remove {ExecutableDisplayName()} from the ignore list."
            : $"Add {ExecutableDisplayName()} to the ignore list.";

    private string ExecutableDisplayName()
    {
        string name = Path.GetFileName(Window.ProcessName.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : $"{name}.exe";
    }

    private TableLayoutPanel CreateIdentityArea()
    {
        var area = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        area.ColumnStyles.Add(new(SizeType.Absolute, 0));
        area.ColumnStyles.Add(new(SizeType.Absolute, 0));
        area.ColumnStyles.Add(new(SizeType.Absolute, 0));
        area.ColumnStyles.Add(new(
            SizeType.Percent,
            100));
        area.RowStyles.Add(new(
            SizeType.Percent,
            100));
        area.Controls.Add(_favorite, 0, 0);
        area.Controls.Add(_ignore, 1, 0);
        area.Controls.Add(_windowCell, 3, 0);
        return area;
    }

    private TableLayoutPanel CreateOpacityCell()
    {
        var cell = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        cell.ColumnStyles.Add(new(SizeType.Percent, 100));
        cell.ColumnStyles.Add(new(SizeType.Absolute, Scale(WindowOpacityValueWidth)));
        cell.RowStyles.Add(new(SizeType.Percent, 100));
        cell.Controls.Add(_opacity, 0, 0);
        cell.Controls.Add(_opacityValue, 1, 0);
        return cell;
    }

    private void UpdateTaskToggleColumns()
    {
        int width = Scale(WindowActionSize);
        _identityArea.ColumnStyles[0].Width = _favoriteVisible ? width : 0;
        _identityArea.ColumnStyles[1].Width = _ignoreVisible ? width : 0;
        _identityArea.ColumnStyles[2].Width =
            _favoriteVisible || _ignoreVisible
                ? Scale(WindowIdentityGap)
                : 0;
    }

    private void RefreshWindowIcon()
    {
        Bitmap? icon = TryGetWindowIcon(Window.Handle, DeviceDpi);
        if (icon is not null)
            _windowCell.SetIcon(icon);
    }

    internal static Bitmap? TryGetWindowIcon(
        nint handle,
        int dpi,
        int logicalSize = WindowIconSize)
    {
        nint iconHandle = GetWindowIconHandle(handle);
        if (iconHandle == 0)
            return null;

        try
        {
            using Icon icon = (Icon)Icon.FromHandle(iconHandle).Clone();
            using Bitmap source = icon.ToBitmap();
            int iconSize = UiScale.ToDevice(logicalSize, dpi);
            var result = new Bitmap(iconSize, iconSize);
            using Graphics graphics = Graphics.FromImage(result);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, iconSize, iconSize));
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static nint GetWindowIconHandle(nint handle)
    {
        // Rows draw at 20px and beyond; the 32px big icon downscales crisply while
        // the 16px small icons blur when upscaled.
        foreach (int size in new[]
                 {
                     NativeMethods.IconBig,
                     NativeMethods.IconSmall2,
                     NativeMethods.IconSmall
                 })
        {
            _ = NativeMethods.SendMessageTimeout(
                handle,
                NativeMethods.WindowMessageGetIcon,
                size,
                0,
                AbortIfHung,
                100,
                out nint icon);
            if (icon != 0)
                return icon;
        }

        nint classIcon = NativeMethods.GetClassLongPtr(
            handle,
            NativeMethods.ClassIcon);
        return classIcon != 0
            ? classIcon
            : NativeMethods.GetClassLongPtr(
                handle,
                NativeMethods.ClassSmallIcon);
    }

    internal static int ActionColumnWidth(bool videoFeatureEnabled) =>
        WindowActionsColumnWidth
        - (videoFeatureEnabled ? 0 : WindowActionSize);

    internal static int LogicalRowHeight(bool compactRows) =>
        compactRows ? CompactRowHeight : RowHeight;

    private static void ConfigureWindowColumns(
        TableLayoutPanel layout,
        int dpi,
        bool videoFeatureEnabled)
    {
        layout.ColumnStyles.Add(new(
            SizeType.Percent,
            100));
        foreach (int width in new[]
                 {
                     WindowStateColumnWidth,
                     WindowStateColumnWidth,
                     WindowOpacityColumnWidth,
                     ActionColumnWidth(videoFeatureEnabled)
                 })
        {
            layout.ColumnStyles.Add(new(
                SizeType.Absolute,
                UiScale.ToDevice(width, dpi)));
        }
    }

    private int Scale(int logicalPixels) =>
        UiScale.ToDevice(
            logicalPixels,
            IsHandleCreated
                ? DeviceDpi
                : _initialDpi);
}
