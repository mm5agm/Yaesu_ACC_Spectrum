// ACC Spectrum Viewer: shows the scope of a Yaesu FTdx101 from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

using YaesuAccScope;

namespace AccSpectrumViewer;

/// <summary>
/// Keeps a connection to the board's USB socket: finds it, reads it, and looks
/// again if the lead is pulled out or the board restarts. It uses the
/// library's finder, which only opens Espressif ports, quietly.
/// </summary>
/// <remarks>The events come on background threads.</remarks>
internal sealed class BoardConnection : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private AccSerialReader? _reader;
    private Task? _loop;
    private bool _suspended;

    /// <summary>A short line saying what the connection is doing, and the port if connected.</summary>
    public event Action<string, string?>? StateChanged;

    /// <summary>A frame arrived.</summary>
    public event Action<AccFrame>? FrameReceived;

    /// <summary>A status text arrived.</summary>
    public event Action<AccStatus>? StatusReceived;

    /// <summary>The port being read, or null.</summary>
    public string? Port
    {
        get { lock (_gate) return _reader?.PortName; }
    }

    /// <summary>Packets lost on the way since connecting.</summary>
    public long PacketsMissed
    {
        get { lock (_gate) return _reader?.PacketsMissed ?? 0; }
    }

    /// <summary>Starts looking for the board.</summary>
    public void Start()
    {
        lock (_gate) _loop ??= Task.Run(Run);
    }

    /// <summary>Closes the port and stops looking, so another program (esptool) can use the board.</summary>
    public void Suspend()
    {
        lock (_gate)
        {
            _suspended = true;
            CloseReader();
        }
        StateChanged?.Invoke("Paused while flashing.", null);
    }

    /// <summary>Starts looking for the board again.</summary>
    public void Resume()
    {
        lock (_gate) _suspended = false;
        _wake.Set();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _wake.Set();
        try { _loop?.Wait(3000); } catch (AggregateException) { }
        lock (_gate) CloseReader();
        _stop.Dispose();
        _wake.Dispose();
    }

    private void Run()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            bool connected, suspended;
            lock (_gate)
            {
                connected = _reader != null;
                suspended = _suspended;
            }
            if (suspended || connected)
            {
                Wait(TimeSpan.FromSeconds(1));
                continue;
            }

            var ports = AccBoardFinder.EspressifPorts();
            if (ports.Count == 0)
            {
                StateChanged?.Invoke("Looking for the board: plug its socket marked USB into this PC.", null);
                Wait(TimeSpan.FromSeconds(2));
                continue;
            }

            string? found = null;
            foreach (var port in ports)
            {
                if (token.IsCancellationRequested) return;
                StateChanged?.Invoke($"Listening on {port} for the board...", null);
                if (AccBoardFinder.Probe(port, null, out _))
                {
                    found = port;
                    break;
                }
            }

            lock (_gate)
            {
                if (_suspended || token.IsCancellationRequested) continue;
                if (found != null) Connect(found);
            }
            if (found == null)
            {
                StateChanged?.Invoke(
                    $"Found {string.Join(", ", ports)} but heard nothing from the board. Is the firmware flashed? Looking again...", null);
                Wait(TimeSpan.FromSeconds(3));
            }
        }
    }

    // Called with the lock held.
    private void Connect(string port)
    {
        var reader = new AccSerialReader(port);
        reader.FrameReceived += (_, e) => FrameReceived?.Invoke(e.Frame);
        reader.StatusReceived += (_, e) => StatusReceived?.Invoke(e.Status);
        reader.Failed += (_, ex) => Lost(reader, ex);
        try
        {
            reader.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StateChanged?.Invoke($"Couldn't open {port}: {ex.Message}", null);
            return;
        }
        _reader = reader;
        StateChanged?.Invoke($"Board on {port}", port);
    }

    private void Lost(AccSerialReader reader, Exception ex)
    {
        lock (_gate)
        {
            if (_reader != reader) return;
            CloseReader();
        }
        StateChanged?.Invoke($"Lost the board on {reader.PortName} ({ex.Message}). Looking again...", null);
        _wake.Set();
    }

    // Called with the lock held.
    private void CloseReader()
    {
        var reader = _reader;
        _reader = null;
        reader?.Dispose();
    }

    private void Wait(TimeSpan time)
    {
        try
        {
            _wake.Wait(time, _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        _wake.Reset();
    }
}
