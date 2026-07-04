using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using static taskTru.NativeMethods;

namespace taskTru;

// Tray icon, tray menus, and tray theming for MainForm.
public sealed partial class MainForm
{
    private (NotifyIcon Icon, ToolStripMenuItem WindowsItem) CreateTrayIcon()
    {
        var menu = new ContextMenuStrip
        {
            ShowItemToolTips = false
        };
        menu.Opening += (_, _) =>
        {
            SetCropsInteractionFocusPaused(true);
            RebuildTrayMenus();
            ApplyTrayMenuTheme(menu);
        };
        menu.Closed += (_, _) => SetCropsInteractionFocusPaused(false);
        var showItem = menu.Items.Add(
            "Show taskTru",
            null,
            (_, _) => RestoreFromTray());
        showItem.Font = new(
            showItem.Font,
            FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());
        var windowsItem = new ToolStripMenuItem("Windows");
        menu.Items.Add(windowsItem);
        menu.Items.Add(new ToolStripSeparator());
        ToolStripItem restoreItem = menu.Items.Add(
            "Restore everything",
            null,
            (_, _) => RestoreAllWindows());
        ToolStripItem settingsItem = menu.Items.Add(
            "Settings",
            null,
            (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripItem exitItem = menu.Items.Add(
            "Exit",
            null,
            (_, _) =>
            {
                _allowExit = true;
                Close();
            });

        var trayIcon = new NotifyIcon
        {
            Text = "taskTru - no active windows",
            Icon = _trayBaseIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        trayIcon.MouseDown += (_, e) =>
        {
            CaptureForegroundWindow(GetForegroundWindow());
            if (e.Button == MouseButtons.Left)
                RestoreFromTray();
        };
        ApplyTrayMenuTheme(menu);
        return (trayIcon, windowsItem);
    }

    private void RebuildTrayMenus()
    {
        ApplyTrayState();
        CaptureForegroundWindow(GetForegroundWindow());
        RebuildTrayWindowMenu();
        RequestFreshVideoScan();
    }

    private void SetCropsInteractionFocusPaused(bool paused)
    {
        foreach (CropSession session in _crops.Values)
            session.Crop.SetInteractionFocusPaused(paused);
    }

    private void RebuildTrayWindowMenu()
    {
        ClearDropDownItems(_windowsTrayItem.DropDownItems);
        List<WindowInfo> windows =
            FilterAndSortWindows(IncludeCroppedWindows(
                WindowManager.Enumerate(
                    Handle)));
        int activeWindowCount = windows.Count(window =>
            IsWindowTaskActive(window.Handle));
        _windowsTrayItem.Text = activeWindowCount switch
        {
            0 => "Windows",
            1 => "Windows (1 active)",
            _ => $"Windows ({activeWindowCount} active)"
        };
        _windowsTrayItem.Tag = new TrayMenuItemState(
            AccentSuffixLength: activeWindowCount > 0
                ? _windowsTrayItem.Text.Length - "Windows ".Length
                : 0);
        _windowsTrayItem.Enabled = windows.Count > 0;

        if (windows.Count == 0)
        {
            _windowsTrayItem.DropDownItems.Add(
                new ToolStripMenuItem("No windows available")
                {
                    Enabled = false
                });
            return;
        }

        foreach (WindowInfo window in windows.Take(MaximumTrayWindows))
        {
            var item = new ToolStripMenuItem(CompactTitle(window.Title))
            {
                // Menus draw 16px images; producing a 20px bitmap here would get
                // resampled a second time and blur.
                Image = WindowRow.TryGetWindowIcon(window.Handle, _layoutDpi, 16),
                Tag = new TrayMenuItemState(
                    ActiveBullet: IsWindowTaskActive(window.Handle))
            };
            AddTrayWindowActions(item, window);
            _windowsTrayItem.DropDownItems.Add(item);
        }

        if (windows.Count > MaximumTrayWindows)
        {
            _windowsTrayItem.DropDownItems.Add(new ToolStripSeparator());
            _windowsTrayItem.DropDownItems.Add(
                new ToolStripMenuItem(
                    "More...",
                    null,
                    (_, _) => RestoreFromTray()));
        }
    }

    private static void ClearDropDownItems(
        ToolStripItemCollection items)
    {
        while (items.Count > 0)
        {
            ToolStripItem item = items[0];
            items.RemoveAt(0);
            item.Image?.Dispose();
            item.Image = null;
            item.Dispose();
        }
    }

    private void AddTrayWindowActions(
        ToolStripMenuItem parent,
        WindowInfo window)
    {
        WindowState state = GetWindowState(window);
        bool isCropped = IsCropped(window.Handle);
        bool hasVideo =
            _settings.ScanForVideoContent
            && TryGetCachedVideoBounds(window, out _);
        parent.DropDownItems.Add(
            CreateCheckedAction(
                "Click-through",
                state.ClickThrough,
                (_, _) => ApplyTrayWindowState(
                    window,
                    state with
                    {
                        ClickThrough = !state.ClickThrough
                    })));
        parent.DropDownItems.Add(
            CreateCheckedAction(
                "Lock on top",
                state.TopMost,
                (_, _) => ApplyTrayWindowState(
                    window,
                    state with
                    {
                        TopMost = !state.TopMost
                    })));
        parent.DropDownItems.Add(
            new ToolStripMenuItem(
                isCropped ? "Uncrop" : "Crop",
                null,
                (_, _) => OnTrayCropClick(window)));
        if (hasVideo)
        {
            parent.DropDownItems.Add(
                new ToolStripMenuItem(
                    "Attempt auto video crop",
                    null,
                    (_, _) => StartVideoCrop(
                        window,
                        GetWindowState(window)))
                {
                    Enabled = !isCropped
                });
        }

        parent.DropDownItems.Add(new ToolStripSeparator());

        var opacity = new ToolStripMenuItem("Opacity");
        foreach (int value in new[] { 100, 75, 50, 25 })
        {
            int opacityValue = value;
            var opacityItem = new ToolStripMenuItem(
                $"{opacityValue}%",
                null,
                (_, _) => ApplyTrayWindowState(
                    window,
                    GetWindowState(window) with
                    {
                        Opacity = opacityValue
                    }))
            {
                Checked = state.Opacity == opacityValue
            };
            opacity.DropDownItems.Add(opacityItem);
        }

        parent.DropDownItems.Add(opacity);
        parent.DropDownItems.Add(new ToolStripSeparator());
        parent.DropDownItems.Add(
            new ToolStripMenuItem(
                "Reset window",
                null,
                (_, _) => ResetWindow(window))
            {
                Enabled = isCropped || IsTrayWindowManaged(window)
            });
    }

    private void OnTrayCropClick(WindowInfo window)
    {
        if (IsCropped(window.Handle))
            StopCrop(window.Handle);
        else
            _ = StartCrop(window, GetWindowState(window));
    }

    private static ToolStripMenuItem CreateCheckedAction(
        string text,
        bool isChecked,
        EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = isChecked
        };
        item.Click += onClick;
        return item;
    }

    private static string CompactTitle(string title) =>
        title.Length <= MaximumTrayTitleLength
            ? title
            : string.Concat(
                title.AsSpan(
                    0,
                MaximumTrayTitleLength - 3),
                "...");

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _refreshTimer.Interval = Math.Max(
            RefreshIntervalMilliseconds,
            10000);
    }

    private void RestoreFromTray()
    {
        _refreshTimer.Interval = RefreshIntervalMilliseconds;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        _ = SetForegroundWindow(Handle);
        ReconcileWindows();
    }

    private void ApplyTrayState(bool force = false)
    {
        int activeWindowCount = ActiveTaskCount();
        bool lightMode = IsLightMode();
        bool themeChanged = _appliedLightMode != lightMode;
        bool activeCountChanged =
            _appliedActiveWindowCount != activeWindowCount;
        if (!force
            && !themeChanged
            && !activeCountChanged)
            return;

        if (force || themeChanged)
        {
            if (IsHandleCreated)
                WindowTheme.Apply(Handle);

            if (_trayIcon.ContextMenuStrip is { } menu)
                ApplyTrayMenuTheme(menu);
        }

        if (force || activeCountChanged)
        {
            _trayIcon.Icon = activeWindowCount > 0
                ? _activeTrayIcon
                : _trayBaseIcon;
        }

        _trayIcon.Text = activeWindowCount switch
        {
            0 => "taskTru - no active windows",
            1 => "taskTru - 1 active window",
            _ => $"taskTru - {activeWindowCount} active windows"
        };
        _appliedLightMode = lightMode;
        _appliedActiveWindowCount = activeWindowCount;
    }

    private int ActiveTaskCount() =>
        _crops.Keys
            .Concat(WindowManager.TrackedHandles)
            .Distinct()
            .Count(handle =>
                IsWindow(handle)
                && IsWindowTaskActive(handle));

    private void OnUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke((Action)(() => ApplyTrayState(force: true)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsLightMode()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int theme
                && theme != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyTrayMenuTheme(ToolStripDropDown menu)
    {
        if (SystemInformation.HighContrast)
        {
            ApplySystemTrayMenuTheme(menu);
            return;
        }

        menu.Renderer = TrayMenuRenderer.Instance;
        menu.BackColor = UiTheme.AppBackground;
        menu.ForeColor = Color.White;
        foreach (ToolStripItem item in menu.Items)
        {
            item.ToolTipText = string.Empty;
            item.BackColor = UiTheme.AppBackground;
            item.ForeColor = item.Enabled
                ? Color.White
                : UiTheme.DisabledText;
            if (item is ToolStripMenuItem menuItem
                && menuItem.HasDropDownItems)
                ApplyTrayMenuTheme(menuItem.DropDown);
        }
    }

    private static void ApplySystemTrayMenuTheme(ToolStripDropDown menu)
    {
        menu.Renderer = new ToolStripSystemRenderer();
        menu.BackColor = SystemColors.Menu;
        menu.ForeColor = SystemColors.MenuText;
        foreach (ToolStripItem item in menu.Items)
        {
            item.ToolTipText = string.Empty;
            item.BackColor = SystemColors.Menu;
            item.ForeColor = item.Enabled
                ? SystemColors.MenuText
                : SystemColors.GrayText;
            if (item is ToolStripMenuItem menuItem
                && menuItem.HasDropDownItems)
                ApplySystemTrayMenuTheme(menuItem.DropDown);
        }
    }

    private sealed record TrayMenuItemState(
        bool ActiveBullet = false,
        int AccentSuffixLength = 0);

    private sealed class TrayMenuRenderer()
        : ToolStripProfessionalRenderer(new TrayMenuColorTable())
    {
        internal static readonly TrayMenuRenderer Instance = new();

        protected override void OnRenderToolStripBackground(
            ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(UiTheme.AppBackground);
        }

        protected override void OnRenderImageMargin(
            ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(UiTheme.AppBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(
            ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled
                || (!e.Item.Selected
                    && e.Item is not ToolStripMenuItem
                    {
                        DropDown.Visible: true
                    }))
            {
                return;
            }

            int dpi = e.ToolStrip?.DeviceDpi ?? UiScale.DefaultDpi;
            Rectangle bounds = new(Point.Empty, e.Item.Size);
            bounds.Inflate(
                -UiScale.ToDevice(2, dpi),
                -UiScale.ToDevice(1, dpi));
            using var brush = new SolidBrush(
                e.Item.Pressed
                    ? UiTheme.ButtonPressed
                    : UiTheme.RowPrimary);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderItemCheck(
            ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            int dpi = e.ToolStrip?.DeviceDpi ?? UiScale.DefaultDpi;
            int inset = UiScale.ToDevice(5, dpi);
            Point[] points =
            [
                new(bounds.Left + inset, bounds.Top + bounds.Height / 2),
                new(bounds.Left + bounds.Width / 2 - UiScale.ToDevice(1, dpi), bounds.Bottom - inset),
                new(bounds.Right - inset, bounds.Top + inset)
            ];
            using var pen = new Pen(
                e.Item.Enabled ? Color.Gainsboro : UiTheme.DisabledText,
                UiScale.ToDevice(2f, dpi))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, points);
        }

        protected override void OnRenderItemText(
            ToolStripItemTextRenderEventArgs e)
        {
            Color textColor = !e.Item.Enabled
                ? UiTheme.DisabledText
                : Color.White;
            string text = e.Text ?? string.Empty;
            Rectangle textBounds = e.TextRectangle;
            if (e.Item.Tag is TrayMenuItemState { ActiveBullet: true })
            {
                int bulletWidth = UiScale.ToDevice(
                    14,
                    e.Item.Owner?.DeviceDpi ?? UiScale.DefaultDpi);
                TextRenderer.DrawText(
                    e.Graphics,
                    "\u2022",
                    e.TextFont,
                    new Rectangle(
                        textBounds.X,
                        textBounds.Y,
                        bulletWidth,
                        textBounds.Height),
                    UiTheme.Accent,
                    e.TextFormat);
                textBounds.X += bulletWidth;
                textBounds.Width = Math.Max(
                    0,
                    textBounds.Width - bulletWidth);
            }

            if (e.Item.Tag is TrayMenuItemState
                {
                    AccentSuffixLength: > 0
                } suffixState
                && text.Length > suffixState.AccentSuffixLength)
            {
                string prefix = text[..^suffixState.AccentSuffixLength];
                string suffix = text[^suffixState.AccentSuffixLength..];
                TextRenderer.DrawText(
                    e.Graphics,
                    prefix,
                    e.TextFont,
                    textBounds,
                    textColor,
                    e.TextFormat);
                int prefixWidth = TextRenderer.MeasureText(
                    e.Graphics,
                    prefix,
                    e.TextFont,
                    new Size(int.MaxValue, textBounds.Height),
                    e.TextFormat | TextFormatFlags.NoPadding).Width;
                textBounds.X += prefixWidth;
                textBounds.Width = Math.Max(
                    0,
                    textBounds.Width - prefixWidth);
                TextRenderer.DrawText(
                    e.Graphics,
                    suffix,
                    e.TextFont,
                    textBounds,
                    UiTheme.Accent,
                    e.TextFormat);
                return;
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                e.TextFont,
                textBounds,
                textColor,
                e.TextFormat);
        }

        protected override void OnRenderArrow(
            ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled != false
                ? UiTheme.SecondaryText
                : UiTheme.DisabledText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(
            ToolStripSeparatorRenderEventArgs e)
        {
            int dpi = e.ToolStrip?.DeviceDpi ?? UiScale.DefaultDpi;
            int y = e.Item.Height / 2;
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(
                pen,
                UiScale.ToDevice(30, dpi),
                y,
                Math.Max(
                    UiScale.ToDevice(34, dpi),
                    e.Item.Width - UiScale.ToDevice(4, dpi)),
                y);
        }
    }

    private sealed class TrayMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground =>
            UiTheme.AppBackground;

        public override Color ImageMarginGradientBegin =>
            UiTheme.AppBackground;

        public override Color ImageMarginGradientMiddle =>
            UiTheme.AppBackground;

        public override Color ImageMarginGradientEnd =>
            UiTheme.AppBackground;

        public override Color MenuBorder =>
            UiTheme.Border;

        public override Color MenuItemBorder =>
            UiTheme.Accent;

        public override Color MenuItemSelected =>
            UiTheme.ButtonHover;

        public override Color MenuItemSelectedGradientBegin =>
            UiTheme.ButtonHover;

        public override Color MenuItemSelectedGradientEnd =>
            UiTheme.ButtonHover;

        public override Color MenuItemPressedGradientBegin =>
            UiTheme.ButtonPressed;

        public override Color MenuItemPressedGradientMiddle =>
            UiTheme.ButtonPressed;

        public override Color MenuItemPressedGradientEnd =>
            UiTheme.ButtonPressed;

        public override Color CheckBackground =>
            UiTheme.ButtonBackground;

        public override Color CheckSelectedBackground =>
            UiTheme.ButtonHover;

        public override Color CheckPressedBackground =>
            UiTheme.ButtonPressed;

        public override Color SeparatorDark =>
            UiTheme.Border;

        public override Color SeparatorLight =>
            UiTheme.AppBackground;
    }
}
