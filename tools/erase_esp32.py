"""Erase the ESP32-S3's flash, so it runs no program and every GPIO is an input.

You don't pick a COM port. Start with the board unplugged and run:

    python tools/erase_esp32.py

The script notes the serial ports already there, then waits for you to plug the
board's UART socket, marked COM on some boards (not the one marked USB), into
the PC. The one port that appears is the board, whatever else is plugged in,
even another device with the same USB chip. Then it:

1. refuses COM3 and COM4, the radio's ports on my PC, and any port whose USB
   chip isn't WCH's (0x1A86, the CH343 and CH340) or Espressif's own (0x303A).
   That keeps esptool off radios: asking a chip what it is pulses DTR and RTS,
   and on a radio's port those can key the transmitter. The Yaesu's CP2105 is
   Silicon Labs (0x10C4) and Icoms are 0x0C26, so neither is ever touched;
2. asks the chip what it is with esptool, and stops unless it's an ESP32-S3. A
   device that isn't an ESP32 can't answer, so it's never erased;
3. shows the chip, its flash and PSRAM and its MAC address, and asks you to type
   yes;
4. runs esptool's erase-flash on that port.

Other ways to run it:

    python tools/erase_esp32.py --list         # only list the serial ports
    python tools/erase_esp32.py --port COM5    # name the port yourself

Install esptool first with: python -m pip install -r tools/requirements.txt
"""

import argparse
import subprocess
import sys
import time

try:
    from serial.tools import list_ports
except ImportError:
    sys.exit("pyserial isn't installed. Run: python -m pip install -r tools/requirements.txt")

ESP32_USB_VENDORS = {0x1A86: "WCH", 0x303A: "Espressif"}
CH343 = (0x1A86, 0x55D3)  # the DevKitC-1 N16R8's UART chip
RADIO_PORTS = {"COM3", "COM4"}
WAIT_SECONDS = 120


def ports():
    return {p.device: p for p in list_ports.comports()}


def describe(port):
    ids = f"{port.vid:04X}:{port.pid:04X}" if port.vid is not None else "no USB ID"
    return f"{port.device}  {ids}  {port.description}"


def wait_for_new_port():
    before = ports()
    print("Serial ports before the board is plugged in:")
    for name in sorted(before):
        print("  " + describe(before[name]))
    print(f"\nPlug the board's UART socket (marked COM on some boards) into the PC now. Waiting up to "
          f"{WAIT_SECONDS} s (Ctrl+C to stop).")
    deadline = time.monotonic() + WAIT_SECONDS
    while time.monotonic() < deadline:
        time.sleep(0.5)
        new = [p for name, p in ports().items() if name not in before]
        if len(new) > 1:
            sys.exit("More than one new port appeared. Plug in only the board.")
        if new:
            time.sleep(1.0)  # let Windows finish setting the port up
            return new[0]
    sys.exit("No new port appeared. If the board was already plugged in, unplug "
             "it, run this again, and plug it in when asked.")


def check_port(port):
    print(f"\nPort: {describe(port)}")
    if port.device.upper() in RADIO_PORTS:
        sys.exit(f"{port.device} is one of the radio's ports on this PC. Stopping.")
    if port.vid not in ESP32_USB_VENDORS:
        sys.exit(f"{port.device}'s USB chip isn't WCH or Espressif, so it may be a "
                 "radio or something else. Stopping without touching it.")
    if (port.vid, port.pid) != CH343:
        print("That isn't the CH343 chip of the board this repository uses "
              "(1A86:55D3). It may still be an ESP32; the chip check comes next.")


def read_chip(device):
    """Ask the chip what it is. Returns esptool's output, or stops."""
    command = [sys.executable, "-m", "esptool", "--port", device, "flash-id"]
    print("\nAsking the chip what it is: " + " ".join(command[1:]))
    result = subprocess.run(command, capture_output=True, text=True)
    output = result.stdout + result.stderr
    if result.returncode:
        print(output)
        sys.exit("esptool couldn't talk to it, so it's not erased. If it's the "
                 "ESP32, hold BOOT, press and release RST, release BOOT, and run "
                 "this again.")
    if "ESP32-S3" not in output:
        print(output)
        sys.exit("It answered, but it isn't an ESP32-S3. Not erased.")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--list", action="store_true", help="only list the serial ports")
    parser.add_argument("--port", help="the board's port, e.g. COM5, instead of "
                                       "finding it by plugging it in")
    parser.add_argument("--yes", action="store_true", help="don't ask before erasing")
    args = parser.parse_args()

    if args.list:
        for name, port in sorted(ports().items()):
            print(describe(port))
        return

    if args.port:
        port = ports().get(args.port.upper()) or ports().get(args.port)
        if not port:
            sys.exit(f"There's no {args.port}. Use --list to see the ports.")
    else:
        port = wait_for_new_port()
    check_port(port)

    output = read_chip(port.device)
    wanted = ("Chip type", "Chip is", "Features", "MAC", "Detected flash size")
    print("It's an ESP32-S3:")
    for line in output.splitlines():
        if line.strip().startswith(wanted):
            print("  " + line.strip())

    print(f"\nErasing wipes all of the flash on {port.device}.")
    if not args.yes:
        try:
            answer = input("Erase it? Type yes: ")
        except EOFError:  # no keyboard, e.g. run from another program
            answer = ""
        if answer.strip().lower() != "yes":
            sys.exit("Not erased.")

    command = [sys.executable, "-m", "esptool", "--chip", "esp32s3",
               "--port", port.device, "erase-flash"]
    print(" ".join(command[1:]))
    result = subprocess.run(command)
    if result.returncode:
        print("\nIf it couldn't connect, hold BOOT, press and release RST, release "
              "BOOT, and run this again.")
    else:
        print("\nErased. Press RST on the board. Its LED stays off, which is right.")
    sys.exit(result.returncode)


if __name__ == "__main__":
    main()
