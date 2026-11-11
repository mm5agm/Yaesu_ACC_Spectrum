using System.Text;

namespace YaesuAccScope.Tests;

public class AccPacketReaderTests
{
    private static byte[] Packet(char type, ushort sequence, byte[] payload, byte flags = 0)
    {
        var p = new byte[8 + payload.Length];
        p[0] = (byte)'Y';
        p[1] = (byte)'A';
        p[2] = (byte)type;
        p[3] = flags;
        p[4] = (byte)payload.Length;
        p[5] = (byte)(payload.Length >> 8);
        p[6] = (byte)sequence;
        p[7] = (byte)(sequence >> 8);
        payload.CopyTo(p, 8);
        return p;
    }

    private static byte[] FramePayload(byte fill)
    {
        var f = new byte[4096];
        for (int i = 0; i < f.Length; i++) f[i] = (byte)(fill + i);
        // A false header inside the frame must not matter once the reader is in step.
        f[100] = (byte)'Y';
        f[101] = (byte)'A';
        f[102] = (byte)'T';
        return f;
    }

    [Fact]
    public void Splits_packets_however_the_bytes_are_cut()
    {
        var stream = Packet('F', 1, FramePayload(1))
            .Concat(Packet('T', 2, Encoding.ASCII.GetBytes("ACC pipe: version 2.0.1")))
            .Concat(Packet('F', 3, FramePayload(7), flags: 1))
            .ToArray();

        foreach (int piece in new[] { 1, 2, 7, 8, 9, 100, 4095, 4104, stream.Length })
        {
            var reader = new AccPacketReader();
            var got = new List<AccPacket>();
            for (int at = 0; at < stream.Length; at += piece)
                got.AddRange(reader.Feed(stream, at, Math.Min(piece, stream.Length - at)));

            Assert.Equal(3, got.Count);
            Assert.Equal(AccPacketType.Frame, got[0].Type);
            Assert.Equal(FramePayload(1), got[0].Payload);
            Assert.False(got[0].CutShort);
            Assert.Equal("ACC pipe: version 2.0.1", got[1].Text);
            Assert.True(got[2].CutShort);
            Assert.Equal(3, got[2].Sequence);
            Assert.Equal(0, reader.PacketsMissed);
            Assert.Equal(0, reader.BytesSkipped);
        }
    }

    [Fact]
    public void Finds_its_place_again_after_bad_bytes()
    {
        var junk = new byte[] { 0, (byte)'Y', (byte)'A', (byte)'Q', 0, 0, 0, 0, (byte)'Y', (byte)'A', (byte)'F', 0, 0xFF, 0xFF, 0, 0, (byte)'Y' };
        var stream = junk.Concat(Packet('F', 9, FramePayload(3))).ToArray();
        var reader = new AccPacketReader();

        var got = reader.Feed(stream);

        var only = Assert.Single(got);
        Assert.Equal(9, only.Sequence);
        Assert.Equal(junk.Length, reader.BytesSkipped);
    }

    [Fact]
    public void Counts_missing_packets_but_not_a_restart()
    {
        var reader = new AccPacketReader();
        reader.Feed(Packet('T', 10, Encoding.ASCII.GetBytes("ACC pipe:")));
        reader.Feed(Packet('T', 13, Encoding.ASCII.GetBytes("ACC pipe:")));
        Assert.Equal(2, reader.PacketsMissed);

        reader.Feed(Packet('T', 0, Encoding.ASCII.GetBytes("ACC pipe:")));   // the board restarted
        Assert.Equal(2, reader.PacketsMissed);

        reader.Feed(Packet('T', 65535, Encoding.ASCII.GetBytes("ACC pipe:")));
        reader.Feed(Packet('T', 0, Encoding.ASCII.GetBytes("ACC pipe:")));     // wraps round
        Assert.Equal(2, reader.PacketsMissed);
    }

    [Fact]
    public void Reads_a_raw_stream_from_the_board()
    {
        // Saved by tools/check_frames.py in SPI mode 2: the board's packets as they
        // came, starting part way through one.
        var bytes = File.ReadAllBytes(Captures.PathOf("acc-20261111-113822-mode2.bin"));
        var reader = new AccPacketReader();

        var packets = reader.Feed(bytes);

        var frames = packets.Where(p => p.Type == AccPacketType.Frame).ToList();
        Assert.True(frames.Count > 100, $"only {frames.Count} frames");
        Assert.All(frames, f => Assert.Equal(4096, f.Payload.Length));
        Assert.All(frames, f => Assert.True(AccFrame.Parse(f.Payload).Main.IsValid));
        // The board dropped 35 packets in its first second, before the PC was
        // reading, and its own status says so: the gap in the sequence numbers
        // must match.
        var status = AccStatus.Parse(packets.Last(p => p.Type == AccPacketType.Status).Text);
        Assert.Equal(35, status!.NotSent);
        Assert.Equal(status.NotSent, reader.PacketsMissed);
        Assert.True(reader.BytesSkipped < 8 + 4096, "only the part packet at the start is skipped");
    }
}
