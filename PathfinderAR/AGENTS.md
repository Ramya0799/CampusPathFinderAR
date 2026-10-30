# PathFinder AR – agent notes (read before changing anything)

PathFinder is a smart campus navigation app for AIET Moodbidri (Admin Block + Teaching Block).
This Unity project (Unity 6.3, Built-in render pipeline, Android, AR Foundation + ARCore) shows
AR arrows from a chosen start point to a chosen room.

## Scene: Assets/PathFinderAR.unity
- `AR Session`, `XR Origin` – the AR rig. Do NOT add a second AR Session, XR Origin or AR camera.
- `PathFinder_Campus_3D` – campus model (Assets/Models/Campus/PathFinder_Campus_3D.fbx), generated
  from the architectural PDFs. Children: AdministrativeBlock, TeachingBlock (one child per floor,
  e.g. TB_GF, AB_FF), Site, NavigationAnchors, ReferenceMarkers. Buildings must stand upright:
  in Play mode INFO must show "Model frame (baked …) … up (0.0, 1.0, 0.0)".
- `CampusNavMesh` – NavMeshSurface (AI Navigation 2.0). Agent Humanoid: radius 0.2, height 1.8,
  step 0.4, slope 45. Collect: All Game Objects, Render Meshes. Data asset:
  Assets/PathFinderAR/NavMesh-CampusNavMesh.asset. A correct bake has its height range roughly
  -10..30 m (model RL 54..90); a bake made while the model lay on its side had 4695 vertices with
  bounds (-58.5, -52.3, -0.6)..(55.6, 55.9, 31.8).
- After ANY change to the model, use the menu **PathFinder > Bake + Save NavMesh**
  (Assets/Scripts/PathFinder/Editor/PathFinderCampusSetup.cs). It refuses to run in Play mode, bakes
  CampusNavMesh, saves the asset and the scene. Don't bake by hand.
- `PathFinderNavigator` – PFCampusNavigator component (Campus Root = PathFinder_Campus_3D,
  Route Material = Assets/Models/Campus/PF_Route.mat, shader Unlit/Color).

## Navigation code: Assets/Scripts/PathFinder/
- `PFCampusNavigator.cs` – MonoBehaviour. Loads data, adds stair/lift/step NavMesh links,
  computes routes with NavMesh.CalculatePath, places arrows, ALIGN calibration, 2D MAP fallback,
  IMGUI interface, INFO diagnostics panel.
- `PFNavData.cs` – JSON data classes + PFCampusFrame (maps drawing coordinates to Unity world by
  fitting known mesh bounding-box centres; independent of how the FBX was rotated on import).
- `PFRouteGuide.cs` – turn-by-turn text ("Turn left, then go 12 m", "Take the … staircase up to …")
  and the arrow mesh.
- `Editor/PathFinderCampusSetup.cs` – the Bake + Save NavMesh menu above.
- `Editor/PathFinderFbxPostprocessor.cs` + `PathFinderNodeInfo.cs` – copy the FBX custom properties
  (room names, floor ids, anchor data) onto PathFinderNodeInfo components at import.
- Namespace `PathFinderNav`. No dependency on scripts outside this folder.

## Data: Assets/Resources/PathFinderNavData.json
- `floors` (id, name, shortName, building, rl)
- `destinations` (127 rooms: name, type, floor, building, x, y, rl) – rename rooms here
  (e.g. "Classroom 3" -> real room number); positions are drawing metres.
- `links` (stairs/lift/steps between floors, from the UP/DN marks on the drawings; some flagged
  `approximate`), `startPoints` (17 calibration points with facing direction), `frameRefs`.
- Coordinates: x = drawing right, y = drawing up (metres), origin = Teaching Block north entrance,
  rl = reduced level in metres (model height = rl - 60).
- `Assets/Resources/campus_data.json` belongs to the older code – leave it alone unless asked.

## How AR alignment works
User picks a start point, stands there facing the described direction, presses ALIGN HERE.
The script maps drawing coordinates to AR session space (yaw + translation, floor = camera height
minus eyeHeight 1.35 m). Nudge buttons correct small errors; RE-ALIGN at another start point
(e.g. a lift lobby on another floor) when drift builds up. The campus model is hidden on device.

## Deep linking from Web App

The app supports deep linking from the PathFinder web application:
* **URL format:** `pathfinder://navigate?dest=<destination id>&start=<start point name>`
  * `dest`: `destinations[].id` from `Assets/Resources/PathFinderNavData.json`, URL-encoded (e.g. `pathfinder://navigate?dest=TB_1F%3AStaff%20Room`).
  * `start`: optional `startPoints[].name`, URL-encoded.
* Handled in `PFCampusNavigator.cs` via `Application.deepLinkActivated` and `Application.absoluteURL`.
* Android manifest registration: `scheme="pathfinder"`, `host="navigate"`, intent-filter with `VIEW`, `DEFAULT`, `BROWSABLE`.
* Editor test: Right-click `PFCampusNavigator` > **Test deep link**.

## Rules for changes
- Keep changes inside Assets/Scripts/PathFinder unless the user asks otherwise; show a diff first
  for any existing script outside it.
- Don't move/rotate/scale PathFinder_Campus_3D or CampusNavMesh without re-baking the NavMesh.
- Don't touch XR Origin / AR Session settings unless the user asks.
- Built-in render pipeline: use Standard or Unlit/Color shaders (no URP shaders).
- Verify with Play mode in the Editor: pick a destination + start point, check arrows in the
  Scene view and the INFO panel (links added, walkable vertices per level).

## Model orientation requirement & FBX import quirk

Buildings must stand upright: INFO must show
`Model frame (baked ...) ... up (0.0, 1.0, 0.0)`.
Use the menu **PathFinder > Bake + Save NavMesh** after any change to the model.

> In this project PathFinder_Campus_3D is upright at rotation (270, 180, 0); at
> (0,0,0) it stands on its edge. Bake + Save NavMesh stores the model frame -
> re-run it after any change to the model or its transform.

## NavMesh baking – always use the menu tool

**Never** bake the NavMesh manually via the Navigation window or by
clicking Bake on the NavMeshSurface inspector alone.  
Always use **PathFinder > Bake + Save NavMesh**.
This tool:
1. Refuses to run in Play mode.
2. Runs `PFCampusFrame.Solve()` in Edit mode (requiring method == "meshes", rms < 0.5 m, tilt < 3 deg; otherwise logs error "Model is not upright - fix rotation" and writes nothing).
3. Stores `bakedFrameLocal` and `hasBakedFrame = true` on `PFCampusNavigator` (Undo-recorded, dirty-marked).
4. Calls `NavMesh.RemoveAllNavMeshData()` to flush any stale bake from memory.
5. Bakes `CampusNavMesh` synchronously.
6. Checks `sourceBounds.size.y ≤ 40 m` AND triangulation Y-range ≤ 40 m **before** writing
   the asset (if > 40 m, logs error "Model is on its side - fix PathFinder_Campus_3D rotation", writes nothing).
7. Saves the NavMeshData to `Assets/PathFinderAR/NavMesh-CampusNavMesh.asset`.
8. Saves the scene so the `.unity` and `.asset` are always in sync.
9. Logs the campus rotation, vertex count, bounds, tilt, rms, and refs.

## File-change restrictions

* Only edit files under `Assets/Scripts/PathFinder/` and `AGENTS.md`.
* Do **not** modify: FBX files, JSON data files, XR Origin, AR Session.

## Compile requirements

* All changes must compile with 0 errors in Unity (no editor-only APIs in runtime code).
* The `Editor/` folder is wrapped in `#if UNITY_EDITOR` guards.

## Known limitations / next tasks
- Campus Root must reference the scene object, not the prefab asset (guarded in code).
- Model revision 28 Oct 2026: road surfaces (Site_Roads) were exported upside down in the first FBX
  and are fixed in the current one.
- Approximate data: lift door sides, Admin food-court stair end, Admin first floor -> seminar
  gallery, seminar hall floor <-> gallery connection, Teaching 1F/2F levels (assumed 75.54 / 79.35).
- Not yet tested on a phone (build Android, test ALIGN at the Teaching Block main entrance).
- Possible future: image-target alignment, real room numbers, UI with uGUI instead of IMGUI,
  linking with the rest of the PathFinder app.

