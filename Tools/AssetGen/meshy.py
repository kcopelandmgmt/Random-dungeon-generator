#!/usr/bin/env python3
"""Meshy asset pipeline: text-to-3D preview -> refine -> (rig -> animations) -> download into Unity.

Run:  python -I meshy.py run [--only a,b] [--workers 6] [--reset-failed]
      python -I meshy.py status | balance | library <search>
Standard library only. Keys are read from .secrets/keys.env and never printed.
"""
import argparse, concurrent.futures as cf, json, os, threading, time, urllib.request, urllib.error

KEYS_FILE = r"D:\unity dungeon game\.secrets\keys.env"
OUT_ROOT = r"D:\unity dungeon game\dungeongame\Assets\Generated\models"
LEDGER = os.path.join(OUT_ROOT, "ledger.json")
CEILING = 4500
API = "https://api.meshy.ai"
POLL_S = 10
EST = {"preview-meshy-7.1": 20, "preview-meshy-6-lite": 5, "refine": 10, "rig": 5, "anim": 3}

STYLE = "Stylized low-poly fantasy, hand-painted textures, chunky readable silhouette, dark gothic dungeon palette, game-ready. "
CHAR = "Single character in A-pose facing forward, no base, no pedestal, no background. "
CREA = "Single creature, no base, no background, sits on the ground. "
PROP = "Single object, no background, sits flat on the ground. "
TEX = "hand-painted stylized fantasy texture, dark gothic palette, painterly, no baked lighting"

# Meshy animation library ids (checked against GET /openapi/v1/animations/library)
A_IDLE, A_HIT, A_DEATH, A_ATK = 0, 178, 189, 4


def hero(slug, desc, h, atk, cast):
    return dict(slug=slug, cat="hero", model="meshy-7.1", polys=15000, humanoid=True, rig=True, height=h,
                prompt=STYLE + CHAR + desc,
                actions={"idle": A_IDLE, "attack": atk, "cast": cast, "hit": A_HIT, "death": A_DEATH})


def boss(slug, desc, humanoid=False, h=3.5, atk=128, cast=101, optional_rig=False):
    d = dict(slug=slug, cat="boss", model="meshy-7.1", polys=20000, humanoid=humanoid, rig=humanoid, height=h,
             optional_rig=optional_rig, prompt=STYLE + (CHAR if humanoid else CREA) + desc)
    if humanoid:
        d["actions"] = {"idle": A_IDLE, "attack": atk, "cast": cast, "hit": A_HIT, "death": A_DEATH}
    return d


def mob(slug, desc, humanoid=False, h=1.8, atk=A_ATK):
    d = dict(slug=slug, cat="mob", model="meshy-6-lite", polys=6000, humanoid=humanoid, rig=humanoid, height=h,
             prompt=STYLE + (CHAR if humanoid else CREA) + desc)
    if humanoid:
        d["actions"] = {"idle": A_IDLE, "attack": atk, "death": A_DEATH}
    return d


def prop(slug, desc):
    return dict(slug=slug, cat="prop", model="meshy-6-lite", polys=3000, humanoid=False, rig=False, prompt=STYLE + PROP + desc)


ASSETS = [
    hero("warden", "Heavy armoured knight in thick plate armour, holding a longsword and a large kite shield, steel and dark iron with gold trim.", 1.9, 242, 101),
    hero("reaver", "Muscular barbarian berserker with a huge greataxe, fur pelts, leather straps, war paint, bare scarred arms.", 2.0, 238, 237),
    hero("marksman", "Hooded ranger in a dark green cloak holding a longbow, quiver of arrows on the back, leather armour.", 1.8, 236, 222),
    hero("elementalist", "Robed battle-mage holding a tall staff topped with a glowing crystal, flowing dark blue robes with arcane trim.", 1.8, 125, 129),
    hero("mender", "Priest healer in white and gold robes holding a holy sceptre with a radiant sun emblem, serene hood.", 1.75, 128, 126),
    hero("shade", "Masked rogue assassin with twin daggers, dark leather armour, tattered hood and scarf, lean and agile.", 1.75, 97, 92),
    boss("iron_juggernaut", "Hulking iron golem knight, massive riveted iron plates, glowing furnace chest, giant fists.", True, 3.5, 128, 101),
    boss("hive_matron", "Giant insect queen with a huge swollen egg sac abdomen, chitin plates, mandibles, multiple legs."),
    boss("ashen_colossus", "Towering burning ash giant, body of cracked charcoal stone with glowing embers inside, huge arms.", True, 5.0, 238, 101),
    boss("glasswright", "Crystal-glass sorcerer in jagged glass-shard robes, translucent crystal limbs, glowing core, standing upright.", True, 2.2, 129, 126, optional_rig=True),
    boss("bone_choir", "Cluster of singing skeletons fused into one grotesque mass of bones and open screaming skulls."),
    boss("hound_of_vesh", "Huge shadow hound, four legs, wispy dark smoke fur, glowing eyes, snarling jaws, quadruped."),
    boss("lantern_wraith", "Floating hooded wraith in tattered robes holding a glowing lantern, no legs, ghostly."),
    boss("plague_herald", "Giant plague doctor with a long beaked mask, long coat, holding a smoking censer on a chain.", True, 3.5, 129, 126),
    boss("the_sovereign_below", "Huge crowned undead king in throne-like spiked armour, tattered royal cape, glowing eye sockets, wielding a cruel sword.", True, 4.0, 242, 101),
    mob("bandit", "Human bandit thug with leather armour, bandana, a short sword and a patched cloak.", True, 1.75),
    mob("hollow_knight", "Empty animated suit of plate armour with glowing void eyes, rusted and dented, holding a rusty sword.", True, 1.9),
    mob("skitterer", "Small spider-like creature with eight thin legs, bulbous body and several eyes."),
    mob("ember_wisp", "Floating flame spirit, a swirling teardrop of fire with a face, glowing embers."),
    mob("frostbound", "Ice-encrusted undead warrior covered in frost and icicles, tattered armour, blue glowing eyes.", True, 1.9),
    mob("storm_acolyte", "Robed lightning cultist with hood, sparking crackling hands, belt of charms.", True, 1.75),
    mob("carrion_crawler", "Large segmented crawler like a giant centipede with a tentacled mouth, many legs."),
    mob("gravewalker", "Rotting zombie in shredded clothes, exposed ribs, hunched, arms outstretched.", True, 1.7),
    prop("dungeon_floor_tile", "Flat 2x2 metre square dungeon floor slab of cracked stone."),
    prop("dungeon_wall_block", "Dungeon stone wall section 2 metres wide, 3 metres tall, 2 metres deep, mossy stone bricks."),
    prop("wooden_door", "Dungeon wooden door with iron bands and rivets in a stone frame."),
    prop("vault_door", "Heavy gold-and-iron vault door, thick plates, big bolts and a central wheel lock."),
    prop("stone_pillar", "Round dungeon stone pillar with a carved base and capital, cracked."),
    prop("low_wall", "Waist-high broken stone wall segment with crumbling top."),
    prop("treasure_chest", "Wooden treasure chest with iron bands and a lock, closed."),
    prop("boss_chest", "Ornate large chest with gold filigree, gems and dark wood, closed."),
    prop("shrine", "Glowing stone altar shrine with carved runes and a softly glowing top."),
    prop("dungeon_key", "Large ornate golden key with a skull-shaped bow, lying flat."),
    prop("extract_portal", "Glowing stone archway portal with runes and swirling bright energy inside."),
    prop("spike_trap", "Floor plate trap with sharp metal spikes sticking up."),
    prop("gas_vent", "Floor grate venting green poisonous gas, rusty iron grate in stone."),
    prop("lava_pool", "Cracked stone floor patch with glowing molten lava in the cracks."),
    prop("brazier", "Iron fire brazier on three legs with burning flames and coals."),
]
for _a in ASSETS:
    assert len(_a["prompt"]) <= 800, _a["slug"]

_lock = threading.RLock()
_keys = {}
_bal = {}
_dead = set()


def log(*a):
    with _lock:
        print(time.strftime("%H:%M:%S"), *a, flush=True)


def load_keys():
    for line in open(KEYS_FILE, encoding="utf-8"):
        line = line.strip()
        if line.startswith("MESHY_API_KEY_") and "=" in line:
            n, v = line.split("=", 1)
            _keys[int(n.rsplit("_", 1)[1])] = v.strip()


class Insufficient(Exception):
    pass


def http(method, url, key=None, body=None, tries=8, raw=False):
    delay = 5
    for _ in range(tries):
        req = urllib.request.Request(url, method=method, data=json.dumps(body).encode() if body is not None else None)
        req.add_header("User-Agent", "meshy-pipeline/1.0")
        if key is not None:
            req.add_header("Authorization", "Bearer " + _keys[key])
        if body is not None:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                data = r.read()
                return data if raw else json.loads(data)
        except urllib.error.HTTPError as e:
            txt = e.read().decode("utf-8", "replace")[:300]
            if e.code == 402:
                raise Insufficient(txt)
            if e.code == 429 or e.code >= 500:
                time.sleep(delay)
                delay = min(delay * 2, 90)
                continue
            raise RuntimeError("HTTP %d %s: %s" % (e.code, url.split("meshy.ai")[-1], txt))
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(delay)
            delay = min(delay * 2, 90)
    raise RuntimeError("too many retries: " + url.split("meshy.ai")[-1])


# ---------- ledger ----------
def ledger_read():
    if os.path.exists(LEDGER):
        return json.load(open(LEDGER, encoding="utf-8"))
    return {"ceiling": CEILING, "entries": []}


def ledger_write(l):
    tmp = LEDGER + ".tmp"
    json.dump(l, open(tmp, "w", encoding="utf-8"), indent=1)
    os.replace(tmp, LEDGER)


def ledger_total(l):
    return sum(e["credits"] for e in l["entries"])


def reserve(est, key_idx=None):
    """Enforce the ceiling and choose (or charge) a key; returns the key index."""
    with _lock:
        if ledger_total(ledger_read()) + est > CEILING:
            raise RuntimeError("ledger ceiling %d would be exceeded" % CEILING)
        if key_idx is None:
            cands = [(b, k) for k, b in _bal.items() if k not in _dead and b >= est]
            if not cands:
                raise Insufficient("no key has %d credits" % est)
            key_idx = max(cands)[1]
        _bal[key_idx] = _bal.get(key_idx, 0) - est
        return key_idx


def ledger_add(task_id, key, kind, slug, est):
    with _lock:
        l = ledger_read()
        l["entries"].append({"task_id": task_id, "key": key, "kind": kind, "slug": slug, "credits": est,
                             "estimated": True, "ts": time.strftime("%Y-%m-%dT%H:%M:%S")})
        ledger_write(l)


def ledger_settle(task_id, credits):
    with _lock:
        l = ledger_read()
        for e in l["entries"]:
            if e["task_id"] == task_id:
                e["credits"] = credits
                e["estimated"] = False
        ledger_write(l)


# ---------- manifest ----------
def mpath(slug):
    return os.path.join(OUT_ROOT, slug, "manifest.json")


def mload(a):
    p = mpath(a["slug"])
    if os.path.exists(p):
        return json.load(open(p, encoding="utf-8"))
    return {"slug": a["slug"], "category": a["cat"], "prompt": a["prompt"], "model": a["model"], "polys": a["polys"],
            "status": "pending", "tasks": {}, "files": [], "errors": []}


def msave(m):
    os.makedirs(os.path.join(OUT_ROOT, m["slug"]), exist_ok=True)
    tmp = mpath(m["slug"]) + ".tmp"
    json.dump(m, open(tmp, "w", encoding="utf-8"), indent=1)
    os.replace(tmp, mpath(m["slug"]))


def download(m, url, fname):
    path = os.path.join(OUT_ROOT, m["slug"], fname)
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return True
    try:
        open(path, "wb").write(http("GET", url, raw=True, tries=4))
        if fname not in m["files"]:
            m["files"].append(fname)
        msave(m)
        return True
    except Exception as e:
        log("download failed", m["slug"], fname, str(e)[:120])
        m["errors"].append("download %s: %s" % (fname, str(e)[:150]))
        msave(m)
        return False


# ---------- generic task step ----------
def run_step(m, step, kind, est, body, get_url, post_path, key_idx=None, retries=2, downloader=None):
    """Create (or resume) one Meshy task, poll to a terminal state, download immediately on success.
    Returns the final task JSON, or None on failure."""
    t = m["tasks"].setdefault(step, {})
    slug = m["slug"]
    while True:
        if t.get("status") == "SUCCEEDED":
            if t.get("downloaded"):
                return {"cached": True}
            try:
                r = http("GET", API + get_url + t["id"], t["key"])
                if downloader is None or downloader(r):
                    t["downloaded"] = True
                    msave(m)
                    return r
            except Exception as e:
                m["errors"].append("refetch %s: %s" % (step, str(e)[:150]))
                msave(m)
            return None
        if not t.get("id") or t.get("status") in ("FAILED", "CANCELED"):
            if t.get("attempts", 0) >= retries:
                return None
            res = None
            while res is None:
                try:
                    k = reserve(est, key_idx)
                except (Insufficient, RuntimeError) as e:
                    m["errors"].append("%s: %s" % (step, e))
                    msave(m)
                    return None
                try:
                    res = http("POST", API + post_path, k, body)
                except Insufficient:
                    with _lock:
                        _bal[k] = 0
                    if key_idx is not None:
                        m["errors"].append("%s: key %d out of credits" % (step, k))
                        msave(m)
                        return None
                except RuntimeError as e:
                    with _lock:
                        _bal[k] += est
                    t["attempts"] = t.get("attempts", 0) + 1
                    t["status"] = "FAILED"
                    m["errors"].append("%s create: %s" % (step, str(e)[:250]))
                    msave(m)
                    break
            if res is None:
                if t.get("attempts", 0) >= retries:
                    return None
                continue
            t.update(id=res["result"], key=k, kind=kind, status="PENDING", attempts=t.get("attempts", 0) + 1)
            t.pop("downloaded", None)
            ledger_add(t["id"], k, kind, slug, est)
            msave(m)
            log("submitted", slug, step, "key", k)
        while True:
            try:
                r = http("GET", API + get_url + t["id"], t["key"])
            except Exception as e:
                m["errors"].append("poll %s: %s" % (step, str(e)[:150]))
                msave(m)
                time.sleep(POLL_S)
                continue
            st = r.get("status")
            if st != t["status"]:
                t["status"] = st
                msave(m)
            if st in ("SUCCEEDED", "FAILED", "CANCELED"):
                if r.get("consumed_credits") is not None:
                    ledger_settle(t["id"], r["consumed_credits"])
                break
            time.sleep(POLL_S)
        if st == "SUCCEEDED":
            if downloader is None or downloader(r):
                t["downloaded"] = True
                msave(m)
                return r
            return None
        err = (r.get("task_error") or {}).get("message", "")
        log("FAILED", slug, step, err[:120])
        m["errors"].append("%s FAILED: %s" % (step, err[:200]))
        t["error"] = err[:200]
        msave(m)


def pipeline(a):
    m = mload(a)
    m["status"] = "running"
    msave(m)
    try:
        _pipeline(a, m)
    except Exception as e:
        m["errors"].append("exception: %s" % str(e)[:300])
        m["status"] = "failed"
        msave(m)
        log("EXC", a["slug"], str(e)[:200])
    return m


def _pipeline(a, m):
    slug = a["slug"]
    pbody = {"mode": "preview", "prompt": a["prompt"], "ai_model": a["model"], "should_remesh": True,
             "target_polycount": a["polys"], "topology": "triangle", "target_formats": ["fbx", "glb"], "origin_at": "bottom"}
    if a["humanoid"]:
        pbody["pose_mode"] = "a-pose"
    if not run_step(m, "preview", "preview", EST["preview-" + a["model"]], pbody,
                    "/openapi/v2/text-to-3d/", "/openapi/v2/text-to-3d"):
        m["status"] = "failed"
        msave(m)
        return
    pk = m["tasks"]["preview"]["key"]

    def dl_refine(r):
        mu = r.get("model_urls") or {}
        if mu.get("fbx"):
            ok = download(m, mu["fbx"], slug + ".fbx")
        elif mu.get("glb"):
            ok = download(m, mu["glb"], slug + ".glb")
            m["errors"].append("FBX missing, kept GLB")
        else:
            m["errors"].append("refine: no model urls")
            ok = False
        tex = (r.get("texture_urls") or [{}])[0].get("base_color")
        if tex:
            download(m, tex, slug + "_basecolor.png")
        if r.get("thumbnail_url"):
            download(m, r["thumbnail_url"], slug + "_thumb.png")
        return ok

    rbody = {"mode": "refine", "preview_task_id": m["tasks"]["preview"]["id"], "enable_pbr": False,
             "texture_resolution": "2k", "texture_prompt": TEX, "ai_model": a["model"]}
    if not run_step(m, "refine", "refine", EST["refine"], rbody, "/openapi/v2/text-to-3d/", "/openapi/v2/text-to-3d",
                    key_idx=pk, downloader=dl_refine):
        m["status"] = "failed"
        msave(m)
        return
    rk = m["tasks"]["refine"]["key"]

    if a["rig"]:
        def dl_rig(r):
            res = r.get("result") or r
            ok = bool(res.get("rigged_character_fbx_url")) and download(m, res["rigged_character_fbx_url"], slug + "_rigged.fbx")
            ba = res.get("basic_animations") or {}
            if ba.get("walking_fbx_url"):
                download(m, ba["walking_fbx_url"], slug + "_anim_walk.fbx")
            if ba.get("running_fbx_url"):
                download(m, ba["running_fbx_url"], slug + "_anim_run.fbx")
            return ok

        rig = run_step(m, "rig", "rig", EST["rig"],
                       {"input_task_id": m["tasks"]["refine"]["id"], "height_meters": a["height"]},
                       "/openapi/v1/rigging/", "/openapi/v1/rigging", key_idx=rk, downloader=dl_rig,
                       retries=1 if a.get("optional_rig") else 2)
        if not rig:
            m["rig_failed"] = True
            m["status"] = "done" if a.get("optional_rig") else "failed"
            msave(m)
            return
        rigid, rigkey = m["tasks"]["rig"]["id"], m["tasks"]["rig"]["key"]
        failed = []

        def do_anim(item):
            name, aid = item

            def dl(r):
                u = (r.get("result") or {}).get("animation_fbx_url")
                return bool(u) and download(m, u, "%s_anim_%s.fbx" % (slug, name))

            if not run_step(m, "anim_" + name, "anim", EST["anim"], {"rig_task_id": rigid, "action_id": aid},
                            "/openapi/v1/animations/", "/openapi/v1/animations", key_idx=rigkey, downloader=dl):
                failed.append(name)

        with cf.ThreadPoolExecutor(max_workers=5) as ex:
            list(ex.map(do_anim, list(a["actions"].items())))
        if failed:
            m["errors"].append("animations failed: " + ",".join(failed))
            m["status"] = "partial"
            msave(m)
            return
    m["status"] = "done"
    msave(m)
    log("DONE", slug)


def init_balances():
    load_keys()
    for k in sorted(_keys):
        try:
            _bal[k] = int(http("GET", API + "/openapi/v1/balance", k)["balance"])
        except Exception as e:
            log("balance failed for key", k, str(e)[:80])
            _dead.add(k)
            _bal[k] = 0


def cmd_run(args):
    os.makedirs(OUT_ROOT, exist_ok=True)
    init_balances()
    log("balances", dict(_bal), "ledger total", ledger_total(ledger_read()))
    only = set(args.only.split(",")) if args.only else None
    todo = [a for a in ASSETS if not only or a["slug"] in only]
    if args.reset_failed:
        for a in todo:
            if os.path.exists(mpath(a["slug"])):
                m = mload(a)
                for t in m["tasks"].values():
                    if t.get("status") in ("FAILED", "CANCELED"):
                        t["attempts"] = 0
                if m["status"] in ("failed", "partial"):
                    m["status"] = "pending"
                msave(m)
    todo = [a for a in todo if mload(a)["status"] != "done"]
    log("assets to process:", len(todo))
    with cf.ThreadPoolExecutor(max_workers=args.workers) as ex:
        list(ex.map(pipeline, todo))
    cmd_status(args)


def cmd_status(_):
    l = ledger_read()
    per = {}
    for e in l["entries"]:
        per[e["key"]] = per.get(e["key"], 0) + e["credits"]
    print("ledger total", ledger_total(l), "of", CEILING, "per key", per)
    for a in ASSETS:
        p = mpath(a["slug"])
        if os.path.exists(p):
            m = json.load(open(p, encoding="utf-8"))
            print("%-22s %-8s %2d files  %s" % (a["slug"], m["status"], len(m["files"]), "; ".join(m["errors"][-2:])[:100]))
        else:
            print("%-22s pending" % a["slug"])


def cmd_balance(_):
    init_balances()
    print(_bal)


def cmd_library(args):
    init_balances()
    r = http("GET", API + "/openapi/v1/animations/library?page_size=50&search=" + urllib.request.quote(args.search), min(_keys))
    for x in (r if isinstance(r, list) else r.get("result", [])):
        print(x.get("action_id", x.get("id")), x.get("name"), "|", x.get("category"), "/", x.get("sub_category"))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest="cmd", required=True)
    r_ = sp.add_parser("run")
    r_.add_argument("--only")
    r_.add_argument("--workers", type=int, default=6)
    r_.add_argument("--reset-failed", action="store_true")
    sp.add_parser("status")
    sp.add_parser("balance")
    l_ = sp.add_parser("library")
    l_.add_argument("search")
    args_ = ap.parse_args()
    {"run": cmd_run, "status": cmd_status, "balance": cmd_balance, "library": cmd_library}[args_.cmd](args_)
