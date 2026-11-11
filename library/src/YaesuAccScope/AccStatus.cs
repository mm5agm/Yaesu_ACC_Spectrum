// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YaesuAccScope
{
    /// <summary>
    /// The board's status text, decoded. The board sends one every 5 seconds.
    /// </summary>
    /// <remarks>
    /// Version 2 firmware sends, on one line: "ACC pipe: version 2.0.1, SPI mode
    /// 2, frames 1234, bytes 5054464, cut short 0, not sent 0, DATA1 0, up 49 s,
    /// edges/s CS 25 CLOCK 819261 DATA2 147000 DATA1 0". Fields the text doesn't
    /// have, as from version 1 firmware, are null.
    /// </remarks>
    public sealed class AccStatus
    {
        /// <summary>Every status text starts with this.</summary>
        public const string Prefix = "ACC pipe:";

        private static readonly Regex Version = new Regex(@"version ([0-9][0-9A-Za-z.\-]*)", RegexOptions.CultureInvariant);
        private static readonly Regex Edges = new Regex(@"edges/s((?:\s+[A-Z][A-Z0-9]*\s+\d+)+)", RegexOptions.CultureInvariant);
        private static readonly Regex Pair = new Regex(@"([A-Z][A-Z0-9]*)\s+(\d+)", RegexOptions.CultureInvariant);

        private AccStatus(string text) => Text = text;

        /// <summary>The whole text.</summary>
        public string Text { get; }

        /// <summary>The firmware version, such as "2.0.1".</summary>
        public string? FirmwareVersion { get; private set; }

        /// <summary>The SPI mode the firmware samples in: 2 is right for the FTdx101.</summary>
        public int? SpiMode { get; private set; }

        /// <summary>Frames received from the radio since the board started.</summary>
        public long? Frames { get; private set; }

        /// <summary>Bytes received from the radio since the board started.</summary>
        public long? Bytes { get; private set; }

        /// <summary>Frames that filled the board's buffer and may be cut short.</summary>
        public long? CutShort { get; private set; }

        /// <summary>Packets the board couldn't send because the PC wasn't reading fast enough.</summary>
        public long? NotSent { get; private set; }

        /// <summary>The DATA1 input (ACC pin 3) when the text was made: 0 or 1.</summary>
        public int? Data1 { get; private set; }

        /// <summary>Seconds since the board started.</summary>
        public long? UpSeconds { get; private set; }

        /// <summary>
        /// Rising edges in the last second on each input, by name: CS, CLOCK,
        /// DATA2 and DATA1. With the radio streaming, expect CS 25 and CLOCK about
        /// 819200. Empty before version 2.
        /// </summary>
        public IReadOnlyDictionary<string, long> EdgesPerSecond { get; private set; } = new Dictionary<string, long>();

        /// <summary>Decodes a status text, or returns null if it doesn't start with "ACC pipe:".</summary>
        public static AccStatus? Parse(string? text)
        {
            if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var s = new AccStatus(text);

            var v = Version.Match(text);
            if (v.Success) s.FirmwareVersion = v.Groups[1].Value.TrimEnd('.');
            s.SpiMode = (int?)Number(text, "SPI mode");
            s.Frames = Number(text, "frames");
            s.Bytes = Number(text, "bytes");
            s.CutShort = Number(text, "cut short");
            s.NotSent = Number(text, "not sent");
            s.Data1 = (int?)Number(text, "DATA1");
            s.UpSeconds = Number(text, "up");

            var e = Edges.Match(text);
            if (e.Success)
            {
                var edges = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (Match m in Pair.Matches(e.Groups[1].Value))
                    edges[m.Groups[1].Value] = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                s.EdgesPerSecond = edges;
            }
            return s;
        }

        // "name 123", taken from before "edges/s" so "DATA1 0" there isn't read twice.
        private static long? Number(string text, string name)
        {
            int end = text.IndexOf("edges/s", StringComparison.Ordinal);
            string head = end < 0 ? text : text.Substring(0, end);
            var m = Regex.Match(head, @"(?:^|[\s,:])" + Regex.Escape(name) + @"\s+(\d+)", RegexOptions.CultureInvariant);
            return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (long?)null;
        }
    }
}
