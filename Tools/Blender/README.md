# Blender character generator

`fonda_figure.py` builds every customer of *La Fonda de la Abuela*, plus the grandma's sleeves
and bracelet, with Python only (no `.blend` files). **The script is the source of truth**: change
the script and re-run it; never hand-edit an exported file.

Built and checked with **Blender 4.5.16 LTS** (any 4.2 LTS or newer should work).

## Run

macOS:

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P Tools/Blender/fonda_figure.py -- --type all
```

Linux (headless, no GPU): Workbench needs OpenGL, so wrap Blender in a virtual X server
(Mesa software GL works):

```bash
xvfb-run -a -s "-screen 0 1280x1024x24" blender -b --factory-startup -P Tools/Blender/fonda_figure.py -- --type all
```

Run from the repo root (paths in the logs and the report are relative to it). A full run takes
about 30 s.

## CLI options (after `--`)

| Option | Default | Meaning |
|---|---|---|
| `--type all` | | Everything: 8 customers, line-ups, grandma pieces, contact sheet, `report.json` |
| `--type customers` | | The 8 customers: previews, FBX, re-import check, both line-ups |
| `--type grandma` | | `Grandma_Sleeve_L/R.fbx`, `Grandma_Bracelet.fbx` and their renders |
| `--type <Type>` | | One customer, e.g. `--type Office` (renders + FBX + check) |
| `--type test` | `test` | Neutral test figure (base body + faces) |
| `--type parts` | | Prompt 2 check: one line-up per part group in `previews/parts/` |
| `--out DIR` | `Assets/Art/Characters` | Where the `.fbx` files and `Fonda_Palette.png` go |
| `--previews DIR` | `Tools/Blender/previews` | Where renders go |
| `--report FILE` | `Tools/Blender/report.json` | Report written by `--type all` |
| `--no-render` | | Skip renders |
| `--no-export` | | Skip FBX export and the re-import check |
| `--json FILE` | | Also dump that run's raw results as JSON |

Use `--out` with a scratch folder for `--type test`, so `Customer_Test.fbx` stays out of the
Unity project.

## Outputs

- `Assets/Art/Characters/Customer_<Type>.fbx` (8), `Grandma_Sleeve_L.fbx`,
  `Grandma_Sleeve_R.fbx`, `Grandma_Bracelet.fbx`
- `Assets/Art/Characters/Fonda_Palette.png`: 256 x 256, 16 x 16 swatches of 16 px, sRGB.
  It is shared by every FBX through the single material `Fonda_Characters`. Colours are sorted, so
  re-runs produce the same bytes. Unused cells are magenta, so a wrong UV is easy to spot.
- `Tools/Blender/previews/`: `<Type>/{front,three_quarter,side,seated}.png`,
  `lineup_standing.png`, `lineup_seated.png`, `Grandma/*.png`, `parts/*.png`, `Test/*.png`,
  `contact_sheet.png`, `smoke.png`
- `Tools/Blender/report.json`: per file: objects, hierarchy, triangles, height, bounding box,
  re-import check, and the root transform as written in the FBX. Also the seated/bar check and
  the re-run comparison.

## Adding a customer type

Add **one entry** to `TYPES` in `fonda_figure.py`:

```python
"Musician": dict(
    es="Músico", size=1.0, wide=1.0,
    skin="#C68A5A", hair="#2B2522", hair_style="fringe",
    shirt="#8B3A3A", trousers="#2A3242", shoes="#1E1E24",
    clothing=["buttons"], hat="cap", hat_colour="#3E7C59", glasses="sunglasses"),
```

Then run `--type all`. New colours are added to the palette automatically. Because the palette
is sorted, swatch positions can move, so **re-export every FBX** (that is what `--type all`
does) and commit the palette together with all FBX files.

Keys (all optional except the colours):

| Key | Values |
|---|---|
| `size`, `wide` | overall scale (child 0.72, adult ~1.0) and body width factor W |
| `skin`, `shirt`, `trousers`, `shoes` | hex colours |
| `hair_style`, `hair` | `short`, `fringe`, `bald`, `bun` + colour |
| `brows`, `mustache` | colours (brows default to the hair colour) |
| `cheeks` | `True` for blush on every face |
| `neutral_brows` | `flat`, `worried`, `frown` (Neutral face only) |
| `neutral_mouth` | `line`, `grin` (Neutral face only) |
| `hat`, `hat_colour` | `helmet`, `cap`, `boina`, `straw`, `sunhat`, `beanie`; `hat_colour` is used by `cap` |
| `headphones` | `True` |
| `glasses` | `glasses`, `sunglasses` |
| `clothing` | list of `tie`, `buttons`, `stripes`, `hivis`, `backpack`, `floral`, `camera` |
| `hand_r`, `hand_l` | `briefcase` / `purse` (right), `cane` (left) |
| `shorts` | `True` |
| `es` | Spanish name used in the game |

New parts go in the "Optional parts" section (`build_hat`, `add_clothing`, ...). Give them
positions in the sketch's figure units and add their colours to `PART_COLOURS`.

## Conventions the Unity code can rely on

```
Customer_<Type>          root empty at the feet (squash & stretch)
  Hips                   empty at hip height
    Leg_L, Leg_R         mesh, origin at the hip joint (rotate -90 on X to sit)
    Torso                mesh, origin at the hips
      Arm_L, Arm_R       mesh, origin at the shoulder (arm tilt baked into the mesh)
        Briefcase/Purse/Cane   hand-held prop, origin at the hand
      Head               mesh, origin at the neck (nod / shake)
        Face_Happy, Face_Neutral, Face_Angry   enable exactly one
        Hair, Hat, Headphones, Glasses         when the type uses them
```

- No mesh has rotation or scale; pivots only have a position.
- Customers look toward Blender -Y, which is Unity +Z (forward).
- `Face_Happy` and `Face_Angry` are exported hidden (FBX `Visibility = 0`). With
  **Import Visibility** on (the default), Unity disables their renderers.
- Seated pose (Blender axes): rotate both legs -90° on X so the feet point forward, and put the root at
  `seat_height - 0.10 * size * 1.55` (0.465 m for an adult on a 0.62 m stool). The thighs then
  rest on the seat. Unity's handedness flip can change the sign; use whichever sign sends the
  feet toward the customer's front (Unity +Z).
- Grandma pieces: origin at the wrist (sleeves: centre of the wrist opening; bracelet: ring
  centre). The sleeve runs toward Unity +Z (the elbow), and the flowers are on Unity +Y.
  The sleeve cuff ends at the wrist plane, so offset the bracelet about 1 cm toward the hand
  (the renders use 8 mm), or it sits inside the cuff.

### Unity import settings

- **Bake Axis Conversion: ON.** The export uses the spec's `bake_space_transform=False`, so each
  FBX root is written with `Lcl Rotation = (-90, 0, 0)` (see `raw_fbx` in `report.json`).
  Without baking, Unity shows X -90° on the root object, and code that sets the root rotation
  would tip the character over.
- Materials: extract or remap to one material using `Fonda_Palette.png`. On the texture set
  **Filter Mode = Point** and **Generate Mip Maps = off**, so swatches never bleed.
- Units: the files declare `UnitScaleFactor = 100` (centimetres), and the export uses
  `apply_scale_options='FBX_SCALE_ALL'`. This was not verified in Unity (not available where the
  files were built). After import, check that an adult is about 1.33 m tall to the top of the
  hair and the root scale is 1.

## Deviations from the original spec (and why)

1. **Bevel segments**: 3 on head and torso, **1** on limbs and details (the spec allows 1–2).
   With 2 segments the base body alone was 1,568 triangles, which left no room for 3 faces and
   accessories under 2,500.
2. **Export `use_selection=False` + `collection=<character>`** instead of `use_selection=True`.
   Blender cannot keep hidden objects selected, so the hidden faces were silently dropped. Each
   character lives in its own collection, so the exported set is exactly that character.
3. **`path_mode='STRIP'`** instead of `'COPY'`. COPY wrote a `Customer_X.fbm/` copy of the
   palette per file. STRIP references `Fonda_Palette.png`, which sits next to every FBX. (The
   exporter also writes an absolute `Filename` field built from the export folder; Unity uses
   the relative name.)
4. **Helmet ridge** follows the dome as an arc. The spec's straight 0.46 bar at y 0.93 floats
   about 7 cm above the dome's front and back.
5. **Clear glasses** get ink pupils on the lens. The palette material is opaque, so the eyes
   behind a clear lens would disappear.
6. **Torso details** (tie, buttons, straps, flowers, camera straps) sit on the rounded torso
   surface instead of a fixed z 0.118, so they don't float near the shoulders.
7. **Stripes / hi-vis** are rounded slabs that follow the torso outline. A beveled box of the
   spec's size showed only as jagged slivers.
