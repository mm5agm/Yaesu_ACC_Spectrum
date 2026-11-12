// ACC Firmware Flasher: writes the ACC board's firmware to its ESP32-S3.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using System.IO.Ports;
using Microsoft.Win32;

namespace AccFirmwareFlasher;

/// <summary>A serial port that belongs to the board.</summary>
internal sealed record BoardPort(string Name, int VendorId, int ProductId, string Description)
{
    /// <summary>The board's COM socket: its CH343 USB-to-serial chip.</summary>
    public bool IsComSocket => VendorId == BoardPorts.WchVendorId && ProductId == BoardPorts.Ch343ProductId;

    /// <summary>The board's USB socket: the ESP32-S3's own USB serial port.</summary>
    public bool IsUsbSocket => VendorId == BoardPorts.EspressifVendorId && ProductId == BoardPorts.EspressifSerialProductId;

    public override string ToString() =>
        IsComSocket ? $"{Name}: the board's COM socket (CH343)"
        : IsUsbSocket ? $"{Name}: the board's USB socket (ESP32-S3)"
        : $"{Name}: {Description}";
}

/// <summary>
/// Finds the board's two ports by USB ID, from the registry, without opening
/// any port. Only the CH343 (1A86:55D3) and the ESP32-S3's own port
/// (303A:1001) are ever listed, so flashing can't pick a radio's CAT port,
/// where the DTR and RTS that esptool uses could key the transmitter.
/// </summary>
internal static class BoardPorts
{
    public const int WchVendorId = 0x1A86;
    public const int Ch343ProductId = 0x55D3;
    public const int EspressifVendorId = 0x303A;
    public const int EspressifSerialProductId = 0x1001;

    /// <summary>True for the two USB IDs the board's ports have.</summary>
    public static bool IsBoardId(int vendorId, int productId) =>
        (vendorId == WchVendorId && productId == Ch343ProductId) ||
        (vendorId == EspressifVendorId && productId == EspressifSerialProductId);

    /// <summary>The board's ports that are plugged in now, COM socket first.</summary>
    public static IReadOnlyList<BoardPort> Find()
    {
        var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
        var found = new List<BoardPort>();
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb == null) return found;
            foreach (var device in usb.GetSubKeyNames())
            {
                if (!TryParseIds(device, out int vid, out int pid) || !IsBoardId(vid, pid)) continue;
                using var deviceKey = usb.OpenSubKey(device);
                if (deviceKey == null) continue;
                foreach (var instance in deviceKey.GetSubKeyNames())
                {
                    using var instanceKey = deviceKey.OpenSubKey(instance);
                    using var parameters = instanceKey?.OpenSubKey("Device Parameters");
                    if (parameters?.GetValue("PortName") is not string name || !present.Contains(name)) continue;
                    if (found.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    var description = instanceKey?.GetValue("FriendlyName") as string ?? name;
                    found.Add(new BoardPort(name, vid, pid, description));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return found.OrderBy(p => p.IsComSocket ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads "VID_1A86&amp;PID_55D3", with or without "&amp;MI_00" after it.</summary>
    internal static bool TryParseIds(string device, out int vendorId, out int productId)
    {
        vendorId = productId = 0;
        var parts = device.Split('&');
        if (parts.Length < 2 || !parts[0].StartsWith("VID_", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].StartsWith("PID_", StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(parts[0].AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out vendorId) &&
               int.TryParse(parts[1].AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out productId);
    }
}
