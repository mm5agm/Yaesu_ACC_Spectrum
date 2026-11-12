// ACC Firmware Flasher: writes the ACC board's firmware to its ESP32-S3.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using System.Reflection;

namespace AccFirmwareFlasher;

/// <summary>
/// Writes the firmware to the ESP32-S3 with esptool. Only the board's own two
/// ports are offered, found by USB ID, never a radio's.
/// </summary>
internal sealed class FlashForm : Form
{
    private readonly ComboBox _ports = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _firmware = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _flash = new() { Text = "&Flash", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", DefaultFont.Size),
        BackColor = Color.FromArgb(0x10, 0x10, 0x10),
        ForeColor = Color.Gainsboro,
        WordWrap = true,
    };
    private CancellationTokenSource? _running;

    public FlashForm()
    {
        Text = "ACC Firmware Flasher " + Version;
        // Sized from the font, so it fits with Windows' text made larger.
        int em = Font.Height;
        ClientSize = new Size(em * 46, em * 30);
        MinimumSize = new Size(em * 34, em * 24);
        StartPosition = FormStartPosition.CenterScreen;
        CancelButton = _close;

        var intro = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10),
            Text = "This writes the firmware to the board's ESP32-S3 with Espressif's esptool. " +
                   "Do it with the radio's DC lead out. " +
                   "Plug the board's socket marked COM into this PC; its socket marked USB works too. " +
                   "Only the board's own ports are listed (USB IDs 1A86:55D3 and 303A:1001), never a radio's.",
        };

        var refresh = new Button { Text = "&Refresh", AutoSize = true };
        refresh.Click += (_, _) => FillPorts();
        var browse = new Button { Text = "&Browse...", AutoSize = true };
        browse.Click += (_, _) => Browse();
        _flash.Click += async (_, _) => await FlashAsync();
        _close.Click += (_, _) => Close();   // while flashing, closing means Stop

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, em));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(intro, 0, 0);
        grid.SetColumnSpan(intro, 3);
        grid.Controls.Add(new Label { Text = "Port:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_ports, 1, 1);
        grid.Controls.Add(refresh, 2, 1);
        grid.Controls.Add(new Label { Text = "Firmware:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_firmware, 1, 2);
        grid.Controls.Add(browse, 2, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(_close);
        actions.Controls.Add(_flash);
        grid.Controls.Add(actions, 0, 3);
        grid.SetColumnSpan(actions, 3);
        grid.Controls.Add(_progress, 0, 4);
        grid.SetColumnSpan(_progress, 3);
        grid.Controls.Add(_log, 0, 5);
        grid.SetColumnSpan(_log, 3);
        Controls.Add(grid);

        _ports.Anchor = _firmware.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _ports.SelectedIndexChanged += (_, _) => UpdateButtons();
        _firmware.SelectedIndexChanged += (_, _) => UpdateButtons();
        FillPorts();
        FillFirmware(null);
        if (!File.Exists(EspTool.ExePath))
            Log($"esptool isn't where it should be ({EspTool.ExePath}). Download the release zip, " +
                "which has it, or run viewer\\get-esptool.ps1 if you built it yourself.");
        WarnIfViewerRunning();
    }

    private void FillPorts()
    {
        var ports = BoardPorts.Find();
        _ports.Items.Clear();
        foreach (var p in ports) _ports.Items.Add(p);
        if (_ports.Items.Count > 0) _ports.SelectedIndex = 0;
        Log(ports.Count == 0
            ? "No board found. Plug its socket marked COM into this PC and press Refresh."
            : "Found " + string.Join(", ", ports.Select(p => p.ToString())) + ".");
        UpdateButtons();
    }

    private void FillFirmware(string? choose)
    {
        _firmware.Items.Clear();
        foreach (var f in EspTool.BundledFirmware()) _firmware.Items.Add(new FirmwareFile(f));
        if (choose != null)
        {
            var chosen = new FirmwareFile(choose);
            _firmware.Items.Insert(0, chosen);
        }
        if (_firmware.Items.Count > 0) _firmware.SelectedIndex = 0;
        UpdateButtons();
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the firmware",
            Filter = "Merged firmware (*-merged.bin)|*-merged.bin|All .bin files (*.bin)|*.bin",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) FillFirmware(dialog.FileName);
    }

    private void UpdateButtons()
    {
        bool running = _running != null;
        _flash.Enabled = !running && _ports.SelectedItem != null && _firmware.SelectedItem != null && File.Exists(EspTool.ExePath);
        _close.Text = running ? "Stop" : "Close";
    }

    private async Task FlashAsync()
    {
        if (_ports.SelectedItem is not BoardPort port || _firmware.SelectedItem is not FirmwareFile firmware) return;
        // Look again just before: the port must still be one of the board's.
        if (!BoardPorts.Find().Any(p => p.Name.Equals(port.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Log($"{port.Name} isn't there any more. Press Refresh.");
            return;
        }
        WarnIfViewerRunning();
        var answer = MessageBox.Show(this,
            $"Write {firmware.Name} to the board on {port.Name}?\n\nThe radio's DC lead should be out.",
            "Flash firmware", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;

        _running = new CancellationTokenSource();
        UpdateButtons();
        _progress.Value = 0;
        try
        {
            var args = EspTool.WriteFlashArguments(port.Name, firmware.Path);
            Log("esptool " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            int code = await EspTool.RunAsync(args, line => BeginInvoke(() => OnOutput(line)), _running.Token);
            Log(code == 0
                ? "Done. The board restarts with the new firmware. Close this window and start ACC Spectrum Viewer."
                : $"esptool stopped with code {code}. See the lines above. If it can't connect, hold the board's BOOT button, " +
                  "press and let go of RST, let go of BOOT, and try again.");
            if (code == 0) _progress.Value = _progress.Maximum;
        }
        catch (OperationCanceledException)
        {
            Log("Stopped. Flash again before using the board: it may only have part of the firmware.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log("Couldn't run esptool: " + ex.Message);
        }
        finally
        {
            _running.Dispose();
            _running = null;
            UpdateButtons();
        }
    }

    /// <summary>
    /// ACC Spectrum Viewer holds the board's USB socket while it runs, so
    /// esptool can't open that port. The COM socket is free either way.
    /// </summary>
    private void WarnIfViewerRunning()
    {
        if (System.Diagnostics.Process.GetProcessesByName("AccSpectrumViewer").Length > 0)
            Log("ACC Spectrum Viewer is running. It holds the board's USB socket, so close it first, " +
                "or flash through the socket marked COM.");
    }

    private static string Version =>
        typeof(FlashForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    private void OnOutput(string line)
    {
        if (IsDisposed) return;
        var p = EspTool.ProgressIn(line);
        if (p != null) _progress.Value = (int)Math.Round(p.Value * 10);
        Log(line);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running != null)
        {
            // Close while flashing means Stop.
            e.Cancel = true;
            _running.Cancel();
            return;
        }
        base.OnFormClosing(e);
    }

    private void Log(string line)
    {
        _log.AppendText(line + Environment.NewLine);
    }

    private sealed record FirmwareFile(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public override string ToString() => Name;
    }
}
