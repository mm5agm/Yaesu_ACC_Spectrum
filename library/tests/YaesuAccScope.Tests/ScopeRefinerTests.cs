using System;
using Xunit;

namespace YaesuAccScope.Tests
{
    public class ScopeRefinerTests
    {
        // Main's fields in a frame (AccFrame.cs).
        private const int MainVfoAt = 3068, MainLeftAt = 3076, MainSpanAt = 3062;

        private static void PutUInt32(byte[] b, int at, long v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        /// <summary>Main's row, Center, with each point's level from <paramref name="level"/>.</summary>
        private static ReceiverScope MainRow(Func<int, byte> level, long vfoHz = 14_074_000, int spanIndex = 4)
        {
            var raw = new byte[4096];
            for (int i = 0; i < AccFrame.Points; i++) raw[i] = (byte)~level(i);   // the radio sends it inverted
            PutUInt32(raw, MainVfoAt, vfoHz);
            PutUInt32(raw, MainLeftAt, 0);
            raw[MainSpanAt] = (byte)((int)ScopePlacement.Center << 6 | spanIndex);
            raw[2917] = 3;
            raw[2918] = 3;
            return AccFrame.Parse(raw).Main;
        }

        private static ReceiverScope Flat(byte level, long vfoHz = 14_074_000, int spanIndex = 4) =>
            MainRow(_ => level, vfoHz, spanIndex);

        [Fact]
        public void The_first_row_is_shown_as_it_is()
        {
            var r = new ScopeRefiner();
            var t = r.Add(MainRow(i => (byte)(i % 200)));
            Assert.Equal(AccFrame.Points, t.Length);
            for (int i = 0; i < t.Length; i++) Assert.Equal(i % 200, t[i]);
        }

        [Fact]
        public void A_rough_floor_settles_to_its_middle()
        {
            // Every point flips between 90 and 110: the raw rows jump 20 steps
            // a frame, the refined trace barely moves.
            var r = new ScopeRefiner();
            float[] t = Array.Empty<float>();
            for (int n = 0; n < 60; n++) t = r.Add(Flat(n % 2 == 0 ? (byte)90 : (byte)110));
            Assert.All(t, v => Assert.InRange(v, 95f, 105f));
        }

        [Fact]
        public void Each_new_row_counts_by_the_weight()
        {
            var r = new ScopeRefiner(0.25);
            r.Add(Flat(100));
            var t = r.Add(Flat(200));
            Assert.All(t, v => Assert.Equal(125f, v));
        }

        [Fact]
        public void Weight_one_is_the_raw_rows()
        {
            var r = new ScopeRefiner(1);
            r.Add(Flat(100));
            Assert.All(r.Add(Flat(7)), v => Assert.Equal(7f, v));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-0.1)]
        [InlineData(1.01)]
        [InlineData(double.NaN)]
        public void Weights_outside_0_to_1_are_refused(double w) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScopeRefiner(w));

        [Fact]
        public void A_new_span_starts_again()
        {
            var r = new ScopeRefiner();
            r.Add(Flat(100, spanIndex: 4));
            Assert.All(r.Add(Flat(200, spanIndex: 5)), v => Assert.Equal(200f, v));
        }

        [Fact]
        public void A_frame_that_makes_no_sense_is_shown_raw_and_starts_again()
        {
            var r = new ScopeRefiner();
            r.Add(Flat(100));
            Assert.All(r.Add(Flat(200, vfoHz: 0)), v => Assert.Equal(200f, v));
            Assert.All(r.Add(Flat(50)), v => Assert.Equal(50f, v));
        }

        [Fact]
        public void A_signal_stays_sharp_while_the_vfo_moves()
        {
            // A carrier at a fixed frequency. Tuning up 20 points a frame moves
            // it 20 points left each row; the refined peak must move with it and
            // stay one point wide rather than smearing into a streak.
            const double hzPerPoint = 20_000.0 / ReceiverScope.SpanPoints;
            const double carrierHz = 14_075_000;
            var r = new ScopeRefiner();
            float[] t = Array.Empty<float>();
            long vfo = 14_074_000;
            ReceiverScope last = null!;
            for (int n = 0; n < 10; n++)
            {
                long v = vfo + (long)Math.Round(n * 20 * hzPerPoint);
                last = MainRow(i => 0, v);
                int at = last.PointAt(carrierHz);
                last = MainRow(i => i == at ? (byte)200 : (byte)50, v);
                t = r.Add(last);
            }
            int expected = last.PointAt(carrierHz);
            Assert.Equal(200f, t[expected], 1);
            Assert.InRange(t[expected - 1], 49f, 51f);
            Assert.InRange(t[expected + 1], 49f, 51f);
        }

        [Fact]
        public void Points_that_come_into_view_take_the_new_row()
        {
            const double hzPerPoint = 20_000.0 / ReceiverScope.SpanPoints;
            var r = new ScopeRefiner();
            r.Add(Flat(100));
            var t = r.Add(Flat(200, vfoHz: 14_074_000 + (long)Math.Round(10 * hzPerPoint)));
            // The 10 points at the top end are new; the rest is the old average
            // moved down, plus 0.3 of the way to 200.
            for (int i = 0; i < AccFrame.Points - 10; i++) Assert.Equal(130f, t[i], 3);
            for (int i = AccFrame.Points - 10; i < AccFrame.Points; i++) Assert.Equal(200f, t[i], 3);
        }

        [Fact]
        public void Slow_tuning_adds_up_to_a_move()
        {
            // A third of a point a frame: no single frame moves a whole point,
            // but three do, so the trace mustn't stay put for ever.
            const double hzPerPoint = 20_000.0 / ReceiverScope.SpanPoints;
            var r = new ScopeRefiner(1e-6);   // so the old average stays put
            r.Add(MainRow(i => (byte)(i % 256)));
            float[] t = Array.Empty<float>();
            for (int n = 1; n <= 3; n++)
                t = r.Add(MainRow(i => 0, 14_074_000 + (long)Math.Round(n * hzPerPoint / 3)));
            Assert.Equal(1f, t[0], 2);   // the old point 1 is now point 0
        }

        [Fact]
        public void A_big_move_starts_again()
        {
            var r = new ScopeRefiner();
            r.Add(Flat(100));
            Assert.All(r.Add(Flat(200, vfoHz: 14_090_000)), v => Assert.Equal(200f, v));
        }

        [Fact]
        public void The_trace_returned_is_the_callers_to_keep()
        {
            var r = new ScopeRefiner();
            var first = r.Add(Flat(100));
            r.Add(Flat(200));
            Assert.All(first, v => Assert.Equal(100f, v));
        }
    }
}
