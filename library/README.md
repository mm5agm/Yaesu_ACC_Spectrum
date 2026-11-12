# YaesuAccScope

A .NET library that reads a Yaesu FTdx101's own scope data from its ACC socket,
through an ESP32-S3 running the firmware in this repo. It gives you, 25 times a
second, the Main and Sub spectrum with their frequencies, and the audio scope.
All of it comes from the radio's own data, with nothing from CAT.

It's .NET Standard 2.0, so it works in .NET 8 and later programs and in .NET
Framework 4.6.1 and later ones.

**Licence: LGPL-3.0.** Your program can use the library without being GPL
itself, closed-source included. If you change the library, you share those changes
under the LGPL. See [COPYING.LESSER](COPYING.LESSER) and [COPYING](COPYING). The
firmware and tools in the rest of this repo stay GPL-3.0.

Status: first version, 0.1.0. It isn't on nuget.org yet, so reference the
project, or build the DLL from `src/YaesuAccScope`.

## Use

```csharp
using YaesuAccScope;

string? port = AccBoardFinder.Find();          // e.g. "COM6", or null
if (port == null) return;

using var reader = new AccSerialReader(port);
reader.FrameReceived += (_, e) =>
{
    ReceiverScope main = e.Frame.Main;          // VFO A
    if (!main.IsValid) return;
    byte[] levels = main.Levels;                // 850 points, higher is stronger
    double left = main.FrequencyAt(0);          // Hz
    double right = main.FrequencyAt(849);
    // draw levels from left to right...
};
reader.StatusReceived += (_, e) => Console.WriteLine(e.Status.Text);
reader.Failed += (_, ex) => Console.WriteLine("Lost the board: " + ex.Message);
reader.Start();
```

### Raw or refined

`Levels` is the raw row, exactly as the radio sent it. Each row is a separate
snapshot, so its noise floor is rough. For a trace a person looks at, use a
`ScopeRefiner` for each receiver. It averages each point over the last few
rows, follows the trace when the VFO moves, and starts again on a new span:

```csharp
var mainRefiner = new ScopeRefiner();           // one per receiver

reader.FrameReceived += (_, e) =>
{
    float[] trace = mainRefiner.Add(e.Frame.Main);   // 850 points, same scale as Levels
    // draw trace...
};
```

Measured on 20m FT8 at a 20 kHz span, the refined trace's floor is about 2.4
times smoother than the raw rows, about level with an SDRplay RSP on the radio's
IF output. It still comes 25 times a second. Use `Levels` for anything that
measures.

The events come on the reader's own thread. Hand the work to your UI thread if
you need to, and don't block: the frames come every 40 ms.

## What it never does

- **It never writes to the board.** A byte sent to the board can change its SPI
  mode, so a program using the library can't do that.
- **It opens the port with DTR and RTS low.** A DTR/RTS sequence on the
  ESP32-S3's own USB port resets the chip.
- **`AccBoardFinder` only opens ports with Espressif's USB vendor ID (303A).** It
  finds them without opening anything: from the registry on Windows and from
  /sys on Linux. On macOS it tries the /dev/cu.usbmodem ports. So it never opens
  a radio's CAT port, where DTR or RTS can key the transmitter.
  `AccSerialReader` opens whatever port you give it, so pass it one that
  `AccBoardFinder` found.

## What's in it

| Class | What it does |
|---|---|
| `AccBoardFinder` | Lists Espressif ports, and listens on each for the board's packets: a status text or a frame. Up to 6 s a port, since a status text comes every 5 s |
| `AccSerialReader` | Reads the port on its own thread and raises `FrameReceived`, `StatusReceived` and `Failed`. Counts frames and lost packets |
| `AccPacketReader` | Splits the board's byte stream into packets, for when you read the bytes yourself or from a file. Finds its place again after bad bytes, and counts lost packets from the sequence numbers |
| `AccFrame` | One frame decoded: `Main` and `Sub` (`ReceiverScope`), `MainAudio` and `SubAudio` (`AudioScope`), the Band button, and the raw bytes |
| `ReceiverScope` | The 850 levels the right way up, the VFO, the span, Center/Cursor/Fix, the centre and edges in Hz, the frequency of each point, 3DSS, and the passband marker |
| `ScopeRefiner` | Turns one receiver's raw rows into a refined trace for eyes: each point averaged over the last few rows, moved with the VFO in Center, and started again on a new span |
| `AudioScope` | The AF-FFT and the oscilloscope, and for Main the oscilloscope time and both attenuators |
| `AccStatus` | The board's status text: firmware version, SPI mode, counts, and edges a second on each input for checking the wiring |

## How sure each part is

Tested on my own captures from my FTdx101MP, in `captures/`:

- The packets, on a raw capture from the board, including a gap of 35 packets
  that the board's own status counted too.
- Main and Sub: the VFO, the span at every span from 1k to 1M, and Center,
  Cursor and Fix.
- Main's AF-FFT, with a 1 kHz and a 2 kHz tone, and Main's audio settings.

Not tested yet:

- **Sub's audio scope.** I've placed it from where the bytes change (AF-FFT in
  bytes 2300-2499, oscilloscope in 2500-2899), not from a tone in Sub's
  passband.
- **The dB scale of the levels.** It's the radio's display data after its own
  scaling.
- **Anything while the radio transmits.** Nothing here transmits.
- **The FTdx101D.** It should be the same, but I've only got the MP.

The frame layout is in [docs/protocol.md](../docs/protocol.md), so you can write a
reader in any language without this library.

## Build and test

```
dotnet test library/YaesuAccScope.slnx
```

The tests need the .NET 10 SDK, and read the captures in `captures/`.
