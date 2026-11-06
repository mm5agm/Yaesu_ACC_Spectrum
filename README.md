# FTdx101 ACC spectrum

Can Yaesu Web Control (YWC) get the FTdx101MP's own scope data from the ACC
socket, through an ESP32-S3 over USB?

Status: **investigation, nothing built and nothing measured on the radio yet.**
The full plan is in [docs/plan.md](docs/plan.md).

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

None chosen yet. The repo is private.
