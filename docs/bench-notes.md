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
| Pin that drives the RGB LED | GPIO48, found during the paddle work with `ledscan` |
| Paddle wiring removed | |

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

Scope ground on pin 2, nothing connected to the ESP32.

| Pin | Signal | High level | Notes |
|---|---|---|---|
| 12 | CNT TX, heartbeat | | |
| 5 | CLOCK | | |
| 6 | CS | | |
| 4 | DATA2 | | |
| 3 | DATA1 | | |
