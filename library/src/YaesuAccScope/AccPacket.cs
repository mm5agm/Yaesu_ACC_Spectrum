// YaesuAccScope: reads a Yaesu FTdx101's own scope data from its ACC socket.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: LGPL-3.0-only (see COPYING.LESSER and COPYING)

using System;
using System.Collections.Generic;
using System.Text;

namespace YaesuAccScope
{
    /// <summary>What a packet from the board carries.</summary>
    public enum AccPacketType
    {
        /// <summary>A frame: the bytes the radio clocked out while CS was low.</summary>
        Frame = 'F',

        /// <summary>A status text, ASCII, starting "ACC pipe:".</summary>
        Status = 'T',
    }

    /// <summary>One packet from the board: an 8-byte header and its payload.</summary>
    /// <remarks>
    /// The header is 'Y' 'A', the type, the flags, the payload length (uint16
    /// little-endian) and a sequence number (uint16 little-endian). See
    /// docs/protocol.md.
    /// </remarks>
    public sealed class AccPacket
    {
        internal AccPacket(AccPacketType type, byte flags, ushort sequence, byte[] payload)
        {
            Type = type;
            Flags = flags;
            Sequence = sequence;
            Payload = payload;
        }

        /// <summary>Frame or status text.</summary>
        public AccPacketType Type { get; }

        /// <summary>The header's flags byte.</summary>
        public byte Flags { get; }

        /// <summary>
        /// True when the frame filled the board's buffer, so it may have been cut
        /// short (flags bit 0).
        /// </summary>
        public bool CutShort => (Flags & 1) != 0;

        /// <summary>
        /// Counts up by one for every packet the board makes, including ones it
        /// couldn't send, so a gap means packets were lost.
        /// </summary>
        public ushort Sequence { get; }

        /// <summary>The payload: the frame bytes, or the status text in ASCII.</summary>
        public byte[] Payload { get; }

        /// <summary>The status text, or null if this is a frame.</summary>
        public string? Text => Type == AccPacketType.Status ? Encoding.ASCII.GetString(Payload) : null;
    }

    /// <summary>
    /// Splits the board's byte stream into packets. Feed it bytes as they arrive,
    /// in pieces of any size; it keeps what's left of a packet for next time.
    /// </summary>
    /// <remarks>
    /// It finds its place again after bad bytes by looking for the next 'Y' 'A'
    /// header with a known type and a sensible length. Not thread-safe: feed it
    /// from one thread.
    /// </remarks>
    public sealed class AccPacketReader
    {
        /// <summary>Bytes in a packet header.</summary>
        public const int HeaderLength = 8;

        /// <summary>
        /// The longest payload taken as real. The firmware's frames are 4096 bytes
        /// and its status texts under 240; anything longer is a false header.
        /// </summary>
        public const int MaxPayload = 8192;

        private byte[] _buffer = new byte[4 * (HeaderLength + MaxPayload)];
        private int _count;
        private int _lastSequence = -1;

        /// <summary>
        /// Packets missing between the ones received, from gaps in the sequence
        /// numbers. A jump back, when the board restarts, isn't counted.
        /// </summary>
        public long PacketsMissed { get; private set; }

        /// <summary>Bytes thrown away while looking for a packet header.</summary>
        public long BytesSkipped { get; private set; }

        /// <summary>Adds bytes from the board and returns the whole packets they complete.</summary>
        public IReadOnlyList<AccPacket> Feed(byte[] data) => Feed(data, 0, data.Length);

        /// <summary>Adds bytes from the board and returns the whole packets they complete.</summary>
        public IReadOnlyList<AccPacket> Feed(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            var packets = new List<AccPacket>();
            while (count > 0)
            {
                int room = _buffer.Length - _count;
                int take = Math.Min(room, count);
                Buffer.BlockCopy(data, offset, _buffer, _count, take);
                _count += take;
                offset += take;
                count -= take;
                Split(packets);
            }
            return packets;
        }

        /// <summary>Forgets any part packet, for when the port is reopened.</summary>
        public void Reset()
        {
            _count = 0;
            _lastSequence = -1;
        }

        private void Split(List<AccPacket> packets)
        {
            int pos = 0;
            while (true)
            {
                int start = FindHeader(pos);
                if (start < 0)
                {
                    // Keep a last 'Y': it may be the start of the next header.
                    int keep = _count > pos && _buffer[_count - 1] == (byte)'Y' ? 1 : 0;
                    BytesSkipped += _count - pos - keep;
                    pos = _count - keep;
                    break;
                }
                BytesSkipped += start - pos;
                pos = start;
                if (_count - pos < HeaderLength) break;

                byte type = _buffer[pos + 2];
                int length = _buffer[pos + 4] | (_buffer[pos + 5] << 8);
                if ((type != (byte)AccPacketType.Frame && type != (byte)AccPacketType.Status) || length > MaxPayload)
                {
                    pos++;
                    BytesSkipped++;
                    continue;
                }
                if (_count - pos < HeaderLength + length) break;

                var payload = new byte[length];
                Buffer.BlockCopy(_buffer, pos + HeaderLength, payload, 0, length);
                ushort sequence = (ushort)(_buffer[pos + 6] | (_buffer[pos + 7] << 8));
                CountGap(sequence);
                packets.Add(new AccPacket((AccPacketType)type, _buffer[pos + 3], sequence, payload));
                pos += HeaderLength + length;
            }

            if (pos > 0)
            {
                Buffer.BlockCopy(_buffer, pos, _buffer, 0, _count - pos);
                _count -= pos;
            }
        }

        private int FindHeader(int from)
        {
            for (int i = from; i < _count - 1; i++)
                if (_buffer[i] == (byte)'Y' && _buffer[i + 1] == (byte)'A') return i;
            return -1;
        }

        private void CountGap(ushort sequence)
        {
            if (_lastSequence >= 0)
            {
                int gap = (ushort)(sequence - _lastSequence - 1);
                if (gap < 0x8000) PacketsMissed += gap;
            }
            _lastSequence = sequence;
        }
    }
}
