Capture and analysis scripts. These never open COM3 or COM4.

Install what they need once, from the top folder of the repository:

```
python -m pip install -r tools/requirements.txt
```

- `erase_esp32.py`: erases the ESP32-S3's flash with esptool, finding the board's
  port from its CH343 chip's USB ID. See [Erasing the ESP32](../README.md#erasing-the-esp32).

- `scope_shot.py`: saves a screenshot, and optionally the samples, from the
  Siglent scope over its LAN port. See [Oscilloscope traces](../README.md#oscilloscope-traces).

- `check_frames.py`: reads the frames the firmware in `firmware/` sends over the
  board's native USB (the socket marked USB, 303A:1001), and reports frames a
  second, frame lengths and where the data ends in each frame (byte 3102, the same
  in every frame, when the SPI mode is right). It saves the raw stream in
  `captures/`. `--mode 2` or `--mode 3` switches the firmware's SPI mode first.
  See step 4 of [docs/plan.md](../docs/plan.md).

- `live_view.py`: shows the scope live from the same port: a trace and a
  waterfall for Main (bytes 0-849) and Sub (bytes 850-1699), each byte inverted.
  The frequency scale comes from each receiver's VFO and span in the frame
  (step 5 of the plan), for Center, Cursor and Fix. `--file
  captures/...bin` plays a saved capture instead. `--dump captures/latest.bin`
  writes the last second of frames there every second, for decoding while the
  viewer stays open.
  Space pauses, A sets the colours again, Esc quits.
