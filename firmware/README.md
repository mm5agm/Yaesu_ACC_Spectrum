My own ESP32-S3 firmware: a dumb pipe (step 3 of
[docs/plan.md](../docs/plan.md)). Version 1 was flashed and run against the
FTdx101MP on 2026-11-11: 25 frames a second, every one 4096 bytes, none lost on
the USB. Version 2 adds the firmware version and edge counts to the status
text, for checking the wiring. Version 2.0.1 was flashed and run on 2026-11-11:
the frames were the same as version 1's (25 a second, 4096 bytes, data ending at
byte 3102 in all 301), and the edge counts were CS 25, CLOCK 819261 and DATA2
about 147000 a second, with DATA1 at 0.

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
  each one's count over the last second, as `edges/s CS 25 CLOCK 819261 ...`.
  The counters are 16 bits, so a timer reads them every 10 ms and adds up
  what each has gained. Version 2.0.0 counted the wraps with the counter's
  interrupt instead, which never fired, so CLOCK read short.
  With the radio streaming, CS should be 25 and CLOCK about 819200
  (25 x 4096 x 8). A line at 0 while the others count points at that wire. The
  PCNT driver turns each pin's pull-up on, and the firmware turns it off again,
  so the lines stay plain inputs as in version 1.
- GPIO14 (DATA1) is a plain input. GPIO38 and GPIO39 are never touched, so the
  heartbeat stays on the link between them.
- LED: blue at start, green pulsing once a second while frames arrive, amber
  when none have come for a second.
- Status text every 5 seconds, on the USB stream and on the COM socket at
  115200. It always starts `ACC pipe: version 2.0.1,` so a program can tell
  it's talking to this firmware, and which version.

## Wi-Fi (version 3)

Version 3 keeps the USB pipe exactly as it was, so ACC Spectrum Viewer works as
before, and adds the scope on a web page over Wi-Fi. Any browser on the home
network can open it: a phone, a tablet or another PC.

- **First start:** the board makes its own network, `ACC-Scope-xxxx`, with no
  password. Join it from a phone and the setup page opens (or go to
  http://192.168.4.1/wifi). Pick the home network, type its password and save;
  the board restarts and joins it.
- **After that:** open http://acc-scope.local, or the address the status text
  gives, such as `wifi 192.168.1.20`. The page shows the scope and waterfall
  with the frequency scale, and the audio scope with its attenuators and
  timebase, for Main and, on an FTdx101, Sub. It tells the FTDX10 from the FTdx101 the same way the library does.
  **Pause** (Space) freezes it, **Audio** (A) shows or hides the audio scopes,
  and **Peak** (P) keeps each point's peak as a fainter blue trace that fades
  slowly, so a signal that came and went can still be seen. The browser
  remembers whether Peak was on.
- **To change the network,** hold BOOT for 3 seconds while the board is
  running. The setup network comes up for 10 minutes, and the board stays on
  the saved network meanwhile. (Not at power-on: BOOT is a strapping pin, so
  held then it puts the ESP32-S3 in download mode and this firmware never runs.)
- **If the saved network can't be joined for 30 seconds,** after a router
  restart say, the setup network comes up as well, so the network can be
  changed, and the board keeps trying. It closes again once the board has
  rejoined and nobody is on it.
- The setup page answers only on the setup network, never on the home network.
- **To forget the network,** send `w` on the USB. The board restarts with no
  network saved, so the setup network comes up.
- The status text gives the Wi-Fi state before `edges/s`: `wifi 192.168.1.20
  rssi -67` (the signal strength in dBm, so stalls on the page can be put down
  to the signal or not), with ` + setup ACC-Scope-xxxx` after it while the
  setup network is up too, or `wifi connecting`, or `wifi setup ACC-Scope-xxxx`.

How it works: `web.cpp` runs on its own task on core 0, so the SPI and USB work
on core 1 never waits for it. Port 80 serves the page (`web/index.html`, built
in by `embed_web.py`), `/status` and `/wifi`. Its small HTTP server holds up to
eight connections at once and answers only those that have sent a whole
request, so the spare connections phone browsers open in advance don't hold the
others up, as they did with Arduino's `WebServer`. Port 81 is a WebSocket that sends
each frame's first 3102 bytes as one binary message, to up to three browsers. A
browser that can't keep up is dropped and reconnects.

Checked on an FTDX10 on 2026-11-21 by [Eugene YO9VA](https://github.com/Eugen68),
with the page open on a phone over Wi-Fi: 25 frames a second on the page, and
`tools/check_frames.py --seconds 20` on the USB at the same time gave 500
frames of 4096 bytes, 25 a second, every one ending at byte 3050, none cut
short. Checked on my FTdx101MP on 2026-11-22 over Wi-Fi: 25 frames a second,
none cut short, with Main and Sub on the page. Not yet checked while transmitting.

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
python -m esptool --chip esp32s3 --port COM5 write-flash 0x0 yaesu-acc-firmware-2.0.1-merged.bin
```

with your board's COM port in place of COM5. Older versions of esptool, before
5.0, spell it `write_flash`.

Writing the whole merged file at 0 also clears the board's settings, its Wi-Fi
network and SPI mode, because the file covers the NVS partition where they're
kept (0x9000 to 0xDFFF). That's right for a new board. For an update, use ACC
Firmware Flasher, which leaves that part out, or write the file in two pieces
that skip it:

```
python -c "d=open('yaesu-acc-firmware-3.0.10-merged.bin','rb').read(); open('p0.bin','wb').write(d[:0x9000]); open('p1.bin','wb').write(d[0xE000:])"
python -m esptool --chip esp32s3 --port COM5 write-flash 0x0 p0.bin 0xe000 p1.bin
```

Flash it, with the radio's DC lead out and the board's lead in the socket marked
COM (COM5 on my PC; `platformio.ini` pins the port so PlatformIO never tries
COM3 or COM4):

```
pio run -t upload
```

With a second lead in the socket marked USB, power up in the usual order (ESP32
first, then the radio) and run `python tools/check_frames.py` from the top folder.
