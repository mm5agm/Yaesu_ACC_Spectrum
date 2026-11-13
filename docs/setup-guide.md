# Setting up the ACC board, step by step

This takes you from the parts to the FTdx101's own Main and Sub scope on your PC:
in ACC Spectrum Viewer, and in Yaesu Web Control (YWC) if you use it. It's how I
built and run mine on an FTdx101MP. The FTDX101D and FTDX10 should send the same
data on the same socket, but I haven't tried either. If you have one, I'd like to
hear how it goes.

It's receive-only. Nothing here transmits or touches the PTT or SEND lines.

**Everything here is at your own risk.** Connecting anything to the ACC socket
can blow a fuse or damage the radio's main board. Read
[Use at your own risk](../README.md#use-at-your-own-risk) and
[fuse-risk.md](fuse-risk.md) before you start.

## 1. What you need

- **The parts in [What I used](../README.md#what-i-used):** a 13-pin mini-DIN
  extension cable (male to female), a ready-made 13-pin plug with wires, an
  ESP32-S3 DevKitC-1 N16R8 on a screw-terminal breakout board, six 470 ohm
  resistors and a short link wire.
- **One USB data lead** for the ESP32's socket marked **USB**. A charge-only lead
  won't work.
- **A multimeter.** An oscilloscope is useful but not essential; see step 5.
- **A Windows PC** for ACC Spectrum Viewer and YWC.
- Heat-shrink, and labels or tape to mark the ground wire.

**Check your board's LED pin.** Version 1.1 of the DevKitC-1 has its LED on
GPIO38, which this project uses for ACC pin 12. The HW678 board I used has it on
GPIO48. See [The board's LED](../README.md#the-boards-led).

## 2. Make up the lead

The plug's wires go to the breakout board. Nothing is cut on the extension
cable: the plug goes into its female end, and its male end goes into the radio.

1. **Find which wire goes to which pin with the meter,** with the plug in the
   extension cable and the cable out of the radio. The colours in this repository
   are those of the Icom lead I used, and yours may differ. Pin numbers are as in
   the radio's manual.
2. **Mark ACC pin 2 as ground.** On the Icom lead it's red; see
   [Yes, ground is the red wire](../README.md#yes-ground-is-the-red-wire).
3. **Insulate the six wires that aren't used,** each with its own heat-shrink:
   pins 1 (power on), 7, 8 (13.8 V behind a fuse), 9 (PTT), 10 (SEND) and 13 (a
   second ground). Pin 8 is the one that matters most.
4. **Meter pin 8** at the radio end of the cable, against every other pin and the
   shell. It must be open to all of them.

## 3. Wire the breakout

The breakout's screw terminals are marked with the ESP32's GPIO numbers, so they
are called terminals here. Each signal wire goes through a 470 ohm resistor at
the terminal end. Ground goes straight to GND.

| ACC pin | Signal | Terminal |
|---|---|---|
| 2 | GND | GND, no resistor |
| 3 | DATA1 | 14 |
| 4 | DATA2 | 11 |
| 5 | CLOCK | 10 |
| 6 | CS | 13 |
| 11 | CNT RX | 39 |
| 12 | CNT TX | 38 |

Then **join terminals 38 and 39 with a short link wire.** The radio sends a
heartbeat on pin 12 once a second, and only streams its scope data when it hears
that heartbeat come back on pin 11. The link sends it straight back. The firmware
never drives GPIO38 or GPIO39, so the link stays on for good.

The picture and more detail are in [Wiring](../README.md#wiring).

## 4. Flash the firmware

Do this before the ESP32 goes anywhere near the radio, so that no old program on
the board can drive a pin.

1. Download `AccSpectrumViewer-x.y.z-win-x64.zip` from the
   [releases page](https://github.com/mm5agm/Yaesu_ACC_Spectrum/releases) and
   unzip it anywhere. Nothing needs installing.
2. With the ESP32 **off the breakout**, plug its socket marked **USB** into the PC.
3. Run `AccFirmwareFlasher.exe`. The first time, Windows may show "Windows
   protected your PC". Click **More info**, then **Run anyway**; see
   [The Windows warning](../viewer/README.md#the-windows-warning-the-first-time).
4. Pick the board's port and press **Flash**. The flasher only ever lists the
   board's own ports, never a radio's. A good flash ends with **Hash of data
   verified** and **Done**. If it can't connect, hold **BOOT**, press and let go of
   **RST**, let go of BOOT, and try again. If the board's port keeps appearing
   and disappearing, which an erased board does on the USB socket, use that
   BOOT and RST step first, or plug a lead into the socket marked **COM** for
   flashing instead.

The board's LED goes amber: the firmware is running and no frames are coming in
yet. Details are in [Flashing safely](../viewer/README.md#flashing-safely).

## 5. Check the radio's levels first

The ESP32-S3 can take at most 3.6 V on a pin. On my FTdx101MP every line is
3.3 V or less, but check yours before the ESP32 goes on, especially on a
different model.

The rule for every plug, wire and probe from here on: **switch the power supply
off and take the radio's DC lead out first.** Switching the radio off on its
front panel isn't enough. With the supply on, the plug's contacts flash as they
go in.

1. With the supply off and the DC lead out, plug the cable into the radio's
   **ACC** socket, with the ESP32 still off the breakout. **Take the link off
   38 and 39 for this check,** so the radio doesn't stream.
2. Put the DC lead in, switch the supply on, and switch the radio on.
3. With the meter's black lead on GND, measure terminals 38, 39, 13, 10, 11 and
   14. On mine each one sat between 3.31 and 3.38 V. **If any reads above
   3.6 V, stop:** don't connect the ESP32. That line needs level shifting first.
4. Switch the radio off, then the supply, and take the DC lead out. Put the link
   back on 38 and 39.

With a scope, you can also watch the lines while the radio streams.
[Safe first connection](../README.md#safe-first-connection) has the full
procedure, and [Oscilloscope traces](../README.md#oscilloscope-traces) shows what
each line looked like on mine.

## 6. Connect and power up

1. With the supply off and the DC lead out, put the ESP32 on the breakout.
2. **ESP32 first:** plug its socket marked **USB** into the PC.
3. **Then the radio:** DC lead in, supply on, radio on.

That way the radio never drives a line into an ESP32 that has no power. When
you finish, do it the other way round: radio off first, then the ESP32.

After two or three seconds the LED pulses green once a second: frames are
arriving from the radio.

## 7. Check it in ACC Spectrum Viewer

Run `AccSpectrumViewer.exe` from the same folder. It finds the board on its own,
and shows Main and Sub, each a trace above a waterfall. The bar along the bottom
checks each wire:

| Reading | Should be | ACC pin | Terminal |
|---|---|---|---|
| CS | 25/s | 6 | 13 |
| CLOCK | about 819,200/s | 5 | 10 |
| DATA2 | changing all the time | 4 | 11 |
| DATA1 | 0, not used | 3 | 14 |
| Frames | 25/s reaching the PC | | |

- **CS and CLOCK both 0:** the radio isn't sending. Check it's on, that the link
  is on 38 and 39, and that ground (ACC pin 2) is connected.
- **One reading 0 while the others count:** check that wire and its resistor.
- **No board found:** check the lead is in the socket marked **USB**, and that it's
  a data lead.

The span is set on the radio, and the viewer follows it. More is in the
[viewer's README](../viewer/README.md).

## 8. Use it in YWC

YWC can show the ACC scope in its spectrum panels instead of an SDR's. It's in
the YWC pre-releases from v2.5.5-pre1. Close ACC Spectrum Viewer first, because
only one program can have the board's port.

In YWC, with the radio model set to FTdx101MP or FTdx101D and saved, go to
**Settings → SDR Spectrum Display**, press **Scan**, then open the
**VFO A SDR** list and choose **ACC scope, Main**, and in the **VFO B SDR** list
choose **ACC scope, Sub**. Save. The rest, including Refine,
is in the YWC manual under
[FTdx101: the radio's own scope from its ACC socket](https://github.com/mm5agm/Yaesu_Web_Control/blob/v2.5.5-pre1/USER_MANUAL.md#ftdx101-the-radios-own-scope-from-its-acc-socket-experimental).

## If something's wrong

- **The LED stays blue:** the firmware started but nothing has happened yet. Wait
  a few seconds.
- **The LED is amber:** the firmware is running but no frames are coming in. Is
  the radio on, and is the link on?
- **Neither USB port appears:** try another lead. Many USB leads only charge.

If you get stuck, or get it working on an FTDX101D or FTDX10, please open an
[issue](https://github.com/mm5agm/Yaesu_ACC_Spectrum/issues).
