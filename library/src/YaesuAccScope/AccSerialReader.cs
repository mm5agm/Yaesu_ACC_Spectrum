// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace YaesuAccScope
{
    /// <summary>A decoded frame and the packet it came in.</summary>
    public sealed class AccFrameEventArgs : EventArgs
    {
        internal AccFrameEventArgs(AccFrame frame, AccPacket packet)
        {
            Frame = frame;
            Packet = packet;
        }

        /// <summary>The decoded frame.</summary>
        public AccFrame Frame { get; }

        /// <summary>The packet, for its sequence number and cut-short flag.</summary>
        public AccPacket Packet { get; }
    }

    /// <summary>A status text from the board.</summary>
    public sealed class AccStatusEventArgs : EventArgs
    {
        internal AccStatusEventArgs(AccStatus status) => Status = status;

        /// <summary>The decoded status text.</summary>
        public AccStatus Status { get; }
    }

    /// <summary>
    /// Reads frames from the board's native USB port (the socket marked USB,
    /// USB ID 303A:1001).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It opens the port with DTR and RTS low and never writes to it. On the
    /// ESP32-S3's own USB port a DTR/RTS sequence resets the chip, and a byte
    /// written to it can change the SPI mode, so a program using this can do
    /// neither.
    /// </para>
    /// <para>
    /// The events come on the reader's own thread. Hand the work to your UI
    /// thread if you need to, and don't block: the board sends 25 frames a
    /// second.
    /// </para>
    /// </remarks>
    public sealed class AccSerialReader : IDisposable
    {
        private readonly object _gate = new object();
        private readonly AccPacketReader _packets = new AccPacketReader();
        private SerialPort? _port;
        private Thread? _thread;
        private volatile bool _stopping;

        /// <summary>Makes a reader for a port, such as "COM6" or "/dev/ttyACM0". Nothing is opened yet.</summary>
        public AccSerialReader(string portName)
        {
            if (string.IsNullOrEmpty(portName)) throw new ArgumentException("A port name is needed.", nameof(portName));
            PortName = portName;
        }

        /// <summary>The port this reads.</summary>
        public string PortName { get; }

        /// <summary>A frame arrived and was decoded.</summary>
        public event EventHandler<AccFrameEventArgs>? FrameReceived;

        /// <summary>A status text arrived. The board sends one every 5 seconds.</summary>
        public event EventHandler<AccStatusEventArgs>? StatusReceived;

        /// <summary>
        /// The port failed while reading, for example because the lead was pulled
        /// out. The reader has stopped; call <see cref="Start"/> to try again.
        /// </summary>
        public event EventHandler<Exception>? Failed;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>, while the port is open.</summary>
        public bool IsRunning => _thread != null && !_stopping;

        /// <summary>The last status text received, or null.</summary>
        public AccStatus? LastStatus { get; private set; }

        /// <summary>Frames received since <see cref="Start"/>.</summary>
        public long FramesReceived { get; private set; }

        /// <summary>Packets lost on the way, from gaps in the sequence numbers.</summary>
        public long PacketsMissed => _packets.PacketsMissed;

        /// <summary>Opens the port and starts reading. Throws if the port can't be opened.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_thread != null) return;
                _stopping = false;
                _packets.Reset();
                FramesReceived = 0;
                _port = OpenQuietly(PortName);
                _thread = new Thread(Run) { IsBackground = true, Name = "ACC scope reader " + PortName };
                _thread.Start();
            }
        }

        /// <summary>Stops reading and closes the port.</summary>
        public void Stop()
        {
            Thread? thread;
            lock (_gate)
            {
                thread = _thread;
                if (thread == null) return;
                _stopping = true;
                ClosePort();
            }
            if (thread != Thread.CurrentThread) thread.Join(2000);
            lock (_gate) _thread = null;
        }

        /// <summary>Stops reading and closes the port.</summary>
        public void Dispose() => Stop();

        /// <summary>
        /// Opens a port the way this library always does: DTR and RTS low, no
        /// handshake, so opening it can't reset the board.
        /// </summary>
        internal static SerialPort OpenQuietly(string portName)
        {
            var port = new SerialPort(portName)
            {
                BaudRate = 115200,      // ignored by USB serial, but a value is needed
                DtrEnable = false,
                RtsEnable = false,
                Handshake = Handshake.None,
                ReadTimeout = 500,
                ReadBufferSize = 256 * 1024,
            };
            port.Open();
            return port;
        }

        private void Run()
        {
            var buffer = new byte[16 * 1024];
            Exception? failure = null;
            try
            {
                var stream = _port!.BaseStream;
                while (!_stopping)
                {
                    int n;
                    try
                    {
                        n = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }
                    if (n <= 0) continue;
                    foreach (var packet in _packets.Feed(buffer, 0, n)) Handle(packet);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException ||
                                       ex is UnauthorizedAccessException || ex is ObjectDisposedException ||
                                       ex is OperationCanceledException)
            {
                if (!_stopping) failure = ex;
            }

            if (failure != null)
            {
                lock (_gate)
                {
                    _stopping = true;
                    ClosePort();
                    _thread = null;
                }
                Failed?.Invoke(this, failure);
            }
        }

        private void Handle(AccPacket packet)
        {
            if (packet.Type == AccPacketType.Frame)
            {
                if (!AccFrame.TryParse(packet.Payload, out var frame)) return;
                FramesReceived++;
                FrameReceived?.Invoke(this, new AccFrameEventArgs(frame!, packet));
            }
            else
            {
                var status = AccStatus.Parse(packet.Text);
                if (status == null) return;
                LastStatus = status;
                StatusReceived?.Invoke(this, new AccStatusEventArgs(status));
            }
        }

        private void ClosePort()
        {
            try { _port?.Close(); } catch (IOException) { }
            _port = null;
        }
    }
}
