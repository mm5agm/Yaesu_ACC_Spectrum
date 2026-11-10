# Bench notes

Measured results only. Anything taken from the manual or other people's work
belongs in [plan.md](plan.md).

## Cable

Amazon UK B0BRQDDTTZ, 13-pin mini-DIN male to female, £18.99. All 13 pins confirmed
wired, 2026-11-06.

Not cut. A ready-made IC-7300MK2 13-pin plug with wires goes into the female end,
because that makes it easier to plug the board into the circuit and unplug it. See
"The cable and the board" in [plan.md](plan.md) for Icom's published colours.

Metered from the cable's male plug to each wire of the Icom lead:

| Pin | Wire colour | Continuity OK |
|---|---|---|
| 1 | | |
| 2 | Red | Yes, 2026-11-06 |
| 3 | Orange | Yes, 2026-11-06 |
| 4 | Yellow | Yes, 2026-11-06 |
| 5 | Green | Yes, 2026-11-06 |
| 6 | Blue | Yes, 2026-11-06 |
| 7 | | |
| 8 | | |
| 9 | | |
| 10 | | |
| 11 | Pink | Yes, 2026-11-06 |
| 12 | Light blue | Yes, 2026-11-06 |
| 13 | | |

## Board

ESP32-S3 DevKitC-1 N16R8 (Amazon UK B0F8N8LZ4G).

| Check | Result |
|---|---|
| Board marked | HW678 |
| Pin that drives the RGB LED | GPIO48, GRB order, checked 2026-10-31 in the paddle work with `ledscan` (Adafruit NeoPixel library) |
| Earlier wiring removed | Yes, bare board in [images/expansion-board.jpg](images/expansion-board.jpg) |

## Pin 8 check

Cable out of the radio. Pin 8 must be open to all other pins and to the shell.

| Date | After what change | Result |
|---|---|---|
| 2026-11-06 | First check, Icom plug in the cable's female end | Open to the other 12 pins and the shield. Meter showed overload on all, over about 40 Mohm |

## Is the 13 V rail live with the radio off?

Measured at the rear +13.8V jack, with an RCA lead plugged in while the supply
is off and a meter on the far end.

| Radio front panel | Supply | Volts |
|---|---|---|
| Off | On | |
| On | On | |

## Levels

Pins are ACC socket pins; the breakout terminal for each is in the README's
Wiring table. Scope ground on ACC pin 2, nothing connected to the ESP32.

Siglent SDS1104X-E, probe at 10X, 1 V/div, DC coupling, long ground lead. Each
line measured at the free end of its 470 ohm resistor on the expansion board,
with the ESP32 module out of the board and no 11-12 link. Screenshots and
measurements read over the scope's LAN port (SCPI).

| ACC pin | Signal | High level | Notes |
|---|---|---|---|
| 12 | CNT TX, heartbeat | 3.36 to 3.40 V | 2026-11-09. Low 0.00 V. Spikes to 3.52 V and dips to -0.12 V, mostly pickup on the long ground lead. Burst once a second, decoded as `61 6C 69 76 65 00` (`alive\0`), all stop bits good. Bit time 8.50 us, so about 117,660 baud, 2.1% faster than 115200; well inside UART tolerance. Burst 501 us long. |
| 11 | CNT RX, input to the radio | 3.38 V, steady | 2026-11-09. No activity, as expected for an input. 3.24 to 3.52 V, 0.28 V peak to peak. A first attempt with the ground clip off read 2.2 to 4.7 V of noise; ignore that. |
| 5 | CLOCK | | |
| 6 | CS | | |
| 4 | DATA2 | | |
| 3 | DATA1 | | |
