// ACC Firmware Flasher: writes the ACC board's firmware to its ESP32-S3.
// Copyright (C) 2026 Colin Campbell MM5AGM
// SPDX-License-Identifier: GPL-3.0-only (see LICENSE in the top folder)

namespace AccFirmwareFlasher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FlashForm());
    }
}
