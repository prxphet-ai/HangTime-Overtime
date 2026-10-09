"""Preflight + generate + build (+ deploy) in one step.

usage: python tools/build.py [--deploy]
Needs the .NET SDK (dotnet) and the game install (see src/HangtimeOvertime.csproj GameDir).
"""
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    r = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "sheets.py"), "gen"], capture_output=True, text=True)
    print("\n".join(l for l in r.stdout.splitlines() if not l.startswith("  TODO")))
    if "wrote" not in r.stdout:
        sys.exit("sheets: not generated")
    env = dict(os.environ)
    local = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "dotnet")
    if os.path.isdir(local):
        env["DOTNET_ROOT"] = local
        env["PATH"] = local + os.pathsep + env["PATH"]
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    exe = os.path.join(local, "dotnet.exe") if os.path.isfile(os.path.join(local, "dotnet.exe")) else "dotnet"
    cmd = [exe, "build", os.path.join(ROOT, "src"), "-c", "Release", "-nologo", "-v", "q"]
    if "--deploy" in sys.argv:
        cmd.append("-p:Deploy=true")
    b = subprocess.run(cmd, capture_output=True, text=True, env=env)
    errs = sorted({re.sub(r"^.*[\\/]src[\\/]", "", l).split(" [")[0] for l in b.stdout.splitlines() if ": error " in l})
    warns = sorted({re.sub(r"^.*[\\/]src[\\/]", "", l).split(" [")[0] for l in b.stdout.splitlines() if ": warning " in l})
    for e in errs:
        print("ERROR " + e)
    for w in warns[:10]:
        print("warn  " + w)
    if b.returncode != 0:
        if not errs:
            print((b.stdout + b.stderr)[-3000:])
        sys.exit("build failed")
    print("build ok" + (" + deployed" if "--deploy" in sys.argv else ""))


if __name__ == "__main__":
    main()
