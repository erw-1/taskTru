using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using static taskTru.NativeMethods;

namespace taskTru;

internal sealed class ThumbnailCropForm : Form
{
    // Only one interacting crop may drive foreground focus; otherwise their timers fight and flash the taskbar.
    private static ThumbnailCropForm? s_interactionFocusOwner;

    private const int ResizeBorder = 7;
    private const int ThickFrameStyle = 0x00040000;
    private const int WindowMessageNcCalcSize = 0x0083;
    private const int WindowMessageNcHitTest = 0x0084;
    private const int WindowMessageMouseActivate = 0x0021;
    private const int WindowMessageSizing = 0x0214;
    private const int HitTransparent = -1;
    private const int MouseActivateNoActivate = 3;
    private const int DefaultDpi = 96;
    private const uint AbortIfHung = 0x0002;
    private const int SizingLeft = 1;
    private const int SizingRight = 2;
    private const int SizingTop = 3;
    private const int SizingTopLeft = 4;
    private const int SizingTopRight = 5;
    private const int SizingBottom = 6;
    private const int SizingBottomLeft = 7;

    private const int HitClient = 1;
    private const int HitCaption = 2;
    private const int HitLeft = 10;
    private const int HitRight = 11;
    private const int HitTop = 12;
    private const int HitTopLeft = 13;
    private const int HitTopRight = 14;
    private const int HitBottom = 15;
    private const int HitBottomLeft = 16;
    private const int HitBottomRight = 17;
    private const int InteractionHandleThickness = 16;
    private const int InteractionHandleLength = 70;

    private readonly nint _target;
    private readonly NativeRect _sourceRectangle;
    private readonly NativeRect _thumbnailSourceRectangle;
    private readonly Rectangle _sourceSelectionBounds;
    private readonly float _aspectRatio;
    private readonly System.Windows.Forms.Timer _targetTimer;
    private readonly System.Windows.Forms.Timer _interactionTimer;
    private readonly ToolTip _toolTip = UiToolTips.Create(5000);
    private readonly Icon? _sourceIcon;
    private readonly InteractionButtonForm _interactionButtonHost;
    private readonly InteractionButtonForm _recropButtonHost;
    private readonly InteractionSliderForm _opacitySliderHost;
    private readonly InteractionButtonForm _clickThroughButtonHost;
    private readonly InteractionButtonForm _closeButtonHost;
    private readonly InteractionHandleForm _interactionHandleHost;
    private readonly bool _useRegionBackedParking;
    private WindowPlacement _originalPlacement;
    private NativeRect _originalBounds;
    private int _originalStyle;
    private ExtendedWindowStyle _originalExtendedStyle;
    private uint _originalColorKey;
    private byte _originalAlpha;
    private LayeredWindowAttribute _originalLayeredFlags;
    private nint _originalRegion;
    private bool _hasOriginalPlacement;
    private bool _hasOriginalLayeredAttributes;
    private bool _hasOriginalRegion;
    private bool _originalTopMost;
    private bool _wasMaximized;
    private bool _targetMoved;
    private bool _resourcesDisposed;
    private bool _fittingMaximizedBounds;
    private nint _thumbnail;
    private bool _interactionsEnabled;
    private bool _interactionFocusPaused;
    private bool _movingWithInteractionHandle;
    private nint _focusedInputWindow;
    private Rectangle _interactionButtonBounds;
    private Rectangle _recropButtonBounds;
    private Rectangle _opacitySliderBounds;
    private Rectangle _clickThroughButtonBounds;
    private Rectangle _interactionRestoreBounds;
    private Rectangle _appliedInteractionRegion;
    private Point _interactionDragStart;
    private Point _interactionWindowStart;
    private FormWindowState _interactionRestoreWindowState;
    private bool _hasInteractionRestoreBounds;
    private bool _showOpacityPercentage;
    private bool _updatingOverlayControls;
    private ulong _gestureZoomStartDistance;
    private Rectangle _gestureZoomStartBounds;
    private WindowState _state = new();

    public event EventHandler? RecropRequested;
    public event Action<int>? OpacityChangeRequested;
    public event EventHandler? ClickThroughRequested;

    public Rectangle SourceSelectionBounds =>
        _sourceSelectionBounds;

    public ThumbnailCropForm(
        nint target,
        string title,
        Rectangle selectedBounds,
        WindowState state)
    {
        _target = target;
        _state = state;
        _sourceIcon = TryGetSourceIcon(target);
        _useRegionBackedParking = NeedsRegionBackedParking(target);

        if (!WindowGeometry.TryGetFrameBounds(target, out Rectangle frameBounds))
            throw new InvalidOperationException("The selected window is no longer available.");

        Rectangle source = Rectangle.Intersect(selectedBounds, frameBounds);
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidOperationException("The selected crop is outside the source window.");

        _sourceSelectionBounds = source;
        NativeRect windowCoordinateBounds =
            GetWindowRect(target, out NativeRect windowBounds)
                ? windowBounds
                : new()
                {
                    Left = frameBounds.Left,
                    Top = frameBounds.Top,
                    Right = frameBounds.Right,
                    Bottom = frameBounds.Bottom
                };
        _sourceRectangle = ToRelativeNativeRect(
            source,
            windowCoordinateBounds.Left,
            windowCoordinateBounds.Top);
        // DWM thumbnails and SetWindowRgn both use window-relative coordinates; mixing frame and window origins leaks title pixels on Win10.
        _thumbnailSourceRectangle = _sourceRectangle;
        _aspectRatio = _sourceRectangle.Width / (float)_sourceRectangle.Height;
        Text = $"{title} - Crop";
        if (_sourceIcon is not null)
            Icon = _sourceIcon;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        KeyPreview = true;
        _interactionButtonHost = new()
        {
            BackColor = UiTheme.ButtonBackground
        };
        _interactionButtonHost.SetButtonText("Interact");
        _toolTip.SetToolTip(
            _interactionButtonHost,
            "Interact with the cropped app.");
        _interactionButtonHost.Click += (_, _) => ToggleInteractions();
        _recropButtonHost = new()
        {
            BackColor = UiTheme.ButtonBackground
        };
        _recropButtonHost.SetButtonText("Recrop");
        _toolTip.SetToolTip(
            _recropButtonHost,
            "Choose a new crop region for this task.");
        _recropButtonHost.Click += (_, _) => RecropRequested?.Invoke(this, EventArgs.Empty);
        _opacitySliderHost = new()
        {
            BackColor = UiTheme.ButtonBackground
        };
        _toolTip.SetToolTip(
            _opacitySliderHost,
            "Adjust the cropped task opacity.");
        _opacitySliderHost.ValueChanged += (_, _) =>
        {
            if (!_updatingOverlayControls)
                OpacityChangeRequested?.Invoke(_opacitySliderHost.Value);
        };
        _clickThroughButtonHost = new()
        {
            BackColor = UiTheme.ButtonBackground
        };
        _clickThroughButtonHost.SetButtonText("Enable click-through");
        _toolTip.SetToolTip(
            _clickThroughButtonHost,
            "Let pointer input pass through this cropped task.");
        _clickThroughButtonHost.Click += (_, _) =>
            ClickThroughRequested?.Invoke(this, EventArgs.Empty);
        _closeButtonHost = new()
        {
            BackColor = UiTheme.ButtonBackground,
            ShowCloseGlyph = true
        };
        _closeButtonHost.AccessibleName = "Close crop";
        _toolTip.SetToolTip(
            _closeButtonHost,
            "Uncrop this task.");
        _closeButtonHost.Click += (_, _) => BeginInvoke(Close);

        _interactionHandleHost = new()
        {
            Cursor = Cursors.SizeAll
        };
        _interactionHandleHost.MouseDown += OnInteractionHandleMouseDown;
        _interactionHandleHost.MouseMove += OnInteractionHandleMouseMove;
        _interactionHandleHost.MouseUp += OnInteractionHandleMouseUp;

        MinimumSize = ToDevice(
            new Size(
                80,
                45),
            GetDpi(target));
        Bounds = source;
        TopMost = state.TopMost;
        _interactionButtonHost.TopMost = state.TopMost;
        _recropButtonHost.TopMost = state.TopMost;
        _opacitySliderHost.TopMost = state.TopMost;
        _clickThroughButtonHost.TopMost = state.TopMost;
        _closeButtonHost.TopMost = state.TopMost;
        _interactionHandleHost.TopMost = state.TopMost;

        DpiChanged += OnCropDpiChanged;
        _targetTimer = new()
        {
            Interval = 1000,
            Enabled = true
        };
        _targetTimer.Tick += (_, _) =>
        {
            if (!IsWindow(_target))
            {
                Close();
                return;
            }

            if (!_interactionsEnabled && IsIconic(_target))
            {
                _ = ParkTarget();
                RefreshThumbnail();
                _ = DwmFlush();
            }
        };
        _interactionTimer = new()
        {
            Interval = 150,
            Enabled = true
        };
        _interactionTimer.Tick += (_, _) =>
        {
            if (IsCursorOverInteractionSurface())
            {
                Point location = PointToClient(Cursor.Position);
                ShowInteractionControls(location);
            }
            else
            {
                HideInteractionControls();
            }

            KeepInteractionFocus();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            // WS_THICKFRAME keeps native resizing, snap, and DWM shadow for the
            // borderless form; WM_NCCALCSIZE below reclaims the frame area so no
            // visible border is drawn.
            parameters.Style |= ThickFrameStyle;
            if (_state.ClickThrough
                || _interactionsEnabled
                || _state.Opacity < 100)
            {
                parameters.ExStyle |=
                    (int)ExtendedWindowStyle.Layered;
            }
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplySmallCorners(Handle);

        if (!RegisterThumbnail())
        {
            BeginInvoke(Close);
            return;
        }

        UpdateThumbnail();
        SaveAndMoveTarget();
        ApplyState(_state);
        LayoutInteractionControls();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_fittingMaximizedBounds
            && WindowState == FormWindowState.Maximized)
        {
            FitMaximizedBounds();
            return;
        }

        LayoutInteractionControls();
        UpdateThumbnail();
        AlignTargetForInteraction();
        AlignParkedTarget();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        LayoutInteractionControls();
        AlignTargetForInteraction();
        AlignParkedTarget();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeResources();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeResources();

        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
            return;

        _resourcesDisposed = true;
        ReleaseInteractionFocus();
        DpiChanged -= OnCropDpiChanged;
        _targetTimer.Stop();
        _interactionTimer.Stop();

        UnregisterThumbnail();

        RestoreTarget();
        if (_originalRegion != 0)
        {
            _ = DeleteObject(_originalRegion);
            _originalRegion = 0;
        }
        Icon = null;
        _sourceIcon?.Dispose();
        _targetTimer.Dispose();
        _interactionTimer.Dispose();
        _toolTip.Dispose();
        _interactionButtonHost.Close();
        _interactionButtonHost.Dispose();
        _recropButtonHost.Close();
        _recropButtonHost.Dispose();
        _opacitySliderHost.Close();
        _opacitySliderHost.Dispose();
        _clickThroughButtonHost.Close();
        _clickThroughButtonHost.Dispose();
        _closeButtonHost.Close();
        _closeButtonHost.Dispose();
        _interactionHandleHost.Close();
        _interactionHandleHost.Dispose();
    }

    private void OnCropDpiChanged(
        object? sender,
        DpiChangedEventArgs e)
    {
        MinimumSize = ToDevice(
            new Size(
                80,
                45),
            e.DeviceDpiNew);
        LayoutInteractionControls();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape && !_interactionsEnabled)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case WindowMessageGesture:
                HandleZoomGesture(message.LParam);
                message.Result = 1;
                return;
            case WindowMessageMouseActivate when _state.ClickThrough || _interactionsEnabled:
                if (_interactionsEnabled)
                    ClaimInteractionFocus();
                message.Result = MouseActivateNoActivate;
                return;
            case WindowMessageNcCalcSize when message.WParam != 0:
                message.Result = 0;
                return;
            case WindowMessageNcHitTest:
                if (_state.ClickThrough)
                {
                    message.Result = HitTransparent;
                    return;
                }

                ShowInteractionControls(PointToClient(MousePosition));
                base.WndProc(ref message);
                if ((int)message.Result == HitClient)
                {
                    if (IsInteractionButtonHit())
                    {
                        message.Result = HitClient;
                        return;
                    }

                    nint hit = HitTest(message.LParam);
                    message.Result = _interactionsEnabled
                        && hit == HitCaption
                        && !IsInteractionHandleHit()
                            ? HitClient
                            : hit;
                }
                return;
            case WindowMessageSizing:
                ApplyAspectRatio(
                    message.WParam,
                    message.LParam);
                message.Result = 1;
                return;
        }

        base.WndProc(ref message);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ShowInteractionControls(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsCursorOverInteractionSurface())
            HideInteractionControls();
    }

    public void ApplyState(WindowState state)
    {
        _state = state;
        if (!IsHandleCreated || IsDisposed)
            return;

        ExtendedWindowStyle style =
            (ExtendedWindowStyle)(uint)GetWindowLong(
                Handle,
                ExtendedStyleIndex);

        // Interact mode keeps the DWM thumbnail visible and merely lets mouse input
        // fall through to the real target aligned underneath the form.
        bool passThrough =
            state.ClickThrough || _interactionsEnabled;
        if (passThrough)
        {
            style |= ExtendedWindowStyle.Layered
                | ExtendedWindowStyle.Transparent
                | ExtendedWindowStyle.NoActivate;
        }
        else
        {
            style &= ~ExtendedWindowStyle.Transparent;
            style &= ~ExtendedWindowStyle.NoActivate;
        }

        bool requiresLayered =
            passThrough || state.Opacity < 100;
        if (requiresLayered)
        {
            style |= ExtendedWindowStyle.Layered;
        }
        else
        {
            style &= ~ExtendedWindowStyle.Layered;
        }
        _ = SetWindowLong(
            Handle,
            ExtendedStyleIndex,
            unchecked((int)style));
        if (requiresLayered)
        {
            int opacity = Math.Clamp(
                state.Opacity,
                0,
                100);
            _ = SetLayeredWindowAttributes(
                Handle,
                0,
                (byte)(opacity * byte.MaxValue / 100),
                LayeredWindowAttribute.Alpha);
        }
        TopMost = state.TopMost;
        _interactionButtonHost.TopMost = state.TopMost;
        _recropButtonHost.TopMost = state.TopMost;
        _opacitySliderHost.TopMost = state.TopMost;
        _clickThroughButtonHost.TopMost = state.TopMost;
        _closeButtonHost.TopMost = state.TopMost;
        _interactionHandleHost.TopMost = state.TopMost;
        _updatingOverlayControls = true;
        _opacitySliderHost.Value = Math.Clamp(state.Opacity, 0, 100);
        _updatingOverlayControls = false;
        if (state.ClickThrough)
            HideInteractionControls();

        _ = SetWindowPos(
            Handle,
            state.TopMost
                ? NativeMethods.TopMost
                : NativeMethods.NotTopMost,
            0,
            0,
            0,
            0,
            WindowPositionFlags.NoMove
                | WindowPositionFlags.NoSize
                | WindowPositionFlags.NoActivate
                | WindowPositionFlags.FrameChanged);
    }

    public void SetInteractionFocusPaused(bool paused) =>
        _interactionFocusPaused = paused;

    public void SetShowOpacityPercentage(bool visible)
    {
        if (_showOpacityPercentage == visible)
            return;

        _showOpacityPercentage = visible;
        _opacitySliderHost.SetShowValue(visible);
        LayoutInteractionControls();
    }

    public bool IsInteracting => _interactionsEnabled;

    // Overlay buttons activate on click, so global shortcuts must recognize them
    // as part of this crop or hotkeys silently stop working after a button press.
    public bool OwnsOverlayWindow(nint handle) =>
        handle != 0
        && (IsOwnWindow(handle, Handle)
            || IsOwnWindow(handle, _interactionButtonHost.Handle)
            || IsOwnWindow(handle, _recropButtonHost.Handle)
            || IsOwnWindow(handle, _opacitySliderHost.Handle)
            || IsOwnWindow(handle, _clickThroughButtonHost.Handle)
            || IsOwnWindow(handle, _closeButtonHost.Handle)
            || IsOwnWindow(handle, _interactionHandleHost.Handle));

    public void RestoreShortcutFocus()
    {
        if (_resourcesDisposed || IsDisposed || !IsHandleCreated)
            return;

        if (_interactionsEnabled)
        {
            ClaimInteractionFocus();
            FocusTargetWindow();
            return;
        }

        if (_state.ClickThrough)
            return;

        BringToFront();
        Activate();
        _ = SetForegroundWindow(Handle);
    }

    public void ToggleInteractions()
    {
        _interactionsEnabled = !_interactionsEnabled;
        _interactionButtonHost.SetButtonText(
            _interactionsEnabled
                ? "Stop interacting"
                : "Interact");
        _toolTip.SetToolTip(
            _interactionButtonHost,
            _interactionsEnabled
                ? "Stop interacting with the cropped app."
                : "Interact with the cropped app.");
        if (_interactionsEnabled)
        {
            // The DWM thumbnail stays as the visible face; the form becomes click-through
            // and the invisible target is aligned 1:1 underneath so real input lands on it.
            // Never reparent or region-reveal the target: cross-process SetParent latches the
            // host into the classic Win98 frame, and revealed regions render as a gray sheet
            // for DirectComposition apps (Paint.NET, Chromium).
            EnterInteractionNativeSize();
            ApplyState(_state);
            AlignTargetForInteraction();
            LayoutInteractionControls();
            ClaimInteractionFocus();
            FocusTargetWindow();
        }
        else
        {
            _focusedInputWindow = 0;
            _appliedInteractionRegion = Rectangle.Empty;
            ReleaseInteractionFocus();
            ParkTarget();
            RestoreInteractionSize();
            RefreshThumbnail();
            _ = DwmFlush();
            ApplyState(_state);
            LayoutInteractionControls();
            BeginInvoke((Action)RecoverFromInteractionExit);
            BeginInvoke((Action)RestoreShortcutFocus);
        }
    }

    private void RecoverFromInteractionExit()
    {
        if (_resourcesDisposed
            || IsDisposed
            || _interactionsEnabled)
        {
            return;
        }

        _ = ParkTarget();
        RefreshThumbnail();
        _ = DwmFlush();
    }

    private void ShowInteractionControls(Point location)
    {
        if (_resourcesDisposed
            || IsDisposed
            || !IsHandleCreated
            || !StateAllowsInteractionOverlay)
        {
            return;
        }

        if (!_interactionButtonHost.Visible)
            _interactionButtonHost.Show(this);
        if (!_closeButtonHost.Visible)
            _closeButtonHost.Show(this);
        if (!_interactionsEnabled)
        {
            if (!_recropButtonHost.Visible)
                _recropButtonHost.Show(this);
            if (!_opacitySliderHost.Visible)
                _opacitySliderHost.Show(this);
            if (!_clickThroughButtonHost.Visible)
                _clickThroughButtonHost.Show(this);
        }

        LayoutInteractionControls(location);
    }

    private void HideInteractionControls()
    {
        if (_resourcesDisposed || IsDisposed || _interactionsEnabled)
            return;

        _interactionButtonHost.Hide();
        _recropButtonHost.Hide();
        _opacitySliderHost.Hide();
        _clickThroughButtonHost.Hide();
        _closeButtonHost.Hide();
        _interactionHandleHost.Hide();
    }

    private bool IsCursorOverInteractionSurface()
    {
        if (!GetCursorPos(out NativePoint cursor))
            return false;

        if (_interactionsEnabled && Bounds.Contains(cursor.X, cursor.Y))
            return true;

        nint window = WindowFromPoint(cursor);
        return IsOwnWindow(window, Handle)
            || IsOwnWindow(window, _interactionButtonHost.Handle)
            || IsOwnWindow(window, _recropButtonHost.Handle)
            || IsOwnWindow(window, _opacitySliderHost.Handle)
            || IsOwnWindow(window, _clickThroughButtonHost.Handle)
            || IsOwnWindow(window, _closeButtonHost.Handle)
            || IsOwnWindow(window, _interactionHandleHost.Handle);
    }

    private static bool IsOwnWindow(nint window, nint owner) =>
        owner != 0
        && (window == owner || IsChild(owner, window));

    private void LayoutInteractionControls() =>
        LayoutInteractionControls(PointToClient(MousePosition));

    private void LayoutInteractionControls(Point location)
    {
        if (_resourcesDisposed || !IsHandleCreated || IsDisposed)
            return;

        int margin = ToDevice(8, DeviceDpi);
        int gap = ToDevice(6, DeviceDpi);
        Size buttonSize = ToDevice(
            new Size(108, 26),
            DeviceDpi);
        var buttonBounds = new Rectangle(
            Math.Max(margin, (ClientSize.Width - buttonSize.Width) / 2),
            margin,
            buttonSize.Width,
            buttonSize.Height);
        _interactionButtonBounds = buttonBounds;
        PlaceOverlay(
            _interactionButtonHost,
            ToScreen(buttonBounds));
        int overlayTop = buttonBounds.Bottom + gap;
        Size recropSize = ToDevice(
            new Size(86, 26),
            DeviceDpi);
        _recropButtonBounds = CenteredOverlayBounds(
            overlayTop,
            recropSize,
            margin);
        overlayTop = _recropButtonBounds.Bottom + gap;
        _opacitySliderBounds = CenteredOverlayBounds(
            overlayTop,
            ToDevice(
                new Size(_showOpacityPercentage ? 184 : 140, 28),
                DeviceDpi),
            margin);
        overlayTop = _opacitySliderBounds.Bottom + gap;
        _clickThroughButtonBounds = CenteredOverlayBounds(
            overlayTop,
            ToDevice(
                new Size(142, 26),
                DeviceDpi),
            margin);
        if (_interactionsEnabled)
        {
            _recropButtonHost.Hide();
            _opacitySliderHost.Hide();
            _clickThroughButtonHost.Hide();
        }
        else
        {
            PlaceOverlay(
                _recropButtonHost,
                ToScreen(_recropButtonBounds));
            PlaceOverlay(
                _opacitySliderHost,
                ToScreen(_opacitySliderBounds));
            PlaceOverlay(
                _clickThroughButtonHost,
                ToScreen(_clickThroughButtonBounds));
        }
        Size closeSize = ToDevice(
            new Size(28, 26),
            DeviceDpi);
        PlaceOverlay(
            _closeButtonHost,
            ToScreen(new(
                Math.Max(margin, ClientSize.Width - margin - closeSize.Width),
                margin,
                closeSize.Width,
                closeSize.Height)));

        if (!_interactionsEnabled || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            _interactionHandleHost.Hide();
            return;
        }

        int thickness = ToDevice(InteractionHandleThickness, DeviceDpi);
        int length = Math.Min(
            ToDevice(InteractionHandleLength, DeviceDpi),
            Math.Max(thickness, Math.Min(ClientSize.Width, ClientSize.Height) / 2));
        Point center = new(ClientSize.Width / 2, ClientSize.Height / 2);
        int dx = location.X - center.X;
        int dy = location.Y - center.Y;

        Rectangle handleBounds;
        if (Math.Abs(dy) >= Math.Abs(dx) && dy < 0)
        {
            _interactionHandleHost.SetSide(InteractionSide.Top);
            handleBounds = new(
                Math.Max(0, (ClientSize.Width - length) / 2),
                -thickness,
                length,
                thickness);
        }
        else if (Math.Abs(dy) >= Math.Abs(dx))
        {
            _interactionHandleHost.SetSide(InteractionSide.Bottom);
            handleBounds = new(
                Math.Max(0, (ClientSize.Width - length) / 2),
                ClientSize.Height,
                length,
                thickness);
        }
        else if (dx < 0)
        {
            _interactionHandleHost.SetSide(InteractionSide.Left);
            handleBounds = new(
                -thickness,
                Math.Max(0, (ClientSize.Height - length) / 2),
                thickness,
                length);
        }
        else
        {
            _interactionHandleHost.SetSide(InteractionSide.Right);
            handleBounds = new(
                ClientSize.Width,
                Math.Max(0, (ClientSize.Height - length) / 2),
                thickness,
                length);
        }

        _interactionHandleHost.Bounds = ToScreen(handleBounds);
        if (!_interactionHandleHost.Visible)
            _interactionHandleHost.Show(this);

        PlaceOverlay(
            _interactionHandleHost,
            ToScreen(handleBounds));
    }

    private bool StateAllowsInteractionOverlay =>
        !_state.ClickThrough && Visible && IsHandleCreated;

    private bool IsInteractionHandleHit() =>
        _interactionsEnabled
        && _interactionHandleHost.Visible
        && _interactionHandleHost.Bounds.Contains(MousePosition);

    private bool IsInteractionButtonHit() =>
        _interactionButtonHost.Visible
        && _interactionButtonBounds.Contains(PointToClient(MousePosition))
        || _recropButtonHost.Visible
        && _recropButtonBounds.Contains(PointToClient(MousePosition))
        || _opacitySliderHost.Visible
        && _opacitySliderBounds.Contains(PointToClient(MousePosition))
        || _clickThroughButtonHost.Visible
        && _clickThroughButtonBounds.Contains(PointToClient(MousePosition));

    private Rectangle CenteredOverlayBounds(
        int top,
        Size preferredSize,
        int margin)
    {
        int width = Math.Min(
            preferredSize.Width,
            Math.Max(1, ClientSize.Width - margin * 2));
        return new(
            Math.Max(margin, (ClientSize.Width - width) / 2),
            top,
            width,
            preferredSize.Height);
    }

    private void FitMaximizedBounds()
    {
        if (_aspectRatio <= 0)
            return;

        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        int width = area.Width;
        int height = (int)Math.Round(width / _aspectRatio);
        if (height > area.Height)
        {
            height = area.Height;
            width = (int)Math.Round(height * _aspectRatio);
        }

        width = Math.Max(MinimumSize.Width, width);
        height = Math.Max(MinimumSize.Height, height);
        _fittingMaximizedBounds = true;
        try
        {
            WindowState = FormWindowState.Normal;
            Bounds = new(
                area.Left + (area.Width - width) / 2,
                area.Top + (area.Height - height) / 2,
                width,
                height);
        }
        finally
        {
            _fittingMaximizedBounds = false;
        }

        LayoutInteractionControls();
        UpdateThumbnail();
        AlignTargetForInteraction();
    }

    private void HandleZoomGesture(nint gestureHandle)
    {
        var info = new GestureInfo
        {
            Size = Marshal.SizeOf<GestureInfo>()
        };
        try
        {
            if (!GetGestureInfo(gestureHandle, ref info)
                || info.Id != GestureZoom
                || info.Arguments == 0)
            {
                return;
            }

            if ((info.Flags & GestureFlagBegin) != 0
                || _gestureZoomStartDistance == 0)
            {
                _gestureZoomStartDistance = info.Arguments;
                _gestureZoomStartBounds = Bounds;
                return;
            }

            ScaleFromGesture(
                _gestureZoomStartBounds,
                new(info.Location.X, info.Location.Y),
                info.Arguments / (double)_gestureZoomStartDistance);
            if ((info.Flags & GestureFlagEnd) != 0)
                _gestureZoomStartDistance = 0;
        }
        finally
        {
            _ = CloseGestureInfoHandle(gestureHandle);
        }
    }

    private void ScaleFromGesture(
        Rectangle startBounds,
        Point center,
        double scale)
    {
        if (_aspectRatio <= 0
            || double.IsNaN(scale)
            || double.IsInfinity(scale)
            || scale <= 0)
        {
            return;
        }

        if (!startBounds.Contains(center))
            center = new(
                startBounds.Left + startBounds.Width / 2,
                startBounds.Top + startBounds.Height / 2);

        int width = Math.Max(
            MinimumSize.Width,
            (int)Math.Round(startBounds.Width * scale));
        int height = Math.Max(
            MinimumSize.Height,
            (int)Math.Round(width / _aspectRatio));
        width = Math.Max(
            MinimumSize.Width,
            (int)Math.Round(height * _aspectRatio));
        double anchorX = Math.Clamp(
            (center.X - startBounds.Left) / (double)Math.Max(1, startBounds.Width),
            0,
            1);
        double anchorY = Math.Clamp(
            (center.Y - startBounds.Top) / (double)Math.Max(1, startBounds.Height),
            0,
            1);
        Bounds = new(
            center.X - (int)Math.Round(width * anchorX),
            center.Y - (int)Math.Round(height * anchorY),
            width,
            height);
    }

    private Rectangle ToScreen(Rectangle bounds)
    {
        Point location = PointToScreen(bounds.Location);
        return new(
            location,
            bounds.Size);
    }

    private static void PlaceOverlay(Form form, Rectangle bounds)
    {
        _ = SetWindowPos(
            form.Handle,
            0,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            WindowPositionFlags.NoActivate
                | WindowPositionFlags.NoZOrder);
        form.Refresh();
    }

    private void OnInteractionHandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _movingWithInteractionHandle = true;
        _interactionDragStart = Cursor.Position;
        _interactionWindowStart = Location;
        _interactionHandleHost.Capture = true;
    }

    private void OnInteractionHandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_movingWithInteractionHandle)
            return;

        Point cursor = Cursor.Position;
        Location = new(
            _interactionWindowStart.X + cursor.X - _interactionDragStart.X,
            _interactionWindowStart.Y + cursor.Y - _interactionDragStart.Y);
    }

    private void OnInteractionHandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _movingWithInteractionHandle = false;
        _interactionHandleHost.Capture = false;
    }

    private void SaveAndMoveTarget()
    {
        if (!IsWindow(_target))
            return;

        _originalPlacement = new()
        {
            Length = Marshal.SizeOf<WindowPlacement>()
        };
        _hasOriginalPlacement = GetWindowPlacement(
            _target,
            ref _originalPlacement);

        if (!GetWindowRect(_target, out _originalBounds))
            return;

        _originalStyle = GetWindowLong(_target, StyleIndex);
        _originalExtendedStyle = (ExtendedWindowStyle)(uint)GetWindowLong(
            _target,
            ExtendedStyleIndex);
        _originalTopMost = _originalExtendedStyle.HasFlag(
            ExtendedWindowStyle.TopMost);
        _hasOriginalLayeredAttributes = _originalExtendedStyle.HasFlag(
                ExtendedWindowStyle.Layered)
            && GetLayeredWindowAttributes(
                _target,
                out _originalColorKey,
                out _originalAlpha,
                out _originalLayeredFlags);
        SaveOriginalRegion();
        _wasMaximized = _hasOriginalPlacement
            && _originalPlacement.ShowCommand == 3;

        if (_hasOriginalLayeredAttributes
            && _originalLayeredFlags.HasFlag(
                LayeredWindowAttribute.Alpha))
        {
            _ = SetLayeredWindowAttributes(
                _target,
                _originalColorKey,
                byte.MaxValue,
                _originalLayeredFlags);
        }

        // Never strip WS_MAXIMIZE for parking: custom-frame apps (browsers)
        // anchor content to the DWM frame, whose offset inside the window rect
        // differs between maximized and normal windows, so demaximizing makes
        // the page reflow by that delta and the crop visibly shifts off the
        // selection. Maximized windows are fully on-screen already and can be
        // parked (and even moved) while staying maximized.
        _targetMoved = ParkTarget();
    }

    private void SaveOriginalRegion()
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
            return;

        if (GetWindowRgn(_target, region) == 0)
        {
            _ = DeleteObject(region);
            return;
        }

        _originalRegion = region;
        _hasOriginalRegion = true;
    }

    private void ApplyTargetCropRegion(Rectangle regionBounds)
    {
        nint region = CreateRectRgn(
            regionBounds.Left,
            regionBounds.Top,
            regionBounds.Right,
            regionBounds.Bottom);
        if (region == 0)
            return;

        if (SetWindowRgn(_target, region, redraw: true) == 0)
            _ = DeleteObject(region);
    }

    // Two parking modes hide the source while its DWM thumbnail is shown:
    // most apps become alpha-0 layered and click-through at a clamped on-screen
    // position (fully invisible, thumbnail still renders); DWM-hostile apps
    // (see NeedsRegionBackedParking) stay opaque but are region-clipped to the
    // crop rect, aligned under the crop form, at the bottom of the z-order.
    private bool ParkTarget()
    {
        if (!IsWindow(_target))
            return false;

        RestoreParkedTargetIfMinimized();
        ApplyTargetParkingStyle();
        if (_useRegionBackedParking)
            ApplyTargetCropRegion(GetParkedTargetRegion());
        else
            _ = SetWindowRgn(_target, 0, redraw: true);
        Rectangle parkedBounds = GetParkedTargetBounds();
        bool moved = SetWindowPos(
            _target,
            _useRegionBackedParking
                ? NativeMethods.Bottom
                : NativeMethods.TopMost,
            parkedBounds.Left,
            parkedBounds.Top,
            parkedBounds.Width,
            parkedBounds.Height,
            WindowPositionFlags.NoActivate
                | WindowPositionFlags.FrameChanged);
        if (_useRegionBackedParking)
            ApplyTargetCropRegion(GetParkedTargetRegion());
        return moved;
    }

    private void AlignParkedTarget()
    {
        if (!_interactionsEnabled && _targetMoved)
            _ = ParkTarget();
    }

    private Rectangle GetParkedTargetBounds()
    {
        Rectangle sourceScreen = Screen.FromRectangle(
            Rectangle.FromLTRB(
                _originalBounds.Left,
                _originalBounds.Top,
                _originalBounds.Right,
                _originalBounds.Bottom)).Bounds;
        int width = Math.Max(1, _originalBounds.Width);
        int height = Math.Max(1, _originalBounds.Height);
        if (_useRegionBackedParking)
            return GetRegionBackedTargetBounds(width, height);

        // A maximized window is fully on-screen by definition; keep it exactly
        // where it is so its content never relayouts while parked.
        if (_wasMaximized)
            return new(
                _originalBounds.Left,
                _originalBounds.Top,
                width,
                height);

        int x = ClampIntoRange(
            _originalBounds.Left,
            sourceScreen.Left - _sourceRectangle.Left,
            sourceScreen.Right - _sourceRectangle.Right);
        int y = ClampIntoRange(
            _originalBounds.Top,
            sourceScreen.Top - _sourceRectangle.Top,
            sourceScreen.Bottom - _sourceRectangle.Bottom);

        return new(x, y, width, height);
    }

    private Rectangle GetRegionBackedTargetBounds(int width, int height)
    {
        // Park the opaque source fully off every monitor. Its DWM thumbnail is
        // the visible face of the crop and keeps rendering off-screen (Chromium
        // does not throttle a window that is merely off-screen rather than
        // occluded), so nothing needs to sit under the crop form. Keeping it
        // on-screen used to make the un-scaled source poke out and stay
        // clickable whenever the thumbnail was resized smaller than the crop.
        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        return new(
            virtualScreen.Left - width - 32,
            virtualScreen.Top,
            width,
            height);
    }

    private Rectangle GetParkedTargetRegion() =>
        Rectangle.FromLTRB(
            _sourceRectangle.Left,
            _sourceRectangle.Top,
            _sourceRectangle.Right,
            _sourceRectangle.Bottom);

    private static int ClampIntoRange(int value, int minimum, int maximum) =>
        minimum <= maximum
            ? Math.Clamp(value, minimum, maximum)
            : minimum;

    private void RestoreParkedTargetIfMinimized()
    {
        if (!IsIconic(_target))
            return;

        _ = ShowWindow(_target, ShowWindowCommand.ShowNoActivate);
    }

    private void AlignTargetForInteraction()
    {
        if (!_interactionsEnabled
            || !_targetMoved
            || !IsWindow(_target)
            || _sourceRectangle.Width <= 0
            || _sourceRectangle.Height <= 0)
        {
            return;
        }

        Size clientPixelSize = GetClientPixelSize();
        if (clientPixelSize.Width <= 0 || clientPixelSize.Height <= 0)
            return;

        if (!_useRegionBackedParking)
        {
            // Alpha-parked targets are alpha-0 and click-through; hit testing skips
            // alpha-0 layered windows, so give them one alpha step while interacting.
            ExtendedWindowStyle style =
                (ExtendedWindowStyle)(uint)GetWindowLong(
                    _target,
                    ExtendedStyleIndex);
            _ = SetWindowLong(
                _target,
                ExtendedStyleIndex,
                unchecked((int)(style & ~ExtendedWindowStyle.Transparent)));
            _ = SetLayeredWindowAttributes(
                _target,
                _originalColorKey,
                1,
                LayeredWindowAttribute.Alpha);
        }

        Rectangle targetBounds = GetInteractionTargetBounds(clientPixelSize);
        _ = SetWindowPos(
            _target,
            Handle,
            targetBounds.Left,
            targetBounds.Top,
            Math.Max(1, targetBounds.Width),
            Math.Max(1, targetBounds.Height),
            WindowPositionFlags.NoActivate);

        // Chromium resets its DWM border color on activation, which draws the raised
        // opaque target's full-frame border around the crop; keep re-hiding it.
        if (_useRegionBackedParking)
            WindowTheme.HideBorder(_target);

        // The crop region limits hit testing to the pixels covered by the form, so
        // the invisible parts of the target cannot swallow clicks around the crop.
        Rectangle region = GetInteractionTargetRegion(clientPixelSize);
        if (region != _appliedInteractionRegion)
        {
            _appliedInteractionRegion = region;
            ApplyTargetCropRegion(region);
        }
    }

    private Rectangle GetInteractionTargetBounds(Size clientPixelSize)
    {
        double scaleX = clientPixelSize.Width / (double)_sourceRectangle.Width;
        double scaleY = clientPixelSize.Height / (double)_sourceRectangle.Height;
        int regionLeft = (int)Math.Round(_sourceRectangle.Left * scaleX);
        int regionTop = (int)Math.Round(_sourceRectangle.Top * scaleY);
        int width = Math.Max(
            1,
            (int)Math.Round(_originalBounds.Width * scaleX));
        int height = Math.Max(
            1,
            (int)Math.Round(_originalBounds.Height * scaleY));
        return new(
            Left - regionLeft,
            Top - regionTop,
            width,
            height);
    }

    private void EnterInteractionNativeSize()
    {
        if (_hasInteractionRestoreBounds)
            return;

        // Use the crop's original pixel size so web/app layouts do not reflow to the user's scaled thumbnail size.
        _interactionRestoreBounds = Bounds;
        _interactionRestoreWindowState = WindowState;
        _hasInteractionRestoreBounds = true;
        if (WindowState != FormWindowState.Normal)
            WindowState = FormWindowState.Normal;

        Size nativeSize = new(
            Math.Max(MinimumSize.Width, _sourceRectangle.Width),
            Math.Max(MinimumSize.Height, _sourceRectangle.Height));
        Point restoreCenter = new(
            _interactionRestoreBounds.Left + _interactionRestoreBounds.Width / 2,
            _interactionRestoreBounds.Top + _interactionRestoreBounds.Height / 2);
        Bounds = CenteredInScreen(
            new(
                _interactionRestoreBounds.Left
                    + _interactionRestoreBounds.Width / 2
                    - nativeSize.Width / 2,
                _interactionRestoreBounds.Top
                    + _interactionRestoreBounds.Height / 2
                    - nativeSize.Height / 2,
                nativeSize.Width,
                nativeSize.Height),
            Screen.FromPoint(restoreCenter).Bounds);
    }

    private void RestoreInteractionSize()
    {
        if (!_hasInteractionRestoreBounds)
            return;

        Bounds = _interactionRestoreBounds;
        WindowState = _interactionRestoreWindowState;
        _hasInteractionRestoreBounds = false;
    }

    private static Rectangle CenteredInScreen(Rectangle bounds, Rectangle screen)
    {
        int x = Math.Clamp(
            bounds.Left,
            screen.Left,
            Math.Max(screen.Left, screen.Right - bounds.Width));
        int y = Math.Clamp(
            bounds.Top,
            screen.Top,
            Math.Max(screen.Top, screen.Bottom - bounds.Height));
        return new(x, y, bounds.Width, bounds.Height);
    }

    private Rectangle GetInteractionTargetRegion(Size clientPixelSize)
    {
        double scaleX = clientPixelSize.Width / (double)_sourceRectangle.Width;
        double scaleY = clientPixelSize.Height / (double)_sourceRectangle.Height;
        return Rectangle.FromLTRB(
            (int)Math.Round(_sourceRectangle.Left * scaleX),
            (int)Math.Round(_sourceRectangle.Top * scaleY),
            (int)Math.Round(_sourceRectangle.Right * scaleX),
            (int)Math.Round(_sourceRectangle.Bottom * scaleY));
    }

    private Size GetClientPixelSize()
    {
        if (IsHandleCreated
            && WindowGeometry.TryGetFrameBounds(Handle, out Rectangle frameBounds))
        {
            return frameBounds.Size;
        }

        if (IsHandleCreated
            && GetWindowRect(Handle, out NativeRect bounds)
            && bounds.Width > 0
            && bounds.Height > 0)
        {
            return new(bounds.Width, bounds.Height);
        }

        return ClientSize;
    }

    private void RestoreTarget()
    {
        if (!IsWindow(_target))
            return;

        RestoreTargetRegion();
        if (!_targetMoved)
            return;

        _ = SetWindowPos(
            _target,
            _originalTopMost
                ? NativeMethods.TopMost
                : NativeMethods.NotTopMost,
            _originalBounds.Left,
            _originalBounds.Top,
            Math.Max(1, _originalBounds.Width),
            Math.Max(1, _originalBounds.Height),
            WindowPositionFlags.NoActivate
                | WindowPositionFlags.FrameChanged);

        if (_hasOriginalPlacement)
            _ = SetWindowPlacement(
                _target,
                ref _originalPlacement);

        RestoreTargetInteractiveStyle();
        WindowTheme.RestoreBorder(_target);
        RefreshNativeFrame(_target);
        _targetMoved = false;
    }

    internal static void ClearNativeCropResidue(nint target)
    {
        if (!IsWindow(target))
            return;

        ExtendedWindowStyle style =
            (ExtendedWindowStyle)(uint)GetWindowLong(
                target,
                ExtendedStyleIndex);
        if (style.HasFlag(ExtendedWindowStyle.Layered))
        {
            _ = SetLayeredWindowAttributes(
                target,
                0,
                byte.MaxValue,
                LayeredWindowAttribute.Alpha);
        }

        style &= ~ExtendedWindowStyle.Layered
            & ~ExtendedWindowStyle.Transparent
            & ~ExtendedWindowStyle.NoActivate;
        _ = SetWindowLong(
            target,
            ExtendedStyleIndex,
            unchecked((int)style));
        _ = SetWindowRgn(target, 0, redraw: true);
        RefreshNativeFrame(target);
    }

    private static void RefreshNativeFrame(nint target)
    {
        if (!IsWindow(target))
            return;

        _ = SetWindowPos(
            target,
            0,
            0,
            0,
            0,
            0,
            WindowPositionFlags.NoMove
                | WindowPositionFlags.NoSize
                | WindowPositionFlags.NoZOrder
                | WindowPositionFlags.NoActivate
                | WindowPositionFlags.FrameChanged);
        _ = InvalidateRect(target, 0, erase: true);
        _ = UpdateWindow(target);
        _ = DwmFlush();
    }

    private void RestoreTargetRegion()
    {
        if (_hasOriginalRegion && _originalRegion != 0)
        {
            if (SetWindowRgn(_target, _originalRegion, redraw: true) != 0)
                _originalRegion = 0;
            return;
        }

        _ = SetWindowRgn(_target, 0, redraw: true);
    }

    private void ApplyTargetParkingStyle()
    {
        if (!IsWindow(_target))
            return;

        if (_useRegionBackedParking)
        {
            RestoreTargetInteractiveStyle();
            WindowTheme.HideBorder(_target);
            return;
        }

        ExtendedWindowStyle parkedStyle =
            _originalExtendedStyle
            | ExtendedWindowStyle.Layered
            | ExtendedWindowStyle.Transparent;
        _ = SetWindowLong(
            _target,
            ExtendedStyleIndex,
            unchecked((int)parkedStyle));
        _ = SetLayeredWindowAttributes(
            _target,
            _originalColorKey,
            0,
            _hasOriginalLayeredAttributes
                ? _originalLayeredFlags | LayeredWindowAttribute.Alpha
                : LayeredWindowAttribute.Alpha);
    }

    private void RestoreTargetInteractiveStyle()
    {
        ExtendedWindowStyle currentStyle =
            (ExtendedWindowStyle)(uint)GetWindowLong(
                _target,
                ExtendedStyleIndex);
        if (currentStyle.HasFlag(ExtendedWindowStyle.Layered))
        {
            _ = SetLayeredWindowAttributes(
                _target,
                _originalColorKey,
                _hasOriginalLayeredAttributes
                    ? _originalAlpha
                    : byte.MaxValue,
                _hasOriginalLayeredAttributes
                    ? _originalLayeredFlags | LayeredWindowAttribute.Alpha
                    : LayeredWindowAttribute.Alpha);
        }

        _ = SetWindowLong(
            _target,
            ExtendedStyleIndex,
            unchecked((int)_originalExtendedStyle));

        if (_hasOriginalLayeredAttributes)
        {
            _ = SetLayeredWindowAttributes(
                _target,
                _originalColorKey,
                _originalAlpha,
                _originalLayeredFlags);
        }
    }

    private nint HitTest(nint lParam)
    {
        int x;
        int y;
        if (GetCursorPos(out NativePoint cursor))
        {
            x = cursor.X;
            y = cursor.Y;
        }
        else
        {
            x = unchecked((short)((long)lParam & 0xFFFF));
            y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        }

        Rectangle bounds = Bounds;
        int resizeBorder = ToDevice(
            ResizeBorder,
            DeviceDpi);
        bool left = x < bounds.Left + resizeBorder;
        bool right = x >= bounds.Right - resizeBorder;
        bool top = y < bounds.Top + resizeBorder;
        bool bottom = y >= bounds.Bottom - resizeBorder;

        if (left && top)
            return HitTopLeft;
        if (right && top)
            return HitTopRight;
        if (left && bottom)
            return HitBottomLeft;
        if (right && bottom)
            return HitBottomRight;
        if (left)
            return HitLeft;
        if (right)
            return HitRight;
        if (top)
            return HitTop;
        if (bottom)
            return HitBottom;

        return HitCaption;
    }

    private void ApplyAspectRatio(nint edgeValue, nint rectanglePointer)
    {
        if (rectanglePointer == 0 || _aspectRatio <= 0)
            return;

        NativeRect rectangle =
            Marshal.PtrToStructure<NativeRect>(rectanglePointer);
        int width = Math.Max(MinimumSize.Width, rectangle.Width);
        int height = Math.Max(MinimumSize.Height, rectangle.Height);
        int edge = (int)edgeValue;

        if (edge is SizingLeft or SizingRight)
            height = (int)Math.Round(width / _aspectRatio);
        else if (edge is SizingTop or SizingBottom)
            width = (int)Math.Round(height * _aspectRatio);
        else if (width / (float)height > _aspectRatio)
            width = (int)Math.Round(height * _aspectRatio);
        else
            height = (int)Math.Round(width / _aspectRatio);

        if (edge is SizingLeft or SizingTopLeft or SizingBottomLeft)
            rectangle.Left = rectangle.Right - width;
        else
            rectangle.Right = rectangle.Left + width;

        if (edge is SizingTop or SizingTopLeft or SizingTopRight)
            rectangle.Top = rectangle.Bottom - height;
        else
            rectangle.Bottom = rectangle.Top + height;

        Marshal.StructureToPtr(rectangle, rectanglePointer, false);
    }

    private void FocusInputWindow(nint inputWindow)
    {
        if (!IsWindow(_target))
            return;

        inputWindow =
            inputWindow != 0 && IsWindow(inputWindow)
                ? inputWindow
                : _target;
        if (GetForegroundWindow() == _target
            && _focusedInputWindow == inputWindow)
        {
            return;
        }

        // SetForegroundWindow from a background process is normally refused;
        // attaching to the target's input queue borrows its focus rights.
        _focusedInputWindow = inputWindow;
        uint targetThread = GetWindowThreadProcessId(inputWindow, out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = targetThread != 0
            && targetThread != currentThread
            && AttachThreadInput(
                currentThread,
                targetThread,
                attach: true);
        try
        {
            _ = SetForegroundWindow(_target);
            _ = SetFocus(inputWindow);
            if (_interactionsEnabled)
                AlignTargetForInteraction();
            else
                _ = BringWindowToTop(_target);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(
                    currentThread,
                    targetThread,
                    attach: false);
            }
        }
    }

    private void KeepInteractionFocus()
    {
        if (!IsWindow(_target) || !_interactionsEnabled)
            return;

        AlignTargetForInteraction();
        if (!OwnsInteractionFocus && IsCursorOverTargetOrOverlay())
            ClaimInteractionFocus();

        if (OwnsInteractionFocus && !_interactionFocusPaused)
            FocusTargetWindow();
    }

    private void FocusTargetWindow() =>
        FocusInputWindow(_target);

    private bool OwnsInteractionFocus =>
        ReferenceEquals(s_interactionFocusOwner, this);

    private void ClaimInteractionFocus()
    {
        if (!_interactionsEnabled || _resourcesDisposed || IsDisposed)
            return;

        if (!OwnsInteractionFocus)
            _focusedInputWindow = 0;

        s_interactionFocusOwner = this;
    }

    private void ReleaseInteractionFocus()
    {
        if (OwnsInteractionFocus)
            s_interactionFocusOwner = null;
    }

    private bool IsCursorOverTargetOrOverlay()
    {
        if (!GetCursorPos(out NativePoint cursor))
            return false;

        nint window = WindowFromPoint(cursor);
        return IsOwnWindow(window, Handle)
            || IsOwnWindow(window, _interactionButtonHost.Handle)
            || IsOwnWindow(window, _recropButtonHost.Handle)
            || IsOwnWindow(window, _opacitySliderHost.Handle)
            || IsOwnWindow(window, _clickThroughButtonHost.Handle)
            || IsOwnWindow(window, _closeButtonHost.Handle)
            || IsOwnWindow(window, _interactionHandleHost.Handle)
            || IsOwnWindow(window, _target);
    }

    private static int GetDpi(nint handle) =>
        Math.Max(
            DefaultDpi,
            (int)GetDpiForWindow(handle));

    private static int ToDevice(int logicalPixels, int dpi) =>
        (int)Math.Round(
            logicalPixels * Math.Max(DefaultDpi, dpi) / (double)DefaultDpi);

    private static Size ToDevice(Size logicalSize, int dpi) =>
        new(
            ToDevice(logicalSize.Width, dpi),
            ToDevice(logicalSize.Height, dpi));

    private static NativeRect ToRelativeNativeRect(
        Rectangle bounds,
        int originX,
        int originY) =>
        new()
        {
            Left = bounds.Left - originX,
            Top = bounds.Top - originY,
            Right = bounds.Right - originX,
            Bottom = bounds.Bottom - originY
        };

    private static bool NeedsRegionBackedParking(nint handle)
    {
        _ = GetWindowThreadProcessId(handle, out uint processId);
        if (processId == 0)
            return false;

        try
        {
            string name = Process.GetProcessById((int)processId).ProcessName;
            // Known DWM-hostile Chromium hosts whose thumbnails go black when
            // alpha-parked; consider a capture-health probe if this list grows.
            return name.Equals("Discord", StringComparison.OrdinalIgnoreCase)
                || name.Equals("msedge", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static Icon? TryGetSourceIcon(nint handle)
    {
        foreach (int size in new[]
                 {
                     IconSmall2,
                     IconSmall,
                     IconBig
                 })
        {
            _ = SendMessageTimeout(
                handle,
                WindowMessageGetIcon,
                size,
                0,
                AbortIfHung,
                100,
                out nint icon);
            if (icon != 0)
                return CloneIcon(icon);
        }

        nint classIcon = GetClassLongPtr(handle, ClassSmallIcon);
        return CloneIcon(
            classIcon != 0
                ? classIcon
                : GetClassLongPtr(handle, ClassIcon));
    }

    private static Icon? CloneIcon(nint handle)
    {
        if (handle == 0)
            return null;

        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateThumbnail() =>
        _ = UpdateThumbnailProperties();

    private bool RegisterThumbnail() =>
        DwmRegisterThumbnail(Handle, _target, out _thumbnail) == 0
        && UpdateThumbnailProperties();

    private void RefreshThumbnail()
    {
        UnregisterThumbnail();

        _ = RegisterThumbnail();
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnail == 0)
            return;

        _ = DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
    }

    private bool UpdateThumbnailProperties()
    {
        Size clientPixelSize = GetClientPixelSize();
        if (_thumbnail == 0 || clientPixelSize.Width <= 0 || clientPixelSize.Height <= 0)
            return false;

        var properties = new DwmThumbnailProperties
        {
            Flags = DwmThumbnailPropertyFlags.Visible
                | DwmThumbnailPropertyFlags.SourceRectangle
                | DwmThumbnailPropertyFlags.DestinationRectangle,
            Visible = true,
            Opacity = byte.MaxValue,
            Source = _thumbnailSourceRectangle,
            Destination = new()
            {
                Left = 0,
                Top = 0,
                Right = clientPixelSize.Width,
                Bottom = clientPixelSize.Height
            }
        };

        return DwmUpdateThumbnailProperties(
            _thumbnail,
            ref properties) == 0;
    }

    private sealed class InteractionButtonForm : InteractionHostForm
    {
        private bool _hovered;
        private bool _pressed;

        public InteractionButtonForm() : base(noActivate: false)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            Cursor = Cursors.Hand;
            Font = new("Segoe UI", 8.25f);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCloseGlyph { get; init; }

        public void SetButtonText(string value)
        {
            Text = value;
            AccessibleName = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = RoundedGeometry.CreatePath(
                bounds,
                ToDevice(5, DeviceDpi));
            using var brush = new SolidBrush(
                _pressed
                    ? UiTheme.ButtonPressed
                    : _hovered
                        ? UiTheme.ButtonHover
                        : UiTheme.ButtonBackground);
            using var pen = new Pen(UiTheme.Border)
            {
                Alignment = PenAlignment.Inset
            };
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            if (ShowCloseGlyph)
            {
                DrawCloseGlyph(e.Graphics);
                return;
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine);
        }

        private void DrawCloseGlyph(Graphics graphics)
        {
            float extent = Math.Min(
                ToDevice(4, DeviceDpi),
                Math.Min(ClientSize.Width, ClientSize.Height) * 0.22f);
            if (extent <= 0)
                return;

            float centerX = ClientSize.Width / 2f;
            float centerY = ClientSize.Height / 2f;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var glyphPen = new Pen(
                _hovered || _pressed ? Color.White : Color.Gainsboro,
                Math.Max(1.4f, ToDevice(1, DeviceDpi) * 1.4f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(
                glyphPen,
                centerX - extent,
                centerY - extent,
                centerX + extent,
                centerY + extent);
            graphics.DrawLine(
                glyphPen,
                centerX - extent,
                centerY + extent,
                centerX + extent,
                centerY - extent);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }
    }

    private sealed class InteractionSliderForm : InteractionHostForm
    {
        private const int ValueTextWidth = 42;

        private readonly RoundedSlider _slider = new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Margin = Padding.Empty
        };
        private bool _showValue;

        public event EventHandler? ValueChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _slider.Value;
            set
            {
                _slider.Value = value;
                Invalidate();
            }
        }

        public InteractionSliderForm() : base(noActivate: false)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            Controls.Add(_slider);
            _slider.ValueChanged += (_, _) =>
            {
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public void SetShowValue(bool visible)
        {
            if (_showValue == visible)
                return;

            _showValue = visible;
            LayoutSlider();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutSlider();
        }

        private void LayoutSlider()
        {
            int horizontalPadding = UiScale.ToDevice(8, DeviceDpi);
            int valueWidth = _showValue
                ? UiScale.ToDevice(ValueTextWidth, DeviceDpi)
                : 0;
            _slider.Bounds = new(
                horizontalPadding,
                0,
                Math.Max(1, Width - horizontalPadding * 2 - valueWidth),
                Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = RoundedGeometry.CreatePath(
                bounds,
                ToDevice(5, DeviceDpi));
            using var brush = new SolidBrush(UiTheme.ButtonBackground);
            using var pen = new Pen(UiTheme.Border)
            {
                Alignment = PenAlignment.Inset
            };
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            if (!_showValue)
                return;

            Rectangle valueBounds = new(
                Width - UiScale.ToDevice(ValueTextWidth + 4, DeviceDpi),
                0,
                UiScale.ToDevice(ValueTextWidth, DeviceDpi),
                Height);
            TextRenderer.DrawText(
                e.Graphics,
                $"{Value}%",
                Font,
                valueBounds,
                Color.White,
                TextFormatFlags.Right
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine);
        }
    }

    private sealed class InteractionHandleForm : InteractionHostForm
    {
        private InteractionSide _side;
        private bool _hovered;
        private bool _pressed;

        public InteractionHandleForm()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
        }

        public void SetSide(InteractionSide side)
        {
            _side = side;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            using var brush = new SolidBrush(
                _pressed
                    ? UiTheme.ButtonPressed
                    : _hovered
                        ? UiTheme.ButtonHover
                        : UiTheme.ButtonBackground);
            using GraphicsPath path = RoundedGeometry.CreatePath(
                bounds,
                ToDevice(6, DeviceDpi));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UiTheme.Border)
            {
                Alignment = PenAlignment.Inset
            };
            e.Graphics.DrawPath(pen, path);

            using var dotBrush = new SolidBrush(UiTheme.SecondaryText);
            bool vertical = _side is InteractionSide.Left or InteractionSide.Right;
            int spacing = ToDevice(5, DeviceDpi);
            int radius = ToDevice(2, DeviceDpi);
            Point center = new(Width / 2, Height / 2);
            for (int offset = -spacing; offset <= spacing; offset += spacing)
            {
                int x = center.X + (vertical ? 0 : offset);
                int y = center.Y + (vertical ? offset : 0);
                e.Graphics.FillEllipse(
                    dotBrush,
                    x - radius,
                    y - radius,
                    radius * 2,
                    radius * 2);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }
    }

    private enum InteractionSide
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private class InteractionHostForm : Form
    {
        private readonly bool _noActivate;

        public InteractionHostForm(bool noActivate = true)
        {
            _noActivate = noActivate;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = UiTheme.AppBackground;
        }

        protected override bool ShowWithoutActivation => _noActivate;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowTheme.ApplySmallCorners(Handle);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                if (_noActivate)
                    parameters.ExStyle |= (int)ExtendedWindowStyle.NoActivate;
                return parameters;
            }
        }
    }
}
