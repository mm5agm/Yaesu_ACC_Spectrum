// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;

namespace YaesuAccScope
{
    /// <summary>
    /// Turns one receiver's raw scope rows into a refined trace for a person to
    /// look at: each point averaged over the last few frames, so the noise floor
    /// settles into a thin line and weak signals stand out of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame the radio sends is a separate snapshot: its scope speed setting
    /// only changes how fast its own waterfall scrolls, not the data. So one
    /// frame has a rough floor, and averaging frames smooths it. Measured on 20m
    /// FT8 at a 20 kHz span on 2026-11-14, the default weight took the ratio of
    /// the strongest signal to the roughness of the floor from 7.7 to 15.9.
    /// SDRplay RSPs on the radio's IF outputs, as Yaesu Web Control draws them,
    /// gave 15.8 (RSP1B) and 16.2 (RSP1). At a 100 kHz span the RSPs were
    /// clearly ahead (about 26 against 16). The refined trace still comes 25
    /// times a second. Details in docs/comparison.md in the repository.
    /// </para>
    /// <para>
    /// The raw rows are still there in <see cref="ReceiverScope.Levels"/>. Use
    /// them for anything that measures; the refined trace is for eyes.
    /// </para>
    /// <para>
    /// The average follows the radio: when the VFO moves in Center the trace
    /// moves with it, so a signal stays sharp while tuning instead of smearing.
    /// A new span, a move of more than half a span, or a frame that doesn't make
    /// sense starts the average again. Use one refiner per receiver.
    /// </para>
    /// </remarks>
    public sealed class ScopeRefiner
    {
        /// <summary>
        /// The default weight of each new frame. 0.3 at 25 frames a second takes
        /// about 0.11 s to follow a change, and smooths the floor about as much as
        /// averaging 6 frames would.
        /// </summary>
        public const double DefaultWeight = 0.3;

        private readonly float _weight;
        private float[]? _average;
        private int _spanHz;
        private double _alignedCentreHz;

        /// <param name="weight">
        /// How much each new frame counts, more than 0 and at most 1. Smaller is
        /// smoother and slower to follow; 1 is the raw rows.
        /// </param>
        public ScopeRefiner(double weight = DefaultWeight)
        {
            if (!(weight > 0 && weight <= 1))
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "The weight must be more than 0 and at most 1.");
            _weight = (float)weight;
        }

        /// <summary>How much each new frame counts.</summary>
        public double Weight => _weight;

        /// <summary>
        /// Adds a receiver's row and returns the refined trace: 850 levels, low
        /// frequency first, on the same scale as <see cref="ReceiverScope.Levels"/>
        /// but with fractions, since an average falls between the radio's steps.
        /// The array is a new one each time, so it can be kept.
        /// </summary>
        public float[] Add(ReceiverScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            var levels = scope.Levels;

            if (!scope.IsValid)
            {
                // Nowhere to put it: show it as it is and start again after.
                Reset();
                return ToFloats(levels);
            }

            if (_average == null || scope.SpanHz != _spanHz || !Follow(scope))
            {
                _average = ToFloats(levels);
                _spanHz = scope.SpanHz;
                _alignedCentreHz = scope.CentreHz;
                return (float[])_average.Clone();
            }

            var avg = _average;
            float w = _weight;
            for (int i = 0; i < avg.Length; i++) avg[i] += w * (levels[i] - avg[i]);
            return (float[])avg.Clone();
        }

        /// <summary>Forgets the average, so the next row starts it again.</summary>
        public void Reset()
        {
            _average = null;
            _spanHz = 0;
            _alignedCentreHz = 0;
        }

        /// <summary>
        /// Moves the average to where the new row sits, by whole points, and says
        /// whether it could: false when the row has moved more than half a span.
        /// Points that come into view take the new row's levels.
        /// </summary>
        private bool Follow(ReceiverScope scope)
        {
            double hzPerPoint = scope.HzPerPoint;
            int shift = (int)Math.Round((scope.CentreHz - _alignedCentreHz) / hzPerPoint);
            if (shift == 0) return true;
            if (Math.Abs(shift) > ReceiverScope.SpanPoints / 2) return false;

            var old = _average!;
            var moved = new float[old.Length];
            var levels = scope.Levels;
            for (int i = 0; i < moved.Length; i++)
            {
                int from = i + shift;
                moved[i] = from >= 0 && from < old.Length ? old[from] : levels[i];
            }
            _average = moved;
            // Keep the part of a point the shift didn't cover, so slow tuning
            // still moves the trace once it adds up to a point.
            _alignedCentreHz += shift * hzPerPoint;
            return true;
        }

        private static float[] ToFloats(byte[] levels)
        {
            var f = new float[levels.Length];
            for (int i = 0; i < f.Length; i++) f[i] = levels[i];
            return f;
        }
    }
}
