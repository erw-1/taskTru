using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace taskTru;

internal sealed class RoundedViewport : Panel
{
    private const int LogicalCornerRadius = 8;
    private readonly CornerMask[] _cornerMasks;
    private Panel? _content;

    private int CornerRadius => UiScale.ToDevice(LogicalCornerRadius, DeviceDpi);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel Content
    {
        get => _content ?? throw new InvalidOperationException("Viewport content has not been assigned.");
        set
        {
            if (ReferenceEquals(_content, value))
                return;

            if (_content is not null)
            {
                _content.ControlAdded -= OnContentChanged;
                _content.ControlRemoved -= OnContentChanged;
                _content.LocationChanged -= OnContentBoundsChanged;
                _content.SizeChanged -= OnContentBoundsChanged;
                Controls.Remove(_content);
            }

            _content = value;
            _content.ControlAdded += OnContentChanged;
            _content.ControlRemoved += OnContentChanged;
            _content.LocationChanged += OnContentBoundsChanged;
            _content.SizeChanged += OnContentBoundsChanged;
            Controls.Add(_content);
            UpdateBoundary();
        }
    }

    public RoundedViewport()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);
        _cornerMasks =
        [
            new(this, CornerPosition.TopLeft),
            new(this, CornerPosition.TopRight),
            new(this, CornerPosition.BottomLeft),
            new(this, CornerPosition.BottomRight)
        ];
        Controls.AddRange(_cornerMasks);
    }

    public void UpdateBoundary()
    {
        LayoutCornerMasks();
        foreach (CornerMask mask in _cornerMasks)
            mask.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateBoundary();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _content is not null)
        {
            _content.ControlAdded -= OnContentChanged;
            _content.ControlRemoved -= OnContentChanged;
            _content.LocationChanged -= OnContentBoundsChanged;
            _content.SizeChanged -= OnContentBoundsChanged;
        }

        base.Dispose(disposing);
    }

    private void OnContentChanged(object? sender, ControlEventArgs e)
    {
        foreach (CornerMask mask in _cornerMasks)
            mask.Invalidate();
    }

    private void OnContentBoundsChanged(object? sender, EventArgs e) =>
        UpdateBoundary();

    private void LayoutCornerMasks()
    {
        int width = Math.Max(0, ClientSize.Width);
        int height = Math.Max(0, ClientSize.Height);
        int size = Math.Min(
            CornerRadius,
            Math.Min(width, height));
        if (size <= 0)
        {
            foreach (CornerMask mask in _cornerMasks)
                mask.SetBounds(0, 0, 0, 0);
            return;
        }

        int right = Math.Max(0, width - size);
        int bodyTop = Math.Clamp(_content?.Top ?? 0, 0, height);
        int bodyBottom = Math.Clamp(_content?.Bottom ?? height, bodyTop, height);
        int bottom = Math.Max(bodyTop, bodyBottom - size);

        _cornerMasks[0].SetBounds(0, bodyTop, size, size);
        _cornerMasks[1].SetBounds(right, bodyTop, size, size);
        _cornerMasks[2].SetBounds(0, bottom, size, size);
        _cornerMasks[3].SetBounds(right, bottom, size, size);
        foreach (CornerMask mask in _cornerMasks)
            mask.BringToFront();
    }

    private Color GetContentColor(int viewportY)
    {
        if (_content is null)
            return BackColor;

        int contentY = viewportY - _content.Top;
        if (contentY < 0 || contentY >= _content.Height)
            return BackColor;

        Control? row = _content.GetChildAtPoint(new(1, contentY), GetChildAtPointSkip.Invisible);
        return row?.BackColor ?? _content.BackColor;
    }

    private enum CornerPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private sealed class CornerMask : Control
    {
        private const int SampleCount = 8;
        private readonly RoundedViewport _viewport;
        private readonly CornerPosition _position;
        private float[] _coverage = [];
        private int[] _pixels = [];
        private Color[] _rowColors = [];
        private Bitmap? _bitmap;
        private Color _renderedBackground;
        private int _coverageRadius;

        public CornerMask(RoundedViewport viewport, CornerPosition position)
        {
            _viewport = viewport;
            _position = position;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            EnsureBitmap();
            bool changed = _renderedBackground != _viewport.BackColor;
            for (int y = 0; y < ClientSize.Height; y++)
            {
                Color contentColor = _viewport.GetContentColor(Top + y);
                if (_rowColors[y] != contentColor)
                {
                    _rowColors[y] = contentColor;
                    changed = true;
                }
            }

            if (changed)
            {
                _renderedBackground = _viewport.BackColor;
                for (int y = 0; y < ClientSize.Height; y++)
                {
                    int rowOffset = y * ClientSize.Width;
                    for (int x = 0; x < ClientSize.Width; x++)
                    {
                        _pixels[rowOffset + x] = ColorMath.Blend(
                            _renderedBackground,
                            _rowColors[y],
                            GetCoverage(x, y)).ToArgb();
                    }
                }

                BitmapData data = _bitmap!.LockBits(
                    new(Point.Empty, ClientSize),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppPArgb);
                try
                {
                    Marshal.Copy(_pixels, 0, data.Scan0, _pixels.Length);
                }
                finally
                {
                    _bitmap.UnlockBits(data);
                }
            }

            e.Graphics.DrawImageUnscaled(_bitmap!, Point.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _bitmap?.Dispose();

            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message message)
        {
            const int WindowMessageNonClientHitTest = 0x0084;
            const int HitTestTransparent = -1;

            if (message.Msg == WindowMessageNonClientHitTest)
            {
                message.Result = HitTestTransparent;
                return;
            }

            base.WndProc(ref message);
        }

        private void EnsureBitmap()
        {
            if (_bitmap is not null && _bitmap.Size == ClientSize)
                return;

            _bitmap?.Dispose();
            _bitmap = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            _pixels = new int[ClientSize.Width * ClientSize.Height];
            _rowColors = new Color[ClientSize.Height];
            _renderedBackground = Color.Empty;
        }

        private float GetCoverage(int x, int y)
        {
            int radius = _viewport.CornerRadius;
            if (_coverageRadius != radius)
            {
                _coverage = CreateCoverageMask(radius);
                _coverageRadius = radius;
            }

            bool right = _position is CornerPosition.TopRight or CornerPosition.BottomRight;
            bool bottom = _position is CornerPosition.BottomLeft or CornerPosition.BottomRight;
            int sourceX = right ? radius - 1 - x : x;
            int sourceY = bottom ? radius - 1 - y : y;
            return _coverage[sourceY * radius + sourceX];
        }

        private static float[] CreateCoverageMask(int radius)
        {
            const int totalSamples = SampleCount * SampleCount;
            var coverage = new float[radius * radius];
            for (int y = 0; y < radius; y++)
            {
                for (int x = 0; x < radius; x++)
                {
                    int inside = 0;
                    for (int sampleY = 0; sampleY < SampleCount; sampleY++)
                    {
                        double pixelY = y + (sampleY + 0.5) / SampleCount;
                        double deltaY = pixelY - radius;
                        for (int sampleX = 0; sampleX < SampleCount; sampleX++)
                        {
                            double pixelX = x + (sampleX + 0.5) / SampleCount;
                            double deltaX = pixelX - radius;
                            if (deltaX * deltaX + deltaY * deltaY <= radius * radius)
                                inside++;
                        }
                    }

                    coverage[y * radius + x] = inside / (float)totalSamples;
                }
            }

            return coverage;
        }
    }
}
