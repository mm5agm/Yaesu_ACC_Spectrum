# Plan: a library anyone can use, then YWC

The frame is decoded (step 5 of [plan.md](plan.md)). This is the plan for turning
it into something other people can use: a library in this repo, YWC as its first
user, and a guide that gets a new user from an empty ESP32 to a working scope one
checked step at a time.

Decided on 2026-11-11:

- The decoder becomes a library **in this repo**, licensed **LGPL-3**, so any
  program can link it unchanged, closed-source loggers included. The firmware
  and the tools stay GPL-3.
- YWC uses the library rather than keeping its own copy of the decoder.

## What gets built, in order

### 1. Firmware v2

- SPI mode 2 is the default. At the moment mode 2 works only because it is
  saved in flash.
- A version number in the status text, so programs know what they're talking to.
- Edge counts a second on CS, CLOCK and DATA2 in the status text, for the
  wiring check (see the guide below).
- Maybe: the heartbeat on GPIO38 and GPIO39, read as inputs with no pull-up.
  That would make "the heartbeat link is missing" certain instead of a guess.
  **Not decided:** those pins are "never touched" at the moment.
- A release with a single merged `.bin` (bootloader, partitions and app), so
  flashing it is one file at address 0.

Flashing follows the bench rules: the radio off and its DC lead out.

### 2. The library: `YaesuAccScope`

**First version on 2026-11-11**, in [library/](../library/README.md): packets, frames, status texts, the serial reader and the board finder, with 48 tests on my captures. Tried on the board: it found COM6 from the status text and read version 2.0.1. Still to do: the network reader (with YWC's re-broadcast) and nuget.org.

A .NET Standard 2.0 library, so it works in .NET 8 programs and in older .NET
Framework ones, which many logging programs still are. In `library/`, with its
own LGPL-3 licence files (the LGPL text and the GPL text it builds on).

- **Packets:** splits the board's byte stream into frames and status texts
  (`'Y' 'A'` header, see firmware/src/main.cpp).
- **Frames:** for Main and for Sub: the 850 levels the right way up, VFO, the
  centre and width of the row, the span the radio shows, placement
  (Center/Cursor/Fix), 3DSS on or off, and the passband marker. Also the
  audio scope: the AF-FFT and the oscilloscope. All of it from the step 5
  table, nothing from CAT.
- **Serial reader:** opens the board's port with DTR and RTS low and **never
  writes to it**, so a program using the library can't reset the board or
  change its SPI mode. It finds the board by listening for its status text,
  and never opens a port that doesn't send one.
- **Network reader** (with step 4): the same frames from YWC's re-broadcast.
- **Tests** on my own captures in captures/step5: every span, Center, Cursor
  and Fix, Sub, the audio scope.
- Published on nuget.org. That needs a nuget.org account and an API key,
  which are mine to create.

Later, only if someone asks: a plain C DLL from the same code (NativeAOT), for
C, C++, Delphi or Python, with no .NET needed.

### 3. The protocol, written down

**Done on 2026-11-11:** [protocol.md](protocol.md).

`docs/protocol.md`: the packet format and the full frame layout, so anyone can
write a reader in any language without the library. Mostly the step 5 tables,
tidied up.

### 4. YWC

A branch off develop. No version bump until I say go.

- **Source:** two new device choices for the SDR panels when the radio is an
  FTdx101MP or D: "ACC scope, Main" and "ACC scope, Sub". The defaults are panel
  A on Main and panel B on Sub. One shared reader owns the port for both panels.
  It runs inside the main app, not in the SDR worker: it's a plain serial port,
  with no native DLL that needs keeping in another process. The worker sends
  every `yaesu-scope:` key to the FT-710 code, so the ACC keys must never reach it.
- **Placement:** from the frame (rfOrdered, centre and span), so no CAT
  polling. Unlike the FT-710 scope, Cursor and Fix work too.
- **The same controls as the RSP1 and RSP1B panels.** Click to tune, the mouse
  wheel, drag, the right-click menu and hold work from the frame's centre and
  span, so they should work as they are. The span buttons are the gap: for the
  FT-710 scope they're refused. For the ACC panels they set the radio's own
  scope span over CAT, as the Scope panel already does. To check: an RSP1B panel
  and an ACC panel side by side on VFO A, every control ticked off. Anything
  that can't work gets a reason in the manual.
- **Re-broadcast:** only one program can have a COM port open. An option in YWC
  passes the frames on over the network, so other programs (N1MM, loggers) can
  use the library's network reader at the same time as YWC.
- **Setup guide page** (below).
- **Docs:** a USER_MANUAL section, the CHANGELOG, and a link here for the
  hardware.

### 5. This repo's page

- **Buy Me a Coffee:** PayPal's donate link, which can carry the program's
  name: `https://www.paypal.com/donate?business=<Merchant ID>&item_name=Yaesu+ACC+Spectrum`.
  The name shows on the payment and on PayPal's email, so I can tell which
  program it came from. YWC and IWC move to the same kind of link, each with
  its own name. Using the Merchant ID keeps my email address off the page.
- **Visitor and download counts:** GitHub keeps only 14 days of visitor
  numbers. Download counts on releases are kept for ever. A daily GitHub Action
  adds each day's numbers to a history file in the repo and draws an SVG badge
  with the totals since it started. Anything older than 14 days on the first
  run is lost. It needs a fine-grained token with read access to the repo's
  traffic, saved as a repo secret. The SVG is drawn by the Action itself, so no
  outside counting service sees the visitors.
- The repo is private at the moment. Making it public is the last step,
  after the firmware release and the guide.

## The setup guide

A page in YWC. Each step has a check that YWC runs itself, and a step opens only
when the one before has a green tick. When a check fails, it says what to look
at.

**Before starting:** the parts list, and the bench rules. Wire only with the
radio off and its DC lead out. Power on the ESP32 first and then the radio, and
power off the radio first.

**Step 1: connect the ESP32 and program it.**
- YWC lists the Espressif USB ports (303A).
- It flashes the firmware with esptool, shipped with YWC. If a new board won't
  go into its bootloader, the guide says to hold BOOT and press RESET.
- **Check:** YWC hears the board's status text with the right firmware version.

**Step 2: wire it to the ACC socket and check the wiring.** With the radio on:
- **2a:** the firmware's edge counts.
  - No CS and no CLOCK: the radio isn't streaming. Look at the heartbeat link,
    ground and the plug. With the GPIO38/39 option, the guide can say which.
  - CS but no CLOCK: the CLOCK wire.
- **2b:** frames are 4096 bytes and the data ends at byte 3102 in every frame.
  That proves DATA2 and the SPI mode. Data that is all 00 or all FF means DATA2
  is open or shorted.
- **2c, the final proof:** VFO A and VFO B in the frame match what YWC reads
  over CAT. Then turn the dial and watch both follow.

## Later

- 3DSS in the browser panel. I already have a prototype in tools/live_view.py.
- An audio scope panel: the AF-FFT and the oscilloscope.
- Test Sub's audio scope (bytes 2300–2899).
- The comparison with the RSP1 and RSP1B: step 7 of [plan.md](plan.md).
- The C DLL.

## Not tested, and can't be

What the frames do while the radio transmits. Nothing here transmits.

## Still to decide

- My PayPal Merchant ID.
- Whether the counts show publicly on the page, and whether the Action counts
  YWC and IWC too.
- Whether the firmware may read GPIO38 and GPIO39 as inputs.
- Which ESP32-S3 board the guide names.
