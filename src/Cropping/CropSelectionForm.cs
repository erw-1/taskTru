using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using static taskTru.NativeMethods;

namespace taskTru;

internal sealed class CropSelectionForm : Form
{
    private const int LogicalMinimumCropSize = 20;
    private const int LogicalFinishWidth = 84;
    private const int LogicalAutoVideoWidth = 128;
    private const int LogicalFinishHeight = 28;
    private const int LogicalButtonGap = 6;
    private const int LogicalHitPadding = 9;
    private const int LogicalHandleRadius = 6;
    private const int DefaultDpi = 96;
    private const int CancelHotKeyId = 0x7411;
    private const int ConfirmHotKeyId = 0x7412;
    private const string CropToolTipText =
        "Drag to draw a crop. Drag inside to move it, drag red edges to resize, Enter finishes, and Esc cancels.";

    private static readonly Color AccentColor =
        Color.FromArgb(255, 55, 55);
    private static readonly Color TransparencyColor =
        Color.Fuchsia;

    private readonly nint _target;
    private readonly Rectangle _targetBounds;
    private readonly int _selectionDpi;
    private readonly CropFinishForm _finishForm = new();
    private readonly ToolTip _cropToolTip = UiToolTips.Create(6500);
    private readonly Rectangle? _autoVideoSelection;
    private CropInputShield? _inputShield;
    private Rectangle _selection;
    private Rectangle _startingSelection;
    private Point _dragStart;
    private Point _adjustStart;
    private SelectionAction _action;
    private bool _creating;
    private bool _adjusting;
    private bool _resourcesDisposed;
    private ulong _gestureZoomStartDistance;
    private Rectangle _gestureZoomStartSelection;

    public Rectangle SelectedBounds { get; private set; }

    public CropSelectionForm(
        nint target,
        Rectangle? initialSelection = null,
        Rectangle? autoVideoSelection = null)
    {
        _target = target;
        if (!WindowGeometry.TryGetFrameBounds(
                target,
                out _targetBounds))
        {
            throw new InvalidOperationException(
                "The selected window is no longer available.");
        }

        _selectionDpi = Math.Max(
            DefaultDpi,
            (int)GetDpiForWindow(target));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = _targetBounds;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = TransparencyColor;
        TransparencyKey = TransparencyColor;
        Opacity = 0.72;
        Cursor = Cursors.Cross;

        _finishForm.FinishRequested += OnFinishRequested;
        _finishForm.AutoVideoCropRequested += OnAutoVideoCropRequested;
        _finishForm.CancelRequested += OnCancelRequested;
        _cropToolTip.SetToolTip(this, CropToolTipText);

        if (initialSelection is { } selection)
            SetInitialSelection(selection);

        _autoVideoSelection = ValidSelection(autoVideoSelection);
        _finishForm.SetAutoVideoCropAvailable(_autoVideoSelection is not null);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UseWaitCursor = false;
        Cursor = Cursors.Cross;
        ShowInputShield();
        UpdateFinishButton();
        BringToFront();
        Activate();
        ArrangeInputWindows();
    }

    private void SetInitialSelection(Rectangle screenSelection)
    {
        Rectangle selection = Rectangle.Intersect(
            screenSelection,
            _targetBounds);
        if (selection.Width < MinimumCropSize
            || selection.Height < MinimumCropSize)
        {
            return;
        }

        _selection = ToLocal(selection);
        _adjusting = true;
        Cursor = CursorFor(HitTest(_selection.Location));
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(
            Handle,
            CancelHotKeyId,
            HotKeyModifiers.NoRepeat,
            (uint)Keys.Escape);
        RegisterHotKey(
            Handle,
            ConfirmHotKeyId,
            HotKeyModifiers.NoRepeat,
            (uint)Keys.Enter);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, CancelHotKeyId);
        UnregisterHotKey(Handle, ConfirmHotKeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowMessageHotKey)
        {
            switch (message.WParam.ToInt32())
            {
                case CancelHotKeyId:
                    CancelSelection();
                    return;
                case ConfirmHotKeyId when _adjusting:
                    ConfirmSelection();
                    return;
            }
        }
        else if (message.Msg == WindowMessageGesture)
        {
            HandleZoomGesture(message.LParam);
            message.Result = 1;
            return;
        }

        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(
        ref Message message,
        Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            CancelSelection();
            return true;
        }

        if (_adjusting && keyData == Keys.Enter)
        {
            ConfirmSelection();
            return true;
        }

        return base.ProcessCmdKey(
            ref message,
            keyData);
    }

    private void ConfirmSelection()
    {
        if (_selection.Width < MinimumCropSize
            || _selection.Height < MinimumCropSize)
        {
            return;
        }

        SelectedBounds = new(
            Left + _selection.Left,
            Top + _selection.Top,
            _selection.Width,
            _selection.Height);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelSelection()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnFinishRequested(object? sender, EventArgs e) =>
        ConfirmSelection();

    private void OnAutoVideoCropRequested(object? sender, EventArgs e)
    {
        if (_autoVideoSelection is not { } selection)
            return;

        SelectedBounds = selection;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs e) =>
        CancelSelection();

    private void DisposeResources()
    {
        if (_resourcesDisposed)
            return;

        _resourcesDisposed = true;
        CloseFinishForm();
        CloseInputShield();
        _cropToolTip.Dispose();
    }

    private void CloseFinishForm()
    {
        _finishForm.FinishRequested -= OnFinishRequested;
        _finishForm.AutoVideoCropRequested -= OnAutoVideoCropRequested;
        _finishForm.CancelRequested -= OnCancelRequested;
        _finishForm.Close();
        _finishForm.Dispose();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HandleMouseDown(e, this);
    }

    private void HandleMouseDown(MouseEventArgs e, Control captureOwner)
    {
        if (e.Button != MouseButtons.Left)
        {
            if (e.Button == MouseButtons.Right)
                CancelSelection();

            return;
        }

        if (!_adjusting)
        {
            BeginSelection(e.Location, captureOwner);
            return;
        }

        _action = HitTest(e.Location);
        if (_action == SelectionAction.None)
        {
            BeginSelection(e.Location, captureOwner);
            return;
        }

        _startingSelection = _selection;
        _adjustStart = e.Location;
        // Keep the floating buttons still while dragging: repositioning them under a
        // fast-moving cursor spawns synthetic mouse messages that starve WM_PAINT and
        // leave the rectangle behind.
        _finishForm.Hide();
        captureOwner.Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        HandleMouseMove(e);
    }

    private void HandleMouseMove(MouseEventArgs e)
    {
        if (_creating)
        {
            Point current = ClampToTarget(e.Location);
            _selection = Rectangle.FromLTRB(
                Math.Min(_dragStart.X, current.X),
                Math.Min(_dragStart.Y, current.Y),
                Math.Max(_dragStart.X, current.X),
                Math.Max(_dragStart.Y, current.Y));
            RepaintNow();
            return;
        }

        if (_adjusting && _action != SelectionAction.None)
        {
            ResizeSelection(e.Location);
            RepaintNow();
            return;
        }

        Cursor = _adjusting
            ? CursorFor(HitTest(e.Location))
            : Cursors.Cross;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        HandleMouseUp(e, this);
    }

    private void HandleMouseUp(MouseEventArgs e, Control captureOwner)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_creating)
        {
            _creating = false;
            if (_selection.Width < MinimumCropSize
                || _selection.Height < MinimumCropSize)
            {
                _selection = ToLocal(_targetBounds);
            }

            _adjusting = true;
            captureOwner.Capture = false;
            Cursor = CursorFor(HitTest(e.Location));
            UpdateFinishButton();
            RepaintNow();
            return;
        }

        _action = SelectionAction.None;
        captureOwner.Capture = false;
        UpdateFinishButton();
        Cursor = CursorFor(HitTest(e.Location));
        RepaintNow();
    }

    // WM_PAINT is starved while drag input floods the queue, so paint synchronously.
    private void RepaintNow()
    {
        Invalidate();
        Update();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        bool hasSelection = !_selection.IsEmpty;
        Rectangle target = ToLocal(_targetBounds);
        Rectangle clearArea = hasSelection ? _selection : target;
        using var shade = new SolidBrush(Color.Black);
        using Region shadeArea = new(ClientRectangle);
        shadeArea.Exclude(clearArea);
        e.Graphics.FillRegion(shade, shadeArea);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var borderPen = new Pen(
            AccentColor,
            ToDevice(
                hasSelection ? 3 : 2,
                _selectionDpi))
        {
            DashStyle = hasSelection
                ? DashStyle.Solid
            : DashStyle.Dash
        };

        Rectangle border = clearArea;
        border.Width = Math.Max(1, border.Width - 1);
        border.Height = Math.Max(1, border.Height - 1);
        e.Graphics.DrawRectangle(borderPen, border);

        if (_adjusting)
            DrawHandles(e.Graphics);

        DrawHint(
            e.Graphics,
            clearArea,
            hasSelection);
    }

    private void DrawHandles(Graphics graphics)
    {
        int radius = ToDevice(
            LogicalHandleRadius,
            _selectionDpi);
        int centerX = _selection.Left + _selection.Width / 2;
        int centerY = _selection.Top + _selection.Height / 2;

        using var brush = new SolidBrush(AccentColor);
        foreach (Point point in new[]
        {
            new Point(_selection.Left, _selection.Top),
            new Point(centerX, _selection.Top),
            new Point(_selection.Right, _selection.Top),
            new Point(_selection.Right, centerY),
            new Point(_selection.Right, _selection.Bottom),
            new Point(centerX, _selection.Bottom),
            new Point(_selection.Left, _selection.Bottom),
            new Point(_selection.Left, centerY)
        })
        {
            graphics.FillEllipse(
                brush,
                point.X - radius,
                point.Y - radius,
                radius * 2,
                radius * 2);
        }
    }

    private void DrawHint(
        Graphics graphics,
        Rectangle anchor,
        bool hasSelection)
    {
        string text = _adjusting
            ? "Drag inside to move  |  Drag outside to redraw  |  Red edges resize  |  Enter to finish  |  Esc to cancel"
            : "Drag to select an area  |  Esc or right-click to cancel";
        Size textSize = TextRenderer.MeasureText(text, Font);
        int padX = ToDevice(4, _selectionDpi);
        int padY = ToDevice(2, _selectionDpi);
        int x = Math.Clamp(
            anchor.Right - textSize.Width - ToDevice(12, _selectionDpi),
            0,
            Math.Max(0, ClientSize.Width - textSize.Width - padX * 2));
        int y = anchor.Top - textSize.Height - ToDevice(6, _selectionDpi);
        if (y < 0)
            y = Math.Min(
                Math.Max(0, ClientSize.Height - textSize.Height - padY * 2),
                anchor.Top + ToDevice(6, _selectionDpi));

        using var background = new SolidBrush(Color.Black);
        graphics.FillRectangle(
            background,
            x,
            y,
            textSize.Width + padX * 2,
            textSize.Height + padY * 2);
        TextRenderer.DrawText(
            graphics,
            text,
            Font,
            new Point(x + padX, y + padY),
            Color.White,
            TextFormatFlags.NoPadding);
    }

    private void BeginSelection(Point location, Control captureOwner)
    {
        Rectangle target = ToLocal(_targetBounds);
        if (!target.Contains(location))
        {
            CancelSelection();
            return;
        }

        _creating = true;
        _adjusting = false;
        _action = SelectionAction.None;
        _dragStart = ClampToTarget(location);
        _selection = new(_dragStart, Size.Empty);
        UpdateFinishButton();
        captureOwner.Capture = true;
        Cursor = Cursors.Cross;
        Invalidate();
    }

    private void UpdateFinishButton()
    {
        bool showFinish = _adjusting && !_selection.IsEmpty;
        bool showAutoVideo = _autoVideoSelection is not null;
        if (!showFinish && !showAutoVideo)
        {
            _finishForm.Hide();
            ArrangeInputWindows();
            return;
        }

        _finishForm.SetShowFinishButton(showFinish);
        _finishForm.SetAutoVideoCropAvailable(showAutoVideo);
        Rectangle anchor = showFinish
            ? _selection
            : ToLocal(_autoVideoSelection!.Value);
        int logicalWidth = showAutoVideo
            ? LogicalAutoVideoWidth
            : LogicalFinishWidth;
        int logicalHeight =
            LogicalFinishHeight
            + (showFinish && showAutoVideo
                ? LogicalButtonGap + LogicalFinishHeight
                : 0);
        Size size = new(
            ToDevice(
                logicalWidth,
                _selectionDpi),
            ToDevice(
                logicalHeight,
                _selectionDpi));
        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        Rectangle bounds = new(
            Math.Clamp(
                Left + anchor.Left + (anchor.Width - size.Width) / 2,
                virtualScreen.Left,
                virtualScreen.Right - size.Width),
            Math.Clamp(
                Top + anchor.Top + (anchor.Height - size.Height) / 2,
                virtualScreen.Top,
                virtualScreen.Bottom - size.Height),
            size.Width,
            size.Height);

        _finishForm.Bounds = bounds;
        if (_finishForm.Visible)
            _finishForm.BringToFront();
        else
        {
            _finishForm.Show(this);
            _finishForm.Bounds = bounds;
        }
        ArrangeInputWindows();
    }

    private void ShowInputShield()
    {
        _inputShield = new(_targetBounds);
        _inputShield.MouseDown += OnShieldMouseDown;
        _inputShield.MouseMove += OnShieldMouseMove;
        _inputShield.MouseUp += OnShieldMouseUp;
        _inputShield.GestureMessage = HandleZoomGesture;
        _cropToolTip.SetToolTip(_inputShield, CropToolTipText);
        _inputShield.Show(this);
        _inputShield.BringToFront();
    }

    private void ArrangeInputWindows()
    {
        _inputShield?.BringToFront();
        if (_finishForm.Visible)
            _finishForm.BringToFront();
    }

    private void CloseInputShield()
    {
        if (_inputShield is null)
            return;

        _inputShield.MouseDown -= OnShieldMouseDown;
        _inputShield.MouseMove -= OnShieldMouseMove;
        _inputShield.MouseUp -= OnShieldMouseUp;
        _inputShield.GestureMessage = null;
        _inputShield.Close();
        _inputShield.Dispose();
        _inputShield = null;
    }

    private void OnShieldMouseDown(object? sender, MouseEventArgs e)
    {
        CropInputShield? shield = _inputShield;
        if (shield is null)
            return;

        HandleMouseDown(
            TranslateShieldMouseEvent(e, shield),
            shield);
        if (!shield.IsDisposed)
            shield.Cursor = Cursor;
    }

    private void OnShieldMouseMove(object? sender, MouseEventArgs e)
    {
        CropInputShield? shield = _inputShield;
        if (shield is null)
            return;

        HandleMouseMove(TranslateShieldMouseEvent(e, shield));
        if (!shield.IsDisposed)
            shield.Cursor = Cursor;
    }

    private void OnShieldMouseUp(object? sender, MouseEventArgs e)
    {
        CropInputShield? shield = _inputShield;
        if (shield is null)
            return;

        HandleMouseUp(
            TranslateShieldMouseEvent(e, shield),
            shield);
        if (!shield.IsDisposed)
            shield.Cursor = Cursor;
    }

    private MouseEventArgs TranslateShieldMouseEvent(
        MouseEventArgs e,
        Control shield)
    {
        Point point = PointToClient(shield.PointToScreen(e.Location));
        return new(
            e.Button,
            e.Clicks,
            point.X,
            point.Y,
            e.Delta);
    }

    private Point ClampToTarget(Point point)
    {
        Rectangle target = ToLocal(_targetBounds);
        return new(
            Math.Clamp(point.X, target.Left, target.Right),
            Math.Clamp(point.Y, target.Top, target.Bottom));
    }

    private void ResizeSelection(Point current)
    {
        int dx = current.X - _adjustStart.X;
        int dy = current.Y - _adjustStart.Y;
        Rectangle updated = _startingSelection;

        switch (_action)
        {
            case SelectionAction.Move:
                updated.Offset(dx, dy);
                break;
            case SelectionAction.TopLeft:
                updated = Rectangle.FromLTRB(
                    updated.Left + dx,
                    updated.Top + dy,
                    updated.Right,
                    updated.Bottom);
                break;
            case SelectionAction.Top:
                updated = Rectangle.FromLTRB(
                    updated.Left,
                    updated.Top + dy,
                    updated.Right,
                    updated.Bottom);
                break;
            case SelectionAction.TopRight:
                updated = Rectangle.FromLTRB(
                    updated.Left,
                    updated.Top + dy,
                    updated.Right + dx,
                    updated.Bottom);
                break;
            case SelectionAction.Right:
                updated.Width += dx;
                break;
            case SelectionAction.BottomRight:
                updated.Width += dx;
                updated.Height += dy;
                break;
            case SelectionAction.Bottom:
                updated.Height += dy;
                break;
            case SelectionAction.BottomLeft:
                updated = Rectangle.FromLTRB(
                    updated.Left + dx,
                    updated.Top,
                    updated.Right,
                    updated.Bottom + dy);
                break;
            case SelectionAction.Left:
                updated = Rectangle.FromLTRB(
                    updated.Left + dx,
                    updated.Top,
                    updated.Right,
                    updated.Bottom);
                break;
        }

        _selection = NormalizeMinimumSize(updated, _action);
        ClampSelection();
    }

    private SelectionAction HitTest(Point point)
    {
        if (_selection.IsEmpty)
            return SelectionAction.None;

        Rectangle selection = _selection;
        Point topLeft = new(selection.Left, selection.Top);
        Point topRight = new(selection.Right, selection.Top);
        Point bottomLeft = new(selection.Left, selection.Bottom);
        Point bottomRight = new(selection.Right, selection.Bottom);

        if (Near(point, topLeft))
            return SelectionAction.TopLeft;
        if (Near(point, topRight))
            return SelectionAction.TopRight;
        if (Near(point, bottomLeft))
            return SelectionAction.BottomLeft;
        if (Near(point, bottomRight))
            return SelectionAction.BottomRight;
        if (Math.Abs(point.Y - selection.Top) <= HitPadding
            && point.X >= selection.Left
            && point.X <= selection.Right)
        {
            return SelectionAction.Top;
        }

        if (Math.Abs(point.Y - selection.Bottom) <= HitPadding
            && point.X >= selection.Left
            && point.X <= selection.Right)
        {
            return SelectionAction.Bottom;
        }

        if (Math.Abs(point.X - selection.Left) <= HitPadding
            && point.Y >= selection.Top
            && point.Y <= selection.Bottom)
        {
            return SelectionAction.Left;
        }

        if (Math.Abs(point.X - selection.Right) <= HitPadding
            && point.Y >= selection.Top
            && point.Y <= selection.Bottom)
        {
            return SelectionAction.Right;
        }

        return selection.Contains(point)
            ? SelectionAction.Move
            : SelectionAction.None;
    }

    private Rectangle NormalizeMinimumSize(
        Rectangle rectangle,
        SelectionAction action)
    {
        int left = rectangle.Left;
        int top = rectangle.Top;
        int right = rectangle.Right;
        int bottom = rectangle.Bottom;

        if (right - left < MinimumCropSize)
        {
            if (action is SelectionAction.Left
                or SelectionAction.TopLeft
                or SelectionAction.BottomLeft)
            {
                left = right - MinimumCropSize;
            }
            else
            {
                right = left + MinimumCropSize;
            }
        }

        if (bottom - top < MinimumCropSize)
        {
            if (action is SelectionAction.Top
                or SelectionAction.TopLeft
                or SelectionAction.TopRight)
            {
                top = bottom - MinimumCropSize;
            }
            else
            {
                bottom = top + MinimumCropSize;
            }
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private bool Near(Point first, Point second) =>
        Math.Abs(first.X - second.X) <= HitPadding
        && Math.Abs(first.Y - second.Y) <= HitPadding;

    private static Cursor CursorFor(SelectionAction action) =>
        action switch
        {
            SelectionAction.Move => Cursors.SizeAll,
            SelectionAction.TopLeft or SelectionAction.BottomRight
                => Cursors.SizeNWSE,
            SelectionAction.TopRight or SelectionAction.BottomLeft
                => Cursors.SizeNESW,
            SelectionAction.Top or SelectionAction.Bottom
                => Cursors.SizeNS,
            SelectionAction.Left or SelectionAction.Right
                => Cursors.SizeWE,
            _ => Cursors.Cross
        };

    private void ClampSelection()
    {
        Rectangle target = ToLocal(_targetBounds);
        _selection.Width = Math.Min(_selection.Width, target.Width);
        _selection.Height = Math.Min(_selection.Height, target.Height);
        _selection.X = Math.Clamp(
            _selection.X,
            target.Left,
            target.Right - _selection.Width);
        _selection.Y = Math.Clamp(
            _selection.Y,
            target.Top,
            target.Bottom - _selection.Height);
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
                || info.Arguments == 0
                || _selection.IsEmpty)
            {
                return;
            }

            if ((info.Flags & GestureFlagBegin) != 0
                || _gestureZoomStartDistance == 0)
            {
                _gestureZoomStartDistance = info.Arguments;
                _gestureZoomStartSelection = _selection;
                return;
            }

            ScaleSelectionFromGesture(
                _gestureZoomStartSelection,
                PointToClient(new(info.Location.X, info.Location.Y)),
                info.Arguments / (double)_gestureZoomStartDistance);
            if ((info.Flags & GestureFlagEnd) != 0)
                _gestureZoomStartDistance = 0;
        }
        finally
        {
            _ = CloseGestureInfoHandle(gestureHandle);
        }
    }

    private void ScaleSelectionFromGesture(
        Rectangle startSelection,
        Point center,
        double scale)
    {
        if (double.IsNaN(scale)
            || double.IsInfinity(scale)
            || scale <= 0)
        {
            return;
        }

        if (!startSelection.Contains(center))
            center = new(
                startSelection.Left + startSelection.Width / 2,
                startSelection.Top + startSelection.Height / 2);

        int width = Math.Max(
            MinimumCropSize,
            (int)Math.Round(startSelection.Width * scale));
        int height = Math.Max(
            MinimumCropSize,
            (int)Math.Round(startSelection.Height * scale));
        double anchorX = Math.Clamp(
            (center.X - startSelection.Left) / (double)Math.Max(1, startSelection.Width),
            0,
            1);
        double anchorY = Math.Clamp(
            (center.Y - startSelection.Top) / (double)Math.Max(1, startSelection.Height),
            0,
            1);
        _selection = new(
            center.X - (int)Math.Round(width * anchorX),
            center.Y - (int)Math.Round(height * anchorY),
            width,
            height);
        _creating = false;
        _adjusting = true;
        _action = SelectionAction.None;
        ClampSelection();
        UpdateFinishButton();
        Invalidate();
    }

    private Rectangle ToLocal(
        Rectangle screenRectangle) =>
        new(
            screenRectangle.X - Left,
            screenRectangle.Y - Top,
            screenRectangle.Width,
            screenRectangle.Height);

    private Rectangle? ValidSelection(Rectangle? screenSelection)
    {
        if (screenSelection is not { } selection)
            return null;

        selection = Rectangle.Intersect(
            selection,
            _targetBounds);
        return selection.Width >= MinimumCropSize
            && selection.Height >= MinimumCropSize
                ? selection
                : null;
    }

    private int MinimumCropSize =>
        ToDevice(
            LogicalMinimumCropSize,
            _selectionDpi);

    private int HitPadding =>
        ToDevice(
            LogicalHitPadding,
            _selectionDpi);

    private static int ToDevice(int logicalPixels, int dpi) =>
        (int)Math.Round(
            logicalPixels * Math.Max(DefaultDpi, dpi) / (double)DefaultDpi);

    // The overlay's transparency-key hole is click-through at the Win32 level,
    // so browsers under it would eat the user's drags. This shield sits above
    // the overlay and owns all pointer input; alpha must be 1/255, not 0,
    // because Windows skips fully transparent layered windows in hit testing.
    private sealed class CropInputShield : Form
    {
        public Action<nint>? GestureMessage;

        public CropInputShield(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = bounds;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 1d / byte.MaxValue;
            Cursor = Cursors.Cross;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= (int)ExtendedWindowStyle.NoActivate;
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WindowMessageGesture
                && GestureMessage is not null)
            {
                GestureMessage(message.LParam);
                message.Result = 1;
                return;
            }

            base.WndProc(ref message);
        }
    }

    private sealed class CropFinishForm : Form
    {
        private readonly RoundedActionButton _finishButton = new()
        {
            Text = "End crop",
            TabStop = false,
            BackColor = UiTheme.ButtonBackground,
            BorderColor = AccentColor,
            BorderWidth = 2,
            ForeColor = Color.White
        };
        private readonly RoundedActionButton _autoVideoButton = new()
        {
            Text = "Auto video crop",
            TabStop = false,
            BackColor = UiTheme.ButtonBackground,
            BorderColor = UiTheme.Accent,
            BorderWidth = 2,
            ForeColor = Color.White,
            Visible = false
        };
        private readonly ToolTip _toolTip = UiToolTips.Create(5000);
        private bool _showFinishButton = true;
        private bool _autoVideoCropAvailable;

        public event EventHandler? FinishRequested;
        public event EventHandler? AutoVideoCropRequested;
        public event EventHandler? CancelRequested;

        public void SetShowFinishButton(bool value)
        {
            if (_showFinishButton == value)
                return;

            _showFinishButton = value;
            _finishButton.Visible = value;
            UpdateButtonBounds();
            UpdateRegion();
        }

        public void SetAutoVideoCropAvailable(bool value)
        {
            if (_autoVideoCropAvailable == value)
                return;

            _autoVideoCropAvailable = value;
            _autoVideoButton.Visible = value;
            UpdateButtonBounds();
            UpdateRegion();
        }

        public CropFinishForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Color.Black;
            _finishButton.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    CancelRequested?.Invoke(this, EventArgs.Empty);
            };
            Controls.Add(_finishButton);
            _autoVideoButton.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    CancelRequested?.Invoke(this, EventArgs.Empty);
            };
            Controls.Add(_autoVideoButton);

            _finishButton.Click += (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty);
            _autoVideoButton.Click += (_, _) => AutoVideoCropRequested?.Invoke(this, EventArgs.Empty);
            _toolTip.SetToolTip(
                _finishButton,
                "Finish the crop. Right-click to cancel.");
            _toolTip.SetToolTip(
                _autoVideoButton,
                "Use the detected video crop.");
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateButtonBounds();
            UpdateRegion();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= (int)ExtendedWindowStyle.NoActivate;
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
                Region?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void UpdateButtonBounds()
        {
            bool showFinish = _showFinishButton;
            bool showAuto = _autoVideoCropAvailable;
            if (showFinish && showAuto)
            {
                int gap = Math.Max(1, Math.Min(8, Height / 10));
                int buttonHeight = Math.Max(1, (Height - gap) / 2);
                _finishButton.Bounds = new(0, 0, Width, buttonHeight);
                _autoVideoButton.Bounds = new(
                    0,
                    buttonHeight + gap,
                    Width,
                    Math.Max(1, Height - buttonHeight - gap));
            }
            else
            {
                _finishButton.Bounds = showFinish
                    ? new(0, 0, Width, Height)
                    : Rectangle.Empty;
                _autoVideoButton.Bounds = showAuto
                    ? new(0, 0, Width, Height)
                    : Rectangle.Empty;
            }
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            using GraphicsPath path = new();
            if (_finishButton.Visible)
                AddButtonPath(path, _finishButton.Bounds);
            if (_autoVideoButton.Visible)
                AddButtonPath(path, _autoVideoButton.Bounds);

            Region? oldRegion = Region;
            Region = new(path);
            oldRegion?.Dispose();
        }

        private static void AddButtonPath(GraphicsPath path, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            int radius = 10;
            bounds.Width = Math.Max(1, bounds.Width - 1);
            bounds.Height = Math.Max(1, bounds.Height - 1);
            path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
        }
    }

    private enum SelectionAction
    {
        None,
        Move,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }
}
