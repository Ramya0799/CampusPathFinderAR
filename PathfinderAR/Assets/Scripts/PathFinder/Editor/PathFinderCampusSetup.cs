// PathFinder – Editor tool: bake NavMesh and save atomically.
// Menu: PathFinder > Bake + Save NavMesh
//
// Rules:
//  - Editor-only (lives in Assets/Scripts/PathFinder/Editor/)
//  - Touches nothing in XR Origin / AR Session, no FBX files, no JSON files.
//  - If there is more than one NavMeshSurface in the scene, reports and stops.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using PathFinderNav;

namespace PathFinderNav.Editor
{
    public static class PathFinderCampusSetup
    {
        private const string MenuPath = "PathFinder/Bake + Save NavMesh";
        private const string NavMeshAssetPath = "Assets/PathFinderAR/NavMesh-CampusNavMesh.asset";

        [MenuItem(MenuPath)]
        public static void BakeAndSaveNavMesh()
        {
            // Guard: must not run in Play mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "PathFinder - Bake + Save NavMesh",
                    "Stop Play first, then run this menu item.",
                    "OK");
                return;
            }

            // Find PFCampusNavigator in the open scene
            var navigator = Object.FindFirstObjectByType<PFCampusNavigator>();
            if (navigator == null)
            {
                Debug.LogError("[PathFinder] Bake + Save NavMesh: PFCampusNavigator not found in the open scene.");
                return;
            }

            // Resolve and store the scene object in navigator.campusRoot
            Undo.RecordObject(navigator, "Bake + Save NavMesh: resolve campusRoot");
            Transform campusRoot = navigator.ResolveCampusRoot();
            EditorUtility.SetDirty(navigator);

            if (campusRoot == null)
            {
                Debug.LogError("[PathFinder] Bake + Save NavMesh: campusRoot not found.");
                return;
            }

            // Load nav data to get frame references
            var navDataAsset = navigator.navData != null
                ? navigator.navData
                : Resources.Load<TextAsset>("PathFinderNavData");

            PFNavDataFile navDataFile = null;
            if (navDataAsset != null)
                navDataFile = JsonUtility.FromJson<PFNavDataFile>(navDataAsset.text);

            var frameRefs = navDataFile != null ? navDataFile.frameRefs : null;

            // Solve frame in Edit mode BEFORE baking
            PFCampusFrame frame = PFCampusFrame.Solve(campusRoot, frameRefs);
            if (!frame.valid)
            {
                Debug.LogError("[PathFinder] Model is not upright - fix rotation (frame solve failed: " + frame.error + ")");
                return;
            }

            float tilt = Vector3.Angle(frame.upUnit, Vector3.up);
            if (frame.method != "meshes" || frame.rms >= 0.5f || tilt >= 3f)
            {
                Debug.LogError("[PathFinder] Model is not upright - fix rotation (method=\"" + frame.method +
                               "\", rms=" + frame.rms.ToString("0.00") + " m, tilt=" + tilt.ToString("0.00") + " deg).");
                return;
            }

            Debug.Log("[PathFinder] Bake + Save NavMesh: Frame solved (" + frame.method + ") – tilt = " +
                      tilt.ToString("0.00") + " deg, rms = " + frame.rms.ToString("0.00") + " m, refs = " + frame.refsUsed);

            // Store baked frame in PFCampusNavigator
            Undo.RecordObject(navigator, "Bake + Save NavMesh: store baked frame");
            navigator.bakedFrameLocal = campusRoot.worldToLocalMatrix * frame.M;
            navigator.hasBakedFrame = true;
            EditorUtility.SetDirty(navigator);

            // Find NavMeshSurface(s) in the scene
            var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            if (surfaces.Length == 0)
            {
                Debug.LogError("[PathFinder] Bake + Save NavMesh: no NavMeshSurface found in the scene.");
                return;
            }
            if (surfaces.Length > 1)
            {
                var names = new System.Text.StringBuilder();
                foreach (var s in surfaces) names.Append("  * ").Append(s.gameObject.name).Append('\n');
                string msg = "Found " + surfaces.Length + " NavMeshSurface components:\n\n" + names.ToString() +
                             "\nPlease remove duplicates and run again.";
                Debug.LogWarning("[PathFinder] Bake + Save NavMesh: " + msg);
                EditorUtility.DisplayDialog("PathFinder - Multiple NavMeshSurfaces", msg, "OK");
                return;
            }

            NavMeshSurface surface = surfaces[0];
            Debug.Log("[PathFinder] Bake + Save NavMesh: baking NavMeshSurface on \"" +
                      surface.gameObject.name + "\" (agentTypeID=" + surface.agentTypeID + ")...");

            // Flush any stale NavMesh data from memory so CalculateTriangulation()
            // only reflects the fresh bake.
            NavMesh.RemoveAllNavMeshData();

            // Bake synchronously
            surface.BuildNavMesh();

            // ── Sanity-check the NEW bake BEFORE writing the asset ─────────────
            NavMeshData data = surface.navMeshData;
            if (data == null)
            {
                Debug.LogError("[PathFinder] Bake + Save NavMesh: BuildNavMesh produced no NavMeshData.");
                return;
            }

            // 1) Check sourceBounds.size.y from the NavMeshData itself (always available).
            float boundsHeightY = data.sourceBounds.size.y;
            if (boundsHeightY > 40f)
            {
                Debug.LogError("[PathFinder] Model is on its side - fix PathFinder_Campus_3D rotation (sourceBounds.size.y = " +
                               boundsHeightY.ToString("0.0") + " m > 40 m). Scene NOT saved, asset NOT overwritten.");
                return;
            }

            // 2) Also verify via triangulation vertices (now only the new bake is loaded).
            NavMeshTriangulation triCheck = NavMesh.CalculateTriangulation();
            int vertCount = triCheck.vertices != null ? triCheck.vertices.Length : 0;
            if (vertCount > 0)
            {
                Vector3 mn2 = triCheck.vertices[0], mx2 = triCheck.vertices[0];
                foreach (var v in triCheck.vertices) { mn2 = Vector3.Min(mn2, v); mx2 = Vector3.Max(mx2, v); }
                float heightRange2 = mx2.y - mn2.y;
                if (heightRange2 > 40f)
                {
                    Debug.LogError("[PathFinder] Model is on its side - fix PathFinder_Campus_3D rotation (triangulation height range = " +
                                   heightRange2.ToString("0.0") + " m > 40 m). Scene NOT saved, asset NOT overwritten.");
                    return;
                }
            }
            else
            {
                Debug.LogWarning("[PathFinder] Bake + Save NavMesh: CalculateTriangulation returned 0 vertices " +
                                 "immediately after bake (normal in Edit mode). sourceBounds check passed.");
            }

            // Ensure the target directory exists
            string dirPath = Path.GetDirectoryName(NavMeshAssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dirPath))
            {
                string parent = Path.GetDirectoryName(dirPath).Replace('\\', '/');
                string child = Path.GetFileName(dirPath);
                AssetDatabase.CreateFolder(parent, child);
            }

            // Persist to the canonical asset path
            string existingPath = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(existingPath))
            {
                AssetDatabase.CreateAsset(data, NavMeshAssetPath);
            }
            else if (existingPath != NavMeshAssetPath)
            {
                AssetDatabase.MoveAsset(existingPath, NavMeshAssetPath);
            }

            // Re-assign the asset back to the surface
            var so = new SerializedObject(surface);
            var navMeshDataProp = so.FindProperty("m_NavMeshData");
            navMeshDataProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshAssetPath);
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Mark scene dirty and save
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (saved)
                Debug.Log("[PathFinder] Bake + Save NavMesh: Scene saved - " + scene.path);
            else
                Debug.LogWarning("[PathFinder] Bake + Save NavMesh: EditorSceneManager.SaveScene returned false. Try Ctrl+S.");

            // Log campus rotation, vertex count, bounds, tilt, rms, refs
            string rotStr = campusRoot != null ? campusRoot.eulerAngles.ToString() : "(not found)";
            Debug.Log("[PathFinder] Bake + Save NavMesh: campus rotation = " + rotStr +
                      ", vertex count = " + vertCount +
                      ", bounds = " + data.sourceBounds +
                      ", tilt = " + tilt.ToString("0.00") + " deg, rms = " + frame.rms.ToString("0.00") +
                      " m, refs = " + frame.refsUsed);
        }

        public static void PrepareCampus() => BakeAndSaveNavMesh();

        // Grey out the menu item while in Play mode
        [MenuItem(MenuPath, true)]
        static bool ValidateBakeAndSaveNavMesh()
        {
            return !EditorApplication.isPlaying;
        }
    }
}
#endif
