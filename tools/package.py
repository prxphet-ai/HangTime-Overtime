"""Builds dist/HangtimeOvertime-v<version>.zip: unzip into Hangtime!/BepInEx/plugins/."""
import glob, os, re, sys, zipfile

root = sys.argv[1]
version = re.search(r'Version = "([^"]+)"', open(os.path.join(root, "src", "Plugin.cs"), encoding="utf-8").read()).group(1)
out = os.path.join(root, "dist", f"HangtimeOvertime-v{version}.zip")
os.makedirs(os.path.dirname(out), exist_ok=True)

readme = f"""Hangtime! Overtime v{version} - by averageprxphet
https://github.com/prxphet-ai/HangTime-Overtime

INSTALL
1. Install BepInEx 5 (x64, 5.4.x) into your Hangtime! folder and run the game once.
   https://github.com/BepInEx/BepInEx/releases
2. Unzip this file into Hangtime!/BepInEx/plugins/
   You should end up with BepInEx/plugins/HangtimeOvertime/HangtimeOvertime.dll
3. Start the game - the title screen reads HangTime! OVERTIME.

VERSUS (title screen, 1V1 / 1V2 / 2V2, up to 4 players on one PC)
Pick a mode at the top, then each player presses a key on their own set (or A on a gamepad) to join:
  W A S D (Space)   ARROW KEYS (Enter)   I J K L (H)   NUMPAD 8 4 5 6 (Numpad Enter)   or a gamepad
Left/right = move (and team on the join screen), up = jump / hit / ready, down = receive.
1V2 is one player plus an AI setter against two players; 2V2 is two a side.

ONLINE (everyone needs this same Overtime version; start the game from Steam)
Title screen -> ONLINE opens the lobby browser:
  - click a lobby to join it, or type a friend's lobby code and SEARCH (finds private lobbies too)
  - CREATE LOBBY: name it, PUBLIC on/off, 2-4 PCs; then INVITE (STEAM) to invite friends directly
In the lobby everyone takes a seat with their own keys (1v1, 1v2, 2v2), T to chat, the host presses START.
After a match, LOBBY takes everyone back to the lobby.
Without Steam: type the host's IP under JOIN BY IP (the host forwards UDP port 27515 on their router).

UNINSTALL: delete the BepInEx/plugins/HangtimeOvertime folder.

SETTINGS: BepInEx/config/melty.hangtime.overtime.cfg (created on first launch).
[Infinite] Difficulty = 0   -> raise it (up to 10) to make Infinite opponents tougher.

Unofficial fan mod, not affiliated with the developers of Hangtime!. MIT license (LICENSE.txt).
"""

files = [(os.path.join(root, "src", "bin", "Release", "HangtimeOvertime.dll"), "HangtimeOvertime/HangtimeOvertime.dll")]
files += [(p, "HangtimeOvertime/emblems/" + os.path.basename(p)) for p in sorted(glob.glob(os.path.join(root, "assets", "emblems", "*.png")))
          if os.path.basename(p) != "preview.png"]
files += [(p, "HangtimeOvertime/title/" + os.path.basename(p)) for p in sorted(glob.glob(os.path.join(root, "assets", "title", "*.png")))]
files += [(os.path.join(root, "LICENSE"), "HangtimeOvertime/LICENSE.txt")]

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for src, arc in files:
        z.write(src, arc)
    z.writestr("HangtimeOvertime/README.txt", readme.replace("\n", "\r\n"))
print(out, os.path.getsize(out) // 1024, "KB,", len(files) + 1, "files")
