// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace YaesuAccScope
{
    /// <summary>
    /// Finds the board's port. It only ever opens ports with Espressif's USB
    /// vendor ID (303A), so it never opens a radio's CAT port, where DTR or RTS
    /// can key the transmitter.
    /// </summary>
    public static class AccBoardFinder
    {
        /// <summary>Espressif's USB vendor ID.</summary>
        public const int EspressifVendorId = 0x303A;

        /// <summary>
        /// How long <see cref="Probe"/> waits by default: a little over the 5
        /// seconds between status texts.
        /// </summary>
        public static readonly TimeSpan DefaultProbeTime = TimeSpan.FromSeconds(6);

        /// <summary>
        /// The serial ports with Espressif's USB vendor ID, found without opening
        /// any port. On Windows from the registry, on Linux from /sys. On macOS,
        /// where neither works, the /dev/cu.usbmodem ports, which is how an
        /// ESP32-S3's own USB port shows up there; check them with
        /// <see cref="Probe"/>.
        /// </summary>
        public static IReadOnlyList<string> EspressifPorts()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return WindowsPorts();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return LinuxPorts();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Directory.GetFiles("/dev", "cu.usbmodem*").OrderBy(p => p, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Opens a port quietly (DTR and RTS low, nothing written) and listens for
        /// the board's packets. True if it hears a frame or a status text in time.
        /// Refuses a port that isn't in <see cref="EspressifPorts"/>.
        /// </summary>
        /// <param name="portName">The port, such as "COM6".</param>
        /// <param name="timeout">How long to listen; <see cref="DefaultProbeTime"/> if null.</param>
        /// <param name="status">The status text, if one came in time.</param>
        public static bool Probe(string portName, TimeSpan? timeout, out AccStatus? status)
        {
            status = null;
            if (!EspressifPorts().Contains(portName, StringComparer.OrdinalIgnoreCase)) return false;

            var packets = new AccPacketReader();
            var buffer = new byte[16 * 1024];
            var clock = Stopwatch.StartNew();
            var limit = timeout ?? DefaultProbeTime;
            bool heard = false;
            try
            {
                using var port = AccSerialReader.OpenQuietly(portName);
                var stream = port.BaseStream;
                while (clock.Elapsed < limit)
                {
                    int n;
                    try { n = stream.Read(buffer, 0, buffer.Length); }
                    catch (TimeoutException) { continue; }
                    foreach (var packet in packets.Feed(buffer, 0, n))
                    {
                        if (packet.Type == AccPacketType.Frame && packet.Payload.Length >= AccFrame.MinLength) heard = true;
                        var s = AccStatus.Parse(packet.Text);
                        if (s != null)
                        {
                            status = s;
                            return true;
                        }
                    }
                    // Frames alone are enough once a few have come whole.
                    if (heard && clock.Elapsed > TimeSpan.FromMilliseconds(500)) return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is OperationCanceledException)
            {
                return false;
            }
            return heard;
        }

        /// <summary>
        /// The first Espressif port that sends the board's packets, or null. Takes
        /// up to <paramref name="timeoutPerPort"/> for each Espressif port.
        /// </summary>
        public static string? Find(TimeSpan? timeoutPerPort = null)
        {
            foreach (var port in EspressifPorts())
                if (Probe(port, timeoutPerPort, out _)) return port;
            return null;
        }

        // HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_303A&PID_xxxx[&MI_xx]\<instance>\Device Parameters\PortName,
        // kept only if the port is present now.
        private static IReadOnlyList<string> WindowsPorts()
        {
            var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string vid = "VID_" + EspressifVendorId.ToString("X4");
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb == null) return Array.Empty<string>();
            foreach (var device in usb.GetSubKeyNames())
            {
                if (!device.StartsWith(vid, StringComparison.OrdinalIgnoreCase)) continue;
                using var deviceKey = usb.OpenSubKey(device);
                if (deviceKey == null) continue;
                foreach (var instance in deviceKey.GetSubKeyNames())
                {
                    using var parameters = deviceKey.OpenSubKey(instance + @"\Device Parameters");
                    if (parameters?.GetValue("PortName") is string name && present.Contains(name)) found.Add(name);
                }
            }
            return found.ToList();
        }

        // /sys/class/tty/<name>/device points at the USB interface; idVendor is in
        // the USB device a level or two up.
        private static IReadOnlyList<string> LinuxPorts()
        {
            var found = new List<string>();
            const string ttys = "/sys/class/tty";
            if (!Directory.Exists(ttys)) return found;
            foreach (var tty in Directory.GetDirectories(ttys).OrderBy(p => p, StringComparer.Ordinal))
            {
                var dir = Path.Combine(tty, "device");
                if (!Directory.Exists(dir)) continue;
                for (int up = 0; up < 4; up++)
                {
                    var vendor = Path.Combine(dir, "idVendor");
                    if (File.Exists(vendor))
                    {
                        if (int.TryParse(File.ReadAllText(vendor).Trim(), System.Globalization.NumberStyles.HexNumber, null, out int id)
                            && id == EspressifVendorId)
                            found.Add("/dev/" + Path.GetFileName(tty));
                        break;
                    }
                    dir = Path.Combine(dir, "..");
                }
            }
            return found;
        }
    }
}
