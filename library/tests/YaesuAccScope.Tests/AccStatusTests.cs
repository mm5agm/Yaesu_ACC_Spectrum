namespace YaesuAccScope.Tests;

public class AccStatusTests
{
    [Fact]
    public void Reads_a_version_2_status()
    {
        var s = AccStatus.Parse(
            "ACC pipe: version 2.0.1, SPI mode 2, frames 1234, bytes 5054464, cut short 0, not sent 3, DATA1 0, up 49 s, edges/s CS 25 CLOCK 819261 DATA2 147000 DATA1 0");

        Assert.NotNull(s);
        Assert.Equal("2.0.1", s!.FirmwareVersion);
        Assert.Equal(2, s.SpiMode);
        Assert.Equal(1234, s.Frames);
        Assert.Equal(5054464, s.Bytes);
        Assert.Equal(0, s.CutShort);
        Assert.Equal(3, s.NotSent);
        Assert.Equal(0, s.Data1);
        Assert.Equal(49, s.UpSeconds);
        Assert.Equal(25, s.EdgesPerSecond["CS"]);
        Assert.Equal(819261, s.EdgesPerSecond["CLOCK"]);
        Assert.Equal(147000, s.EdgesPerSecond["DATA2"]);
        Assert.Equal(0, s.EdgesPerSecond["DATA1"]);
    }

    [Fact]
    public void Missing_fields_are_null()
    {
        var s = AccStatus.Parse("ACC pipe: SPI mode 3, frames 10");

        Assert.NotNull(s);
        Assert.Null(s!.FirmwareVersion);
        Assert.Equal(3, s.SpiMode);
        Assert.Equal(10, s.Frames);
        Assert.Null(s.UpSeconds);
        Assert.Empty(s.EdgesPerSecond);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    public void Anything_else_is_not_a_status(string? text) => Assert.Null(AccStatus.Parse(text));

    [Fact]
    public void Reads_the_status_in_a_raw_capture()
    {
        var reader = new AccPacketReader();
        var texts = reader.Feed(File.ReadAllBytes(Captures.PathOf("acc-20261111-113822-mode2.bin")))
            .Where(p => p.Type == AccPacketType.Status)
            .Select(p => AccStatus.Parse(p.Text))
            .ToList();

        Assert.All(texts, Assert.NotNull);
    }
}
