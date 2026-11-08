# -*- coding: utf-8 -*-
"""Apply the Fault counter IL patch to MajdataPlay 2.0.3 (Windows / Mono).

This is the command-line counterpart of the sensor-color GUI. It performs the
same safety checks in a fixed order and refuses to write on any failure:

    inspect source -> backup -> generate -> inspect generated -> replace
    -> inspect installed

Usage:
    python apply_fault.py "D:\\Games\\MajdataPlay_Build-LATEST"
    python apply_fault.py --game "D:\\Games\\MajdataPlay_Build-LATEST" --check-only
    python apply_fault.py --engine "C:\\path\\to\\patcher.dll" "D:\\Games\\..."

Only Assembly-CSharp.dll is replaced. Charts, skins, score databases and
settings.json are never touched; set Game.BGInfo to "Fault" yourself.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


def find_dotnet():
    p = shutil.which("dotnet")
    if p:
        return p
    for candidate in (r"C:\Program Files\dotnet\dotnet.exe",
                      r"C:\Program Files (x86)\dotnet\dotnet.exe"):
        if os.path.isfile(candidate):
            return candidate
    return None


def game_running():
    if os.name != "nt":
        return False
    try:
        p = subprocess.run(["tasklist", "/FI", "IMAGENAME eq MajdataPlay.exe", "/NH"],
                           capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=20, creationflags=CREATE_NO_WINDOW)
        return "MajdataPlay.exe" in p.stdout
    except Exception:
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("game", nargs="?", help="directory containing MajdataPlay.exe")
    ap.add_argument("--game", dest="game_opt", help="same as the positional argument")
    ap.add_argument("--engine", default=os.path.join(TOOL_DIR, "engine", "patcher.dll"))
    ap.add_argument("--check-only", action="store_true",
                    help="only run --inspect, do not write anything")
    args = ap.parse_args()

    game_dir = (args.game_opt or args.game
                or input("MajdataPlay install directory: ").strip().strip('"'))
    if not os.path.isdir(game_dir):
        sys.exit("Game directory not found: %s" % game_dir)
    target = os.path.join(game_dir, "MajdataPlay_Data", "Managed", "Assembly-CSharp.dll")
    if not os.path.isfile(target):
        sys.exit("Managed assembly not found: %s\nWrong game directory, or unsupported build." % target)
    if not os.path.isfile(args.engine):
        sys.exit("Patch engine missing: %s\nRun:  dotnet build engine-src/patcher.csproj -c Release -o engine" % args.engine)
    dotnet = find_dotnet()
    if not dotnet:
        sys.exit("dotnet runtime not found (.NET 10 desktop runtime is required).")

    def engine(*argv):
        p = subprocess.run([dotnet, args.engine] + list(argv),
                           capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=180, creationflags=CREATE_NO_WINDOW)
        if p.returncode != 0:
            sys.exit("Engine failed (%d): %s" % (p.returncode,
                     (p.stderr or p.stdout or "").strip()))
        return p.stdout.strip().splitlines()[-1]

    def inspect(path):
        try:
            return json.loads(engine(path, "--inspect"))
        except (ValueError, IndexError):
            sys.exit("Incompatible assembly: --inspect did not return valid JSON.\n"
                     "This patch only supports MajdataPlay 2.0.3 20260917-637bba0 (Mono).")

    before = inspect(target)
    print("source inspect : %s" % before)
    if args.check_only:
        print("check-only: no files were written.")
        return

    if game_running():
        sys.exit("MajdataPlay.exe is running. Close the game completely before patching.")

    backups = os.path.join(TOOL_DIR, "backups")
    output = os.path.join(TOOL_DIR, "output")
    os.makedirs(backups, exist_ok=True)
    os.makedirs(output, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup = os.path.join(backups, "Assembly-CSharp_%s.dll" % stamp)
    shutil.copy2(target, backup)
    print("backup         : %s" % backup)

    pending = os.path.join(output, "Assembly-CSharp.pending.dll")
    print("generate       : %s" % engine(target, pending))
    staged = inspect(pending)
    print("staged inspect : %s" % staged)
    if not staged.get("hasFault"):
        sys.exit("Verification failed: generated assembly has no Fault counter. Nothing was replaced.")

    shutil.copy2(pending, target)
    os.remove(pending)
    final = inspect(target)
    print("final inspect  : %s" % final)
    if not final.get("hasFault"):
        sys.exit("Verification failed after replacement. Restore from: %s" % backup)

    print("\nDONE - Fault counter installed.")
    print("Next: edit settings.json -> Game.BGInfo = \"Fault\" (or SecondaryBGInfo),")
    print("      and keep Game.EnableJudgeTimingGauge = false when using SecondaryBGInfo.")
    print("      Launch MajdataPlay.exe manually, then enter a chart to verify.")


if __name__ == "__main__":
    main()
