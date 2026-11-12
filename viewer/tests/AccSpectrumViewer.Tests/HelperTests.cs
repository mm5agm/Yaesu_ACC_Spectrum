namespace AccSpectrumViewer.Tests;

public class ColourStretchTests
{
    [Fact]
    public void Black_is_the_median_and_white_the_strongest()
    {
        var levels = Enumerable.Repeat((byte)40, 800).Concat(Enumerable.Repeat((byte)120, 50)).ToArray();

        var s = ColourStretch.From(levels);

        Assert.Equal(40, s.Low);
        Assert.Equal(120, s.High);
        Assert.Equal(0, s.Lookup[40]);
        Assert.Equal(0, s.Lookup[10]);
        Assert.Equal(255, s.Lookup[120]);
        Assert.Equal(255, s.Lookup[200]);
        Assert.InRange(s.Lookup[80], 120, 135);
    }

    [Fact]
    public void A_flat_trace_still_has_a_range()
    {
        var s = ColourStretch.From(Enumerable.Repeat((byte)50, 850).ToArray());

        Assert.Equal(50, s.Low);
        Assert.Equal(70, s.High);
    }

    [Fact]
    public void A_trace_at_the_top_stays_in_range()
    {
        var s = ColourStretch.From(Enumerable.Repeat((byte)255, 850).ToArray());

        Assert.True(s.Low < s.High);
        Assert.Equal(255, s.High);
    }

    [Fact]
    public void The_trace_bottom_is_below_the_noise()
    {
        var levels = Enumerable.Range(0, 850).Select(i => (byte)(i % 100)).ToArray();

        var s = ColourStretch.From(levels);

        Assert.True(s.TraceLow <= s.Low);
        Assert.InRange(s.TraceLow, 0, 10);
    }

    [Fact]
    public void Following_moves_part_of_the_way_each_frame()
    {
        // Switched on: flat, low levels first, then real noise and signals.
        var startup = ColourStretch.From(Enumerable.Repeat((byte)5, 850).ToArray());
        var real = ColourStretch.From(Enumerable.Repeat((byte)60, 800).Concat(Enumerable.Repeat((byte)160, 50)).ToArray());

        var s = startup.Towards(real, 0.04);
        Assert.InRange(s.Low, 6, 7);

        for (int i = 0; i < 125; i++) s = s.Towards(real, 0.04);   // 5 seconds at 25 a second
        Assert.InRange(s.Low, 58, 60);
        Assert.InRange(s.High, 156, 160);
        Assert.True(s.Low < s.High);
    }

    [Fact]
    public void Following_the_same_levels_stays_put()
    {
        var a = ColourStretch.From(Enumerable.Range(0, 850).Select(i => (byte)(40 + i % 60)).ToArray());
        var b = a.Towards(a, 0.02);

        Assert.Equal(a.Low, b.Low);
        Assert.Equal(a.High, b.High);
        Assert.Equal(a.Lookup, b.Lookup);
    }

    [Fact]
    public void The_waterfall_runs_black_to_white()
    {
        Assert.Equal(unchecked((int)0xFF000000), Palette.Waterfall[0]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), Palette.Waterfall[255]);
        Assert.Equal(unchecked((int)0xFF00008C), Palette.Waterfall[60]);
        Assert.All(Palette.Waterfall, c => Assert.Equal(0xFF, (c >> 24) & 0xFF));
    }
}

public class BoardPortsTests
{
    [Theory]
    [InlineData("VID_1A86&PID_55D3", 0x1A86, 0x55D3)]
    [InlineData("VID_303A&PID_1001&MI_00", 0x303A, 0x1001)]
    [InlineData("vid_10c4&pid_ea60", 0x10C4, 0xEA60)]
    public void Reads_usb_ids(string device, int vid, int pid)
    {
        Assert.True(BoardPorts.TryParseIds(device, out int v, out int p));
        Assert.Equal(vid, v);
        Assert.Equal(pid, p);
    }

    [Theory]
    [InlineData("ROOT_HUB30")]
    [InlineData("VID_1A86")]
    [InlineData("VID_ZZZZ&PID_55D3")]
    public void Ignores_other_names(string device) => Assert.False(BoardPorts.TryParseIds(device, out _, out _));

    [Theory]
    [InlineData(0x1A86, 0x55D3, true)]     // the board's CH343
    [InlineData(0x303A, 0x1001, true)]     // the ESP32-S3's own port
    [InlineData(0x10C4, 0xEA60, false)]    // CP210x: the FTdx101's CAT ports
    [InlineData(0x1A86, 0x7523, false)]    // a CH340 on something else
    [InlineData(0x303A, 0x0002, false)]
    public void Only_the_boards_ids_are_board_ports(int vid, int pid, bool board) =>
        Assert.Equal(board, BoardPorts.IsBoardId(vid, pid));

    [Fact]
    public void Says_which_socket()
    {
        Assert.Equal("COM5: the board's COM socket (CH343)", new BoardPort("COM5", 0x1A86, 0x55D3, "x").ToString());
        Assert.Equal("COM6: the board's USB socket (ESP32-S3)", new BoardPort("COM6", 0x303A, 0x1001, "x").ToString());
    }
}

public class EspToolTests
{
    [Fact]
    public void Writes_the_merged_file_at_zero()
    {
        var args = EspTool.WriteFlashArguments("COM5", @"C:\f\yaesu-acc-firmware-2.0.1-merged.bin");

        Assert.Equal(new[] { "--chip", "esp32s3", "--port", "COM5", "--baud", "460800", "write-flash", "0x0",
                             @"C:\f\yaesu-acc-firmware-2.0.1-merged.bin" }, args);
    }

    [Theory]
    [InlineData("Writing at 0x00010000 [===>        ]  35.2% 65536/185344 bytes...", 35.2)]
    [InlineData("Writing at 0x0002d4a0 (100 %)", 100)]
    [InlineData("Writing at 0x00000000... (7 %)", 7)]
    public void Reads_progress(string line, double expected) => Assert.Equal(expected, EspTool.ProgressIn(line));

    [Theory]
    [InlineData("Connecting....")]
    [InlineData("Chip type: ESP32-S3 (QFN56) (revision v0.2)")]
    [InlineData("Wrote 1234 bytes at 0x00000000 in 1.2 seconds (500 %)")]
    public void Other_lines_have_no_progress(string line) => Assert.Null(EspTool.ProgressIn(line));
}

public class FormattingTests
{
    [Theory]
    [InlineData(14_074_000, "14,074")]
    [InlineData(14_074_500, "14,074.5")]
    [InlineData(7_074_250, "7,074.25")]
    [InlineData(20_000, "20")]
    public void Khz(long hz, string text) => Assert.Equal(text, ScopeView.Khz(hz));

    [Theory]
    [InlineData(49, "49 s")]
    [InlineData(119, "119 s")]
    [InlineData(1440, "24 min")]
    [InlineData(7200, "2 h 0 min")]
    [InlineData(9000, "2 h 30 min")]
    public void Uptime(long seconds, string text) => Assert.Equal(text, MainForm.Uptime(seconds));
}
