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
| 11 | CNT RX, input to the radio | 3.37 V, steady | 2026-11-10. No activity, as expected for an input. **Correction:** the 2026-11-09 reading in this row was taken with the pink and orange wires swapped on the board, so it was really ACC pin 3. Found and fixed on 2026-11-10. |
| 6 | CS | 3.31 V idle | 2026-11-10. Idle high with no link. With the link: 25.002 Hz, low 36.54 ms, high 3.45 ms; one low per 4096-byte frame. Link off: stays high (watched 14 s). |
| 5 | CLOCK | 3.32 V | 2026-11-10. Idle high with no link. With the link: 1.97 MHz, bursts of 8 clocks, one per byte, about 5.3 us from the last rising edge of one byte to the first of the next. 8.9 us per byte, so 36.5 ms per frame, matching CS. Edges ring to 3.72 V and -0.40 V on the long ground lead. |
| 4 | DATA2 | 3.31 V | 2026-11-10. Idle high with no link. With the link it carries the data: about 27.8 ms switching, then low for about 12.2 ms, of which CS is high for 3.45 ms, so the last 8.7 ms (about 980 bytes) of each frame is zeros. Peaks 3.68 to 3.72 V at the edges. |
| 3 | DATA1 | 3.34 V | 2026-11-10. Idle high with no link. With the link: still no data, with the Sub receiver on and with it off. About 0.3 V of ripple that follows the frames, crosstalk from the clock and DATA2 wires; with the probe's ground spring it still peaks at 3.9 V. The ripple goes with the link off. |

All six lines are at 3.3 to 3.4 V, under the ESP32-S3's 3.6 V absolute maximum,
so stage 1 passes. The only readings above 3.6 V are nanosecond ringing at the
clock and data edges and the crosstalk peaks on DATA1. Through 470 ohms they can
put no more than a few hundred microamps into the ESP32's protection diodes.
Pictures for every row are in "Oscilloscope traces" in the README.

## Ground pins

| Date | Check | Result |
|---|---|---|
| 2026-11-10 | ACC pin 2 to ACC pin 13, cable out of the radio | Open; the cable doesn't join them |
| 2026-11-10 | ACC pin 2 to ACC pin 13, cable in the radio, radio off | Near 0 ohms, the same as the meter leads shorted: both are ground inside the radio |
