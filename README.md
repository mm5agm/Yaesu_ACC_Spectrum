# Yaesu ACC spectrum

Can Yaesu Web Control (YWC) get a Yaesu's own scope data from the ACC socket,
through an ESP32-S3 over USB?

The work starts on an FTdx101MP. The FTDX101D and FTDX10 are expected to send the
same data on the same socket (other people's projects have done it on the FTDX10),
so the same board and code should suit them too. The FT-710 doesn't need it: it
already sends its scope over USB, and YWC uses that.

Status: **investigation, nothing built and nothing measured on the radio yet.**
The full plan is in [docs/plan.md](docs/plan.md).

The extension cable isn't cut. A ready-made 13-pin plug with wires, from the
IC-7300MK2, goes into its female end, so the board is easy to plug in and unplug.

## Use at your own risk

**Everything in this repository is used entirely at your own risk.** Connecting
anything to the ACC socket can blow a fuse or damage the radio's main board, and the
repair may mean sending the radio back to Yaesu. If you damage your radio, or anything
else, that's your responsibility. The author accepts no liability for it, and nothing
here is approved or supported by Yaesu. There is no warranty of any kind, and the
information here may be wrong. Read [docs/fuse-risk.md](docs/fuse-risk.md) first.

## The board's LED

The board is an ESP32-S3 DevKitC-1 N16R8, marked HW678, and its RGB LED is on GPIO48
in GRB order, so it's free for status lights. It's an addressable LED, so it needs a
data stream, not a plain high or low. It's driven with the
[Adafruit NeoPixel](https://github.com/adafruit/Adafruit_NeoPixel) library and kept
dim, because it's dazzling at full power.

Some boards differ: version 1.1 of the DevKitC-1 has its LED on GPIO38, which this
project uses for pin 12. Check yours before wiring it. See "The cable and the board"
in [docs/plan.md](docs/plan.md).

## Wiring

![The ESP32-S3 on its screw-terminal board, with the ACC wires marked. Bottom row, the 3V3 to GND row: ACC pin 5 CLOCK (green wire) to GPIO10, pin 4 DATA2 (yellow) to GPIO11, pin 6 CS (blue) to GPIO13, pin 3 DATA1 (orange) to GPIO14, each through 330 ohms, and pin 2 GND (red) to the GND terminal at the end, no resistor. Top row: pin 11 CNT RX (pink) to GPIO39 and pin 12 CNT TX (light blue) to GPIO38, each through 330 ohms.](docs/images/acc-wiring.jpg)

The six signal wires that go to the ESP32 (pins 3, 4, 5, 6, 11 and 12) each have a
330 ohm resistor in series at the ESP32 end. Ground (pin 2) goes straight to GND.

These pins are **not connected** and have no resistors. Heat-shrink over the end of
each wire, one wire at a time, is all they need:

- Pin 1, brown: Power ON, the remote power-on line
- Pin 7, purple: 3.3 V through 1k, which can't power anything
- Pin 8, gray: 13.8 V behind a fuse
- Pin 9, white: the radio's PTT line
- Pin 10, black: the radio's SEND line
- Pin 13, light green: a second ground; pin 2 is the one we use

The details, and the checks to make before connecting anything, are in "Wiring to
the ESP32-S3" in [docs/plan.md](docs/plan.md).

### Yes, ground is the red wire

Red for ground looks like the last thing any sane person would do, but the colours
aren't my choice. They come with the ready-made 13-pin lead (the IC-7300MK2's ACC
plug), and they follow the resistor colour code by pin number, not by what the wire
does: pin 1 brown, 2 red, 3 orange, 4 yellow, 5 green, 6 blue, 7 purple, 8 gray,
9 white and 10 black. Pink, light blue and light green cover pins 11 to 13. Pin 2 is
ground, so ground is red.

The upside is that anyone who knows the code can read a wire's pin number from its
colour. The downside is that the usual meanings don't apply:

- **Red is ground.** Mark it GND with a label or a band of tape.
- **Gray is 13.8 V** (pin 8). It's not connected.
- **Black is SEND** (pin 10), not ground. It's not connected.
- Blue and light blue are different signals, and so are green and light green.

Other leads may use other colours, so check yours with a meter before wiring.

## Layout

| Folder | What goes there |
|---|---|
| `firmware/` | Our own ESP32-S3 firmware |
| `tools/` | Capture and analysis scripts |
| `captures/` | Short frame captures from the radio, with a note of the radio's settings |
| `docs/` | The plan and the bench notes |

If this works, the firmware, wiring and bench notes stay here, and the PC code
goes into YWC as a serial input beside `Ft4222Bridge`.

## Rules

- **Receive-only.** Nothing here keys the radio.
- **Read [docs/fuse-risk.md](docs/fuse-risk.md) before touching the radio.** Pin 8
  is 13.8 V behind a fuse, and pins 9 and 10 are the PTT and SEND lines. ACC pins
  1, 7, 8, 9, 10 and 13 are never connected.
- **Plug and unplug with the supply off.**
- **Check the levels before connecting the ESP32.** See "Before connecting the
  ESP32" in the plan.
- **Use the S3's native USB.** The stream is 102.4 kB/s, more than a USB-serial
  chip at 921600 baud can carry.
- **Tools never open COM3 or COM4.** Those are the radio's ports. Name the
  ESP32's port explicitly.
- **No SLTPLAN code in this repo.** Their projects have no licence. Their
  firmware may be flashed as an outside comparison, but it isn't copied here,
  and our firmware is written from the MIT sources and our own captures.

## Licence

GPL-3.0, the same as the other repositories. See [LICENSE](LICENSE). Its sections 15
and 16 also cover the lack of warranty and liability. The repo is private for now.
