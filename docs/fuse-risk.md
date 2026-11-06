# The FTdx101 fuses: what they protect and how they blow

Written before anything is connected to the radio. Nothing here has been measured
on the radio. Each claim says whether it comes from the schematic, Yaesu, an owner's
report or reasoning.

## Short answer

**The fuse people warn about is probably the DVI one.** Yaesu says the radio has a
150 mA internal fuse that protects the main board, that it blows when a DVI-D to
HDMI or VGA adapter draws too much, and that the radio then goes back to Yaesu. The
schematic doesn't show a fuse there, so that part is unresolved.

**The fuse this project could blow is F1003.** ACC pin 8 connects to the radio's
13.8 V rail through only this 3.15 A chip fuse and a 0 ohm link. Connect pin 8 to
ground and the fuse should blow. Nothing in the plan needs pin 8, so the risk is a
slip with the wire or the plug, and the rules below deal with that.

**Pin 8 touching a data pin may be worse than a blown fuse**, and **pins 9 and 10
aren't spare**: the manual calls them Reserve, but the schematic names them PTT and
SEND.

I found no report of a board change being needed. Yaesu says "returned to Yaesu for
service", and owners describe swapping a single surface-mount fuse.

## Which fuse is which

From the Technical Supplement (`docs/manuals/FTDX101MP_D_Technical_Supplement.pdf`
in YWC): MAIN-1 schematic on PDF page 35, MAIN-2 schematic on PDF page 36, parts
list on PDF pages 47-48.

| Fuse | Rating | Feeds | Source |
|---|---|---|---|
| F1001 | 3.15 A | Rear **+13.8V** RCA jack (J1017) | Schematic |
| F1002 | 3.15 A | **TUNER** socket (J1019), through ferrite FB1006 | Schematic |
| F1003 | 3.15 A | **ACC pin 8** (J1021), through 0 ohm link R1289 | Schematic |
| "150 mA fuse" | 150 mA | The DVI socket, going by Yaesu's note | Yaesu. Not on the schematic |

- F1001, F1002 and F1003 are the same part, a KAB2402-322NA31010 chip fuse, on side A
  of the MAIN unit. All three are fed from the rail labelled `13VUS`.
- They are the only parts called fuses in the supplement. A resettable part, Q1009
  (NANOSMDC035F, a "poly switch"), sits on the output of standby regulator Q1006, not
  on the DVI path.
- The 3.15 A is printed on the schematic. A distributor's listing for a Matsuo
  KAB2402322 agrees.
- The manual limits the rear jack to 200 mA, far below 3.15 A. So these fuses guard
  against a short, not an overload: a load drawing 1 A is over the limit but shouldn't
  blow the fuse.

### The DVI fuse doesn't match the schematic

[Yaesu's note](https://www.yaesu.com/pdf/Important%20Monitor%20Converter%20Info.pdf),
which covers the FTDX101D/MP, FTDX10 and FT-710, says:

> your radio has a 150mA internal protection fuse that protect the main-board when a
> high voltage DVI-D to HDMI/VGA adapter is used. If this fuse is open it will
> require your radio to be returned to Yaesu for service.

Its rule is that the converter's maximum power divided by 5 V must not be over
150 mA, so 700 mW gives 140 mA and passes.

The MAIN-2 schematic shows `DVI5V` going through ferrite FB1805, **R1811 (a 0 ohm link
in the parts list)** and diode D1800 to pin 14 of J1803. There's no fuse on that path.
Forum posts say the same: that the FTDX101 has a zero ohm link where a fuse might be.
Different board revisions could explain it, but I couldn't confirm that.

Either way, keep to Yaesu's limit. If there's no fuse, too much current has to fail
something else on that path, which could explain a board-level repair. That's a guess.

## How they get blown

| Cause | Fuse | Evidence |
|---|---|---|
| DVI to HDMI or VGA adapter drawing too much | "150 mA" | Yaesu's note |
| Plugging a rear cable in with the radio on | Rear 13.8 V fuse | One FTDX101MP owner's report |
| Pin 8 shorted to pin 2, pin 13, the shell or a cut wire | F1003 | Schematic reasoning. No report found |
| A short on the TUNER cable or tuner | F1002 | Schematic reasoning. No report found |

The rear jack is easy to short, because an RCA plug's centre pin can touch the shell
on the way in. That fits the owner's report but is my reasoning.

### Repair

Yaesu says the radio goes back for service. Owners on the FTDX101D list describe
replacing the surface-mount fuse behind the rear jack themselves. One says Yaesu
charged about 67 cents a fuse plus shipping, and another fitted a self-resetting fuse
and called the job "not for the faint of heart". One owner lost main-band receive after
pulling a miniature coax lead while lifting the board. These are forum reports, and
this isn't a recommendation to do it.

## ACC socket, from the schematic

Pin numbers and labels are the manual's. Nets and series parts are as drawn on MAIN-1.

| Pin | Manual label | Net | Series part | This project |
|---|---|---|---|---|
| 1 | Power ON | `ACC_PON`, 100k to ground (R1255) | R1232, 0 ohm | **Not connected.** Reaches the power-on circuit through D1003 |
| 2 | GND | Ground | None | ESP32 GND |
| 3 | DATA1 | `ACC_MISO` | R1254, 0 ohm | Scope only at first. The name suggests data into the radio |
| 4 | DATA2 | `ACC_MOSI_BUF` | R1253, 0 ohm | ESP32 GPIO11 |
| 5 | CLOCK | `ACC_CLK_BUF` | R1252, 0 ohm | ESP32 GPIO10 |
| 6 | CS | `ACC_FS_BUF` | 0 ohm link | ESP32 GPIO13 |
| 7 | 3.3V | `D33` | **R1230, 1k** | **Not connected.** About 3 mA at most, so it can't power anything |
| 8 | 13.8V | `13VUS` | **R1289, 0 ohm, then F1003** | **Not connected.** The dangerous pin |
| 9 | Reserve | **`ACC_PTT`** | R1228, 0 ohm | **Not connected, not probed** |
| 10 | Reserve | **`ACC_SEND`** | R1184, 0 ohm | **Not connected, not probed** |
| 11 | CNT RX | `ACC_UMISO` | R1166, 0 ohm | ESP32 GPIO39 |
| 12 | CNT TX | `ACC_UMOSI` | R1143, 0 ohm | ESP32 GPIO38 |
| 13 | Reserve | Ground | R1106, 0 ohm | Not connected |

The direction and behaviour of pins 9 and 10 aren't known, so treat them as able to
key the transmitter.

## Worse than the fuse

This is reasoning from the schematic, not a reported failure.

Pins 3, 4, 5, 6, 11 and 12 reach the radio's logic through 0 ohm links. If pin 8
touches one of them, 13.8 V goes straight into low-voltage logic. F1003 needn't blow
first, because logic can fail at far less than 3.15 A. That would be a board fault, not
a fuse fault.

The 330 ohm resistors in the plan sit on the ESP32 side of the wire. They protect the
ESP32, and do nothing for a short between two wires on the radio side of them.

On the socket, pin 8 sits next to pins 4 and 7, and pin 12 is directly below it:

```
 4  3  2  1
 8  7  6  5
12 11 10  9
      13
```

## Rules for this project

1. **Pin 8 is cut and insulated before the plug goes near the radio.** Cut pins 1, 7,
   8, 9, 10 and 13 to different lengths so the bare ends can't meet, and heat-shrink
   each one on its own.
2. **Meter the plug before the first connection and after any wiring change.** With the
   cable out of the radio, pin 8 must be open to the other 12 pins and to the plug's
   metal shell.
3. **Plug and unplug only with the supply off.** That means off at the power supply,
   not just at the radio's front panel. Treat pin 8 as live whenever the supply is on.
4. **Never put a probe or wire into the radio's socket.** Measure only at the breakout.
5. **Only pins 2, 3, 4, 5, 6, 11 and 12 reach the breakout.**
6. **Take no power from the radio.** The ESP32 runs from its own USB lead.
7. **Clip the scope's ground to pin 2 only.**
8. **No DVI adapter changes as part of this work.**

## Not known yet

- **Is `13VUS` live with the radio switched off?** The name suggests "unswitched", but
  that isn't proof. To measure it, plug an RCA lead into the rear +13.8V jack with the
  supply off, then read the far end with a meter. Rule 3 assumes it's live until then.
- **Is the ACC socket's shell grounded?** I didn't establish this from the schematic.
  Rule 2 assumes it is.
- **Does this drawing match this radio?** A later board revision could differ, which
  might also explain the DVI difference.
- **Has anyone blown F1003?** I found no report of it, or of a board change.

## Sources

Read directly:

- FTDX101MP/D Technical Supplement, in YWC `docs/manuals/`
- FTDX101MP/D Operation Manual, in YWC `docs/manuals/`
- [Yaesu: Important Monitor Converter Info](https://www.yaesu.com/pdf/Important%20Monitor%20Converter%20Info.pdf)

Search excerpts only, because these sites refused the fetch. Read the threads before
relying on them:

- [FTDX101D: Lost 12 volts on 6 month old](https://groups.io/g/FTDX101D/topic/lost_12_volts_on_6_month_old/98924532): rear 13.8 V lost, fuse behind the jack, Yaesu's price, the coax warning
- [FTDX101D: thread 86646604](https://groups.io/g/FTDX101D/topic/86646604) and [FTDX-101D: thread 89050753](https://groups.io/g/FTDX-101D/topic/89050753): fuse blown plugging in a rear cable, replaced with a polyfuse
- [FTDX101D: Monitor, is it](https://groups.io/g/FTDX101D/topic/monitor_is_it/93120742): Yaesu's 150 mA advice
- [FTDX101D: thread 86032600](https://groups.io/g/FTDX101D/topic/86032600) and [ftdx-10: DVI-D to VGA adapter](https://groups.io/g/ftdx-10/topic/dvi_d_to_vga_adapter/96002166): the zero ohm link
- [FT-710 external monitor dilemma](https://w0wc.com/yaesu-ft-710-external-monitor-dilemma/)

Listing, not a datasheet:

- [Matsuo KAB2402322](https://www.ozdisan.com/en/p/fuses-849/matsuo-kab2402322-1223472)
