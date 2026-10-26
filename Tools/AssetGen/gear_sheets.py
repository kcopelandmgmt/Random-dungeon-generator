"""Lay the tooltip cards of GearShowcaseTests (dungeongame/Logs/gear/tier<N>-<k>.png + drops.json) out as one sheet per dungeon tier.

Run with: python -I Tools/AssetGen/gear_sheets.py   (needs Pillow)
Writes sheet-tier1.png, sheet-tier2.png and sheet-tier3.png beside the cards. Each sheet has a title ("Tier I drops"), the tier's
item-level range and unique multiplier, and the cards in a row-packed grid (4 columns, top aligned), each captioned with its source.
"""
import json
import os
from PIL import Image, ImageDraw, ImageFont

DIR = r"D:\unity dungeon game\dungeongame\Logs\gear"
FONT_DIR = r"C:\Windows\Fonts"
COLS, PAD, TITLE_H, CAP_H = 4, 22, 130, 48
BG, GOLD, TEXT, DIM = (18, 18, 22), (218, 176, 96), (232, 226, 214), (160, 154, 144)
RARITY = {"Normal": (225, 225, 225), "Magic": (110, 150, 255), "Rare": (255, 220, 90), "Unique": (240, 140, 60)}


def font(names, size):
    for n in names:
        p = os.path.join(FONT_DIR, n)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def sheet(tier, drops, rec):
    cards = []
    for d in sorted(drops, key=lambda d: d["index"]):
        p = os.path.join(DIR, d["file"])
        if os.path.exists(p):
            cards.append((d, Image.open(p).convert("RGB")))
    if not cards:
        print("no cards for tier", tier)
        return
    cw = max(c.width for _, c in cards)
    rows = [cards[i:i + COLS] for i in range(0, len(cards), COLS)]
    row_h = [max(c.height for _, c in r) + CAP_H for r in rows]
    W = PAD + COLS * (cw + PAD)
    H = TITLE_H + PAD + sum(h + PAD for h in row_h)
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    f_title = font(["georgiab.ttf", "georgia.ttf"], 46)
    f_sub = font(["segoeui.ttf", "arial.ttf"], 30)
    f_cap = font(["segoeui.ttf", "arial.ttf"], 28)
    name = rec["name"]
    d.text((PAD + 6, 18), f"{name} drops", fill=GOLD, font=f_title)
    lo, hi = rec["minItemLevel"], rec["maxItemLevel"]
    gs = f"gear score {rec['minGearScore']}+" if rec["maxGearScore"] < 0 else f"gear score {rec['minGearScore']}-{rec['maxGearScore']}"
    sub = (f"Item levels {lo}-{hi} (chests, loot rooms, bosses, vaults; level = room tier + {rec['itemLevelOffset']})   "
           f"unique chance x{rec['uniqueChanceMultiplier']:g}   admits {gs}")
    d.text((PAD + 8, 82), sub, fill=DIM, font=f_sub)
    y = TITLE_H + PAD
    for r, rh in zip(rows, row_h):
        for i, (dr, card) in enumerate(r):
            x = PAD + i * (cw + PAD)
            img.paste(card, (x + (cw - card.width) // 2, y))
            col = RARITY.get(dr["rarity"], TEXT)
            cap = f"{dr['rarity']} | iL {dr['itemLevel']} | gs {dr['gearScore']} | from a {dr['source']}"
            d.text((x + 6, y + card.height + 6), cap, fill=col, font=f_cap)
        y += rh + PAD
    out = os.path.join(DIR, f"sheet-tier{tier}.png")
    img.save(out, optimize=True)
    print("wrote", out, img.size)


if __name__ == "__main__":
    with open(os.path.join(DIR, "drops.json"), encoding="utf-8") as fh:
        data = json.load(fh)
    for rec in data["tiers"]:
        sheet(rec["tier"], [d for d in data["drops"] if d["tier"] == rec["tier"]], rec)
