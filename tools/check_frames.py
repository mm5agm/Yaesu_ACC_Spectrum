"""Check the frames the ESP32 firmware sends from the ACC socket, and save them.

The board must be on its native USB socket (marked USB), running the firmware in
firmware/. That socket shows up as an Espressif port, 303A:1001. This script only
opens a port with that USB ID, and never COM3 or COM4, the radio's ports on my PC.

    python tools/check_frames.py               # read 5 s, report, save to captures/
    python tools/check_frames.py --seconds 20
    python tools/check_frames.py --mode 2      # switch the firmware to SPI mode 2 first
    python tools/check_frames.py --port COM6   # name the port yourself

It reports frames a second (25 expected), the frame lengths (4096 expected), and
where the data ends in each frame, and saves the raw stream as
captures/acc-<date>-<time>-mode<N>.bin for decoding.

The FTdx101MP's frames don't end with the FT-710's trailer FF 01 EE 01. Each one
has 3102 bytes of data and then zeros. With the right SPI mode (2) the data ends
at the same byte in every frame; with the wrong one (3) it wanders by a byte and
single bits flip, so a steady end is the check that the mode is right.

The firmware's packet format is in firmware/src/main.cpp: 'Y' 'A' type flags
length(uint16 LE) sequence(uint16 LE), then the payload. Type 'F' is a frame,
type 'T' is status text.
"""

import argparse
import collections
import datetime
import pathlib
import re
import struct
import sys
import time

try:
    import serial
    from serial.tools import list_ports
except ImportError:
    sys.exit("pyserial isn't installed. Run: python -m pip install -r tools/requirements.txt")

ESPRESSIF_USB = (0x303A, 0x1001)   # the ESP32-S3's own USB serial port
RADIO_PORTS = {"COM3", "COM4"}
TRAILER = bytes.fromhex("FF01EE01")
HEADER = struct.Struct("<2sBBHH")
CAPTURES = pathlib.Path(__file__).resolve().parent.parent / "captures"


def find_port(name=None):
    ports = {p.device.upper(): p for p in list_ports.comports()}
    if name:
        port = ports.get(name.upper())
        if not port:
            sys.exit(f"There's no {name}.")
        candidates = [port]
    else:
        candidates = [p for p in ports.values() if (p.vid, p.pid) == ESPRESSIF_USB]
        if not candidates:
            sys.exit("No ESP32-S3 USB port (303A:1001). Is the lead in the socket marked USB, "
                     "and is the firmware flashed?")
        if len(candidates) > 1:
            sys.exit("More than one ESP32-S3 USB port: "
                     + ", ".join(p.device for p in candidates) + ". Name one with --port.")
    port = candidates[0]
    if port.device.upper() in RADIO_PORTS:
        sys.exit(f"{port.device} is one of the radio's ports on this PC. Stopping.")
    if (port.vid, port.pid) != ESPRESSIF_USB:
        sys.exit(f"{port.device} isn't the ESP32-S3's own USB port (303A:1001). Stopping.")
    return port.device


def open_port(device):
    s = serial.Serial()
    s.port = device
    s.baudrate = 115200       # ignored by USB serial, but pyserial wants one
    s.timeout = 0.1
    # Both low. On the ESP32-S3's own USB port, a DTR/RTS sequence resets the chip.
    s.dtr = False
    s.rts = False
    s.open()
    return s


def set_mode(device, mode):
    with open_port(device) as s:
        s.write(str(mode).encode())
        s.flush()
    print(f"Asked for SPI mode {mode}; waiting for the board to restart.")
    time.sleep(1.5)
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if any(p.device.upper() == device.upper() for p in list_ports.comports()):
            time.sleep(0.5)
            return
        time.sleep(0.3)
    sys.exit(f"{device} didn't come back after the restart.")


def packets(stream):
    """Split the raw stream into (type, flags, seq, payload). Resyncs on 'YA'."""
    i, skipped = 0, 0
    while True:
        j = stream.find(b"YA", i)
        if j < 0 or j + HEADER.size > len(stream):
            break
        magic, kind, flags, length, seq = HEADER.unpack_from(stream, j)
        if chr(kind) not in "FT" or j + HEADER.size + length > len(stream):
            i = j + 1
            continue
        skipped += j - i
        start = j + HEADER.size
        yield chr(kind), flags, seq, stream[start:start + length]
        i = start + length
    if skipped:
        print(f"Skipped {skipped} bytes between packets (lost or garbled).")


def report(stream, seconds, mode_text):
    frames, texts, lengths, cut = [], [], collections.Counter(), 0
    seqs = []
    for kind, flags, seq, payload in packets(stream):
        seqs.append(seq)
        if kind == "T":
            texts.append(payload.decode("ascii", "replace"))
        else:
            frames.append(payload)
            lengths[len(payload)] += 1
            cut += flags & 1
    gaps = sum(1 for a, b in zip(seqs, seqs[1:]) if (a + 1) & 0xFFFF != b)

    print(f"\n{len(stream)} bytes in {seconds:.1f} s ({len(stream) / seconds / 1000:.1f} kB/s)")
    for t in texts[-2:]:
        print("Board says: " + t)
    if mode_text:
        print(mode_text)
    print(f"Frames: {len(frames)}, {len(frames) / seconds:.1f} a second (25 expected)")
    if not frames:
        print("No frames. Is the radio on, and are CLOCK, DATA2 and CS on GPIO10, 11 and 13?")
        return
    print("Lengths: " + ", ".join(f"{n} bytes x{c}" for n, c in lengths.most_common(6))
          + "  (4096 expected)")
    if cut:
        print(f"{cut} frames filled the firmware's 8192-byte buffer, so they may be cut short.")
    if gaps:
        print(f"{gaps} gaps in the packet sequence: the PC or the USB dropped packets.")
    data_ends = collections.Counter(len(f.rstrip(b"\0")) for f in frames)
    print("Data ends at byte: " + ", ".join(f"{o} x{c}" for o, c in data_ends.most_common(4))
          + "  (3102 in every frame on the FTdx101MP)")
    if len(data_ends) > 1:
        print("The end wanders, so bits are being sampled on the wrong edge. "
              "Try the other SPI mode (--mode 2 or --mode 3).")
    ends = sum(1 for f in frames if f.endswith(TRAILER))
    if ends:
        print(f"Ending with the FT-710's FF 01 EE 01: {ends} of {len(frames)}")
    f = frames[len(frames) // 2]
    print("A frame's first 32 bytes: " + f[:32].hex(" ").upper())
    print("and last 32:              " + f[-32:].hex(" ").upper())


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", help="the board's USB port, e.g. COM6")
    parser.add_argument("--seconds", type=float, default=5.0)
    parser.add_argument("--mode", type=int, choices=(2, 3), help="set the SPI mode first")
    parser.add_argument("--no-save", action="store_true", help="don't save the capture")
    args = parser.parse_args()

    device = find_port(args.port)
    if args.mode:
        set_mode(device, args.mode)
        device = find_port(args.port)

    stream = bytearray()
    with open_port(device) as s:
        # The first half second holds what the board buffered while nothing was
        # reading, with gaps in it, before the live frames. Throw it away, so the
        # gap count is about the live stream.
        settle = time.monotonic()
        while time.monotonic() - settle < 0.5:
            s.read(65536)
        s.write(b"i")                 # a status packet, so the report shows the mode
        start = time.monotonic()
        while time.monotonic() - start < args.seconds:
            stream += s.read(65536)
        seconds = time.monotonic() - start
    stream = bytes(stream)

    mode = re.search(rb"SPI mode (\d)", stream)
    mode_n = mode.group(1).decode() if mode else "unknown"
    report(stream, seconds, None)

    if not args.no_save and stream:
        CAPTURES.mkdir(exist_ok=True)
        name = datetime.datetime.now().strftime(f"acc-%Y%m%d-%H%M%S-mode{mode_n}.bin")
        (CAPTURES / name).write_bytes(stream)
        print(f"\nSaved {len(stream)} bytes to captures/{name}")


if __name__ == "__main__":
    main()
