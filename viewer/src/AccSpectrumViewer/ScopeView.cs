// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using YaesuAccScope;

namespace AccSpectrumViewer;

/// <summary>
/// One receiver's scope: a trace above a waterfall, or the last 40 traces in
/// perspective (3DSS), with the frequency scale from the radio's own VFO and
/// span. It draws the way tools/live_view.py does.
/// </summary>
internal sealed class ScopeView : Control
{
    private const int Points = AccFrame.Points;
    private const int FallRows = 300;
    private const int Depth = 40;

    private readonly int[] _fall = new int[Points * FallRows];
    private readonly Bitmap _fallBitmap = new(Points, FallRows, PixelFormat.Format32bppRgb);
    private readonly List<byte[]> _history = new();     // newest first
    private readonly Pen[] _depthPens = Enumerable.Range(0, Depth).Select(k => new Pen(Palette.Depth(k, Depth), k == 0 ? 2f : 1f)).ToArray();
    private readonly Pen _tracePen = new(Color.FromArgb(0xFF, 0xD4, 0x00));
    private readonly Pen _gridPen = new(Color.FromArgb(0x22, 0x22, 0x22));
    private readonly Pen _edgePen = new(Color.FromArgb(0x33, 0x33, 0x33));
    private readonly Pen _vfoPen = new(Color.FromArgb(0xAA, 0x33, 0x33)) { DashPattern = new[] { 3f, 3f } };
    private readonly Brush _scaleBrush = new SolidBrush(Color.FromArgb(0x88, 0xAA, 0xAA));
    private readonly Brush _edgeBrush = new SolidBrush(Color.FromArgb(0x55, 0x66, 0x66));
    private readonly Font _titleFont;
    private readonly Font _scaleFont;
    private ColourStretch? _stretch;
    private double? _traceTop;
    private bool _fallChanged;
    private ReceiverScope? _scope;
    private string? _message = "Waiting for frames...";

    public ScopeView(string name)
    {
        ReceiverName = name;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        ForeColor = Color.White;
        _titleFont = new Font(DefaultFont, FontStyle.Bold);
        _scaleFont = new Font(DefaultFont.FontFamily, DefaultFont.Size * 0.85f);
        Array.Fill(_fall, unchecked((int)0xFF000000));
        _fallChanged = true;
    }

    /// <summary>"Main" or "Sub".</summary>
    public string ReceiverName { get; }

    /// <summary>True to draw 3DSS instead of the trace and waterfall.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Show3dss { get; set; }

    /// <summary>Works the colours out again from the next trace.</summary>
    public void ResetColours()
    {
        _stretch = null;
        _traceTop = null;
    }

    /// <summary>Adds a trace: a waterfall row and a 3DSS line. Call <see cref="Control.Invalidate()"/> after.</summary>
    public void AddTrace(ReceiverScope scope)
    {
        var previous = _scope;
        _scope = scope;
        _message = scope.IsValid ? null : "No frequency in the frame. Is the SPI mode right?";
        var levels = scope.Levels;
        // The colours follow the levels slowly, and start again at once when
        // the picture changes: a new span or band, or the radio starting to
        // send real frames after being switched on.
        var target = ColourStretch.From(levels);
        bool fresh = _stretch == null || NewPicture(previous, scope);
        _stretch = fresh ? target : _stretch!.Towards(target, FollowRate);
        _traceTop = NextTraceTop(fresh ? null : _traceTop, levels.Max());

        Array.Copy(_fall, 0, _fall, Points, Points * (FallRows - 1));
        var lookup = _stretch.Lookup;
        for (int i = 0; i < Points; i++) _fall[i] = Palette.Waterfall[lookup[levels[i]]];
        _fallChanged = true;

        _history.Insert(0, levels);
        if (_history.Count > Depth) _history.RemoveAt(_history.Count - 1);
    }

    private const double FollowRate = 0.04;
    private const double TopFallRate = 0.01;
    private const double Headroom = 0.1;

    /// <summary>
    /// The level at the top of the trace: up at once to a stronger peak, so a
    /// signal never goes off the top, and back down slowly, 1% of the gap a
    /// frame, so the scale doesn't jump about as FT8 signals come and go.
    /// </summary>
    internal static double NextTraceTop(double? top, int peak) =>
        top == null || peak >= top ? peak : top.Value - (top.Value - peak) * TopFallRate;

    /// <summary>The trace's bottom level and its range, with 10% headroom above the top.</summary>
    internal static (int Low, double Range) TraceScale(int low, double top) =>
        (low, Math.Max((top - low) * (1 + Headroom), 1));

    private (int Low, double Range) TraceScale() => TraceScale(_stretch!.TraceLow, Math.Max(_traceTop ?? _stretch.High, _stretch.Low + 20));

    /// <summary>True when the scope shows something else: valid now and not before, another span, or moved by more than half a span.</summary>
    internal static bool NewPicture(ReceiverScope? before, ReceiverScope now)
    {
        if (before == null || before.IsValid != now.IsValid) return true;
        if (!now.IsValid) return false;
        return before.SpanHz != now.SpanHz || Math.Abs(before.CentreHz - now.CentreHz) > now.SpanHz / 2;
    }

    /// <summary>Shows a message in place of the tuning, such as when the board is lost.</summary>
    public void ShowMessage(string message)
    {
        _message = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int titleHeight = _titleFont.Height + 6;
        DrawTitle(g);
        var area = new Rectangle(0, titleHeight, Width, Math.Max(Height - titleHeight, 1));

        if (Show3dss)
        {
            Draw3dss(g, area);
            DrawScale(g, new Rectangle(area.X, area.Y, area.Width, _scaleFont.Height * 2 + 4), grid: false);
            return;
        }

        int traceHeight = Math.Max(area.Height * 7 / 10, 40);   // the waterfall gets the other 3/10
        var trace = new Rectangle(area.X, area.Y, area.Width, traceHeight);
        var fall = new Rectangle(area.X, trace.Bottom, area.Width, Math.Max(area.Bottom - trace.Bottom, 1));
        DrawScale(g, trace, grid: true);
        DrawTrace(g, trace);
        DrawWaterfall(g, fall);
    }

    private void DrawTitle(Graphics g)
    {
        string text = ReceiverName;
        var s = _scope;
        if (s != null && s.IsValid)
        {
            text += $"    VFO {Khz(s.VfoHz)} kHz    span {Khz(s.SpanHz)} kHz    {s.Placement}";
            if (s.Is3dss) text += "    (radio in 3DSS)";
        }
        if (_message != null) text += "    " + _message;
        TextRenderer.DrawText(g, text, _titleFont, new Point(4, 3), ForeColor, TextFormatFlags.NoPrefix);
    }

    // x of a point's middle, across a rectangle.
    private static float XAt(Rectangle r, double point) => (float)(r.Left + (point + 0.5) * r.Width / Points);

    private void DrawScale(Graphics g, Rectangle r, bool grid)
    {
        double division = ReceiverScope.SpanPoints / 10.0;
        if (grid)
        {
            for (int k = -5; k <= 5; k++)
            {
                float x = XAt(r, ReceiverScope.CentrePoint + k * division);
                g.DrawLine(Math.Abs(k) == 5 ? _edgePen : _gridPen, x, r.Top, x, r.Bottom);
            }
            for (int q = 1; q < 4; q++)
            {
                int y = r.Top + r.Height * q / 4;
                g.DrawLine(_gridPen, r.Left, y, r.Right, y);
            }
        }

        var s = _scope;
        if (s == null || !s.IsValid) return;
        using var centred = new StringFormat { Alignment = StringAlignment.Center };
        for (int k = -4; k <= 4; k += 2)
        {
            float x = XAt(r, ReceiverScope.CentrePoint + k * division);
            g.DrawString(Khz(s.CentreHz + k * s.SpanHz / 10), _scaleFont, _scaleBrush, x, r.Top + 2, centred);
        }
        float left = XAt(r, ReceiverScope.CentrePoint - 5 * division), right = XAt(r, ReceiverScope.CentrePoint + 5 * division);
        g.DrawString(Khz(s.StartHz), _scaleFont, _edgeBrush, left + 3, r.Top + _scaleFont.Height + 2);
        using var rightAligned = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(Khz(s.EndHz), _scaleFont, _edgeBrush, right - 3, r.Top + _scaleFont.Height + 2, rightAligned);

        if (grid)
        {
            double vfoPoint = ReceiverScope.CentrePoint + (s.VfoHz - s.CentreHz) / s.HzPerPoint;
            if (vfoPoint >= 0 && vfoPoint < Points)
            {
                float x = XAt(r, vfoPoint);
                g.DrawLine(_vfoPen, x, r.Top, x, r.Bottom);
            }
        }
    }

    private void DrawTrace(Graphics g, Rectangle r)
    {
        if (_history.Count == 0 || _stretch == null) return;
        var levels = _history[0];
        var (low, range) = TraceScale();
        var points = new PointF[Points];
        for (int i = 0; i < Points; i++)
        {
            double y = r.Bottom - 1 - (levels[i] - low) * (r.Height - 2) / range;
            points[i] = new PointF(XAt(r, i), (float)Math.Clamp(y, r.Top, r.Bottom - 1));
        }
        g.SmoothingMode = SmoothingMode.None;
        g.DrawLines(_tracePen, points);
    }

    private void DrawWaterfall(Graphics g, Rectangle r)
    {
        if (_fallChanged)
        {
            var data = _fallBitmap.LockBits(new Rectangle(0, 0, Points, FallRows), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < FallRows; row++)
                    Marshal.Copy(_fall, row * Points, data.Scan0 + row * data.Stride, Points);
            }
            finally
            {
                _fallBitmap.UnlockBits(data);
            }
            _fallChanged = false;
        }
        int rows = Math.Min(FallRows, r.Height);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        // One row of pixels per trace, newest at the top, so the waterfall's speed doesn't change with the window's height.
        g.DrawImage(_fallBitmap, new Rectangle(r.X, r.Y, r.Width, rows), new Rectangle(0, 0, Points, rows), GraphicsUnit.Pixel);
    }

    // The last 40 traces in perspective, the way the radio draws 3DSS: the
    // front one full width at the bottom, each further back higher, narrower,
    // smaller and leaning right.
    private void Draw3dss(Graphics g, Rectangle r)
    {
        if (_history.Count == 0 || _stretch == null) return;
        var (low, range) = TraceScale();
        const int step = 2;
        int count = (Points + step - 1) / step;
        var line = new PointF[count];
        var shape = new PointF[count + 2];
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int k = _history.Count - 1; k >= 0; k--)
        {
            double depth = (double)k / Depth;
            double scale = 1 - 0.45 * depth;
            double baseline = r.Bottom - 6 - depth * r.Height * 0.55;
            double x0 = r.Left + r.Width * (1 - scale) / 2 + depth * r.Width * 0.08;
            double amplitude = r.Height * 0.42 * scale;
            var levels = _history[k];
            for (int j = 0; j < count; j++)
            {
                int i = j * step;
                double v = Math.Clamp((levels[i] - low) / range, 0, 1);
                line[j] = new PointF((float)(x0 + i * r.Width * scale / Points), (float)(baseline - v * amplitude));
            }
            shape[0] = new PointF(line[0].X, (float)baseline);
            Array.Copy(line, 0, shape, 1, count);
            shape[^1] = new PointF(line[^1].X, (float)baseline);
            g.FillPolygon(Brushes.Black, shape);
            g.DrawLines(_depthPens[k], line);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    /// <summary>Hz as kHz: "14,074", "14,074.5".</summary>
    internal static string Khz(long hz) => (hz / 1000.0).ToString("#,0.###", CultureInfo.InvariantCulture);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fallBitmap.Dispose();
            foreach (var p in _depthPens) p.Dispose();
            _tracePen.Dispose();
            _gridPen.Dispose();
            _edgePen.Dispose();
            _vfoPen.Dispose();
            _scaleBrush.Dispose();
            _edgeBrush.Dispose();
            _titleFont.Dispose();
            _scaleFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
