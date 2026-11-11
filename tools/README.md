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
  second, frame lengths and the `FF 01 EE 01` trailer. It saves the raw stream in
  `captures/`. `--mode 2` or `--mode 3` switches the firmware's SPI mode first.
  See step 4 of [docs/plan.md](../docs/plan.md).
