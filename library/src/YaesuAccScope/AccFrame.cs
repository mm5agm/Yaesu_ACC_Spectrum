// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;

namespace YaesuAccScope
{
    /// <summary>Where the radio puts the scope window, from its span byte.</summary>
    public enum ScopePlacement
    {
        /// <summary>The byte held a value that isn't known.</summary>
        Unknown = -1,

        /// <summary>The window moves with the cursor.</summary>
        Cursor = 0,

        /// <summary>The window is centred on the VFO.</summary>
        Center = 1,

        /// <summary>The window stays put; the radio keeps a separate span for Fix.</summary>
        Fix = 2,
    }

    /// <summary>
    /// One frame from the FTdx101's ACC socket, decoded. Everything comes from
    /// the frame itself, with nothing from CAT. See docs/protocol.md for the
    /// layout.
    /// </summary>
    public sealed class AccFrame
    {
        /// <summary>
        /// The shortest frame that holds every field decoded here. The radio's
        /// frames are 4096 bytes, with zeros from byte 3102 on.
        /// </summary>
        public const int MinLength = 3102;

        /// <summary>Points in each receiver's scope row.</summary>
        public const int Points = 850;

        private AccFrame(byte[] raw)
        {
            Raw = raw;
            Main = new ReceiverScope(raw, 0, vfoAt: 3068, leftAt: 3076, spanAt: 3062, modeAt: 2917, passbandAt: 3036);
            Sub = new ReceiverScope(raw, Points, vfoAt: 3094, leftAt: 3098, spanAt: 3084, modeAt: 2918, passbandAt: 3040);
            MainAudio = new AudioScope(raw, fftAt: 1701, fftPoints: 199, oscAt: 1900, settingsAt: 3056);
            SubAudio = new AudioScope(raw, fftAt: 2300, fftPoints: 200, oscAt: 2500, settingsAt: -1);
        }

        /// <summary>The frame's bytes, as they came from the radio.</summary>
        public byte[] Raw { get; }

        /// <summary>The Main receiver's scope, VFO A.</summary>
        public ReceiverScope Main { get; }

        /// <summary>The Sub receiver's scope, VFO B.</summary>
        public ReceiverScope Sub { get; }

        /// <summary>Main's audio scope: the AF-FFT and the oscilloscope.</summary>
        public AudioScope MainAudio { get; }

        /// <summary>
        /// Sub's audio scope. Not tested on its own yet: the layout is taken from
        /// where the bytes change, not from a tone in Sub's passband.
        /// </summary>
        public AudioScope SubAudio { get; }

        /// <summary>
        /// The Band button on the radio's scope: false for MAIN, true for SUB
        /// (byte 2916).
        /// </summary>
        public bool SubBandSelected => Raw[2916] != 0;

        /// <summary>Decodes a frame. Throws if it's shorter than <see cref="MinLength"/>.</summary>
        public static AccFrame Parse(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.Length < MinLength)
                throw new ArgumentException($"A frame needs at least {MinLength} bytes; this one has {frame.Length}.", nameof(frame));
            return new AccFrame(frame);
        }

        /// <summary>Decodes a frame, or returns false if it's too short.</summary>
        public static bool TryParse(byte[] frame, out AccFrame? decoded)
        {
            decoded = frame != null && frame.Length >= MinLength ? new AccFrame(frame) : null;
            return decoded != null;
        }

        internal static long ReadUInt32BigEndian(byte[] b, int at) =>
            ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];
    }

    /// <summary>One receiver's scope row and where it sits in frequency.</summary>
    public sealed class ReceiverScope
    {
        /// <summary>
        /// The radio's span list, by the low 4 bits of the span byte.
        /// </summary>
        public static readonly int[] Spans = { 1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000 };

        /// <summary>
        /// Points the span covers. The 850 points cover a little more than the
        /// span: the span is the middle 791, with about 30 points beyond each
        /// edge. Measured at every span from 1k to 1M (788 to 795, 791 on average).
        /// </summary>
        public const int SpanPoints = 791;

        /// <summary>
        /// The point the centre frequency falls on. A steady carrier tuned to the
        /// VFO in Center sat on point 425 of 0 to 849.
        /// </summary>
        public const int CentrePoint = 425;

        private readonly byte[] _raw;
        private readonly int _levelsAt;
        private readonly int _spanAt;
        private readonly int _modeAt;
        private byte[]? _levels;

        internal ReceiverScope(byte[] raw, int levelsAt, int vfoAt, int leftAt, int spanAt, int modeAt, int passbandAt)
        {
            _raw = raw;
            _levelsAt = levelsAt;
            _spanAt = spanAt;
            _modeAt = modeAt;
            VfoHz = AccFrame.ReadUInt32BigEndian(raw, vfoAt);
            LeftEdgeHz = AccFrame.ReadUInt32BigEndian(raw, leftAt);
            PassbandMarkerHz = AccFrame.ReadUInt32BigEndian(raw, passbandAt);
        }

        /// <summary>
        /// The 850 levels, low frequency first, turned the right way up so a
        /// higher number is a stronger signal. The radio sends each byte inverted.
        /// The dB scale isn't known yet.
        /// </summary>
        public byte[] Levels
        {
            get
            {
                if (_levels == null)
                {
                    var levels = new byte[AccFrame.Points];
                    for (int i = 0; i < levels.Length; i++) levels[i] = (byte)~_raw[_levelsAt + i];
                    _levels = levels;
                }
                return _levels;
            }
        }

        /// <summary>The receiver's VFO in Hz: VFO A for Main, VFO B for Sub.</summary>
        public long VfoHz { get; }

        /// <summary>
        /// The left edge of the span in Hz, as the radio sends it. Right in Cursor
        /// and Fix; in Center the radio doesn't keep it up to date, so use
        /// <see cref="StartHz"/> instead.
        /// </summary>
        public long LeftEdgeHz { get; }

        /// <summary>
        /// Where the radio draws the receiver's passband marker: VFO + 1500 Hz +
        /// IF shift.
        /// </summary>
        public long PassbandMarkerHz { get; }

        /// <summary>The span byte's low 4 bits: the place in <see cref="Spans"/>.</summary>
        public int SpanIndex => _raw[_spanAt] & 0x0F;

        /// <summary>The span the radio shows, in Hz, or 0 if the span byte isn't known.</summary>
        public int SpanHz => SpanIndex < Spans.Length ? Spans[SpanIndex] : 0;

        /// <summary>Center, Cursor or Fix, from the span byte's top 2 bits.</summary>
        public ScopePlacement Placement
        {
            get
            {
                int p = _raw[_spanAt] >> 6;
                return p <= 2 ? (ScopePlacement)p : ScopePlacement.Unknown;
            }
        }

        /// <summary>
        /// The radio's scope mode byte, its SS P3 character: 0 to 2 are 3DSS, 3
        /// is the waterfall in Center, 6 Cursor and 9 Fix. Sub has shown 5 in
        /// Center too.
        /// </summary>
        public byte ScopeMode => _raw[_modeAt];

        /// <summary>
        /// True when the radio draws this receiver in 3DSS. The levels are the same
        /// either way; it's only how the radio draws them.
        /// </summary>
        public bool Is3dss => ScopeMode <= 2;

        /// <summary>
        /// True when the VFO, span and placement make sense: the VFO between 30 kHz
        /// and 75 MHz, and a known span and placement.
        /// </summary>
        public bool IsValid =>
            VfoHz >= 30_000 && VfoHz <= 75_000_000 && SpanHz > 0 && Placement != ScopePlacement.Unknown;

        /// <summary>
        /// The centre of the span in Hz: the VFO in Center, the left edge plus half
        /// the span in Cursor and Fix.
        /// </summary>
        public long CentreHz => Placement == ScopePlacement.Center ? VfoHz : LeftEdgeHz + SpanHz / 2;

        /// <summary>The left edge of the span in Hz, in every placement.</summary>
        public long StartHz => CentreHz - SpanHz / 2;

        /// <summary>The right edge of the span in Hz, in every placement.</summary>
        public long EndHz => CentreHz + SpanHz / 2;

        /// <summary>Hz from one point to the next.</summary>
        public double HzPerPoint => (double)SpanHz / SpanPoints;

        /// <summary>
        /// The frequency of a point, 0 to 849, in Hz. Points below about 30 and
        /// above about 820 lie outside the span.
        /// </summary>
        public double FrequencyAt(int point) => CentreHz + (point - CentrePoint) * HzPerPoint;

        /// <summary>The nearest point to a frequency. It can be outside 0 to 849.</summary>
        public int PointAt(double hz) =>
            SpanHz == 0 ? CentrePoint : (int)Math.Round(CentrePoint + (hz - CentreHz) / HzPerPoint);
    }

    /// <summary>
    /// One receiver's audio scope: the AF-FFT and the oscilloscope the radio can
    /// show.
    /// </summary>
    public sealed class AudioScope
    {
        /// <summary>Columns in the oscilloscope, each a max byte and a min byte.</summary>
        public const int OscColumns = 200;

        private readonly byte[] _raw;
        private readonly int _fftAt;
        private readonly int _oscAt;
        private readonly int _settingsAt;
        private byte[]? _fft;

        internal AudioScope(byte[] raw, int fftAt, int fftPoints, int oscAt, int settingsAt)
        {
            _raw = raw;
            _fftAt = fftAt;
            FftPoints = fftPoints;
            _oscAt = oscAt;
            _settingsAt = settingsAt;
        }

        /// <summary>Points in the AF-FFT: 199 for Main, 200 for Sub.</summary>
        public int FftPoints { get; }

        /// <summary>
        /// The AF-FFT, from 0 to about 3.8 kHz, about 19-20 Hz a point, turned the
        /// right way up so higher is stronger. Measured on Main: a 1 kHz tone on
        /// point 49 and 2 kHz on point 101.
        /// </summary>
        public byte[] Fft
        {
            get
            {
                if (_fft == null)
                {
                    var fft = new byte[FftPoints];
                    for (int i = 0; i < fft.Length; i++) fft[i] = (byte)~_raw[_fftAt + i];
                    _fft = fft;
                }
                return _fft;
            }
        }

        /// <summary>The top of oscilloscope column <paramref name="column"/>, 0 to 199, centred on 128.</summary>
        public byte OscMax(int column) => _raw[_oscAt + 2 * CheckColumn(column)];

        /// <summary>The bottom of oscilloscope column <paramref name="column"/>, 0 to 199, centred on 128.</summary>
        public byte OscMin(int column) => _raw[_oscAt + 2 * CheckColumn(column) + 1];

        /// <summary>
        /// The oscilloscope sweep time setting, t0 to t5 as 0 to 5. At t0 a column
        /// is 50 us, 10 ms across. Known for Main only; null for Sub.
        /// </summary>
        public int? OscTime => _settingsAt < 0 ? (int?)null : _raw[_settingsAt] & 0x07;

        /// <summary>The oscilloscope attenuator, a0 to a2 as 0 to 2. Main only.</summary>
        public int? OscAttenuator => _settingsAt < 0 ? (int?)null : (_raw[_settingsAt] >> 3) & 0x03;

        /// <summary>The AF-FFT attenuator, 0 to 2. Main only.</summary>
        public int? FftAttenuator => _settingsAt < 0 ? (int?)null : (_raw[_settingsAt] >> 5) & 0x03;

        private static int CheckColumn(int column)
        {
            if (column < 0 || column >= OscColumns) throw new ArgumentOutOfRangeException(nameof(column));
            return column;
        }
    }
}
