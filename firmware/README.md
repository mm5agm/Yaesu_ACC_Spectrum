Our own ESP32-S3 firmware, first version: a dumb pipe (step 3 of
[docs/plan.md](../docs/plan.md)). Written and compiled on 2026-11-11, **not yet
flashed or run against the radio.**

What it does:

- SPI slave on GPIO10 (CLOCK), GPIO11 (DATA2) and GPIO13 (CS), with DMA. Each
  CS-low period is one transfer, so the frames come out whole whatever their
  length (up to 8192 bytes).
- Sends each frame over the native USB (the socket marked USB) behind an 8-byte
  header: `Y` `A`, a type (`F` frame, `T` status text), flags, the length and a
  sequence number. The radio's bytes are unchanged.
- SPI mode 3 to start with. The clock idles high, so it's mode 2 or 3. The PC
  can switch it by sending `2` or `3`; it's saved in flash and the board
  restarts. `tools/check_frames.py --mode 2` does that.
- GPIO14 (DATA1) is a plain input. GPIO38 and GPIO39 are never touched, so the
  heartbeat stays on the link between them.
- LED: blue at start, green pulsing once a second while frames arrive, amber
  when none have come for a second.
- Status text every 5 seconds, on the USB stream and on the COM socket at
  115200.

Build it (from this folder):

```
pio run
```

Flash it, with the radio's DC lead out and the board's lead in the socket marked
COM (COM5 on my PC; `platformio.ini` pins the port so PlatformIO never tries
COM3 or COM4):

```
pio run -t upload
```

Then move the lead to the socket marked USB, power up in the usual order (ESP32
first, then the radio) and run `python tools/check_frames.py` from the top folder.
