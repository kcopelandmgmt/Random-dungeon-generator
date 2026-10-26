"""Lay the studio portraits (PortraitTests -> dungeongame/Logs/portraits/<group>-<slug>.png) out as labelled sheets.

Run with: python -I Tools/AssetGen/portrait_sheets.py   (needs Pillow)
Writes sheet-classes.png, sheet-bosses.png and sheet-mobs.png beside the portraits.
"""
import os
from PIL import Image, ImageDraw, ImageFont

DIR = r"D:\unity dungeon game\dungeongame\Logs\portraits"
FONT_DIR = r"C:\Windows\Fonts"

SHEETS = {
    "classes": ("The six classes", 3, [
        ("class", "warden", "Warden", "Tank. Holds the doorway. Sentinel or Breaker."),
        ("class", "reaver", "Reaver", "Melee bruiser. Berserker or Duelist."),
        ("class", "marksman", "Marksman", "Ranged. Sharpshooter or Volleyer."),
        ("class", "elementalist", "Elementalist", "Caster. Pyromancy, Cryomancy or Stormcalling."),
        ("class", "mender", "Mender", "Healer. Warder or Plaguebearer."),
        ("class", "shade", "Shade", "Assassin. Assassin or Trapper."),
    ]),
    "bosses": ("The nine bosses", 3, [
        ("boss", "iron_juggernaut", "Iron Juggernaut", "Crushes one target, tramples the furthest."),
        ("boss", "ashen_colossus", "Ashen Colossus", "Ground Slam leaves burning lava."),
        ("boss", "glasswright", "Glasswright", "Splits the room with crystal walls."),
        ("boss", "plague_herald", "Plague Herald", "Drops poison clouds under you."),
        ("boss", "hive_matron", "Hive Matron", "Spawns a brood that hunts her mark."),
        ("boss", "bone_choir", "Bone Choir", "Raises the dead; interrupt its dirge."),
        ("boss", "hound_of_vesh", "Hound of Vesh", "A chaser. Its howl slows you."),
        ("boss", "lantern_wraith", "Lantern Wraith", "Snuffs your abilities for a moment."),
        ("boss", "the_sovereign_below", "The Sovereign Below", "The final boss. Its death opens the way out."),
    ]),
    "mobs": ("Dungeon mobs", 4, [
        ("mob", "bandit", "Bandit", "Loots the dead."),
        ("mob", "hollow_knight", "Hollow Knight", "Holds doorways. Also the 10:00 elite, larger."),
        ("mob", "frostbound", "Frostbound", "Ice-bound undead warrior."),
        ("mob", "storm_acolyte", "Storm Acolyte", "Ranged caster; keeps its distance."),
        ("mob", "gravewalker", "Gravewalker", "Rises once unless burned."),
        ("mob", "skitterer", "Skitterer", "Fast, comes in swarms."),
        ("mob", "ember_wisp", "Ember Wisp", "Floats over hazards."),
        ("mob", "carrion_crawler", "Carrion Crawler", "Eats corpses before you can loot them."),
    ]),
}

CELL_W, CELL_H, LABEL_H, PAD, TITLE_H = 384, 512, 86, 18, 84
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


def sheet(key, title, cols, entries):
    rows = (len(entries) + cols - 1) // cols
    W = PAD + cols * (CELL_W + PAD)
    H = TITLE_H + PAD + rows * (CELL_H + LABEL_H + PAD)
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    f_title = font(["georgiab.ttf", "georgia.ttf"], 40)
    f_name = font(["georgiab.ttf", "georgia.ttf"], 24)
    f_role = font(["segoeui.ttf", "arial.ttf"], 16)
    d.text((PAD + 6, 22), title.upper(), fill=GOLD, font=f_title)
    for i, (group, slug, name, role) in enumerate(entries):
        x = PAD + (i % cols) * (CELL_W + PAD)
        y = TITLE_H + PAD + (i // cols) * (CELL_H + LABEL_H + PAD)
        d.rectangle([x, y, x + CELL_W, y + CELL_H + LABEL_H], fill=CARD)
        path = os.path.join(DIR, f"{group}-{slug}.png")
        if os.path.exists(path):
            p = Image.open(path).convert("RGB").resize((CELL_W, CELL_H), Image.LANCZOS)
            img.paste(p, (x, y))
        else:
            d.text((x + 20, y + 20), "missing render", fill=DIM, font=f_role)
        d.line([x + 14, y + CELL_H + 6, x + CELL_W - 14, y + CELL_H + 6], fill=GOLD, width=1)
        d.text((x + 14, y + CELL_H + 12), name, fill=TEXT, font=f_name)
        for j, line in enumerate(wrap(d, role, f_role, CELL_W - 28)[:2]):
            d.text((x + 14, y + CELL_H + 44 + j * 20), line, fill=DIM, font=f_role)
    out = os.path.join(DIR, f"sheet-{key}.png")
    img.save(out, optimize=True)
    print("wrote", out, img.size)


if __name__ == "__main__":
    for k, (t, c, e) in SHEETS.items():
        sheet(k, t, c, e)
