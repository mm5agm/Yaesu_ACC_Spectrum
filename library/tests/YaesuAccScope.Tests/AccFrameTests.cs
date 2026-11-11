namespace YaesuAccScope.Tests;

/// <summary>
/// The step 5 captures in captures/step5, each named after what was set on the
/// radio. See step 5 of docs/plan.md.
/// </summary>
public class AccFrameTests
{
    [Theory]
    [InlineData("main-200k-center-a141", 14_100_000, 200_000, ScopePlacement.Center, 14_100_000)]
    [InlineData("main-200k-fix", 14_087_070, 200_000, ScopePlacement.Fix, 14_100_000)]
    [InlineData("main-20k-place-center", 14_087_070, 20_000, ScopePlacement.Center, 14_087_070)]
    [InlineData("main-20k-place-cursor", 14_087_070, 20_000, ScopePlacement.Cursor, 14_087_070)]
    // The radio keeps a separate span for Fix: 10k here while Center was on 20k.
    [InlineData("main-20k-place-fix", 14_087_070, 10_000, ScopePlacement.Fix, 14_005_000)]
    [InlineData("main-span20k-a1408707", 14_087_070, 20_000, ScopePlacement.Center, 14_087_070)]
    [InlineData("main-span50k-a14075", 14_075_000, 50_000, ScopePlacement.Center, 14_075_000)]
    [InlineData("main-span50k-a1409707", 14_097_070, 50_000, ScopePlacement.Center, 14_097_070)]
    [InlineData("main-sweep-span1k", 14_096_770, 1_000, ScopePlacement.Center, 14_096_770)]
    [InlineData("main-sweep-span2k", 14_096_470, 2_000, ScopePlacement.Center, 14_096_470)]
    [InlineData("main-sweep-span5k", 14_095_570, 5_000, ScopePlacement.Center, 14_095_570)]
    [InlineData("main-sweep-span10k", 14_094_070, 10_000, ScopePlacement.Center, 14_094_070)]
    [InlineData("main-sweep-span20k", 14_091_070, 20_000, ScopePlacement.Center, 14_091_070)]
    [InlineData("main-sweep-span50k", 14_082_070, 50_000, ScopePlacement.Center, 14_082_070)]
    [InlineData("main-sweep-span100k", 14_067_070, 100_000, ScopePlacement.Center, 14_067_070)]
    [InlineData("main-sweep-span200k", 14_037_070, 200_000, ScopePlacement.Center, 14_037_070)]
    [InlineData("main-sweep-span1000k", 13_797_070, 1_000_000, ScopePlacement.Center, 13_797_070)]
    [InlineData("audio-radio-scotland-810-am", 810_000, 100_000, ScopePlacement.Center, 810_000)]
    public void Main_tuning(string capture, long vfo, int span, ScopePlacement placement, long centre)
    {
        foreach (var frame in Captures.Step5(capture))
        {
            var main = frame.Main;
            Assert.True(main.IsValid);
            Assert.Equal(vfo, main.VfoHz);
            Assert.Equal(span, main.SpanHz);
            Assert.Equal(placement, main.Placement);
            Assert.Equal(centre, main.CentreHz);
            Assert.Equal(centre - span / 2, main.StartHz);
            Assert.Equal(centre + span / 2, main.EndHz);
        }
    }

    [Theory]
    [InlineData("sub-100k-center", 7_674_000, 100_000, ScopePlacement.Center, 7_674_000)]
    [InlineData("sub-100k-cursor", 7_674_000, 100_000, ScopePlacement.Cursor, 7_674_000)]
    [InlineData("sub-fix", 7_674_000, 200_000, ScopePlacement.Fix, 7_100_000)]
    [InlineData("sub-span20k", 7_474_000, 20_000, ScopePlacement.Center, 7_474_000)]
    public void Sub_tuning(string capture, long vfo, int span, ScopePlacement placement, long centre)
    {
        foreach (var frame in Captures.Step5(capture))
        {
            var sub = frame.Sub;
            Assert.True(sub.IsValid);
            Assert.Equal(vfo, sub.VfoHz);
            Assert.Equal(span, sub.SpanHz);
            Assert.Equal(placement, sub.Placement);
            Assert.Equal(centre, sub.CentreHz);
        }
    }

    [Fact]
    public void Levels_are_turned_the_right_way_up()
    {
        var frame = Captures.Step5("main-span20k")[0];

        for (int i = 0; i < AccFrame.Points; i++)
        {
            Assert.Equal(255 - frame.Raw[i], frame.Main.Levels[i]);
            Assert.Equal(255 - frame.Raw[AccFrame.Points + i], frame.Sub.Levels[i]);
        }
    }

    [Fact]
    public void The_span_is_the_middle_791_points()
    {
        var main = Captures.Step5("main-sweep-span20k")[0].Main;

        Assert.Equal(main.CentreHz, main.FrequencyAt(ReceiverScope.CentrePoint));
        Assert.Equal(425, main.PointAt(main.CentreHz));
        Assert.InRange(main.PointAt(main.StartHz), 29, 30);
        Assert.InRange(main.PointAt(main.EndHz), 820, 821);
        Assert.Equal(20_000.0 / 791, main.HzPerPoint, 6);
    }

    [Fact]
    public void Passband_marker_is_the_vfo_plus_1500()
    {
        var frame = Captures.Step5("main-span20k")[0];

        Assert.Equal(frame.Main.VfoHz + 1500, frame.Main.PassbandMarkerHz);
        Assert.Equal(frame.Sub.VfoHz + 1500, frame.Sub.PassbandMarkerHz);
    }

    [Fact]
    public void Sees_3dss()
    {
        Assert.All(Captures.Step5("main-3dss-center"), f => Assert.True(f.Main.Is3dss));
        Assert.All(Captures.Step5("main-wf-before-3dss"), f => Assert.False(f.Main.Is3dss));
        Assert.All(Captures.Step5("main-3dss-center"), f => Assert.Equal(20_000, f.Main.SpanHz));
    }

    [Fact]
    public void Band_button()
    {
        Assert.False(Captures.Step5("main-span10k")[0].SubBandSelected);
        Assert.True(Captures.Step5("sub-fix")[0].SubBandSelected);
    }

    [Theory]
    [InlineData("audio-main-tone1000", 49)]
    [InlineData("audio-main-tone2000", 101)]
    public void Main_af_fft_shows_the_tone(string capture, int point)
    {
        var frames = Captures.Step5(capture);
        var sum = new int[frames[0].MainAudio.FftPoints];
        foreach (var f in frames)
            for (int i = 0; i < sum.Length; i++) sum[i] += f.MainAudio.Fft[i];

        // Skip the first few points, which sit near DC.
        int peak = Enumerable.Range(10, sum.Length - 10).MaxBy(i => sum[i]);
        Assert.InRange(peak, point - 2, point + 2);
    }

    [Theory]
    [InlineData("audio-fft_att_back_0", 0, 2, 4)]
    [InlineData("audio-fft_att_1", 1, 2, 4)]
    [InlineData("audio-fft_att_2", 2, 2, 4)]
    [InlineData("audio-osc_att_a0", 0, 0, 4)]
    [InlineData("audio-osc_att_a1", 0, 1, 4)]
    [InlineData("audio-osc_att_back_a2", 0, 2, 4)]
    public void Main_audio_settings(string capture, int fftAttenuator, int oscAttenuator, int oscTime)
    {
        var audio = Captures.Step5(capture)[0].MainAudio;

        Assert.Equal(fftAttenuator, audio.FftAttenuator);
        Assert.Equal(oscAttenuator, audio.OscAttenuator);
        Assert.Equal(oscTime, audio.OscTime);
        Assert.Null(Captures.Step5(capture)[0].SubAudio.OscTime);
    }

    [Fact]
    public void Oscilloscope_columns_are_max_then_min()
    {
        foreach (var f in Captures.Step5("audio-radio-scotland-810-am"))
            for (int c = 0; c < AudioScope.OscColumns; c++)
            {
                Assert.True(f.MainAudio.OscMax(c) >= f.MainAudio.OscMin(c));
                Assert.True(f.SubAudio.OscMax(c) >= f.SubAudio.OscMin(c));
            }
    }

    [Fact]
    public void A_short_frame_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AccFrame.Parse(new byte[3101]));
        Assert.False(AccFrame.TryParse(new byte[100], out var none));
        Assert.Null(none);
    }

    [Fact]
    public void All_zeros_is_not_valid()
    {
        var frame = AccFrame.Parse(new byte[4096]);

        Assert.False(frame.Main.IsValid);
        Assert.False(frame.Sub.IsValid);
    }
}
