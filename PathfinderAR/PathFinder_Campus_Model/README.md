# PathFinder campus model — AIET Moodbidri (Admin Block + Teaching Block)

3D model generated directly from the architectural drawings
`ADMIN BLOCK FLOOR PLANS.pdf` (Archvista, 18/12/2025) and `TEACHING BLOCK FLOOR PLANS.pdf`
(Archvista, sheets 01–06 of 08, 17/03/2025, drawing 2025-INS-58-CS-IP-DRA-ZO-001 V3 R2).

## What is in this package

| Path | Contents |
|---|---|
| `fbx/PathFinder_Campus_3D.fbx` | Both blocks, every floor, site context, navigation anchors, reference markers |
| `fbx/PathFinder_Admin_Block.fbx` | Administrative Block only (same origin as the campus file) |
| `fbx/PathFinder_Teaching_Block.fbx` | Teaching Block only (same origin as the campus file) |
| `blend/PathFinder_Campus_3D.blend` | Blender 5.2 source scene |
| `previews/` | Rendered views of the model |
| `data/room_inventory.json` | 361 labelled spaces with the fields from the brief (building, floor, room id/name/type, source page, dimensions, position, elevation, confidence) |
| `data/nav_anchors.json` | 414 navigation anchors (room labels, stair ends, lifts, entrances) in model coordinates |
| `data/floor_*.json`, `data/site.json` | Traced geometry per floor in site metres (walls, glazing, footprints, voids, room polygons, labels) |
| `scripts/` | Reproducible pipeline (PDF → JSON → Blender → FBX) |
| `unity/` | Optional Unity helpers that copy the FBX custom properties onto a component |
| `VALIDATION_REPORT.md` | What was checked, what is verified, what is approximate, what is missing |

## Model contents

Ten floors, each a separate object group so it can be shown, hidden or highlighted on its own:

| Block | Floor id | Level (RL, m) | Source |
|---|---|---|---|
| Teaching | `TB_LG03` | 60.61 | Teaching sheet 01 |
| Teaching | `TB_LG02` | 64.26 | sheet 02 |
| Teaching | `TB_LG01` | 67.92 | sheet 03 |
| Teaching | `TB_GF` | 71.73 | sheet 04 |
| Teaching | `TB_1F` | 75.54 *(assumed)* | sheet 05 |
| Teaching | `TB_2F` | 79.35 *(assumed)*, roof 83.16 *(assumed)* | sheet 06 |
| Admin | `AB_FC` (food-court level) | 67.16 | Teaching sheet 03 (Mar-2025 revision) |
| Admin | `AB_GF` | 71.73 | Admin p1 (Dec-2025) |
| Admin | `AB_FF` | 75.54 | Admin p2 |
| Admin | `AB_2F` (seminar hall + upper gallery) | 79.65, gallery 83.21–87.67, roof 91.00 *(assumed)* | Admin p3 |

Per floor: `<floor>_Slab`, `_Walls` (walls, columns, window sills/heads, door lintels), `_Glazing`,
`_Railings`, `_Roof`/`_Parapet` where nothing covers the floor, `_Zone_NN_<room>` floor patches for
labelled rooms (for destination highlighting), and schematic stair flights (`<floor>_<stair>_to_<floor>`).
Site: terrain, roads, plaza paving, courtyard lawn, and massing boxes for the existing library,
existing Admin Block 1 and the neighbouring house.

Hierarchy:

```
PathFinderCampus
├─ AdministrativeBlock ─ AB_FC, AB_GF, AB_FF, AB_2F, steps
├─ TeachingBlock ─ TB_LG03 … TB_2F, steps
├─ Site
├─ NavigationAnchors   (NAV_<floor>_<type>_<n>_<name>, NAV_<floor>_<stair>_Up/Down, NAV_<floor>_Lift_n)
└─ ReferenceMarkers    (PF_Origin, PF_Marker_PlanRight_10m, PF_Marker_PlanUp_10m, PF_Marker_Up_10m, PF_TrueNorth_10m, PF_NorthArrow)
```

Every object carries custom properties (`pfType`, `floorId`, `buildingId`, `rl`, `roomName`,
`roomType`, `confidence`, …) exported as FBX user properties.

## Coordinates

* Units: metres. Scale calibrated from the printed metric 17.50 m / 22.50 m dimension on every Teaching
  sheet (10.239 pt/m); Admin sheets registered to the Teaching site sheet through 14 shared columns.
* Origin: centre of the Teaching Block north entrance (top of the entrance steps). Height 0 = RL 60.00 m.
* Plan axes: model +X = drawing right, +Y = drawing up, +Z = up. True north is 175° CCW from +X
  (drawing up points roughly east).
* Export: Forward = Z, Up = Y, Apply Transform, FBX scale "All". With these settings Unity should place
  model (X, Y, Z) at Unity (x = X, y = Z, z = Y), i.e. the Unity top view matches the drawing.
  **Check after import:** `PF_Marker_PlanRight_10m` should sit at Unity (10, 0, 0),
  `PF_Marker_PlanUp_10m` at (0, 0, 10) and `PF_Marker_Up_10m` at (0, 10, 0). If the first two come in
  negated, rotate the imported root 180° about Y. Navigation code should read positions from the
  `NavigationAnchors` children rather than from the JSON, so it stays correct either way.

## Importing into Unity

1. Copy the `.fbx` files into `Assets/Models/Campus/` of `unity-ar-navigation`.
2. Model tab: Scale Factor 1, Convert Units on. Rig: None. Animation: off.
3. Materials tab: Use External Materials (Legacy) or extract them; set `PF_Glass` to a transparent
   surface type (URP Lit → Surface Type Transparent, alpha ≈ 0.35).
4. Optional: add `unity/PathFinderNodeInfo.cs` (runtime) and `unity/Editor/PathFinderFbxPostprocessor.cs`
   (editor) before importing; every imported object then gets a `PathFinderNodeInfo` component with the
   FBX metadata. These two scripts were written for this export but were not compiled here.
5. Colliders: avoid "Generate Colliders" on the whole model; add Mesh Colliders only to the
   `_Slab`, `_Walls` and `Site_Terrain` objects you need for raycasts.
6. The model origin is not tied to the real campus. For AR, place it with a calibration step
   (image target or known start point + heading) — see VALIDATION_REPORT.md.

## Regenerating

```
pip install pymupdf opencv-python shapely mapbox-earcut scipy scikit-image bpy   # bpy = Blender 5.x as a module
mkdir src_pdfs && cp "ADMIN BLOCK FLOOR PLANS.pdf" src_pdfs/admin.pdf && cp "TEACHING BLOCK FLOOR PLANS.pdf" src_pdfs/teaching.pdf
cd scripts
python3 run_extract.py      # PDF vectors -> data/floor_*.json (+ preview PNGs in analysis/floors)
python3 run_site.py         # site hatches -> data/site.json
python3 build_scene.py      # -> data/scene.json, room_inventory.json, nav_anchors.json
python3 blender_build.py    # -> output/*.fbx and .blend   (or: blender --background --python blender_build.py)
python3 validate_fbx.py     # re-imports the FBX files -> output/fbx_validation.json
python3 blender_render.py   # preview renders
```

Floor levels, clip regions, stair cores, steps, roads, assumed heights and the model origin are all
in `scripts/config.py`; edit there when a drawing revision or a site survey arrives.
