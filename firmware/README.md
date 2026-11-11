My own ESP32-S3 firmware: a dumb pipe (step 3 of
[docs/plan.md](../docs/plan.md)). Version 1 was flashed and run against the
FTdx101MP on 2026-11-11: 25 frames a second, every one 4096 bytes, none lost on
the USB. Version 2 adds the firmware version and edge counts to the status
text, for checking the wiring. It builds, but hasn't been flashed yet.

What it does:

- SPI slave on GPIO10 (CLOCK), GPIO11 (DATA2) and GPIO13 (CS), with DMA. Each
  CS-low period is one transfer, so the frames come out whole whatever their
  length (up to 8192 bytes).
- Sends each frame over the native USB (the socket marked USB) behind an 8-byte
  header: `Y` `A`, a type (`F` frame, `T` status text), flags, the length and a
  sequence number. The radio's bytes are unchanged.
- A packet goes out whole or not at all, through a 32 KB USB send buffer. My
  first build wrote whatever fitted in the default buffer, so the PC got about
  one packet in nine, with pieces of different frames joined together. When
  the PC isn't reading, packets are dropped and counted ("not sent" in the
  status); they still take a sequence number, so the PC sees the gap.
- SPI mode 2. The clock idles high, so it's mode 2 or 3. In mode 3 the data
  ended at byte 3101 in some frames and 3102 in others, and single bits
  flipped; in mode 2 it ends at 3102 in every frame. The PC can switch it by
  sending `2` or `3`; it's saved in flash and the board restarts.
  `tools/check_frames.py --mode 3` does that.
- Edge counts. The ESP32's pulse counter (PCNT) counts rising edges on CS,
  CLOCK, DATA2 and DATA1, alongside the SPI slave, and the status text gives
  each one's count over the last second, as `edges/s CS 25 CLOCK 819200 ...`.
  With the radio streaming, CS should be 25 and CLOCK about 819200
  (25 x 4096 x 8). A line at 0 while the others count points at that wire. The
  PCNT driver turns each pin's pull-up on, and the firmware turns it off again,
  so the lines stay plain inputs as in version 1.
- GPIO14 (DATA1) is a plain input. GPIO38 and GPIO39 are never touched, so the
  heartbeat stays on the link between them.
- LED: blue at start, green pulsing once a second while frames arrive, amber
  when none have come for a second.
- Status text every 5 seconds, on the USB stream and on the COM socket at
  115200. It always starts `ACC pipe: version 2.0.0,` so a program can tell
  it's talking to this firmware, and which version.

Both sockets can be plugged into the PC at once: COM for flashing and status
text, USB for the frames. That's how I run it, so nothing has to be moved
between flashing and capturing.

Build it (from this folder):

```
pio run
```

Each build also writes `release/yaesu-acc-firmware-<version>-merged.bin`: the
bootloader, partition table and app in one file that flashes at address 0. That
file goes on the GitHub release, so you can flash it without building anything:
on Espressif's [ESP Tool](https://espressif.github.io/esptool-js/) page, or with

```
python -m esptool --chip esp32s3 --port COM5 write-flash 0x0 yaesu-acc-firmware-2.0.0-merged.bin
```

with your board's COM port in place of COM5. Older versions of esptool, before
5.0, spell it `write_flash`.

Flash it, with the radio's DC lead out and the board's lead in the socket marked
COM (COM5 on my PC; `platformio.ini` pins the port so PlatformIO never tries
COM3 or COM4):

```
pio run -t upload
```

With a second lead in the socket marked USB, power up in the usual order (ESP32
first, then the radio) and run `python tools/check_frames.py` from the top folder.
