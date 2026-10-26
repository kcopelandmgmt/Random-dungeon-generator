#!/usr/bin/env python3
"""Meshy text-to-image pipeline: painted fantasy icons.

Sets:  abilities (default) -> dungeongame/Assets/Generated/icons/<abilityId>.png, one per AbilityBook.All id.
       items               -> dungeongame/Assets/Generated/item-icons/<slug>.png, one per item base, consumable and material
                              (slug = lower-case name with underscores, e.g. plate_helm, health_potion, prime_shards).
Run:  python -I meshy_images.py pilot  [--models nano-banana,nano-banana-2-lite,gpt-image-2]   # 3 icons x N models into icons/_pilot
      python -I meshy_images.py run    [--set items] [--model M] [--only id,id] [--workers 6] [--redo id,id] [--reset-failed]
      python -I meshy_images.py status [--set items]
Reuses meshy.py (keys, http, ledger). Resumable: finished icons are skipped, unfinished tasks are polled again.
Manifest: <set folder>/manifest.json (id, prompt, model, task, key, credits, file, status). Ledger kind "image"
(item tasks are logged with slug "item/<slug>"). The item set has its own ceiling, ITEM_BUDGET credits including retries.
"""
import argparse, concurrent.futures as cf, json, os, sys, threading, time

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import meshy as M  # noqa: E402

OUT = r"D:\unity dungeon game\dungeongame\Assets\Generated\icons"
MANIFEST = os.path.join(OUT, "manifest.json")
DEFAULT_MODEL = "nano-banana-2-lite"
EST_CREDITS = {"nano-banana": 5, "nano-banana-2": 8, "nano-banana-2-lite": 4, "gpt-image-2": 10,
               "gpt-image-2-5-flare": 10, "gpt-image-2-5-sunburst": 10}   # reservation only; the settled value is the measured one

STYLE = ("Square painted fantasy game ability icon, one bold central symbol that stays readable at 64 pixels, "
         "dark vignette background, strong rim light, hand-painted dark gothic style, high contrast, "
         "no text, no letters, no border, no UI frame. ")
COLOUR = {
    "fire": "Colour key: fire orange and ember red glow.",
    "frost": "Colour key: icy cyan and pale blue glow.",
    "lightning": "Colour key: violet-white lightning glow.",
    "poison": "Colour key: toxic green glow.",
    "shadow": "Colour key: deep shadow purple glow.",
    "holy": "Colour key: radiant holy gold glow.",
    "steel": "Colour key: cold steel grey and silver highlights.",
}
ULT = " Ultimate ability: more ornate, a grander composition with an ornate glowing sigil and radiating light."

# id -> (colour key, motif). Every AbilityBook.All id is covered (checked by `status`).
ICONS = {
    "Sentinel.basic": ("steel", "a knight's longsword slashing in a bright crescent arc"),
    "Sentinel.bolt": ("steel", "a heavy kite shield punching forward with impact shockwave lines"),
    "Sentinel.zone": ("steel", "a tall tower shield wall with a faint protective barrier glowing behind it"),
    "Sentinel.mobility": ("steel", "an armoured knight planted in place, a glowing taunting war banner and a ring of shining chains"),
    "Breaker.basic": ("steel", "a massive warhammer swinging in a wide arc"),
    "Breaker.bolt": ("steel", "a warhammer crushing down with a burst of cracks and shards"),
    "Breaker.zone": ("steel", "a hammer slamming the ground, a cone of shockwave and flying rubble"),
    "Breaker.mobility": ("steel", "an armoured fist and shoulder charging forward trailing dust and speed lines"),
    "Berserker.basic": ("steel", "a great battle axe swinging in a sweeping arc"),
    "Berserker.bolt": ("steel", "an axe cleaving a wide cone of red slashes dripping blood"),
    "Berserker.zone": ("steel", "twin axes whirling in a spinning circle of blades"),
    "Berserker.mobility": ("steel", "an axe leaping forward in a lunge with blood-red motion streaks"),
    "Duelist.basic": ("steel", "a slim rapier making a quick tight slash"),
    "Duelist.bolt": ("steel", "a rapier thrust piercing forward, a drop of blood on the tip"),
    "Duelist.zone": ("steel", "a rapier parrying and deflecting a blow with a bright spark burst"),
    "Duelist.mobility": ("steel", "a rapier lunging forward with long motion streaks"),
    "Sharpshooter.basic": ("steel", "a single longbow arrow flying in flight"),
    "Sharpshooter.bolt": ("steel", "a long arrow piercing straight through several glowing targets in a line"),
    "Sharpshooter.zone": ("steel", "a scatter of iron caltrop spikes on the ground"),
    "Sharpshooter.mobility": ("steel", "a hooded archer leaping backwards, arcing motion trail"),
    "Volleyer.basic": ("steel", "a quick arrow with a feathered fletching speeding past"),
    "Volleyer.bolt": ("steel", "a rapid burst of three arrows flying together"),
    "Volleyer.zone": ("steel", "a rain of arrows falling from the sky onto a circle on the ground"),
    "Volleyer.mobility": ("steel", "a quick sideways dodge roll, a cloaked figure tumbling with a swirl trail"),
    "Assassin.basic": ("shadow", "a curved dagger making a quick slash with a purple streak"),
    "Assassin.bolt": ("shadow", "a dagger plunged in from behind a silhouette, a bright critical flash"),
    "Assassin.zone": ("shadow", "a swirling smoke cloud with a dashing figure passing through it"),
    "Assassin.mobility": ("shadow", "a vanishing hooded figure fading into translucent shadow, only glowing eyes visible"),
    "Trapper.basic": ("steel", "a compact iron crossbow with a loaded bolt, aimed forward"),
    "Trapper.bolt": ("steel", "a crossbow bolt trailing a thick rope net snare"),
    "Trapper.zone": ("steel", "a thin taut glowing tripwire stretched between two stakes"),
    "Trapper.mobility": ("shadow", "a puff of smoke with a dashing cloaked figure vanishing"),
    "Warder.basic": ("holy", "a holy sceptre firing a small bolt of golden light"),
    "Warder.bolt": ("holy", "a glowing golden healing hand with rising golden sparkles and a plus-shaped light"),
    "Warder.zone": ("holy", "a glowing golden sanctuary circle of runes with a soft protective dome of light"),
    "Warder.mobility": ("holy", "a cleansing golden light washing away dark chains from an open hand"),
    "Plaguebearer.basic": ("poison", "a lobbed glowing green ember trailing smoke"),
    "Plaguebearer.bolt": ("poison", "a smouldering green bolt dripping toxic poison"),
    "Plaguebearer.zone": ("poison", "a swirling toxic green miasma cloud with healing motes inside"),
    "Plaguebearer.mobility": ("poison", "a purifying green-white light washing away dark corruption from an open hand"),
    "Pyromancy.basic": ("fire", "a small fiery ember spark"),
    "Pyromancy.bolt": ("fire", "a blazing fireball bolt streaking forward with a flame tail"),
    "Pyromancy.zone": ("fire", "a long wall of roaring flames"),
    "Pyromancy.mobility": ("fire", "a figure teleporting in a burst of embers leaving a trail of small fires"),
    "Cryomancy.basic": ("frost", "a small glittering frost mote with frozen sparkles"),
    "Cryomancy.bolt": ("frost", "a sharp ice shard spear flying forward"),
    "Cryomancy.zone": ("frost", "a glacial field, a circle of jagged ice crystals and frost on the ground"),
    "Cryomancy.mobility": ("frost", "a skidding slide leaving a trail of ice across the floor"),
    "Stormcalling.basic": ("lightning", "a small crackling lightning spark"),
    "Stormcalling.bolt": ("lightning", "a chain lightning arc jumping between three targets"),
    "Stormcalling.zone": ("lightning", "a circle of static electricity with crackling arcs rising from the ground"),
    "Stormcalling.mobility": ("lightning", "a figure blinking away in a flash, a violet-white teleport burst"),
    "Breaker.ult": ("steel", "a collapsing stone archway with huge falling boulders and rubble sealing a doorway"),
    "Trapper.ult": ("steel", "a ring of many glowing tripwires and iron traps sealing off a room"),
    "Pyromancy.ult": ("fire", "an entire chamber engulfed in an explosive firestorm inferno"),
    "Volleyer.ult": ("frost", "a dome of suppressing frozen chains and arrows locking a circular zone, silencing sigils"),
    "Stormcalling.ult": ("lightning", "a dark thundercloud swallowing the light, a blackout sphere of crackling silence"),
    "Sentinel.ult": ("steel", "a colossal radiant aegis shield, immovable, a ring of protective light"),
    "Duelist.ult": ("steel", "a perfect glowing parry, crossed rapiers deflecting every blow in a burst of sparks"),
    "Berserker.ult": ("fire", "a roaring berserker silhouette bathed in blood-red aura, raised axes, glowing wounds closing"),
    "Cryomancy.ult": ("frost", "absolute zero, a huge snowflake crystal freezing a whole circle solid in ice"),
    "Warder.ult": ("holy", "a fallen hero rising in a pillar of golden resurrection light with angelic wings"),
    "Plaguebearer.ult": ("poison", "an outbreak, a huge green plague skull with spreading toxic spores"),
    "Assassin.ult": ("shadow", "a glowing purple death mark sigil on a target, a skull crosshair seen through walls"),
    "Sharpshooter.ult": ("steel", "a watchful eye in a sniper crosshair, multiple arrows locking onto targets"),
    "Mob.basic": ("steel", "a monster's clawed swipe leaving three slash marks"),
    "Mob.ranged": ("shadow", "a dark magical bolt flung from a monster's hand"),
    "Boss.basic": ("steel", "a giant brutal fist smashing downward"),
    "Boss.slam": ("fire", "a huge ground slam with a circular shockwave and cracked floor"),
}


# ---------- item set ----------
ITEM_OUT = r"D:\unity dungeon game\dungeongame\Assets\Generated\item-icons"
ITEM_BUDGET = 330   # credits for the whole item set, retries included (ledger entries with slug item/*)

ITEM_STYLE = ("Square painted dark-fantasy game inventory item icon: a single object, centred, the whole object visible with a "
              "generous empty margin around it, on a plain very dark charcoal background with a soft dark vignette, gentle warm rim light, "
              "hand-painted dark gothic style, high contrast, crisp silhouette that stays readable at 64 pixels. "
              "No text, no letters, no border, no frame, no UI, no character, no hands, no body, nothing else in the picture. ")
ITEM_KEY = {
    "steel": "Material: forged dark steel with cold silver highlights and a worn leather grip.",
    "heavy": "Heavy plate armour: polished dark steel plates, rivets and cold silver highlights, heavy and solid.",
    "medium": "Medium armour: dark brown tooled leather with iron studs, rivets and small buckles.",
    "light": "Light armour: soft woven cloth and silk in deep plum and dusky blue with fine gold embroidered trim, no metal.",
    "fire": "Colour key: fire orange and ember red glow.",
    "frost": "Colour key: icy cyan and pale blue glow.",
    "lightning": "Colour key: violet-white lightning glow.",
    "holy": "Colour key: radiant holy gold glow.",
    "gold": "Material: polished antique gold with a softly glowing gemstone.",
    "red": "Colour key: deep crimson glow through glass.",
    "green": "Colour key: toxic pale green glow through glass.",
    "tools": "Material: worn leather and dark iron tools.",
    "silver": "Material: tarnished silver with cool highlights.",
    "iron": "Material: rusty dark iron and dull steel scraps.",
    "prism": "Colour key: pale gold and prismatic crystal light.",
    "arcane": "Colour key: swirling blue-violet arcane glow.",
}
# name (as the game spells it) -> (key, motif). Every ItemBase.All name, then Consumable.All, then the crafting materials and silver.
ITEM_ICONS = {
    "Longsword": ("steel", "a knight's longsword with a straight double-edged blade, a simple cross-guard and leather-wrapped grip, laid diagonally"),
    "Greataxe": ("steel", "a huge two-handed greataxe with a broad crescent steel head on a long iron-banded wooden haft, laid diagonally"),
    "Warhammer": ("steel", "a heavy two-handed warhammer with a square steel head and a back spike on a long haft, laid diagonally"),
    "Shortbow": ("steel", "a compact recurve hunting shortbow of dark wood with a taut string, upright"),
    "Longbow": ("steel", "a tall yew longbow with a leather-wrapped grip and a taut string, laid diagonally"),
    "Twin Daggers": ("steel", "a pair of curved daggers crossed in an X"),
    "Fire Staff": ("fire", "a gnarled dark wooden mage staff topped with a glowing ember crystal wreathed in small flames, laid diagonally"),
    "Frost Rod": ("frost", "a short silver mage rod topped with a jagged pale blue ice crystal trailing frost mist, laid diagonally"),
    "Storm Wand": ("lightning", "a slim dark wand with a violet crystal tip crackling with small lightning arcs, laid diagonally"),
    "Holy Sceptre": ("holy", "an ornate gold sceptre with a radiant sunburst head, laid diagonally"),
    "Censer": ("fire", "a brass censer hanging from a short chain, glowing coals and pale smoke curling from its pierced lid"),
    "Plate Helm": ("heavy", "a closed steel great helm with a narrow eye slit"),
    "Plate Cuirass": ("heavy", "a steel plate breastplate cuirass with pauldrons, front view"),
    "Gauntlets": ("heavy", "a pair of empty articulated steel plate gauntlets"),
    "Plate Greaves": ("heavy", "a pair of steel plate leg greaves with knee cops, standing side by side"),
    "Sabatons": ("heavy", "a pair of armoured steel plate sabatons, side view"),
    "Leather Hood": ("medium", "a dark leather hood with a studded rim and a short mantle"),
    "Brigandine": ("medium", "a studded leather brigandine vest with riveted plates, front view"),
    "Leather Gloves": ("medium", "a pair of empty leather gloves with studded knuckles"),
    "Studded Leggings": ("medium", "a pair of studded leather leggings, laid flat"),
    "Riding Boots": ("medium", "a pair of tall leather riding boots with buckles, side view"),
    "Silk Cowl": ("light", "a soft silk cowl hood with a draped collar"),
    "Woven Robe": ("light", "a flowing woven mage robe with wide sleeves, laid flat, front view"),
    "Silk Wraps": ("light", "a pair of empty silk hand wraps, fingerless cloth gloves"),
    "Cloth Trousers": ("light", "a pair of loose cloth trousers with a sash belt, laid flat"),
    "Soft Shoes": ("light", "a pair of soft cloth slippers, side view"),
    "Amulet": ("gold", "an amulet pendant with a glowing deep red gemstone on a fine chain"),
    "Ring": ("gold", "a single signet ring set with a glowing blue gemstone, three-quarter view"),
    "Health Potion": ("red", "a round glass potion flask filled with glowing crimson liquid, cork stopper"),
    "Antidote": ("green", "a slim glass vial of glowing green antidote with a wax-sealed stopper"),
    "Salvage Kit": ("tools", "a rolled-open leather tool roll holding a small hammer, pincers and a chisel"),
    "Silver": ("silver", "a small stack of tarnished silver coins with one coin leaning against it"),
    "Scrap": ("iron", "a small heap of scrap metal: bent iron plates, rivets and a broken buckle"),
    "Prime Shards": ("prism", "a cluster of three faceted glowing crystal shards"),
    "Essence": ("arcane", "a small glass orb holding swirling glowing arcane essence mist"),
}


def item_slug(name):
    return name.lower().replace(" ", "_").replace("'", "")


def build_item_prompt(name):
    key, motif = ITEM_ICONS[name]
    return ITEM_STYLE + "Subject: " + motif + ". " + ITEM_KEY[key]


def build_prompt(aid):
    el, motif = ICONS[aid]
    p = STYLE + "Subject: " + motif + ". " + COLOUR[el]
    if aid.endswith(".ult"):
        p += ULT
    return p


# ---------- manifest ----------
_mlock = threading.RLock()


class IconSet:
    """One family of icons: where it goes, what it asks for, how its files and ledger entries are named, its own ceiling."""
    def __init__(self, name, out, icons, prompt, style, file_of, ledger_slug, budget=None):
        self.name, self.out, self.icons, self.prompt, self.style = name, out, icons, prompt, style
        self.file_of, self.ledger_slug, self.budget = file_of, ledger_slug, budget
        self.manifest = os.path.join(out, "manifest.json")

    def spent(self):
        """Credits the ledger holds for this set (reservations included)."""
        pre = self.ledger_slug("")
        return sum(e["credits"] for e in M.ledger_read()["entries"] if e.get("kind") == "image" and e.get("slug", "").startswith(pre))


SETS = {
    "abilities": IconSet("abilities", OUT, ICONS, lambda i: build_prompt(i), STYLE, lambda i: i + ".png", lambda i: i),
    "items": IconSet("items", ITEM_OUT, ITEM_ICONS, lambda i: build_item_prompt(i), ITEM_STYLE, lambda i: item_slug(i) + ".png",
                     lambda i: "item/" + (item_slug(i) if i else ""), ITEM_BUDGET),
}


def mload(st=None):
    st = st or SETS["abilities"]
    if os.path.exists(st.manifest):
        return json.load(open(st.manifest, encoding="utf-8"))
    return {"style": st.style, "icons": {}}


def msave(m, st=None):
    st = st or SETS["abilities"]
    with _mlock:
        os.makedirs(st.out, exist_ok=True)
        tmp = st.manifest + ".tmp"
        json.dump(m, open(tmp, "w", encoding="utf-8"), indent=1)
        for _ in range(20):
            try:
                os.replace(tmp, st.manifest)
                return
            except PermissionError:
                time.sleep(0.25)
        os.replace(tmp, st.manifest)


_blk = threading.Lock()


def generate(aid, prompt, model, path, rec, save, budget_set=None):
    """Create (or resume) one text-to-image task and download its PNG to `path`. rec is the manifest record (mutated).
    budget_set: an IconSet whose own ceiling must hold before a new task is submitted; its ledger slugs are used."""
    est = EST_CREDITS.get(model, 8)
    for _attempt in range(3):
        if not rec.get("task") or rec.get("status") in ("FAILED", "CANCELED"):
            try:
                if budget_set is not None and budget_set.budget is not None:
                    with _blk:
                        if budget_set.spent() + est > budget_set.budget:
                            raise RuntimeError("%s budget %d would be exceeded (spent %d)" % (budget_set.name, budget_set.budget, budget_set.spent()))
                k = M.reserve(est)
            except (M.Insufficient, RuntimeError) as e:
                rec["status"] = "failed"; rec["error"] = str(e)[:200]; save(); return False
            try:
                res = M.http("POST", M.API + "/openapi/v1/text-to-image", k,
                             {"prompt": prompt, "ai_model": model, "aspect_ratio": "1:1"})
            except M.Insufficient:
                with M._lock:
                    M._bal[k] = 0
                continue
            except RuntimeError as e:
                with M._lock:
                    M._bal[k] += est
                rec["error"] = str(e)[:200]; rec["status"] = "FAILED"; rec["task"] = None; save(); continue
            rec.update(task=res["result"], key=k, model=model, prompt=prompt, status="PENDING", error=None)
            M.ledger_add(rec["task"], k, "image", budget_set.ledger_slug(aid) if budget_set is not None else aid, est)
            save()
            M.log("submitted", aid, model)
        while True:
            try:
                r = M.http("GET", M.API + "/openapi/v1/text-to-image/" + rec["task"], rec["key"])
            except Exception as e:
                rec["error"] = "poll: " + str(e)[:120]; time.sleep(M.POLL_S); continue
            st = r.get("status")
            if st in ("SUCCEEDED", "FAILED", "CANCELED"):
                break
            time.sleep(4)
        rec["status"] = st
        if r.get("consumed_credits") is not None:
            rec["credits"] = r["consumed_credits"]
            M.ledger_settle(rec["task"], r["consumed_credits"])
        if st == "SUCCEEDED":
            urls = r.get("image_urls") or []
            if not urls:
                rec["status"] = "FAILED"; rec["error"] = "no image_urls"; rec["task"] = None; save(); continue
            os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, "wb").write(M.http("GET", urls[0], raw=True, tries=4))
            rec["file"] = os.path.basename(path)
            rec["status"] = "done"
            save()
            M.log("DONE", aid, "credits", rec.get("credits"))
            return True
        rec["error"] = (r.get("task_error") or {}).get("message", "")[:200]
        rec["task"] = None
        save()
    rec["status"] = "failed"; save()
    return False


def cmd_pilot(args):
    M.init_balances()
    models = args.models.split(",")
    picks = ["Pyromancy.bolt", "Warder.bolt", "Sentinel.basic"]
    pdir = os.path.join(OUT, "_pilot")
    pm_path = os.path.join(pdir, "pilot.json")
    os.makedirs(pdir, exist_ok=True)
    pm = json.load(open(pm_path)) if os.path.exists(pm_path) else {}
    lk = threading.Lock()

    def save():
        with lk:
            json.dump(pm, open(pm_path, "w"), indent=1)

    jobs = [(a, m) for m in models for a in picks]

    def one(j):
        a, m = j
        rec = pm.setdefault(a + "@" + m, {})
        if rec.get("status") == "done":
            return
        generate(a, build_prompt(a), m, os.path.join(pdir, "%s__%s.png" % (m, a)), rec, save)

    with cf.ThreadPoolExecutor(max_workers=6) as ex:
        list(ex.map(one, jobs))
    for k, v in pm.items():
        print(k, v.get("status"), v.get("credits"), v.get("error") or "")


def cmd_run(args):
    st = SETS[args.set]
    M.init_balances()
    m = mload(st)
    ids = [i for i in st.icons if not args.only or i in args.only.split(",")]
    redo = set(args.redo.split(",")) if args.redo else set()
    todo = []
    for i in ids:
        rec = m["icons"].setdefault(i, {})
        path = os.path.join(st.out, st.file_of(i))
        if i in redo:
            rec.clear()
            if os.path.exists(path):
                os.replace(path, path + ".old")
        if args.reset_failed and rec.get("status") == "failed":
            rec.clear()
        if rec.get("status") == "done" and os.path.exists(path):
            continue
        todo.append(i)
    M.log("icons to process:", len(todo), "ledger total", M.ledger_total(M.ledger_read()),
          "" if st.budget is None else "set spent %d of %d" % (st.spent(), st.budget))

    def save():
        msave(m, st)

    def one(i):
        try:
            generate(i, st.prompt(i), args.model, os.path.join(st.out, st.file_of(i)), m["icons"][i], save, st if st.budget is not None else None)
            if st.budget is not None:
                m["icons"][i]["slug"] = os.path.splitext(st.file_of(i))[0]; save()
        except Exception as e:
            m["icons"][i]["status"] = "failed"; m["icons"][i]["error"] = str(e)[:200]; save()
            M.log("EXC", i, str(e)[:150])

    with cf.ThreadPoolExecutor(max_workers=args.workers) as ex:
        list(ex.map(one, todo))
    msave(m, st)
    cmd_status(args)


def cmd_status(args):
    st = SETS[getattr(args, "set", "abilities")]
    m = mload(st)
    done = [v for v in m["icons"].values() if v.get("status") == "done"]
    cr = sum(v.get("credits") or 0 for v in done)
    print("%s: icons done %d of %d, credits %s, ledger total %d of %d%s" % (
        st.name, len(done), len(st.icons), cr, M.ledger_total(M.ledger_read()), M.CEILING,
        "" if st.budget is None else ", set spent %d of %d" % (st.spent(), st.budget)))
    for i in st.icons:
        v = m["icons"].get(i, {})
        if v.get("status") != "done":
            print(" ", i, v.get("status", "pending"), v.get("error") or "")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest="cmd", required=True)
    p_ = sp.add_parser("pilot")
    p_.add_argument("--models", default="nano-banana,nano-banana-2-lite,gpt-image-2")
    r_ = sp.add_parser("run")
    r_.add_argument("--set", choices=sorted(SETS), default="abilities")
    r_.add_argument("--model", default=DEFAULT_MODEL)
    r_.add_argument("--only")
    r_.add_argument("--redo")
    r_.add_argument("--workers", type=int, default=6)
    r_.add_argument("--reset-failed", action="store_true")
    s_ = sp.add_parser("status")
    s_.add_argument("--set", choices=sorted(SETS), default="abilities")
    a_ = ap.parse_args()
    {"pilot": cmd_pilot, "run": cmd_run, "status": cmd_status}[a_.cmd](a_)
