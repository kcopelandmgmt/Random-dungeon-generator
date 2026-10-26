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
