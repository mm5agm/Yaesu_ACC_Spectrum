# Comparison tools

These made the figures in [docs/comparison.md](../../docs/comparison.md). They
record the ACC scope and the two SDRs at the same moment, then measure them the
same way. Everything is receive only.

- `compare-capture.csproj`, `Program.cs`: records for a given number of seconds.
  The ACC rows come straight from the board, through the YaesuAccScope library.
  The SDR frames come from a running YWC on `localhost:8080`, exactly as YWC
  sends them to its pages. YWC must not be using the ACC board itself.
  It writes `<prefix>-acc.bin`, `<prefix>-sdrA.bin`, `<prefix>-sdrB.bin` and
  `<prefix>-t0.txt`.
- `toggle.py`: runs a capture while switching one receive setting on Main
  (the attenuator, or a scope setting such as LEVEL) every second or two, and
  logs when. `seg.py` splits the capture at the switches. Switching back and
  forth cancels slow fading on the band.
- `an.py <prefix> <span Hz>`: floor spread, jitter, strongest signal and clarity
  for each source, with the ACC rows replayed at each Refine step.
- `sig.py <prefix> <span Hz> <centre Hz>`: frequency offset between the sources,
  and how far weak signals stand out of the floor on each, signal by signal.
- `plot.py`: the pictures in [docs/images/compare/](../../docs/images/compare/).

Python needs numpy, and matplotlib for `plot.py`. Set `ACC_COMPARE_DATA` to the
folder for the recordings (default `data`) and `ACC_PORT` to the board's port.
The recordings aren't in the repo; a minute of all four is about 10 MB.
