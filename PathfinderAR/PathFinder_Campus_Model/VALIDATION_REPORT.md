# PathFinder campus model — validation report

Generated 28 Oct 2026 from the two supplied PDFs. Everything below was checked in this build; items
that could not be checked are listed as such.

## A. Source inspection

| Document | Pages | Content used |
|---|---|---|
| ADMIN BLOCK FLOOR PLANS.pdf (18/12/2025) | 4 | p1 Ground floor, p2 First floor, p3 2nd floor / seminar-hall upper gallery. **p4 is an exact duplicate of p3.** |
| TEACHING BLOCK FLOOR PLANS.pdf (17/03/2025) | 6 | Sheets 01–06 of 08: LG03, LG02, LG01 + food-court level, GF, 1F, 2F. **Sheets 07–08 not supplied.** |

All pages are vector CAD exports; geometry was read from the vector paths (not from a raster trace)
and every page was also rendered and inspected visually.

The Teaching sheets also show an older (Mar-2025) Admin Block layout. The model uses the newer
Dec-2025 Admin set for GF/1F/2F and the Mar-2025 sheet only for the food-court level (+67.16), which
the Dec-2025 set references (stair landing "67.16 (+23'-0")") but does not draw.

## B. Scale, registration and dimension checks

| Check | Result |
|---|---|
| Teaching sheets scale | 22.50 m dimension = 230.363 pt and 17.50 m = 179.205 pt → 10.238 / 10.240 pt/m (agree to 0.02 %) |
| Teaching sheet-to-sheet registration | Translation to the shared dimension-line vertex (sheet 05 is offset 128 pt on the page; corrected) |
| Admin sheets → site | 14 structural columns matched to Teaching sheet 04; RMS residual 0.8 pt (~0.08 m); Admin p2 drawn at a slightly different scale, registered to p1 through 23 columns |
| Teaching classroom 42'3" × 31'6" (12.88 × 9.60 m) | traced 12.84 × 9.56 m (−0.3 %) on GF and 1F |
| Teaching classroom 31'6" × 48'0" (9.60 × 14.63 m) | traced 9.56 × 14.60 m |
| Admin lift 7'2" × 6'0" (2.18 × 1.83 m) | traced 2.12 × 1.80 m |
| Admin interview cubicle 8'0" × 9'3" (2.44 × 2.82 m) | traced 2.40 × 2.76 m |

## C. Floor levels

| Floor | RL (m) | Status |
|---|---|---|
| TB LG03 / LG02 / LG01 / GF | 60.61 / 64.26 / 67.92 / 71.73 | printed on sheets |
| TB 1F | 75.54 | **assumed** = Admin first floor (+50'6"); not printed on sheet 05 |
| TB 2F | 79.35 | **assumed** (3.81 m storey); not printed on sheet 06 |
| TB roof | 83.16 | **assumed** |
| Admin food-court level / GF / FF | 67.16 / 71.73 / 75.54 | printed |
| Admin seminar hall floor | 79.65 | printed (note: drawing labels it +65'-0", which does not match the other ft/m pairs; the metric value was used) |
| Admin upper gallery | 83.21 → 87.67 (rows), stair landing 86.97 | printed spot levels; row heights between them interpolated |
| Admin roof | 91.00 | **assumed** |

## D. Model content (PathFinder_Campus_3D.fbx)

| Item | Count / value |
|---|---|
| Objects | 759 (324 meshes, 435 empties) |
| Triangles | ~153 k |
| Materials | 28, every mesh has a material |
| Floors | 10 (6 Teaching, 4 Admin) |
| Labelled spaces in inventory | 361 (151 toilets/WC cubicles, 63 corridors, 36 classrooms, 21 lift/lift-lobby, …) |
| Room floor zones | 245 (214 traced from the drawn walls, 31 approximated from the printed size at the label) |
| Navigation anchors | 414 (362 room/corridor labels, 30 stair ends, 16 lift stops, 6 entrance step ends) |
| Bounding box | 127.5 × 172.4 m plan, 32.9 m high (RL 59.15 – 92.00) |

Re-import test (Blender FBX importer, fresh scene) for all three files: files open, hierarchy and names
intact, custom properties present on 754 of 759 objects, no object with non-unit scale, no mesh
without a material. FBX header: FBX 7.4, Y-up, UnitScaleFactor 100 (cm) with geometry baked in cm,
so Unity's "Convert Units" gives 1 unit = 1 m.

**Not tested here:** import into the actual `unity-ar-navigation` project (the project was not
available in this session), Unity axis orientation (use the reference markers, see README), and
anything on a physical device.

## E. Accuracy classes

| Element | Class |
|---|---|
| Wall, column and partition outlines; door gaps; window positions; floor outlines; courtyard and double-height voids | **Traced** from the vector drawings (±~0.1 m) |
| Window sill 0.90 m / head 2.10 m, door lintel 2.10 m, slab 0.15 m, railing 1.0 m, parapet 1.0 m | **Assumed** standard values (no sections/elevations supplied) |
| Stair positions | Teaching: traced from the stair-tread layer. Admin: digitised from the plans |
| Stair flights (tread count, direction, landing) | **Schematic** — they connect the right floors in the right core, but are not detailed stair designs |
| Seminar-hall gallery tiers | **Approximate** rows between printed spot levels; stage height assumed 0.75 m |
| Food-court level | From the Mar-2025 sheet; may differ from the current design |
| Teaching LG01 / food-court split | At y = 56.3 m (site frame); the NE lobby and boys' wash room there are modelled at +67.16 although parts may be at +67.92 |
| Terrain | **Provisional**: interpolated from 9 printed spot levels plus assumed fill points, smoothed |
| Roads | **Approximate** centrelines digitised from the site sheets; widths from labels (23'0", 16'0", 16'6"); NH-169 carriageway width assumed 10 m |
| Existing library, existing Admin Block 1, house | **Massing only**: outlines traced from the site sheet, heights assumed (11 m, 12 m, 6 m) |
| Room zones marked "approximate" | Rectangle of the printed size centred on the label, clipped to the floor |
| True north | From the drawing compass, 175° ± 2° from plan +X |

## F. Missing inputs for a fully accurate, campus-wide model

1. Teaching Block sheets 07–08 (likely roof / sections / elevations) and printed levels for 1F, 2F and roof.
2. Admin Block seminar-hall floor plan at +79.65 (p4 duplicates p3), roof level and sections.
3. Building sections/elevations for real sill, lintel, floor-to-floor and parapet heights.
4. A topographic survey (or at least more spot levels) for terrain, roads, retaining walls and ramps.
5. Plans for the existing library, existing Admin Block 1 and every other campus building.
6. A surveyed control point (or GPS + heading of the main entrance) to tie the model to the real campus for AR.

## G. Not done in this step

Unity scene integration, the floor-aware navigation graph (edges/pathfinding), AR alignment, the
blue AR-marker fix and the PathFinder app connection all require the existing `unity-ar-navigation`
project and app code, which were not accessible in this session. The navigation anchors and
custom properties in the FBX are laid out to feed that work.
