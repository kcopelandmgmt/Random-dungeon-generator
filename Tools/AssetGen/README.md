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
