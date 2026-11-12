// ACC Firmware Flasher: writes the ACC board's firmware to its ESP32-S3.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AccFirmwareFlasher;

/// <summary>
/// Runs Espressif's esptool, which comes with the viewer in its esptool
/// folder, to write the firmware. esptool is GPL-2.0, from
/// https://github.com/espressif/esptool, and runs as a separate program.
/// </summary>
internal static partial class EspTool
{
    /// <summary>Where esptool.exe should be: the esptool folder beside the viewer.</summary>
    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "esptool", "esptool.exe");

    /// <summary>The firmware files that came with the viewer, newest first.</summary>
    public static IReadOnlyList<string> BundledFirmware()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "firmware");
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.GetFiles(folder, "*.bin").OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The arguments to write a merged firmware file (bootloader, partition
    /// table and app in one) at address 0, as on the GitHub release.
    /// </summary>
    public static string[] WriteFlashArguments(string port, string firmware) =>
        new[] { "--chip", "esp32s3", "--port", port, "--baud", "460800", "write-flash", "0x0", firmware };

    /// <summary>The percentage in a line of esptool's progress, such as "Writing at 0x00010000 [===>   ] 35.2% 65536/185344 bytes...".</summary>
    public static double? ProgressIn(string line)
    {
        var m = Percent().Match(line);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p <= 100
            ? p
            : null;
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d+)?)\s?%")]
    private static partial Regex Percent();

    /// <summary>
    /// Runs esptool with the arguments, passing each line of its output on as
    /// it comes. Returns its exit code: 0 when it worked.
    /// </summary>
    public static async Task<int> RunAsync(IEnumerable<string> arguments, Action<string> output, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        start.Environment["PYTHONUNBUFFERED"] = "1";

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        process.WaitForExit();      // lets the last output lines arrive
        return process.ExitCode;
    }
}
