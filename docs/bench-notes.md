# Bench notes

Measured results only. Anything taken from the manual or other people's work
belongs in [plan.md](plan.md).

## Cable

[Amazon UK B0BRQDDTTZ](https://www.amazon.co.uk/dp/B0BRQDDTTZ), 13-pin mini-DIN male to female, £18.99. All 13 pins confirmed
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

ESP32-S3 DevKitC-1 N16R8 ([Amazon UK B0F8N8LZ4G](https://www.amazon.co.uk/dp/B0F8N8LZ4G)).

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

## Stage 2 levels

2026-11-10. The same scope and settings, long ground lead. The ESP32-S3, erased
with tools/erase_esp32.py, in the board and powered from the PC through its COM
socket before the radio was switched on. Link on. Each line measured at the
ESP32's terminal.

| ACC pin | Signal | Result |
|---|---|---|
| 12 | CNT TX, heartbeat | `alive` and a zero byte, 8.49 us a bit, 0 to 3.28 V (3.68 V peak to peak with ringing). 15 bursts in a row, 0.9996 to 1.0002 s apart. |
| 11 | CNT RX | Not measured again: joined to pin 12 by the link. |
| 6 | CS | 25.00 Hz, period 39.99 to 40.00 ms, low 36.54 ms, high 3.32 V. Ringing to 3.96 V peak to peak at the edges. |
| 5 | CLOCK | 1.96 MHz in bursts of 8. Rise 10 to 90% 43 to 45 ns (stage 1: 45 to 46 ns). Overshoot 3.68 V and -0.40 V at 200 ns/div (stage 1: 3.64 V and -0.40 V). |
| 4 | DATA2 | About 28 ms of data per 40 ms frame, 0 to 3.32 V, 3.76 V peak to peak at the edges. |
| 3 | DATA1 | No data. Mean 3.31 V, 0.8 V of ripple, 3.04 to 3.84 V. |

Every line is the same as in stage 1, so stage 2 passes. The edges aren't
measurably softer: the ESP32's input capacitance, a few pF behind 470 ohms, adds
about 2 ns. One shot was taken by mistake on terminal 12 of the ESP32, which isn't
wired to anything. It sat near 0 V with ripple, and was deleted.

## Ground pins

| Date | Check | Result |
|---|---|---|
| 2026-11-10 | ACC pin 2 to ACC pin 13, cable out of the radio | Open; the cable doesn't join them |
| 2026-11-10 | ACC pin 2 to ACC pin 13, cable in the radio, radio off | Near 0 ohms, the same as the meter leads shorted: both are ground inside the radio |

## Scope

2026-11-11: I patched the Siglent SDS1104X-E to 200 MHz bandwidth (firmware
8.1.6.1.35R2). Every shot above was taken before the patch, at 100 MHz. The
signals are 2 MHz with edges of about 45 ns, so the patch makes no visible
difference to these readings, but rise times measured after it may come out a
few ns faster.

## First frames

2026-11-11. Firmware from `firmware/` flashed over COM5 with the radio's DC lead
out. Both of the board's sockets plugged into the PC: COM (COM5) and USB (COM6,
303A:1001). Radio on, link on 38 to 39, scope not connected.

| Test | Result |
|---|---|
| Radio off | Status text only, no frames. LED amber. |
| First build, SPI mode 3 | The board counted every frame, but the PC got about 1 packet in 9: the USB send buffer was too small and partial packets went out. Fixed in the firmware. |
| Fixed build, SPI mode 3 | 26 frames/s, all 4096 bytes. Data ends at byte 3101 or 3102, changing between frames; fixed bytes differ by single bits. Wrong edge. |
| Fixed build, SPI mode 2 | 25.0 frames/s once the first half second is skipped, no sequence gaps, all 4096 bytes. Data ends at byte 3102 in every frame and the fixed bytes are identical. No `FF 01 EE 01` trailer. LED pulsing green once a second. |

## Two and three probes

2026-11-13. The same scope, now at 200 MHz, probes at 10X, stacked at 2 V/div. I
read the scope over SCPI and checked each capture before saving. Link on.

| Trace | ESP32 | Result |
|---|---|---|
| CNT TX and CS from power-on, 500 ms/div | Out | CS high 0.47 s before the first heartbeat. Heartbeat every 1.000 s. The first CS low comes on the second heartbeat, 1.000 s after the first, then every 40.0 ms. Both idle at about 3.3 V. |
| CS and CLOCK, 2 us/div | Out | First clock rise 4.07 us after CS falls. Bursts of 8 at 1.95 MHz, 8.9 to 9.0 us apart. CS 3.24 V to 0 V. Clock 3.84 V to -0.64 V. The clock rests low before CS falls and between bytes. |
| CS, CLOCK and DATA2, 2 us/div | Out | First clock 4.07 us after CS. First bytes 0xB1 0x90, read on the rising edge, MSB first. CS high 3.32 V. Clock 3.84 V to -0.64 V. DATA2 3.60 V to -0.32 V. |
| CS, CLOCK and DATA2, 2 us/div | In | First clock 4.03 us after CS. 1.97 MHz. First bytes 0xBF 0xD2. CS high 3.24 V. Clock 3.84 V to -0.48 V. DATA2 peak 3.60 V. |

The levels match 2026-11-10. New: on 2026-11-10, with no link, the clock idled
high at 3.32 V, but while the radio streams it rests low between bytes. Mode 2
decodes it correctly, as in [First frames](#first-frames).

Two retakes: one with probe 1 on terminal 14 (DATA1) instead of 13, and one with
probe 1's switch knocked to 1X, which made CS read over 12 V.
