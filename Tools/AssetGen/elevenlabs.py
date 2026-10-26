"""ElevenLabs audio generator for the dungeon game. Python 3 stdlib only; run: python -I elevenlabs.py
Resumable: slugs already in manifest.json with a file on disk are skipped. Key from .secrets/keys.env.
Option: --no-music (use 30 s sound-effect ambient beds instead of the music endpoint)."""
import json, os, re, sys, time, urllib.request, urllib.error

ROOT = r"D:\unity dungeon game"
KEYS = os.path.join(ROOT, ".secrets", "keys.env")
OUT = os.path.join(ROOT, "dungeongame", "Assets", "Generated", "audio")
MANIFEST = os.path.join(OUT, "manifest.json")
BASE = "https://api.elevenlabs.io"
CEILING = 60000  # total program spend (character_count) must not pass this
DANIEL = "onwK4e9ZLuTAKqWW03F9"
KALLIXIS = "cPoqAvGWCPfCfyPMwe4z"
STYLE = "dark fantasy dungeon game sound effect, dry, punchy, no reverb tail, no music: "


def load_key():
    for line in open(KEYS, encoding="utf-8"):
        if line.startswith("ELEVENLABS_API_KEY="):
            return line.split("=", 1)[1].strip().strip('"')
    sys.exit("no key")


KEY = load_key()


def call(method, path, body=None):
    for attempt in range(6):
        req = urllib.request.Request(
            BASE + path, method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers={"xi-api-key": KEY, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code == 429 or e.code >= 500:
                time.sleep(2 ** (attempt + 1))
                continue
            raise RuntimeError("HTTP %d %s" % (e.code, e.read()[:300].decode("utf-8", "replace")))
        except (urllib.error.URLError, OSError):
            time.sleep(2 ** (attempt + 1))
    raise RuntimeError("retries exhausted")


def used():
    return json.loads(call("GET", "/v1/user/subscription"))["character_count"]


manifest = json.load(open(MANIFEST)) if os.path.exists(MANIFEST) else {}


def save():
    os.makedirs(OUT, exist_ok=True)
    json.dump(manifest, open(MANIFEST, "w"), indent=2)


failed = []


def gen(slug, cat, prompt, kind, body, path, dur=None, loop=False):
    rel = "%s/%s.mp3" % (cat, slug)
    if slug in manifest and os.path.exists(os.path.join(OUT, rel)):
        return
    before = used()
    if before > CEILING - 3000:
        sys.exit("ceiling reached")
    try:
        data = call("POST", path, body)
    except Exception as e:
        failed.append((slug, str(e)[:200]))
        print("FAIL", slug, str(e)[:200])
        return
    os.makedirs(os.path.join(OUT, cat), exist_ok=True)
    open(os.path.join(OUT, rel), "wb").write(data)
    after = used()
    manifest[slug] = {"file": rel, "category": cat, "prompt": prompt, "kind": kind,
                      "duration": dur, "loop": loop, "credits": after - before}
    save()
    print("ok", slug, after - before)


def sfx(slug, cat, prompt, dur=None, loop=False):
    body = {"text": STYLE + prompt, "prompt_influence": 0.5, "loop": loop}
    if dur:
        body["duration_seconds"] = dur
    gen(slug, cat, prompt, "sfx", body, "/v1/sound-generation?output_format=mp3_44100_128", dur, loop)


def speech(slug, cat, text, voice):
    body = {"text": text, "model_id": "eleven_multilingual_v2",
            "voice_settings": {"stability": 0.45, "similarity_boost": 0.8, "style": 0.35}}
    gen(slug, cat, text, "speech", body, "/v1/text-to-speech/%s?output_format=mp3_44100_128" % voice)


def snake(t):
    return re.sub(r"[^a-z0-9]+", "_", t.lower()).strip("_")[:60]


ELEM = {
    "physical": ("heavy weapon-like force burst", "dull heavy blunt impact crunch"),
    "fire": ("fire spell whoosh ignition", "fireball explosion burst crackle"),
    "frost": ("icy spell chime and crystallising", "ice shatter crack"),
    "lightning": ("electric spell charge zap", "thunder crack lightning strike"),
    "poison": ("bubbling toxic spell hiss", "acid splash sizzle"),
    "shadow": ("dark whispering spell swell", "dark void thump implosion"),
    "holy": ("warm choral healing spell chime", "soft radiant heal shimmer"),
}

MUSIC = [("music_exploration", "slow dark fantasy exploration ambient music, sparse low strings, drones, tension, loopable, instrumental"),
         ("music_combat", "driving dark fantasy combat music, war drums, low brass, aggressive strings, loopable, instrumental"),
         ("music_boss", "epic dark fantasy boss battle music, choir, heavy percussion, menacing brass, loopable, instrumental")]
BEDS = [("ambient_bed_exploration", "dark dungeon exploration ambient bed, slow drones, sparse low strings"),
        ("ambient_bed_combat", "tense combat ambient bed, rhythmic low drums and drones"),
        ("ambient_bed_boss", "ominous boss ambient bed, deep choir drone and rumbling")]


def music():
    if "--no-music" in sys.argv:
        return beds()
    for i, (slug, p) in enumerate(MUSIC):
        if slug in manifest:
            continue
        n = len(failed)
        gen(slug, "ambience", p, "music",
            {"prompt": p, "music_length_ms": 60000, "force_instrumental": True, "model_id": "music_v1"},
            "/v1/music?output_format=mp3_44100_128", 60, True)
        if len(failed) > n:
            return beds()
        if i == 0 and manifest[slug]["credits"] * 3 > 15000:
            print("music too costly; stopping after one track")
            return


def beds():
    for slug, p in BEDS:
        sfx(slug, "ambience", p, 30, True)


def build():
    c = "abilities"
    for e, (a, b) in ELEM.items():
        sfx("cast_" + e, c, a, 1.2)
        sfx("impact_" + e, c, b, 1.0)
    for s, p, d in [
            ("swing_sword", "sword swing slash whoosh", 0.8), ("swing_axe", "heavy axe swing whoosh", 0.9),
            ("swing_hammer", "war hammer swing and heavy thud", 1.0), ("swing_dagger", "quick dagger slash swish", 0.6),
            ("bow_release", "bowstring release twang", 0.7), ("arrow_impact", "arrow thunk into wood", 0.6),
            ("shield_bash", "metal shield bash clang", 0.8), ("dash_whoosh", "fast dash whoosh with cloth", 0.6),
            ("zone_spawn", "magical ground zone appearing hum", 1.5),
            ("buff_on", "empowering buff activation rising chime", 1.2),
            ("ultimate_charge", "powerful energy charging up rising swell", 2.0),
            ("ultimate_release", "massive ultimate ability explosion release", 2.0)]:
        sfx(s, c, p, d)
    c = "combat"
    for i in range(1, 4):
        sfx("hit_flesh_%d" % i, c, "sword hitting flesh wet slice", 0.6)
    for i in range(1, 3):
        sfx("hit_armour_%d" % i, c, "blade striking metal armour clang", 0.6)
    for i in range(1, 3):
        sfx("player_hurt_%d" % i, c, "male adventurer pain grunt", 0.7)
    sfx("player_downed", c, "adventurer collapsing with groan", 1.5)
    sfx("player_death", c, "adventurer death cry and body fall", 2.0)
    sfx("revive", c, "magical revival gasp and holy rising chime", 2.0)
    for i in range(1, 3):
        sfx("mob_death_%d" % i, c, "dungeon monster death screech and collapse", 1.2)
    for i in range(1, 3):
        sfx("boss_roar_%d" % i, c, "gigantic monstrous boss roar", 2.5)
    sfx("boss_death", c, "giant boss death roar and crumbling collapse", 3.0)
    for i in range(1, 5):
        sfx("footstep_stone_%d" % i, c, "single boot footstep on stone floor", 0.5)
    c = "world"
    for s, p, d, l in [
            ("door_open", "heavy wooden dungeon door creaking open", 1.8, False),
            ("door_close", "heavy dungeon door slamming shut", 1.2, False),
            ("vault_unlock", "heavy metal vault lock clunking and unlocking", 2.0, False),
            ("chest_open", "wooden treasure chest opening with creak", 1.3, False),
            ("key_pickup", "small metal key jingle pickup", 0.8, False),
            ("shrine_activate", "mystical shrine awakening resonant tone", 2.0, False),
            ("extract_channel_loop", "magical channeling energy hum rising portal", 4.0, True),
            ("extract_success", "triumphant escape magical whoosh and chime", 2.0, False),
            ("spike_trap", "metal spikes shooting out of floor", 0.8, False),
            ("gas_vent_loop", "poison gas venting hiss steady", 4.0, True),
            ("lava_loop", "bubbling lava ambient steady", 5.0, True),
            ("torch_loop", "wall torch fire crackling steady", 5.0, True),
            ("loot_pickup", "loot item pickup bright clink", 0.6, False),
            ("silver_pickup", "silver coins pickup jingle", 0.7, False)]:
        sfx(s, c, p, d, l)
    c = "ui"
    for s, p, d in [("ui_click", "short crisp UI button click", 0.5), ("ui_hover", "soft subtle UI hover tick", 0.5),
                    ("ui_confirm", "UI confirm positive stone-and-metal chime", 0.8),
                    ("ui_error", "UI error denied low thud buzz", 0.7),
                    ("round_start_horn", "war horn blast signalling round start", 2.0),
                    ("countdown_tick", "single countdown tick beep", 0.5)]:
        sfx(s, c, p, d)
    c = "ambience"
    sfx("dungeon_ambience_loop", c, "dark dungeon ambience, distant drips, low wind, faint echoes, eerie drone", 30, True)
    sfx("boss_room_loop", c, "ominous boss chamber ambience, deep rumbling drone, tension, distant growls", 20, True)
    music()
    lines = ["The Sovereign Below has fallen.", "The way out has opened at the heart of the dungeon.",
             "Something old walks the halls, and it carries a relic.",
             "A vault's lock has failed. It will not stay open long.",
             "A shrine burns bright: one soul brings back the dead, for a little while.",
             "The doors are open.", "The extracts are open.", "The way out lies beneath the Sovereign.",
             "Round over.", "You have extracted.", "You have fallen."]
    for t in lines:
        speech(snake(t), "voice/announcer", t, DANIEL)
    taunts = {
        "iron_juggernaut": "Your steel will rust in my shadow.",
        "hive_matron": "My children are hungry, and you are small.",
        "ashen_colossus": "I am the ash of kingdoms. Join them.",
        "glasswright": "I shall shatter you into a thousand pieces.",
        "bone_choir": "Sing with us, little soul. Sing your last.",
        "hound_of_vesh": "Run. The hunt is the best part.",
        "lantern_wraith": "Follow my light into the dark.",
        "plague_herald": "Breathe deep. The rot is patient.",
        "the_sovereign_below": "Kneel. Every crown in this dungeon is mine."}
    for k, t in taunts.items():
        speech(k, "voice/boss", t, KALLIXIS)


if __name__ == "__main__":
    start = used()
    build()
    print("spent", used() - start, "failed", failed)
