"""Lay the UI gallery (UiGalleryTests -> dungeongame/Logs/ui-gallery/NN-<name>.png, each 1920x1080) out as labelled contact sheets and index.md.

Run with: python -I Tools/AssetGen/ui_gallery_sheet.py   (needs Pillow)
Writes index.md, sheet-menus.png and sheet-inround.png beside the screenshots.
"""
import os
from PIL import Image, ImageDraw, ImageFont

DIR = r"D:\unity dungeon game\dungeongame\Logs\ui-gallery"
FONT_DIR = r"C:\Windows\Fonts"

# stem -> (label, what it shows)
SHOTS = {
    "01-main-no-profile": ("Main menu, first launch", "No profile yet: 'Create your first character', the welcome card. Stash, Vendor and Crafting are greyed out."),
    "02-create-character": ("Create character", "Name field and the six classes; three are locked behind account level. Description of the chosen class and its specs."),
    "03-main-with-character": ("Main menu, with a character", "Play, Characters, Stash, Vendor, Crafting Bench, Settings, Quit; the account card shows level, XP, silver, stash size, potions and the worn kit."),
    "04-characters": ("Characters", "The account's three characters (Warden, Shade, Elementalist) with spec and gear score; the chosen one is highlighted."),
    "05-prepare": ("Prepare for the descent", "Spec cards with the full ability list, enemy bot teams 1 to 3, the tier-wall line, Enter the dungeon. A starter-kit character, so the wall admits it."),
    "06-prepare-tier-wall": ("Prepare, over the tier wall", "The same screen for a character whose gear score (113) is above what Tier I admits (50): the line turns red."),
    "07-stash-item-selected": ("Stash and equipment, stash item chosen", "Paper doll on the left (worn gear, two empty jewellery slots), the 4x5 stash grid, the details card with the gear-score change if worn, Equip."),
    "08-stash-worn-selected": ("Stash and equipment, worn item chosen", "A worn item selected: its details card and Unequip."),
    "09-vendor": ("Vendor", "Buy potions, antidotes and salvage kits; sell or salvage stash items one by one (with the salvage yield), paging."),
    "10-crafting": ("Crafting bench", "Slot, base, item level with costs, guaranteed affix families and Force Rare, the preview with rarity odds and cost, the materials card."),
    "11-settings-audio": ("Settings, Audio", "Master, music and effects sliders."),
    "12-settings-graphics": ("Settings, Graphics", "Quality preset, display mode, resolution, vsync, frame cap, render scale, shadows."),
    "13-settings-controls": ("Settings, Controls", "Key bindings for movement, abilities and actions; gamepad summary."),
    "14-online": ("Play online", "Host a round (port) or join (address), with the notes on UPnP and dedicated servers."),
    "15-lobby": ("Lobby (client)", "Joined a dedicated server on 127.0.0.1: teams with seats, the status line, 'Waiting for the host', Leave."),
    "16-loading": ("Loading", "The 'Entering the dungeon' screen between the menu and the round."),
    "20-hud-countdown": ("HUD, spawn countdown", "Round clock, compass, mini-map, health and resource bars, five ability slots, two pocket slots, 'The doors open in 10'."),
    "21-hud-exploring": ("HUD, exploring", "The calm state: nothing open, the world around the player."),
    "22-hud-fight": ("HUD, in a fight", "Abilities cast, cooldown numbers on the slots, resource spent, mobs on top of the player."),
    "23-hud-downed": ("HUD, downed", "The DOWNED banner and the downed recap card with the last hits and Give up."),
    "24-loot-chest": ("Loot panel, chest", "The panel that opens at an opened chest: rarity stripe, base, item level, weight, key affixes, Take."),
    "25-loot-tooltip-comparison": ("Item tooltip, equip comparison", "Hovering a loot row: every affix with its tier, durability, gear score, and 'If worn: gear score +N (replaces ...)'."),
    "26-inventory-salvage": ("Inventory with Salvage Kit", "I opens the inventory: load against capacity, worn and carried rows with Salvage and Drop, pockets."),
    "27-inventory-after-salvage": ("Inventory after a salvage", "One item broken down: the row is gone, the kit count and the load dropped. No confirmation or result message exists."),
    "28-roll-prompt": ("Group roll prompt", "A teammate in range: Need, Greed, Pass with a countdown bar, beside the loot panel."),
    "29-roll-answered": ("Group roll, answered", "After choosing Pass: 'You chose Pass, 1 of 2 answered'."),
    "30-shrine": ("Shrine panel", "At an altar with two souls: Arm ultimate, Resurrect (no fallen teammate), the souls count."),
    "31-extract-channel": ("Extracting", "Standing in the extract gate holding Space: the 'Extracting...' channel bar."),
    "32-summary-extracted": ("Round summary, extracted", "The round numbers on the left, the profile settlement on the right (gained, kept, lost), XP bar, Return to the menu."),
    "33-pause": ("Pause menu", "Resume, Settings, Leave round, with the death warning."),
    "34-death-recap": ("Death recap", "YOU DIED card with how, the last hits, Continue."),
    "35-summary-died": ("Round summary, died", "Everything lost: items and consumables, the naked-character note, Return to the menu."),
}

SHEETS = {
    "menus": ("Menus and screens between rounds", [k for k in SHOTS if int(k[:2]) < 20]),
    "inround": ("In the round", [k for k in SHOTS if int(k[:2]) >= 20]),
}

THUMB_W, THUMB_H, LABEL_H, PAD, TITLE_H, COLS = 640, 360, 74, 18, 84, 3
BG, CARD, GOLD, TEXT, DIM = (18, 18, 22), (28, 28, 34), (218, 176, 96), (232, 226, 214), (160, 154, 144)


def font(names, size):
    for n in names:
        p = os.path.join(FONT_DIR, n)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def wrap(draw, text, f, width):
    words, lines, line = text.split(), [], ""
    for w in words:
        t = (line + " " + w).strip()
        if draw.textlength(t, font=f) <= width:
            line = t
        else:
            lines.append(line)
            line = w
    if line:
        lines.append(line)
    return lines


def sheet(key, title, stems):
    rows = (len(stems) + COLS - 1) // COLS
    W = PAD + COLS * (THUMB_W + PAD)
    H = TITLE_H + PAD + rows * (THUMB_H + LABEL_H + PAD)
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    f_title = font(["georgiab.ttf", "georgia.ttf"], 40)
    f_name = font(["georgiab.ttf", "georgia.ttf"], 22)
    f_role = font(["segoeui.ttf", "arial.ttf"], 15)
    d.text((PAD + 6, 22), title.upper(), fill=GOLD, font=f_title)
    for i, stem in enumerate(stems):
        label, what = SHOTS[stem]
        x = PAD + (i % COLS) * (THUMB_W + PAD)
        y = TITLE_H + PAD + (i // COLS) * (THUMB_H + LABEL_H + PAD)
        d.rectangle([x, y, x + THUMB_W, y + THUMB_H + LABEL_H], fill=CARD)
        path = os.path.join(DIR, stem + ".png")
        if os.path.exists(path):
            p = Image.open(path).convert("RGB").resize((THUMB_W, THUMB_H), Image.LANCZOS)
            img.paste(p, (x, y))
        else:
            d.text((x + 20, y + 20), "missing screenshot", fill=DIM, font=f_role)
        d.line([x + 12, y + THUMB_H + 6, x + THUMB_W - 12, y + THUMB_H + 6], fill=GOLD, width=1)
        d.text((x + 12, y + THUMB_H + 10), stem[:2] + "  " + label, fill=TEXT, font=f_name)
        for j, line in enumerate(wrap(d, what, f_role, THUMB_W - 24)[:2]):
            d.text((x + 12, y + THUMB_H + 40 + j * 18), line, fill=DIM, font=f_role)
    out = os.path.join(DIR, f"sheet-{key}.png")
    img.save(out, optimize=True)
    print("wrote", out, img.size)


def index():
    lines = ["# UI gallery", "",
             "Every screen and modal, captured by `UiGalleryTests` (category `Gallery`) at exactly 1920x1080 with text rasterised at that size.",
             "Regenerate: `bash Tools/Build/unity-batch.sh tests PlayMode UiGalleryTests`, then `python -I Tools/AssetGen/ui_gallery_sheet.py`.", "",
             "Contact sheets: `sheet-menus.png`, `sheet-inround.png`.", ""]
    for key, (title, stems) in SHEETS.items():
        lines += [f"## {title}", "", "| File | Screen | What it shows |", "|------|--------|---------------|"]
        for stem in stems:
            label, what = SHOTS[stem]
            have = os.path.exists(os.path.join(DIR, stem + ".png"))
            lines.append(f"| `{stem}.png`{'' if have else ' (missing)'} | {label} | {what} |")
        lines.append("")
    extra = sorted(f for f in os.listdir(DIR) if f.endswith(".png") and not f.startswith("sheet-") and f[:-4] not in SHOTS)
    if extra:
        lines += ["## Not described above", ""] + [f"* `{f}`" for f in extra] + [""]
    path = os.path.join(DIR, "index.md")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))
    print("wrote", path)


if __name__ == "__main__":
    for k, (t, stems) in SHEETS.items():
        sheet(k, t, stems)
    index()
