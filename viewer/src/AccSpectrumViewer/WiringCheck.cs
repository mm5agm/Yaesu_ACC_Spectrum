// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using YaesuAccScope;

namespace AccSpectrumViewer;

/// <summary>How one reading compares with what a working board shows.</summary>
internal enum CheckState
{
    /// <summary>Nothing to judge yet.</summary>
    Unknown,
    /// <summary>As expected.</summary>
    Good,
    /// <summary>There, but not what's expected.</summary>
    Warning,
    /// <summary>Missing: something's wrong.</summary>
    Bad,
    /// <summary>Shown for interest; not judged.</summary>
    Info,
}

/// <summary>One line of the wiring check.</summary>
internal sealed record CheckItem(string Name, string Value, CheckState State, string Where);

/// <summary>The wiring check: each reading, and what to do about it.</summary>
internal sealed record WiringReport(IReadOnlyList<CheckItem> Items, CheckState Overall, string Advice);

/// <summary>
/// Turns the board's status text into a wiring check. With the radio
/// streaming, a working board counts about 25 CS edges and 819,200 CLOCK
/// edges a second, and DATA2 changes all the time.
/// </summary>
internal static class WiringCheck
{
    /// <param name="status">The last status text, or null if none has come.</param>
    /// <param name="framesPerSecond">Frames reaching this PC in the last second, or null if not connected.</param>
    /// <param name="framesDecode">False if frames are coming but the tuning in them makes no sense; null if no frames.</param>
    public static WiringReport Evaluate(AccStatus? status, double? framesPerSecond, bool? framesDecode)
    {
        var items = new List<CheckItem>();
        if (status == null)
        {
            if (framesPerSecond is > 0) items.Add(Frames(framesPerSecond.Value));
            return new WiringReport(items, CheckState.Unknown,
                framesPerSecond == null
                    ? "Not connected to the board yet."
                    : "Waiting for the board's status text, which comes every 5 seconds.");
        }

        var edges = status.EdgesPerSecond;
        if (edges.Count == 0)
        {
            if (framesPerSecond != null) items.Add(Frames(framesPerSecond.Value));
            return new WiringReport(items, CheckState.Unknown,
                $"Firmware {status.FirmwareVersion ?? "version 1"} doesn't count edges. Flash version 2 or later for the wiring check.");
        }

        long cs = edges.TryGetValue("CS", out var c) ? c : 0;
        long clock = edges.TryGetValue("CLOCK", out var k) ? k : 0;
        long data2 = edges.TryGetValue("DATA2", out var d) ? d : 0;
        long data1 = edges.TryGetValue("DATA1", out var d1) ? d1 : 0;

        items.Add(new CheckItem("CS", $"{cs:N0}/s", Judge(cs, 20, 30), "ACC pin 6, terminal 13; 25 expected"));
        items.Add(new CheckItem("CLOCK", $"{clock:N0}/s", Judge(clock, 780_000, 860_000), "ACC pin 5, terminal 10; about 819,200 expected"));
        items.Add(new CheckItem("DATA2", $"{data2:N0}/s", data2 == 0 ? CheckState.Bad : data2 < 1000 ? CheckState.Warning : CheckState.Good,
                                "ACC pin 4, terminal 11; changes all the time"));
        items.Add(new CheckItem("DATA1", $"{data1:N0}/s", CheckState.Info, "ACC pin 3, terminal 14; not used"));
        if (status.SpiMode != null)
            items.Add(new CheckItem("SPI mode", status.SpiMode.Value.ToString(), status.SpiMode == 2 ? CheckState.Good : CheckState.Warning,
                                    "the firmware's setting; 2 for the FTdx101"));
        if (framesPerSecond != null) items.Add(Frames(framesPerSecond.Value));

        string advice;
        CheckState overall = CheckState.Bad;
        if (cs == 0 && clock == 0)
            advice = "Nothing is coming from the radio. Check it's switched on, that ACC pins 11 and 12 are linked " +
                     "(terminals 39 and 38) so it streams, and that ground (ACC pin 2) is connected.";
        else if (cs == 0)
            advice = "CLOCK is there but CS isn't. Check the CS wire: ACC pin 6 to terminal 13, through its 470 ohm resistor.";
        else if (clock == 0)
            advice = "CS is there but CLOCK isn't. Check the CLOCK wire: ACC pin 5 to terminal 10, through its 470 ohm resistor.";
        else if (data2 == 0)
            advice = "CS and CLOCK are there but there's no data. Check the DATA2 wire: ACC pin 4 to terminal 11, through its 470 ohm resistor.";
        else if (framesDecode == false)
            advice = "Frames are coming but don't decode. The SPI mode may be wrong: run tools/check_frames.py --mode 2 once.";
        else if (items.Any(i => i.State is CheckState.Warning or CheckState.Bad))
        {
            overall = CheckState.Warning;
            advice = "Everything's connected, but a reading is outside what's expected. A loose wire or a long lead can cause it.";
        }
        else
        {
            overall = CheckState.Good;
            advice = "The wiring looks right: CS, CLOCK and DATA2 are all as expected.";
        }
        return new WiringReport(items, overall, advice);
    }

    private static CheckItem Frames(double perSecond) =>
        new("Frames", $"{perSecond:0.#}/s",
            perSecond <= 0 ? CheckState.Bad : perSecond is >= 23 and <= 27 ? CheckState.Good : CheckState.Warning,
            "reaching this PC; 25 expected");

    private static CheckState Judge(long value, long low, long high) =>
        value == 0 ? CheckState.Bad : value >= low && value <= high ? CheckState.Good : CheckState.Warning;
}
