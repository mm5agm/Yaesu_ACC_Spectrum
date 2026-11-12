// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

namespace AccSpectrumViewer;

/// <summary>The waterfall's colours and the 3DSS line colours.</summary>
internal static class Palette
{
    /// <summary>Black, blue, cyan, yellow, red, white: weak to strong. The same as tools/live_view.py.</summary>
    public static readonly int[] Waterfall = Make(
        (0, 0x000000), (60, 0x00008C), (120, 0x00AADC), (170, 0xF0E600), (215, 0xE61E00), (255, 0xFFFFFF));

    private static int[] Make(params (int At, int Rgb)[] stops)
    {
        var colours = new int[256];
        for (int v = 0; v < 256; v++)
        {
            for (int s = 0; s < stops.Length - 1; s++)
            {
                var (a, ca) = stops[s];
                var (b, cb) = stops[s + 1];
                if (v < a || v > b) continue;
                double t = (double)(v - a) / (b - a);
                int Mix(int shift)
                {
                    int from = (ca >> shift) & 0xFF, to = (cb >> shift) & 0xFF;
                    return (int)Math.Round(from + (to - from) * t);
                }
                colours[v] = unchecked((int)0xFF000000) | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
                break;
            }
        }
        return colours;
    }

    /// <summary>
    /// The 3DSS line colour for the trace <paramref name="k"/> back from the
    /// front: bright yellow at the front, through green, to dark blue at the back.
    /// </summary>
    public static Color Depth(int k, int depth)
    {
        double t = depth <= 1 ? 0 : (double)k / (depth - 1);
        (double At, int R, int G, int B)[] stops = { (0, 255, 230, 60), (0.35, 60, 220, 120), (0.7, 30, 110, 200), (1, 20, 30, 90) };
        for (int s = 0; s < stops.Length - 1; s++)
        {
            var a = stops[s];
            var b = stops[s + 1];
            if (t < a.At || t > b.At) continue;
            double u = (t - a.At) / (b.At - a.At);
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * u), (int)Math.Round(a.G + (b.G - a.G) * u),
                                  (int)Math.Round(a.B + (b.B - a.B) * u));
        }
        return Color.FromArgb(stops[^1].R, stops[^1].G, stops[^1].B);
    }
}

/// <summary>
/// How a receiver's levels map to colours and to the trace's height. Black is
/// the median level, about the noise floor, so the noise stays dark and weak
/// signals such as FT8 show against it; white is the strongest signal seen.
/// </summary>
internal sealed class ColourStretch
{
    private readonly double _low, _high, _traceLow;

    private ColourStretch(double low, double high, double traceLow)
    {
        _low = low;
        _high = high;
        _traceLow = traceLow;
        Low = (int)Math.Round(low);
        High = Math.Max((int)Math.Round(high), Low + 1);
        TraceLow = Math.Min((int)Math.Round(traceLow), Low);
        low = Low;
        high = High;
        Lookup = new byte[256];
        for (int v = 0; v < 256; v++)
            Lookup[v] = v <= low ? (byte)0 : v >= high ? (byte)255 : (byte)Math.Round((v - low) * 255.0 / (high - low));
    }

    /// <summary>The level drawn black: the median.</summary>
    public int Low { get; }

    /// <summary>The level drawn white: the strongest, and at least 20 above <see cref="Low"/>.</summary>
    public int High { get; }

    /// <summary>The bottom of the trace, below <see cref="Low"/> so the noise stays in view.</summary>
    public int TraceLow { get; }

    /// <summary>Level to palette index.</summary>
    public byte[] Lookup { get; }

    /// <summary>Works out the stretch from one trace's levels.</summary>
    public static ColourStretch From(IReadOnlyList<byte> levels)
    {
        if (levels.Count == 0) return new ColourStretch(0, 255, 0);
        var sorted = levels.ToArray();
        Array.Sort(sorted);
        int low = sorted[sorted.Length / 2];
        int high = Math.Min(Math.Max((int)sorted[^1], low + 20), 255);
        if (high <= low) low = high - 1;
        return new ColourStretch(low, high, Math.Min((int)sorted[sorted.Length / 20], low));
    }

    /// <summary>
    /// A step of the way from this stretch towards <paramref name="target"/>:
    /// <paramref name="rate"/> 0.04 a frame, at 25 frames a second, gets 95%
    /// of the way in about 3 seconds, so the colours settle without flickering.
    /// </summary>
    public ColourStretch Towards(ColourStretch target, double rate) => new(
        _low + (target._low - _low) * rate,
        _high + (target._high - _high) * rate,
        _traceLow + (target._traceLow - _traceLow) * rate);
}
