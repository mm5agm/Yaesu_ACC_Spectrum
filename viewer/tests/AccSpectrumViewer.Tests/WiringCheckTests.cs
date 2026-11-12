using YaesuAccScope;

namespace AccSpectrumViewer.Tests;

public class WiringCheckTests
{
    private static AccStatus Status(string edges, string mode = "2") =>
        AccStatus.Parse($"ACC pipe: version 2.0.1, SPI mode {mode}, frames 1234, up 49 s, edges/s {edges}")!;

    private static CheckItem Item(WiringReport r, string name) => r.Items.Single(i => i.Name == name);

    [Fact]
    public void A_working_board_is_all_good()
    {
        var r = WiringCheck.Evaluate(Status("CS 25 CLOCK 819261 DATA2 147000 DATA1 0"), 25.1, true);

        Assert.Equal(CheckState.Good, r.Overall);
        Assert.StartsWith("The wiring looks right", r.Advice);
        Assert.Equal(CheckState.Good, Item(r, "CS").State);
        Assert.Equal(CheckState.Good, Item(r, "CLOCK").State);
        Assert.Equal(CheckState.Good, Item(r, "DATA2").State);
        Assert.Equal(CheckState.Info, Item(r, "DATA1").State);
        Assert.Equal(CheckState.Good, Item(r, "SPI mode").State);
        Assert.Equal(CheckState.Good, Item(r, "Frames").State);
        Assert.Equal("819,261/s", Item(r, "CLOCK").Value);
    }

    [Fact]
    public void Nothing_from_the_radio_says_check_the_link_and_ground()
    {
        var r = WiringCheck.Evaluate(Status("CS 0 CLOCK 0 DATA2 0 DATA1 0"), 0, null);

        Assert.Equal(CheckState.Bad, r.Overall);
        Assert.Contains("pins 11 and 12", r.Advice);
        Assert.Contains("ground", r.Advice);
    }

    [Theory]
    [InlineData("CS 0 CLOCK 819261 DATA2 147000 DATA1 0", "ACC pin 6 to terminal 13")]
    [InlineData("CS 25 CLOCK 0 DATA2 147000 DATA1 0", "ACC pin 5 to terminal 10")]
    [InlineData("CS 25 CLOCK 819261 DATA2 0 DATA1 0", "ACC pin 4 to terminal 11")]
    public void A_missing_wire_is_named(string edges, string where)
    {
        var r = WiringCheck.Evaluate(Status(edges), 0, null);

        Assert.Equal(CheckState.Bad, r.Overall);
        Assert.Contains(where, r.Advice);
    }

    [Fact]
    public void Frames_that_dont_decode_point_to_the_spi_mode()
    {
        var r = WiringCheck.Evaluate(Status("CS 25 CLOCK 819261 DATA2 147000 DATA1 0", "3"), 25, false);

        Assert.Contains("--mode 2", r.Advice);
        Assert.Equal(CheckState.Warning, Item(r, "SPI mode").State);
    }

    [Fact]
    public void A_reading_out_of_range_is_a_warning()
    {
        var r = WiringCheck.Evaluate(Status("CS 12 CLOCK 819261 DATA2 147000 DATA1 0"), 25, true);

        Assert.Equal(CheckState.Warning, Item(r, "CS").State);
        Assert.Equal(CheckState.Warning, r.Overall);
    }

    [Fact]
    public void Slow_frames_are_a_warning()
    {
        var r = WiringCheck.Evaluate(Status("CS 25 CLOCK 819261 DATA2 147000 DATA1 0"), 12, true);

        Assert.Equal(CheckState.Warning, Item(r, "Frames").State);
        Assert.Equal(CheckState.Warning, r.Overall);
    }

    [Fact]
    public void Without_a_board_it_says_so()
    {
        var r = WiringCheck.Evaluate(null, null, null);

        Assert.Equal(CheckState.Unknown, r.Overall);
        Assert.Empty(r.Items);
        Assert.Equal("Not connected to the board yet.", r.Advice);
    }

    [Fact]
    public void Connected_but_no_status_yet_waits()
    {
        var r = WiringCheck.Evaluate(null, 25, true);

        Assert.Equal(CheckState.Unknown, r.Overall);
        Assert.Contains("Waiting", r.Advice);
        Assert.Equal("Frames", Assert.Single(r.Items).Name);
    }

    [Fact]
    public void Version_1_firmware_asks_for_version_2()
    {
        var r = WiringCheck.Evaluate(AccStatus.Parse("ACC pipe: SPI mode 2, frames 10")!, 25, true);

        Assert.Equal(CheckState.Unknown, r.Overall);
        Assert.Contains("version 2 or later", r.Advice);
    }
}
