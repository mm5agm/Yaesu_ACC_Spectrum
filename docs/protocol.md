# The protocol

How to read the board's data in any language, without the library. Everything
here comes from my own captures from my FTdx101MP (step 5 of [plan.md](plan.md)).
The library in [library/](../library/README.md) does all of this in C#.

## The port

The board sends its data on the ESP32-S3's own USB port, the socket marked USB.
It shows up as a serial port with USB ID 303A:1001. The baud rate is ignored.

- **Open it with DTR and RTS low.** A DTR/RTS sequence on that port resets the
  ESP32-S3.
- **Don't write to it** unless you mean to. The board takes one-byte commands:
  `2` or `3` sets the SPI mode, saved in flash, and restarts the board. Mode 2 is
  right for the FTdx101. `i` asks for a status text.

## Packets

Every packet is an 8-byte header and a payload:

| Byte | What |
|---|---|
| 0-1 | `Y` `A` (0x59 0x41) |
| 2 | Type: `F` (0x46) a frame, `T` (0x54) a status text |
| 3 | Flags: bit 0 set when the frame filled the board's buffer and may be cut short |
| 4-5 | Payload length, little-endian |
| 6-7 | Sequence number, little-endian. It counts every packet the board makes, including any it couldn't send, so a gap means lost packets |

The board sends a packet whole or not at all. To find your place in the stream,
look for `Y` `A` followed by a known type and a length no more than a few
thousand. After that, each packet starts where the last one ended.

### Status text

ASCII, every 5 seconds and when asked with `i`. Version 2 firmware sends, on
one line:

```
ACC pipe: version 2.0.1, SPI mode 2, frames 1234, bytes 5054464, cut short 0, not sent 0, DATA1 0, up 49 s, edges/s CS 25 CLOCK 819261 DATA2 147000 DATA1 0
```

"not sent" counts packets the board dropped because the PC wasn't reading. The
edge counts are rising edges in the last second on each input. With the radio
streaming, expect CS 25 and CLOCK about 819,200. All zeros means the radio isn't
sending. Version 1 firmware has no version and no edge counts.

### Frames

25 a second, 4096 bytes each: exactly the bytes the radio clocked out while CS
was low. Bytes 3102 to 4095 are always zero.

## The frame

All frequencies are in Hz. "BE" means 4 bytes, big-endian.

### The scope rows

| Bytes | What |
|---|---|
| 0-849 | Main's scope row, 850 points, low frequency first |
| 850-1699 | Sub's, the same |

Each byte is inverted: subtract it from 255 to get a level where higher is
stronger. Each step is about 0.42 dB, measured with the radio's attenuator
(within about 10%; see [comparison.md](comparison.md)). There's no absolute
zero: the radio's scope LEVEL shifts the rows by about 2.2 steps per dB, and
at LEVEL 0 or below the floor starts to sit on 0 or 1.

### Where each row sits

| Main | Sub | What |
|---|---|---|
| 3068-3071 | 3094-3097 | VFO (VFO A for Main, VFO B for Sub), BE |
| 3062 | 3084 | Span and placement: see below |
| 3076-3079 | 3098-3101 | Left edge of the span, BE. Right in Cursor and Fix; not kept up to date in Center |
| 3036-3039 | 3040-3043 | The passband marker: VFO + 1500 Hz + IF shift, BE. Also at 3064-3067 and 3086-3089 |
| 2961-2964 | 3009-3012 | The VFO again, BCD in 10 Hz steps: `14 07 50 00` is 14.075.00 MHz |
| 2917 | 2918 | Scope mode, the radio's SS P3 character: 0-2 are 3DSS, 3 Center, 6 Cursor, 9 Fix. Sub has shown 5 in Center too |

The span byte:

- **Low 4 bits:** the span. 0 = 1k, 1 = 2k, 2 = 5k, 3 = 10k, 4 = 20k, 5 = 50k,
  6 = 100k, 7 = 200k, 8 = 500k, 9 = 1M.
- **Top 2 bits:** the placement. 1 = Center, 0 = Cursor, 2 = Fix.

The radio keeps a separate span for Fix.

**The centre** is the VFO in Center, and the left edge plus half the span in
Cursor and Fix.

**The 850 points cover more than the span.** The span is the middle 791 points,
with about 30 beyond each edge. The centre is on point 425. So point `p` is at

```
centre + (p - 425) * span / 791
```

I measured this at every span from 1k to 1M by tuning a known distance and
seeing how far the trace moved: 788 to 795 points, 791 on average.

**3DSS** changes only the scope mode byte. The rows are the same; it's only how
the radio draws them.

### The audio scope

The AF-FFT and oscilloscope the radio can show for each receiver.

| Bytes | What |
|---|---|
| 1700 | Always `01` |
| 1701-1899 | Main's AF-FFT: 199 points, 0 to about 3.8 kHz, 19-20 Hz a point. Inverted like the scope rows. A 1 kHz tone shows on point 49, 2 kHz on 101 |
| 1900-2299 | Main's oscilloscope: 200 columns, each a max byte then a min byte, centred on 128. At sweep time t0 a column is 50 us, 10 ms across |
| 2300-2499 | Sub's AF-FFT, 200 points. **Not tested on its own:** bytes 2300 and 2301 move with the AF-FFT attenuator like the rest, so I've taken them as part of it |
| 2500-2899 | Sub's oscilloscope, the same as Main's. Not tested on its own |
| 3056 | Main's settings. Bits 0-2: oscilloscope time, t0 to t5. Bits 3-4: oscilloscope attenuator, a0 to a2. Bits 5-6: AF-FFT attenuator, 0 to 2 |

### Other bytes

| Bytes | What |
|---|---|
| 2916 | The Band button on the radio's scope: 0 MAIN, 1 SUB |
| 2926-2927 | Follows Main's span: about 4,740,000 / span, capped at 395. Not known |
| 2930-2931 | The same for Sub |

Bytes 2900-3101 not listed here stay the same frame to frame and haven't been
worked out.

## Not known

- What the frames do while the radio transmits. Nothing here transmits.
- The exact dB scale, and whether it is quite straight near the bottom: the
  floor moved a little less than a strong carrier when the attenuator was
  switched.
- Whether the FTdx101D sends the same. It should, but I've only got the MP.
