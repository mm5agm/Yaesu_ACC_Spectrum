// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

namespace AccSpectrumViewer;

/// <summary>
/// The wiring check along the bottom of the window: a coloured light and
/// reading for each input, wrapping onto more lines if the window is narrow,
/// and a line of advice. Hovering over a reading says where it's wired.
/// </summary>
internal sealed class WiringView : Control
{
    private const int Gap = 18;
    private readonly Font _nameFont = new(DefaultFont, FontStyle.Bold);
    private readonly Font _valueFont = new(DefaultFont, FontStyle.Regular);
    private readonly ToolTip _tip = new();
    private readonly List<(Rectangle Box, CheckItem Item)> _boxes = new();
    private WiringReport _report = WiringCheck.Evaluate(null, null, null);
    private string _header = "Wiring check";
    private CheckItem? _hovered;

    public WiringView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(0x14, 0x14, 0x14);
        ForeColor = Color.Gainsboro;
        Height = Arrange(1000).Height;
    }

    /// <summary>The report shown now.</summary>
    public WiringReport Report => _report;

    /// <summary>Shows a new report, with a header such as "Firmware 2.0.1, up 49 s".</summary>
    public void Show(WiringReport report, string header)
    {
        _report = report;
        _header = header;
        FitHeight();
        Invalidate();
    }

    internal static Color ColourOf(CheckState state) => state switch
    {
        CheckState.Good => Color.FromArgb(0x3C, 0xC8, 0x50),
        CheckState.Warning => Color.FromArgb(0xF0, 0xB4, 0x1E),
        CheckState.Bad => Color.FromArgb(0xE6, 0x3C, 0x32),
        CheckState.Info => Color.FromArgb(0x50, 0x78, 0xB4),
        _ => Color.FromArgb(0x60, 0x60, 0x60),
    };

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    private void FitHeight()
    {
        if (Width <= 0) return;
        int h = Arrange(Width).Height;
        if (h != Height) Height = h;
    }

    private int Dot => _nameFont.Height - 6;

    private Size Measure(string text, Font font) => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

    // Where each part goes for a given width.
    private (List<(Rectangle Box, CheckItem? Item, string Text)> Parts, Rectangle Advice, int Height) Arrange(int width)
    {
        var parts = new List<(Rectangle, CheckItem?, string)>();
        int line = _nameFont.Height + 4;
        int x = 8, y = 6;
        var header = Measure(_header, _nameFont);
        parts.Add((new Rectangle(x, y, header.Width, line), null, _header));
        x += header.Width + Gap;
        foreach (var item in _report.Items)
        {
            int w = Dot + 6 + Measure(item.Name + " ", _nameFont).Width + Measure(item.Value, _valueFont).Width;
            if (x + w > width - 8 && x > 8)
            {
                x = 8;
                y += line;
            }
            parts.Add((new Rectangle(x, y, w, line), item, item.Name));
            x += w + Gap;
        }
        y += line + 2;
        var adviceSize = TextRenderer.MeasureText(_report.Advice, _valueFont, new Size(Math.Max(width - 30 - Dot, 50), 0),
                                                  TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        var advice = new Rectangle(8 + Dot + 6, y, Math.Max(width - 22 - Dot, 50), adviceSize.Height);
        return (parts, advice, advice.Bottom + 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var (parts, advice, _) = Arrange(Width);
        _boxes.Clear();
        int dot = Dot;
        foreach (var (box, item, text) in parts)
        {
            if (item == null)
            {
                TextRenderer.DrawText(g, text, _nameFont, box.Location, Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                continue;
            }
            _boxes.Add((box, item));
            using (var brush = new SolidBrush(ColourOf(item.State)))
                g.FillEllipse(brush, box.X, box.Y + (_nameFont.Height - dot) / 2, dot, dot);
            int textX = box.X + dot + 6;
            var name = item.Name + " ";
            TextRenderer.DrawText(g, name, _nameFont, new Point(textX, box.Y), Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, item.Value, _valueFont, new Point(textX + Measure(name, _nameFont).Width, box.Y), ForeColor,
                                  TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        using (var brush = new SolidBrush(ColourOf(_report.Overall)))
            g.FillEllipse(brush, 8, advice.Y + (_valueFont.Height - dot) / 2, dot, dot);
        TextRenderer.DrawText(g, _report.Advice, _valueFont, advice, Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var item = _boxes.FirstOrDefault(b => b.Box.Contains(e.Location)).Item;
        if (item == _hovered) return;
        _hovered = item;
        _tip.SetToolTip(this, item == null ? null : $"{item.Name}: {item.Where}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _valueFont.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
