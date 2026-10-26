# AssetGen: Meshy pipeline

`meshy.py` (Python 3, standard library only) generates every model in its `ASSETS` list through the Meshy API and
writes it into `dungeongame/Assets/Generated/models/<slug>/`.

```
python -I meshy.py run [--only slug,slug] [--workers 6] [--reset-failed]
python -I meshy.py status        # ledger total and one line per asset
python -I meshy.py balance       # per-key credit balance
python -I meshy.py library idle  # search the Meshy animation library for action ids
```

Pipeline per asset: text-to-3D preview, refine (texture), then for humanoids rigging and one animation task per action.
Files per folder: `<slug>.fbx`, `<slug>_basecolor.png`, `<slug>_thumb.png`, `<slug>_rigged.fbx`,
`<slug>_anim_<action>.fbx` (walk and run come from the rig result), and `manifest.json`.

* Keys come from `.secrets/keys.env` (`MESHY_API_KEY_n`); never printed. Each new job takes the key with the most
  credits; refine, rig and animation reuse the key of the task they build on (task ids are per account).
* `models/ledger.json` records every task, key index, kind and credits; the tool refuses to exceed 4,500.
* Resumable: rerun the same command; finished steps are skipped, unfinished tasks are polled again. A failed step is
  retried once; `--reset-failed` grants fresh attempts.
* Regenerating a model: move its files into a subfolder (e.g. models/<slug>/_old, manifest included) so the skip logic sees it as pending, then run --only <slug>. Wave 5.5 (2026-10-26) redid ember_wisp, frostbound, ashen_colossus and glasswright with a shared MUTED suffix (muted dark-fantasy palette, warm torchlight accents); the first versions are in _old, a discarded second frostbound/glasswright try in _attempt1/_attempt2.
* Edit prompts, polycounts and animation action ids in the `ASSETS` list at the top of the file.

## elevenlabs.py

`python -I Tools/AssetGen/elevenlabs.py` generates all sound effects, announcer/boss speech and music into
`dungeongame/Assets/Generated/audio/<category>/<slug>.mp3` with `manifest.json` beside them (slug, file,
category, prompt, duration, loop, credits). Resumable (skips slugs already present), retries 429/5xx, stops
before the 60,000-character ceiling. `--no-music` makes 30 s looping ambient beds instead of music tracks.
Key: `ELEVENLABS_API_KEY` in `.secrets/keys.env`.

## meshy_images.py (ability icons)

`python -I Tools/AssetGen/meshy_images.py run --model gpt-image-2` generates one painted icon per ability (all 69 ids of
`AbilityBook.All`, prompt = shared style suffix + a hand-written motif + element colour key; ultimates get an "ornate" clause)
through Meshy text-to-image into `dungeongame/Assets/Generated/icons/<abilityId>.png` (1024x1024) with `manifest.json` beside them
(prompt, model, task, key, credits). Resumable and concurrent (6 workers); `--only a,b` limits, `--redo a,b` regenerates (old file
kept as `.old`), `--reset-failed`, `status`. `pilot` renders 3 icons per candidate model into `icons/_pilot`. Tasks go in the shared
`models/ledger.json` (kind `image`, 4,500 ceiling). Pilot (2026-10-26): `nano-banana` and `nano-banana-2-lite` cost 3 credits but draw
an inset square frame despite "no border"; `gpt-image-2` costs 9, fills the canvas, no frame, no text: chosen (69 icons = 621 credits).
Then `bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.AbilityIconBaker.FromImages` boxes them to 128 px, adds the element
frame (gold double line for ultimates) into `Assets/UI/AbilityIcons/` and refreshes the lookup; any ability with no image falls back to
the old particle baker (`AbilityIconBaker.Bake` still bakes everything that way).

### Item icons (`--set items`)

`python -I Tools/AssetGen/meshy_images.py run --set items --model gpt-image-2` paints one inventory icon per item base (the 28 of
`ItemBase.All`: 11 weapons, 15 armour pieces, Amulet, Ring), per consumable (`Consumable.All`) and for the purse (Silver, Scrap, Prime
Shards, Essence): 35 icons into `dungeongame/Assets/Generated/item-icons/<slug>.png` (slug = name lower-cased with underscores:
`plate_helm`, `health_potion`) with `manifest.json` beside them. Prompt = `ITEM_STYLE` (single object, centred, plain dark background,
no frame, no text) + a motif + a key: armour weight is grouped by material (Heavy = dark steel plate, Medium = brown leather with iron
studs, Light = plum and blue cloth and silk), magic weapons get their element glow. Ledger entries use slug `item/<slug>`; the set has its
own ceiling, `ITEM_BUDGET` = 330 credits including retries (`status --set items` prints what it has spent). First run (2026-10-26):
35 icons, 315 credits, no regenerations. Then `bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.ItemIconSetup.Run` (also
greybox step 35) imports them as 256 px mipmapped sprites and writes the Resources lookup `item-icons/Resources/ItemIcons.asset` read by
`ItemIcons.For(item or name)`, plus `item-icons/Resources/PaperDollModels.asset` (the hero entries of the avatar library, for the
Stash & Equipment screen's live model).

## portrait_sheets.py

Studio portraits of every class, boss and mob come from the PlayMode test `PortraitTests` (category Portraits; runs with `unity-batch.sh tests PlayMode`) into `dungeongame/Logs/portraits/<group>-<slug>.png`. `python -I Tools/AssetGen/portrait_sheets.py` lays them out as labelled sheets (`sheet-classes.png`, `sheet-bosses.png`, `sheet-mobs.png`). Needs Pillow.

## gear_sheets.py

The PlayMode test `GearShowcaseTests` (category Showcase) rolls items per dungeon tier through the real `LootRoller` (item level = room loot tier + the tier's offset, the room's source, the tier's unique multiplier), renders eight tooltip cards per tier (`ItemText.Tooltip` in the game's tooltip panel style) to `dungeongame/Logs/gear/tier<N>-<k>.png` with `drops.json` beside them, and captures the Stash & Equipment screen for a Warden in the starter kit and in a Tier III set (`paperdoll-starter.png`, `paperdoll-t3.png`). `python -I Tools/AssetGen/gear_sheets.py` lays the cards out as `sheet-tier1.png` to `sheet-tier3.png` with a title and the tier's item-level range. Needs Pillow.
