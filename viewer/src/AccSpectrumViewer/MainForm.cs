// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using YaesuAccScope;

namespace AccSpectrumViewer;

/// <summary>
/// The main window: Main and Sub scopes, the wiring check, and buttons.
/// Space pauses, D switches to 3DSS, A works the colours out again.
/// </summary>
internal sealed class MainForm : Form
{
    public const string ProjectUrl = "https://github.com/mm5agm/Yaesu_ACC_Spectrum";

    private readonly BoardConnection _board = new();
    private readonly ConcurrentQueue<AccFrame> _frames = new();
    private readonly ScopeView _main = new("Main") { Dock = DockStyle.Fill };
    private readonly ScopeView _sub = new("Sub") { Dock = DockStyle.Fill };
    private readonly WiringView _wiring = new() { Dock = DockStyle.Bottom };
    private readonly Label _state = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gainsboro };
    private readonly CheckBox _pause = MakeToggle("&Pause");
    private readonly CheckBox _dss = MakeToggle("&3DSS");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();
    private readonly ToolTip _tips = new();
    private AccStatus? _status;
    private int _framesThisSecond;
    private double? _framesPerSecond;
    private bool? _framesDecode;
    private string? _port;

    public MainForm()
    {
        Text = "ACC Spectrum Viewer " + Version;
        BackColor = Color.Black;
        ForeColor = Color.White;
        ClientSize = new Size(1280, 860);
        MinimumSize = new Size(700, 500);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        var colours = MakeButton("&Colours", "Work the waterfall colours out again from what's on screen now (A)");
        colours.Click += (_, _) => ResetColours();
        var project = MakeButton("Project page", ProjectUrl);
        project.Click += (_, _) => OpenUrl(ProjectUrl);
        var tips = _tips;
        tips.SetToolTip(_pause, "Freeze the display (Space)");
        tips.SetToolTip(_dss, "Show the last 40 traces in perspective, like the radio's 3DSS (D)");
        _dss.CheckedChanged += (_, _) =>
        {
            _main.Show3dss = _sub.Show3dss = _dss.Checked;
            _main.Invalidate();
            _sub.Invalidate();
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };
        buttons.Controls.AddRange(new Control[] { _pause, _dss, colours, project });
        buttons.Dock = DockStyle.None;
        buttons.Anchor = AnchorStyles.Left;
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(0x14, 0x14, 0x14),
            Padding = new Padding(4, 2, 8, 2),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(buttons, 0, 0);
        top.Controls.Add(_state, 1, 0);

        var scopes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Black };
        scopes.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        scopes.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _main.Margin = _sub.Margin = new Padding(4, 2, 4, 2);
        scopes.Controls.Add(_main, 0, 0);
        scopes.Controls.Add(_sub, 0, 1);

        Controls.Add(scopes);
        Controls.Add(_wiring);
        Controls.Add(top);

        _board.StateChanged += (text, port) => OnUi(() => ShowState(text, port));
        _board.FrameReceived += frame =>
        {
            _frames.Enqueue(frame);
            while (_frames.Count > 50) _frames.TryDequeue(out _);   // the screen fell behind: keep the newest
        };
        _board.StatusReceived += status => OnUi(() =>
        {
            _status = status;
            UpdateWiring();
        });
        _timer.Tick += (_, _) => Tick();
    }

    private static string Version =>
        typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
        _board.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _board.Dispose();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space:
                _pause.Checked = !_pause.Checked;
                return true;
            case Keys.D:
                _dss.Checked = !_dss.Checked;
                return true;
            case Keys.A:
                ResetColours();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Tick()
    {
        bool changed = false;
        while (_frames.TryDequeue(out var frame))
        {
            _framesThisSecond++;
            _framesDecode = frame.Main.IsValid || frame.Sub.IsValid;
            if (_pause.Checked) continue;
            _main.AddTrace(frame.Main);
            _sub.AddTrace(frame.Sub);
            changed = true;
        }
        if (changed)
        {
            _main.Invalidate();
            _sub.Invalidate();
        }

        if (_rateClock.Elapsed >= TimeSpan.FromSeconds(1))
        {
            _framesPerSecond = _port == null ? null : _framesThisSecond / _rateClock.Elapsed.TotalSeconds;
            _framesThisSecond = 0;
            _rateClock.Restart();
            if (_framesPerSecond is 0) _framesDecode = null;
            UpdateWiring();
        }
    }

    private void ShowState(string text, string? port)
    {
        bool lost = _port != null && port == null;
        _port = port;
        _state.Text = text;
        _tips.SetToolTip(_state, text);     // in full, when the window is too narrow for it
        Text = "ACC Spectrum Viewer " + Version + (port == null ? "" : " - board on " + port);
        if (port == null)
        {
            _status = null;
            _framesPerSecond = null;
            _framesDecode = null;
            UpdateWiring();
            if (lost)
            {
                _main.ShowMessage("Not connected.");
                _sub.ShowMessage("Not connected.");
            }
        }
    }

    private void UpdateWiring()
    {
        var header = "Wiring check";
        if (_status != null)
        {
            header = $"Firmware {_status.FirmwareVersion ?? "1"}";
            if (_status.UpSeconds != null) header += $", up {Uptime(_status.UpSeconds.Value)}";
        }
        long missed = _board.PacketsMissed;
        if (missed > 0) header += $", {missed:N0} lost";
        _wiring.Show(WiringCheck.Evaluate(_status, _framesPerSecond, _framesDecode), header);
    }

    internal static string Uptime(long seconds) =>
        seconds < 120 ? $"{seconds} s" : seconds < 7200 ? $"{seconds / 60} min" : $"{seconds / 3600} h {seconds % 3600 / 60} min";

    private void ResetColours()
    {
        _main.ResetColours();
        _sub.ResetColours();
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(url, "Couldn't open the browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static CheckBox MakeToggle(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(0x30, 0x30, 0x30),
        Margin = new Padding(3, 3, 3, 3),
    };

    private static Button MakeButton(string text, string tip)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,   // fit the text, like the Pause and 3DSS toggles
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0x30, 0x30, 0x30),
            Margin = new Padding(3, 3, 3, 3),
            AccessibleDescription = tip,
        };
        return b;
    }
}
