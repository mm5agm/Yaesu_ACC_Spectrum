# FTdx101MP: spectrum data from the ACC socket via an ESP32-S3

Status: **draft plan, nothing built.** The cable has arrived and the board is
chosen. See "The cable and the board".

**Use at your own risk.** If you damage your radio, that's your responsibility. See
the [README](../README.md#use-at-your-own-risk).

## What it is

The ACC socket on the back of the FTdx101 is the port the SCU-LAN10 plugs
into. While a listener answers its heartbeat, the radio streams the data for
its own scope over SPI: 4096-byte frames, 25 a second, with the waterfall
for both receivers, the audio FFT and waveform, and a block of settings.

It's the same frame format YWC already decodes for the FT-710
([ft710-native-scope.md](https://github.com/mm5agm/Yaesu_Web_Control/blob/develop/docs/design/ft710-native-scope.md) in YWC), where the radio has the
SPI bridge built in. The FTdx101 has no bridge, so an ESP32-S3 does the job:
it reads SPI from the ACC socket, answers the heartbeat, and sends the
frames to the PC over USB or Wi-Fi.

Receive-only. Nothing in this plan keys the radio.

**Read [fuse-risk.md](fuse-risk.md) before connecting anything.** ACC pin 8 is
13.8 V behind a 3.15 A fuse, and pins 9 and 10 are the PTT and SEND lines.

## ACC socket pinout

From page 17 of the FTdx101 operating manual
(`docs/manuals/FTDX101MP_D_OM_ENG_EH068H213_2102P-JS-3.pdf` in YWC, PDF page 19).

| Pin | Manual label | Function | Direction | This project |
|---|---|---|---|---|
| 1 | Power ON | Remote power-on line (believed to be how the SCU-LAN10 switches the radio on) | into radio | **Not connected** |
| 2 | GND | Signal ground | — | ESP32 GND |
| 3 | DATA1 | SPI data line 1, `ACC_MISO` on the schematic. Function not yet confirmed; the name suggests data into the radio | to be confirmed | Scope first, then ESP32 GPIO14 if useful |
| 4 | DATA2 | SPI data line 2: the scope frames (4096 bytes each) | out of radio | ESP32 GPIO11 |
| 5 | CLOCK | SPI clock, 2 MHz, SPI mode 1. The radio is the SPI master | out of radio | ESP32 GPIO10 |
| 6 | CS | SPI chip select, active low: low for each frame, 25 times a second | out of radio | ESP32 GPIO13 |
| 7 | 3.3V | 3.3 V through a 1k resistor (R1230), about 3 mA at most | out of radio | **Not connected.** It can't power anything |
| 8 | 13.8V | 13.8 V through a 0 ohm link and F1003 (3.15 A) | out of radio | **Not connected.** A short blows the fuse, and a touch on a data pin could damage the radio's logic |
| 9 | Reserve | `ACC_PTT` on the schematic | not known | **Not connected, not probed** |
| 10 | Reserve | `ACC_SEND` on the schematic | not known | **Not connected, not probed** |
| 11 | CNT RX | Control serial into the radio (UART, 115200). Carries the reply to the heartbeat, within 10 ms | into radio | ESP32 GPIO39 (UART TX) |
| 12 | CNT TX | Control serial out of the radio (UART, 115200). Sends the heartbeat `alive\0` once a second | out of radio | ESP32 GPIO38 (UART RX) |
| 13 | Reserve | Ground, through a 0 ohm link (R1106) | — | **Not connected** |

"Direction" is as seen from the radio. The labels in the "Manual label"
column are exactly as printed in the manual; the functions come from the
manual where it says, and otherwise from the Technical Supplement schematic
and the SCU-LAN10 protocol work listed under "Where the code is". The
direction and behaviour of pins 9 and 10 are still unknown.

The socket is a **13-pin mini-DIN**: three rows of four pins plus one at the
bottom by the key. The manual's drawing shows the socket on the radio. That
is the same view as the female end of the extension cable; looking into the
male plug, the layout is mirrored left to right.

![Cable male plug seen looking at the pins, key at the top. Top row left to right: pins 1, 2, 3, 4. Second row: 5, 6, 7, 8. Third row: 9, 10, 11, 12. Pin 13 is alone at the bottom.](images/acc-male-plug.svg)

This is the cable's male plug, looking at its pins. The IC-7300MK2 plug looks the same,
so the drawing works for both.

## The cable and the board

**The cable isn't cut.** The first idea was to cut the extension cable (13-pin
mini-DIN male to female, Amazon UK B0BRQDDTTZ, £18.99) and use its wires. A
ready-made 13-pin plug with wires attached was available instead, from the
IC-7300MK2, so that plug goes into the cable's female end and its wires are the
ones used. This makes it easier to plug the board into the circuit and unplug it.

```
Radio ACC socket <- cable male | cable female <- IC-7300MK2 plug -> wires -> breakout
```

All 13 pins of the extension cable are confirmed wired.

**The wire colours in this project are those of the ready-made lead, which came
from Icom with the plug already soldered on. They aren't the colours you get by
cutting the Amazon extension cable in two. If you cut that cable, meter every wire
to find its pin, and don't go by the colours here.**

Icom and Yaesu number the socket the same way. Icom's drawing (IC-7300MK2 Basic
Manual, page 74) is just upside down, with the key at the bottom. The colours below
are from that page and refer to Icom's supplied cable, so they're to be metered
before use and recorded in [bench-notes.md](bench-notes.md).

| Pin | Icom wire colour | FTdx101 label | This project |
|---|---|---|---|
| 1 | Brown | Power ON | **Insulate** |
| 2 | **Red** | GND | ESP32 GND, scope ground |
| 3 | Orange | DATA1 | Scope only at first |
| 4 | Yellow | DATA2 | Scope frames |
| 5 | Green | CLOCK | SPI clock |
| 6 | Blue | CS | SPI chip select |
| 7 | Purple | 3.3V | **Insulate** |
| 8 | **Gray** | 13.8V | **Insulate. The dangerous wire** |
| 9 | White | Reserve (`ACC_PTT`) | **Insulate, not probed** |
| 10 | **Black** | Reserve (`ACC_SEND`) | **Insulate, not probed** |
| 11 | Pink | CNT RX | Heartbeat reply |
| 12 | Light blue | CNT TX | Heartbeat |
| 13 | Light green | Reserve (ground) | **Insulate** |

**Why ground is red.** The colours appear to follow the resistor colour code by pin
number, not by what the wire does: 1 brown, 2 red, 3 orange, 4 yellow, 5 green, 6 blue,
7 purple, 8 gray, 9 white and 10 black (0 in the code). Pink, light blue and light green
then cover pins 11 to 13. Icom's manual doesn't give this reason, but the match is
exact. It means anyone who knows the code can read a wire's pin number from its colour.

So the usual meanings don't apply: red is ground, gray is 13.8 V and black is SEND, not
ground. ESP32 GND and the scope ground go only to the red wire, which is worth marking
as GND with a label or a band of tape. Blue and light blue are different signals, and so
are green and light green.

**The board** is an ESP32-S3 DevKitC-1 N16R8 (WROOM-1-N16R8, marked HW678, Amazon UK
B0F8N8LZ4G). Anything still wired to it from earlier work comes off first.

- It normally has two USB sockets. Use the one marked USB, the S3's native USB, not
  the one marked UART, which is marked COM on mine.
- Its RGB LED is on GPIO48, found by scanning the pins one at a time. So GPIO38 is
  free for the heartbeat. Version
  1.1 of the DevKitC-1 has the LED on GPIO38, so another board may differ. It's an
  addressable RGB LED in GRB order, so it takes a data stream, not a plain high or low.
  Drive it with the Adafruit NeoPixel library, kept dim. The core's `neopixelWrite()`
  only ever drives the first pin it was called with, which is no good for a scan.
- The UART (COM) socket is the board's CH343 port, COM5 on this PC. Flashing tools are pinned
  to the board's port by name, because COM3 and COM4 are the radio's.

## Pins I need

Seven of the 13: pins 2, 4, 5, 6, 11 and 12, plus pin 3 for probing.

| ACC pin | Manual label | Icom wire | Function | Direction | Why |
|---|---|---|---|---|---|
| 2 | GND | **Red** | Signal ground | — | Common ground with the ESP32 |
| 3 | DATA1 | Orange | SPI data line 1 | to be confirmed | **Scope only at first.** Nothing drives it |
| 4 | DATA2 | Yellow | SPI data line 2 | radio → ESP32 | The scope frames |
| 5 | CLOCK | Green | SPI clock | radio → ESP32 | 2 MHz clock for the data |
| 6 | CS | Blue | SPI chip select | radio → ESP32 | Low for each 4096-byte frame |
| 11 | CNT RX | Pink | Control serial into the radio | ESP32 → radio | Heartbeat reply |
| 12 | CNT TX | Light blue | Control serial out of the radio | radio → ESP32 | Heartbeat, `alive\0` once a second |

## Pins to leave unconnected

The other six.

| ACC pin | Manual label | Icom wire | Function | Why not |
|---|---|---|---|---|
| 1 | Power ON | Brown | Remote power-on line | Switches the radio on; not needed |
| 7 | 3.3V | Purple | 3.3 V through 1k | It can't power anything |
| 8 | 13.8V | **Gray** | 13.8 V through F1003 | A short blows the fuse, and a touch on a data pin could damage the radio's logic |
| 9 | Reserve | White | `ACC_PTT` | The radio's PTT line |
| 10 | Reserve | **Black** | `ACC_SEND` | The radio's SEND line |
| 13 | Reserve | Light green | Ground | Pin 2 is the ground I use |

Insulate each of these wires on its own (heat-shrink, not just tape round the
bundle), with the ends staggered, so none can touch another wire or the ESP32.
None of them reaches the breakout, and they aren't probed.

## Wiring to the ESP32-S3

This is the pin mapping SLTPLAN's firmware uses, so their code runs
unchanged. On a board with its LED on GPIO38, my own firmware can move that signal
to another pin.

| ACC pin | Manual label | Function | ESP32-S3 | Role on the ESP32 |
|---|---|---|---|---|
| 1 | Power ON | Remote power-on line | — | Not connected |
| 2 | GND | Signal ground | GND | Ground |
| 3 | DATA1 | SPI data line 1 | GPIO14 (suggested) | Spare input, only once the scope shows what's on it |
| 4 | DATA2 | SPI data line 2 | GPIO11 | SPI slave data in |
| 5 | CLOCK | SPI clock | GPIO10 | SPI slave clock in |
| 6 | CS | SPI chip select | GPIO13 | SPI slave chip select in |
| 7 | 3.3V | 3.3 V through 1k | — | Not connected |
| 8 | 13.8V | 13.8 V through F1003 | — | Not connected |
| 9 | Reserve | `ACC_PTT` | — | Not connected |
| 10 | Reserve | `ACC_SEND` | — | Not connected |
| 11 | CNT RX | Control serial into the radio | GPIO39 | UART TX (heartbeat reply out) |
| 12 | CNT TX | Control serial out of the radio | GPIO38 | UART RX (heartbeat in) |
| 13 | Reserve | Ground | — | Not connected |

On the screw-terminal expansion board, the wires go here. The terminal marked "3"
is GPIO3, not ACC pin 3. In this photo the bottom row's numbers read the right way up
and the top row's are upside down. The unmarked photo is
[expansion-board.jpg](images/expansion-board.jpg).

![The ESP32-S3 on its screw-terminal board, with the ACC wires marked. Bottom row, the 3V3 to GND row: ACC pin 5 CLOCK (green wire) to GPIO10, pin 4 DATA2 (yellow) to GPIO11, pin 6 CS (blue) to GPIO13, pin 3 DATA1 (orange) to GPIO14, each through 470 ohms, and pin 2 GND (red) to the GND terminal at the end, no resistor. Top row: pin 11 CNT RX (pink) to GPIO39 and pin 12 CNT TX (light blue) to GPIO38, each through 470 ohms.](images/acc-wiring.jpg)

Notes:

- Check that GPIO10, 11, 13, 14, 38 and 39 are broken out on the board.
  The N16R8's octal PSRAM takes GPIO35–37, but the pins above don't use those.
  This board's RGB LED is on GPIO48. No ACC signal uses that pin, and my firmware
  will use it for status lights.
- Put a 470 Ω resistor in series with every signal line (pins 3, 4, 5, 6,
  11 and 12), but not ground. On the lines from the radio it limits the
  current if the levels turn out higher than expected or a wire goes to the
  wrong pin, and does no harm at 2 MHz. On pin 11, the one line the ESP32
  drives, it limits the current into the radio's input if the ESP32 is
  powered while the radio is off, or if the wire lands on the wrong pin.
  115200 baud passes through it untouched. Mount the resistors at the ESP32
  end. Why 470 Ω and not another value is in "Resistor value and the 2 MHz
  clock" in the [README](../README.md).
- Keep the wires from the cable to the ESP32 short; it's a 2 MHz clock.
- Power the ESP32 from its own USB lead, never from the ACC socket.

## Before connecting the ESP32: check the levels

The step-by-step procedure, with the scope settings and the pass and fail levels,
is in "Safe first connection" in the [README](../README.md#safe-first-connection).
In short: measure every line with the radio on and nothing else connected, and
connect the ESP32 only if every line is 3.3 V or less.

## Where the code is

| Source | Licence | What it gives me |
|---|---|---|
| [SLTPLAN/FTDX10_WF_HUB/ESP32](https://github.com/SLTPLAN/FTDX10_WF_HUB/tree/main/ESP32) | **None** (asked in [issue #1](https://github.com/SLTPLAN/FTDX10_WF_HUB/issues/1)) | Complete ESP32-S3 firmware `DX10_WF.ino` plus `remote.py` |
| [SLTPLAN/FTDX10_WF](https://github.com/SLTPLAN/FTDX10_WF) | None | The PC side of the same project |
| [ratmandu/YaesuWFTesting](https://github.com/ratmandu/YaesuWFTesting) | MIT | First published frame layout and test tools |
| [CarrierWaveApp/sculan10-protocol](https://github.com/CarrierWaveApp/sculan10-protocol) | MIT | Written spec of the SCU-LAN10 protocol |
| [sanjint.com/yaesu-ftdx10-scope](https://sanjint.com/yaesu-ftdx10-scope) | — | Write-up of the ACC signals on the FTdx10 |
| YWC: `Ft710ScopeFrame` and the FT-710 scope path | Mine | The frame decoder and spectrum display I already have |

What SLTPLAN's firmware does:

- Arduino settings: board "ESP32S3 Dev Module", USB Mode "USB-OTG",
  USB CDC On Boot "Disabled".
- Reads each frame as an SPI slave (mode 1, 2 MHz, 4096 bytes while CS is
  low) and answers `alive\0` within 10 ms.
- Sends each frame wrapped as `66 CC FF`, a 2-byte big-endian length, a
  1-byte XOR checksum, then the payload.
- Outputs on USB CDC, on UDP port 51235 (all 25 frames a second, after the
  client sends `HELLO`) and on TCP port 51234.

## Most of it already exists in YWC

The FT-710 sends the same frames over the same kind of SPI link. The
difference is only where the SPI-to-USB bridge sits:

```
FT-710:    radio SPI ──> FT4222H (inside the radio) ──USB──> YWC
FTdx101:   ACC SPI   ──> ESP32-S3 (outside)         ──USB──> YWC
```

Everything after the bridge is already written and working for the FT-710:
`Ft710ScopeFrame` (finding whole frames in the byte stream, decoding them),
CAT placement by `ScopeCatGate`, and the spectrum display. The ESP32 only
has to stand in for the FT4222H.

The FT4222H is a fixed-function chip with no firmware. The ESP32 needs a
little, but only enough to be a dumb pipe:

- **SPI slave:** read the bytes while CS is low (ESP-IDF's `spi_slave`
  driver with DMA does this).
- **USB:** write the bytes out unchanged over USB CDC. In the end I put a
  small header in front of each frame (see step 3), because the FTdx101's
  frames have no trailer to find them by.
- **Heartbeat:** the FT-710 answers its own heartbeat inside the radio; on
  the FTdx101 something outside has to. Either the ESP32 echoes `alive\0`
  back, or pins 11 and 12 are simply linked by a wire and the ESP32 needn't
  touch them at all. The reply has to arrive within 10 ms. The FTDX10
  projects report that SPI starts after two or three answered heartbeats and
  stops two or three seconds after the replies stop.

That's a few dozen lines, so I don't need SLTPLAN's firmware or its
licence.

**I go straight to my own firmware, without flashing SLTPLAN's first.** Decided
on 2026-11-10, after both stages of the level checks passed:

- The hardware is already proven. The scope showed CS, CLOCK and DATA2 working
  at the ESP32's own terminals, so a known-good firmware would add nothing.
- SLTPLAN's firmware answers the heartbeat itself. With the 11 to 12 link on,
  it and the radio would both drive pin 11, so the link would have to come off
  just to test someone else's code.
- It keeps this repository clear of their unlicensed code, even as a test.

If my firmware gets no frames, theirs is still there to help find out why.

What's left to check:

- ~~Whether FTdx101 frames end with the same `FF 01 EE 01` trailer as the
  FT-710's.~~ They don't (2026-11-11). The firmware takes the frame edges
  from CS and sends each frame behind its own header, so the PC never has to
  search for them.
- The extra content on the FTdx101: the Sub receiver's waterfall at bytes
  850–1699, and whatever differs in the settings block.
- On the YWC side, the source is a USB serial port instead of the FT4222H.
  That's a new input class in `Yaesu_Sdr_Worker` beside `Ft4222Bridge`, not
  a new decoder.

Rules for my own firmware:

- Write it from scratch. Use the facts the other projects found (pins,
  levels, timings, frame layout, the heartbeat) and my own scope captures,
  but no one else's code, not even the MIT projects'. Facts about the
  protocol aren't anyone's property; their code is.
- Credit SLTPLAN for showing that an ESP32-S3 can do it.

## Steps

1. **Cable.** Don't cut it. Check the male plug fits the ACC socket with the
   supply off. Plug the IC-7300MK2 plug into the female end, and meter each
   wire back to the matching pin on the male plug. That confirms Icom's colours
   and the cable's pin-for-pin wiring together. Then insulate pins 1, 7, 8, 9,
   10 and 13, and meter pin 8 open to everything before the plug goes near the
   radio.
2. **Levels.** Stage 1 of "Safe first connection" in the README, nothing
   connected to the ESP32. Then stage 2, with the erased ESP32 connected. **Both
   passed on 2026-11-10;** see docs/bench-notes.md.
3. **My own firmware, first version.** The dumb pipe described above, in
   `firmware/`, built with PlatformIO (the toolchain YWC_IWC_PADDLE uses).
   **Flashed and working on 2026-11-11:**
   - SPI slave on GPIO10 (clock), GPIO11 (DATA2) and GPIO13 (CS), with DMA.
     GPIO14 (DATA1) is left as a plain input; it carries nothing so far.
   - Raw bytes out over the native USB (the socket marked USB), unchanged.
     The stream is 102.4 kB/s, more than a USB-serial link at 921600 baud
     can carry.
   - The heartbeat is left to the link on terminals 38 and 39. GPIO38 and
     GPIO39 stay inputs, so nothing else drives the line.
   - **SPI mode 2.** The clock idles high, so it's mode 2 or 3. Mode 3 samples
     on the edge where the radio changes DATA2, so bits flip. Mode 2 gives the
     same end of data and the same fixed bytes in every frame. It's still a
     setting, saved in flash.
   - Whole packets only, through a 32 KB USB send buffer. The first build lost
     about eight packets in nine and joined pieces of frames together.
   - Flash it through the COM socket with the radio's DC lead out, then power
     up in the usual order: ESP32 first, then the radio. A second lead stays
     in the USB socket, so nothing moves between flashing and capturing.
4. **Check the frames.** `tools/check_frames.py` reads the native USB port
   (found by its USB ID, 303A:1001, never COM3 or COM4) and reports frames a
   second, frame length and where the data ends. **Done on 2026-11-11**, in
   SPI mode 2:
   - 25.0 frames a second, matching the 25.00 Hz the scope measured on CS,
     with no packets lost. Every frame 4096 bytes. (The first runs showed 26
     a second: the frames the board had buffered before the port opened came
     in as a burst. `check_frames.py` now throws away the first half second.)
   - No `FF 01 EE 01` trailer anywhere in the frame.
   - Bytes 0-1699 change from frame to frame and run smoothly, about 0x98 to
     0xE9: the two waterfalls step 5 expects, 850 bytes each.
   - Bytes 1700-1701 were `01 43` in every frame of that capture. Later
     captures showed byte 1701 changing from frame to frame, so only byte 1700
     is fixed; 1701 belongs with the block after it.
   - Bytes 1701-2899 change from frame to frame. Not decoded yet.
   - Bytes 2900-3101 are the same in every frame: probably the settings block.
   - Bytes 3102-4095 are zeros, which matches the scope's 8.7 ms of low DATA2
     at the end of each frame.

   The captures are in `captures/`: `acc-20261111-113822-mode2.bin` (good) and
   `acc-20261111-113757-mode3.bin` (the wrong mode, kept for comparison).

   **First spectrum on 2026-11-11.** `tools/live_view.py` draws bytes 0-849 and
   850-1699, inverted, as a trace and a waterfall. They're Main and Sub: the
   same signals, Sub shifted a little because its VFO was on a slightly
   different frequency, and at a lower level. 24 frames a second on screen.
   There's a picture in `docs/images/first-spectrum.png`.
5. **Decode the FTdx101 frame.** Bytes 0–849 are the Main waterfall
   (bit-inverted, they're in wfview's format), 850–1699 the Sub
   waterfall. Map the settings block (about 144 bytes at the end) by
   changing one control at a time on the radio and diffing captures.
   Work out what pin 3 carries.

   **Started on 2026-11-11.** I changed one control at a time from YWC's Radio
   Scope card and its settings, and compared the fixed block before and after.
   `live_view.py --dump captures/latest.bin` keeps the last second of frames
   in a file, so captures don't need the viewer closed. The captures are in
   `captures/step5/`, named after what was set. Found so far:

   | Bytes | What | How it's written |
   |---|---|---|
   | 3068-3071 | VFO A, the Main scope's centre | Hz, 4 bytes big-endian |
   | 3094-3097 | VFO B, the Sub scope's centre | Hz, 4 bytes big-endian |
   | 2961-2964 | VFO A again | BCD, 10 Hz steps: `14 07 50 00` is 14.0750.00 |
   | 3009-3012 | VFO B again | BCD: `07 47 40 00` is 7.4740.00 |
   | 3036-3039, 3064-3067 | VFO A + 1500 Hz | Hz, big-endian. Not sure what for |
   | 3040-3043, 3086-3089 | VFO B + 1500 Hz | the same |
   | 3062 | Main span and placement | low 4 bits the span: 0 = 1k, 1 = 2k, 2 = 5k, 3 = 10k, 4 = 20k, 5 = 50k, 6 = 100k, 7 = 200k, 8 = 500k, 9 = 1M. Top 2 bits the placement: 1 Center, 0 Cursor, 2 Fix |
   | 3084 | Sub span and placement | the same |
   | 3076-3079 | Main's left edge in Cursor and Fix | Hz, big-endian. Not kept up to date in Center |
   | 3098-3101 | Sub's left edge in Cursor and Fix | the same |
   | 2917 | Main's scope mode, the SS P3 character: 3 = W/F Center L, 6 = Cursor, 9 = Fix | |
   | 2918 | Sub's scope mode, the same | |
   | 2926-2927 | follows Main's span: about 4,740,000 / span, capped at 395 (1k-10k 395, 20k 237, 50k 94, 100k 47, 200k 23, 500k 9, 1M 4) | unknown |
   | 2930-2931 | the same for Sub | unknown |
   | 2916 | 0 or 1: the Band button in the Radio Scope card, MAIN or SUB | |

   In Center the scope is centred on the VFO: with VFO A tuned onto a steady
   carrier (14.097.07, USB), the carrier sat on point 425 of 850. In Cursor and
   Fix the left-edge field gives the window. Fix at 14.000 with 200k gave the
   same trace as Center with VFO A on 14.100, to the point. The radio keeps a
   separate span for Fix: Main's was 10k while Center was on 20k.

   **The 850 points cover more than the span.** At each span from 1k to 1M I
   tuned VFO A a quarter of the span and measured how far the whole trace
   moved. Every time it was 197-199 points, so the span is the middle 791
   points (788 to 795), with about 30 points beyond each edge:

   | Span | 1k | 2k | 5k | 10k | 20k | 50k | 100k | 200k | 500k | 1M |
   |---|---|---|---|---|---|---|---|---|---|---|
   | Points in the span | 792 | 790 | 791 | 795 | 788 | 792 | 790 | 789 | 791 | 791 |

   `live_view.py` draws the scale that way, for all three placements.

   Still to do: CW and the other modes (the VFO + 1500 Hz fields), 3DSS, and
   bytes 1701-2899.
6. **YWC input.** A serial-port source in `Yaesu_Sdr_Worker` beside
   `Ft4222Bridge`, feeding the existing `Ft710ScopeFrame` path. Centre
   frequency and span are in the frame itself (step 5), so YWC doesn't need
   CAT for them, unlike the FT-710. Then the
   FTdx101 additions: Main and Sub waterfalls, each placed on its own VFO.
7. **Compare with the RSP1 and RSP1B.** Once YWC draws a spectrum from the ACC
   data, compare it with the two SDRs on the 9 MHz IF outputs: the RSP1B on VFO
   A and the RSP1 on VFO B. Receive only: off-air signals, or a signal
   generator through an attenuator into the antenna socket at S9 or below.
   Nothing keys the radio.
   - **Setup.** Same centre and span on all three, Main on VFO A and Sub on VFO
     B, so the ACC Main waterfall lines up with the RSP1B and the ACC Sub
     waterfall with the RSP1. Note the radio's scope settings (span, level,
     speed), because the ACC data is the radio's display data after its own
     scaling.
   - **Pictures.** Screenshots of the same moment from each source, saved
     side by side in `docs/images/compare/`: a quiet band, a busy band (40 m
     in the evening, or a contest weekend), a single steady carrier, and a
     strong signal next to a weak one.
   - **Frequency accuracy.** Where a steady carrier sits on each, against the
     dial frequency. The RSP1 has a plain crystal, about 5 ppm, so up to about
     45 Hz out at 9 MHz before YWC's correction. The RSP1B's reference is
     better, and the ACC data comes from the radio's own reference.
   - **Resolution.** Points across the span (850 per waterfall line in the
     ACC frame) and the bin width that gives at each span, against the RSPs'
     FFT size in YWC. Can two close carriers be told apart?
   - **Noise floor and dynamic range.** The ACC data is one byte a point, so
     at most 256 levels, and its dB scale is unknown. Step the radio's
     attenuator (0, 6, 12, 18 dB) on a steady carrier to find the dB per
     count. Then compare the gap between the noise floor and the strongest
     signal with the RSPs'.
   - **Update rate and latency.** Frames a second from each (25 from the ACC).
     For latency, retune the VFO and compare how soon each display follows,
     from the frame timestamps.
   - **Span.** The widest and narrowest span each one can show.
   - **What each needs.** The ACC route needs only the ESP32 on the ACC
     socket; the SDRs need the IF outputs, the receivers and SDRplay's driver.

   Write the results in a table in the README, one row per measurement, with a
   column each for the ACC, the RSP1B and the RSP1.

## Open questions

- ~~Logic levels on the ACC pins.~~ Measured 2026-11-10: all 3.3 to 3.4 V. See
  the Levels table in [bench-notes.md](bench-notes.md).
- ~~Does pin 3 carry the Sub receiver?~~ No. On 2026-11-10 it carried no data
  with the Sub receiver on or off, so both waterfalls come on DATA2. The
  schematic calls it `ACC_MISO`, so it's probably the line a listener uses to
  send to the radio.
- What do `ACC_PTT` and `ACC_SEND` on pins 9 and 10 do? I don't need them, and
  they aren't to be probed.
- Does the radio need a menu setting before it streams to the ACC socket,
  as the FT-710 does (EX 03-01-26 SCU-LAN10)?
- Firmware: one reseller says the SCU-LAN10 needs FTdx101 firmware of at least
  MAIN V01-14, DISPLAY V01-06, IF DSP V01-05 and SDR V02-03. The ACC stream is
  what the SCU-LAN10 uses, so if the radio doesn't stream with the link on,
  check the firmware versions first.
- Will the radio stream to the ESP32 and an SCU-LAN10 at the same time? Not
  needed now.
