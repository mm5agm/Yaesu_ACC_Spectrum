# PlatformIO extra script: after each build, merge the bootloader, partition
# table, boot_app0 and the app into one file that flashes at address 0, for
# the release. The offsets and flash settings come from PlatformIO itself.
Import("env")
import re

def merge(source, target, env):
    build = env.subst("$BUILD_DIR")
    src = open(env.subst("$PROJECT_SRC_DIR/main.cpp"), encoding="utf-8").read()
    version = re.search(r'FW_VERSION = "([^"]+)"', src).group(1)
    out = env.subst(f"$PROJECT_DIR/release/yaesu-acc-firmware-{version}-merged.bin")
    images = []
    for offset, path in env.get("FLASH_EXTRA_IMAGES", []):
        images += [offset, env.subst(path)]
    images += [env.subst("$ESP32_APP_OFFSET"), f"{build}/firmware.bin"]
    env.Execute(env.VerboseAction(" ".join([
        '"$PYTHONEXE"', '"$OBJCOPY"', "--chip", "esp32s3", "merge_bin",
        "--flash_mode", env.subst("${__get_board_flash_mode(__env__)}"),
        "--flash_freq", env.subst("${__get_board_f_flash(__env__)}"),
        "--flash_size", env.BoardConfig().get("upload.flash_size", "16MB"),
        "-o", f'"{out}"'] + [f'"{i}"' if "/" in i or "\\" in i else i for i in images]),
        f"Merged image: {out}"))

env.AddPostAction("$BUILD_DIR/firmware.bin", merge)
