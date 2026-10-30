// PathFinder – campus route finding + AR arrows + 2D map fallback.
//
// Setup (one GameObject in the AR scene):
//   1. Add this component to an empty GameObject (e.g. "PathFinderNavigator").
//   2. Campus Root   = PathFinder_Campus_3D (the imported FBX in the scene)
//      Route Material = an Unlit/Color material (or URP Unlit in URP projects)
//      AR Camera      = XR Origin > Camera Offset > Main Camera (filled automatically if empty)
//   3. Needs: a baked NavMesh (NavMeshSurface "CampusNavMesh") and Resources/PathFinderNavData.json.
//
// How it works: routes are computed on the baked NavMesh in "campus space" (where the hidden model
// sits). Stair / lift / step connections from the drawings are added as NavMesh links at start-up.
// Pressing ALIGN at a known start point (standing there, facing the stated direction) computes the
// transform from the drawing coordinates to the AR session; arrows are placed in AR space with that
// transform, so nothing in the AR rig (XR Origin, AR Session, camera) is modified. The campus model's
// own import orientation is recovered from known meshes (see PFCampusFrame), so it does not matter
// how Unity rotated or converted the FBX.
//
// Define PF_NO_ARFOUNDATION in Player Settings > Scripting Define Symbols if the project has no AR Foundation.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
#if !PF_NO_ARFOUNDATION
using UnityEngine.XR.ARFoundation;
#endif

namespace PathFinderNav
{
    public class PFCampusNavigator : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("The imported PathFinder_Campus_3D object in this scene")]
        public Transform campusRoot;
        [Tooltip("AR camera (XR Origin > Camera Offset > Main Camera). Uses Camera.main if empty.")]
        public Camera arCamera;
        [Tooltip("URP Unlit material used for the route arrows (colour is set by this script)")]
        public Material routeMaterial;
        [Tooltip("Optional. Defaults to Resources/PathFinderNavData.json")]
        public TextAsset navData;

        [Header("Web App Integration")]
        [Tooltip("URL of the PathFinder web app opened by the Back button")]
        public string webAppUrl = "https://192.168.140.23:3000";

        [Header("Behaviour")]
        public bool hideModelOnDevice = true;
        public bool previewRouteInEditor = true;
        [Tooltip("Height of the phone above the floor when you press ALIGN (m)")]
        public float eyeHeight = 1.35f;
        public float arrowSpacing = 1.0f;
        public float arrowVisibleDistance = 25f;
        [Tooltip("Recalculate the route when you are this far (m) from it")]
        public float rerouteDistance = 6f;
        public Color arrowColor = new Color(0f, 0.85f, 1f, 1f);
        public Color destinationColor = new Color(1f, 0.7f, 0f, 1f);
        public Color userColor = new Color(1f, 0.15f, 0.2f, 1f);

        // ----- serialized baked frame from Bake + Save NavMesh
        [SerializeField, HideInInspector] public bool hasBakedFrame;
        [SerializeField, HideInInspector] public Matrix4x4 bakedFrameLocal;   // model -> campusRoot local space

        // ----- runtime state
        PFNavDataFile data;
        PFCampusFrame frame;
        readonly List<PFLinkRecord> linkRecords = new List<PFLinkRecord>();
        readonly List<NavMeshLinkInstance> linkInstances = new List<NavMeshLinkInstance>();
        int linksAdded, linksSkipped;

        PFStartPoint startPoint;
        PFDestination destination;
        Vector3[] corners;
        float[] cum;
        float totalLength;
        bool partialRoute;
        List<PFManeuver> maneuvers = new List<PFManeuver>();

        Transform routeRoot;
        readonly List<Transform> arrows = new List<Transform>();
        readonly List<float> arrowDist = new List<float>();
        readonly List<float> arrowRL = new List<float>();
        readonly List<Vector3> arrowPos = new List<Vector3>();   // campus space
        readonly List<Vector3> arrowDir = new List<Vector3>();   // campus space
        readonly List<Transform> poles = new List<Transform>();
        readonly List<Vector3> polePos = new List<Vector3>();    // campus space (base of pole)
        readonly List<float> poleHalf = new List<float>();
        Transform beacon;
        LineRenderer mapLine;
        Transform userDot;
        Mesh chevron;
        Material arrowMat, destMat, userMat;
        string pendingDeepLink;
        bool navReady;

        bool aligned;
        Quaternion yawQ = Quaternion.identity;   // model (canonical) -> AR rotation about vertical
        Vector3 arT = Vector3.zero;              // model (canonical) -> AR translation
        string alignedAt = "";
        float userDist, deviation, userRL = float.NaN;
        Vector3 userCampus;
        float lastReroute = -10f;
        PFVoice voice;
        int nextManeuverIdx = -1;
        bool spokenWithin8m;
        bool arrivedSpoken;

        bool mapMode;
        Camera mapCam;
        float mapFocusRL;
        readonly List<Renderer> campusRenderers = new List<Renderer>();
        readonly Dictionary<Renderer, float> rendererRL = new Dictionary<Renderer, float>();
        readonly Dictionary<Renderer, bool> rendererIsRoof = new Dictionary<Renderer, bool>();
        readonly List<float> levelList = new List<float>();

        string arStatus = "";
        bool arTracking, arUnsupported, autoMapDone;
        string message = "";
        string fatal = "";
        string diag = "";
        bool showDiag;
        int skippedNoMesh, skippedInvalid;

        // ----- UI state
        enum Picker { None, Destination, Start }
        Picker picker = Picker.None;
        int page;
        string filterBuilding = "";   // "", "Teaching Block", "Admin Block"
        string filterFloor = "";      // floor id or ""
        GUIStyle sBox, sBig, sSmall, sBtn, sBtnSmall, sItem, sTitle;
        float U;

        bool OnDevice { get { return !Application.isEditor; } }

        public Transform ResolveCampusRoot()
        {
            if (campusRoot == null || !campusRoot.gameObject.scene.IsValid())
            {
                Debug.LogWarning("[PathFinder] Campus Root is null or points to a prefab asset outside the scene. Resolving to scene object 'PathFinder_Campus_3D'.");
                var go = GameObject.Find("PathFinder_Campus_3D");
                if (go != null) campusRoot = go.transform;
            }
            return campusRoot;
        }

        // =====================================================================================
        void Awake()
        {
            Application.deepLinkActivated += OnDeepLinkActivated;
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                pendingDeepLink = Application.absoluteURL;
            }
        }

        void Start()
        {
            voice = PFVoice.Get(gameObject);
            if (arCamera == null) arCamera = Camera.main;
            ResolveCampusRoot();
            if (campusRoot == null) { fatal = "Campus Root is not set (drag PathFinder_Campus_3D into it)."; return; }

            TextAsset ta = navData != null ? navData : Resources.Load<TextAsset>("PathFinderNavData");
            if (ta == null) { fatal = "PathFinderNavData.json not found in Assets/Resources."; return; }
            data = JsonUtility.FromJson<PFNavDataFile>(ta.text);
            if (data == null || data.destinations == null || data.floors == null)
            { fatal = "PathFinderNavData.json could not be read."; return; }
            if (data.links == null) data.links = new PFLink[0];
            if (data.startPoints == null) data.startPoints = new PFStartPoint[0];

            if (hasBakedFrame)
            {
                frame = PFCampusFrame.FromMatrix(campusRoot.localToWorldMatrix * bakedFrameLocal, "baked");
            }
            else
            {
                frame = PFCampusFrame.Solve(campusRoot, data.frameRefs);
            }
            if (!frame.valid) { fatal = frame.error; return; }
            float tilt = Vector3.Angle(frame.upUnit, Vector3.up);
            if (tilt > 3f)
            {
                fatal = "The campus model is not lying flat (it is tilted " + Mathf.RoundToInt(tilt) + " degrees), so the NavMesh cannot work.\n" +
                        "Stop Play, then use menu PathFinder > Bake + Save NavMesh.";
                Debug.LogError("[PathFinder] " + fatal);
                return;
            }

            // Log for the 11 frameRefs whether renderer.isPartOfStaticBatch is true
            if (data.frameRefs != null)
            {
                var sbBatch = new StringBuilder("[PathFinder] Static batch check for frameRefs:\n");
                foreach (var r in data.frameRefs)
                {
                    Transform t = PFCampusFrame.FindDeep(campusRoot, r.name);
                    Renderer rend = t != null ? t.GetComponent<Renderer>() : null;
                    sbBatch.Append("  * ").Append(r.name).Append(": ")
                           .Append(rend != null ? "isPartOfStaticBatch = " + rend.isPartOfStaticBatch : "renderer not found")
                           .Append('\n');
                }
                Debug.Log(sbBatch.ToString());
            }

            foreach (var f in data.floors) if (!levelList.Contains(f.rl)) levelList.Add(f.rl);
            levelList.Sort();

            SetupMaterials();
            CacheCampusRenderers();
            if (OnDevice && hideModelOnDevice) SetCampusVisible(false);

            var rr = new GameObject("PF_RouteRoot");
            routeRoot = rr.transform;
            chevron = PFMeshes.Chevron(0.5f, 0.42f);

            message = "Choose a destination, then a start point.";
            ApplyRouteRoot();
            StartCoroutine(InitNavigation());

            // Buffer deep link if received at launch; applied at the end of InitNavigation()
            if (string.IsNullOrEmpty(pendingDeepLink) && !string.IsNullOrEmpty(Application.absoluteURL))
            {
                pendingDeepLink = Application.absoluteURL;
            }
        }

        IEnumerator InitNavigation()
        {
            yield return null;   // let every NavMeshSurface register its data first
            AddNavMeshLinks();
            RunDiagnostics();
            if (linksAdded == 0) showDiag = true;
            navReady = true;

            // Apply deep link after stair/lift links and diagnostics are ready
            if (!string.IsNullOrEmpty(pendingDeepLink))
            {
                string url = pendingDeepLink;
                pendingDeepLink = null;
                ProcessDeepLink(url);
            }
            else if (destination != null && (startPoint != null || aligned))
            {
                RecomputeRoute();
            }
        }

        void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLinkActivated;
            foreach (var inst in linkInstances)
                if (NavMesh.IsLinkValid(inst)) NavMesh.RemoveLink(inst);
            linkInstances.Clear();
        }

        // =====================================================================================
        void SetupMaterials()
        {
            Material baseMat = routeMaterial;
            if (baseMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                baseMat = new Material(sh);
                Debug.LogWarning("[PathFinder] Route Material not set - using a runtime material. Assign one so it is included in builds.");
            }
            arrowMat = new Material(baseMat); arrowMat.color = arrowColor;
            destMat = new Material(baseMat); destMat.color = destinationColor;
            userMat = new Material(baseMat); userMat.color = userColor;
        }

        void CacheCampusRenderers()
        {
            var floorRL = new Dictionary<string, float>();
            foreach (var f in data.floors) floorRL[f.id] = f.rl;
            campusRenderers.Clear();
            campusRenderers.AddRange(campusRoot.GetComponentsInChildren<Renderer>(true));
            foreach (var r in campusRenderers)
            {
                float rl = float.NaN;
                Transform t = r.transform;
                while (t != null && t != campusRoot)
                {
                    float v;
                    if (floorRL.TryGetValue(t.name, out v)) { rl = v; break; }
                    t = t.parent;
                }
                rendererRL[r] = rl;
                string n = r.gameObject.name;
                rendererIsRoof[r] = n.Contains("_Roof") || n.Contains("_Parapet") || n.Contains("Plaza_Deck");
            }
        }

        void SetCampusVisible(bool on)
        {
            foreach (var r in campusRenderers) if (r != null) r.enabled = on;
        }

        bool Snap(Vector3 p, float radius, out Vector3 snapped)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(p, out hit, radius, NavMesh.AllAreas)) { snapped = hit.position; return true; }
            snapped = p;
            return false;
        }

        /// <summary>Nearest NavMesh point on the same level: within 6 m horizontally and 1.2 m vertically.</summary>
        bool SnapLevel(Vector3 p, out Vector3 snapped)
        {
            NavMeshHit hit;
            float[] radii = { 1.5f, 3f, 6f };
            foreach (float r in radii)
            {
                if (NavMesh.SamplePosition(p, out hit, r, NavMesh.AllAreas))
                {
                    float dv = Mathf.Abs(Vector3.Dot(hit.position - p, frame.upUnit));
                    if (dv <= 1.2f) { snapped = hit.position; return true; }
                }
            }
            snapped = p;
            return false;
        }

        void AddNavMeshLinks()
        {
            int agent = NavMesh.GetSettingsByIndex(0).agentTypeID;
            foreach (var L in data.links)
            {
                Vector3 a = frame.ToWorld(L.ax, L.ay, L.arl);
                Vector3 b = frame.ToWorld(L.bx, L.by, L.brl);
                Vector3 sa, sb;
                if (!SnapLevel(a, out sa) || !SnapLevel(b, out sb))
                {
                    linksSkipped++;
                    skippedNoMesh++;
                    Debug.LogWarning("[PathFinder] Link skipped (no NavMesh near an end): " + L.name + " " + L.fromFloor + " -> " + L.toFloor);
                    continue;
                }
                var ld = new NavMeshLinkData();
                ld.startPosition = sa;
                ld.endPosition = sb;
                ld.width = L.kind == "lift" ? 1.0f : 1.2f;
                ld.costModifier = L.kind == "lift" ? 3f : 1f;
                ld.bidirectional = true;
                ld.area = 0;
                ld.agentTypeID = agent;
                NavMeshLinkInstance inst = NavMesh.AddLink(ld);
                if (NavMesh.IsLinkValid(inst))
                {
                    linkInstances.Add(inst);
                    linkRecords.Add(new PFLinkRecord { kind = L.kind, name = L.name, a = sa, b = sb });
                    linksAdded++;
                }
                else { linksSkipped++; skippedInvalid++; }
            }
            Debug.Log("[PathFinder] NavMesh links added: " + linksAdded + ", skipped: " + linksSkipped +
                      " (no NavMesh nearby: " + skippedNoMesh + ", rejected: " + skippedInvalid + ")");
        }

        static string V(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")";
        }

        // Message shown at the top of the screen when the NavMesh appears stale.
        // Set by RunDiagnostics, rendered in OnGUI before the normal top panel.
        string navMeshStaleWarning = "";

        void RunDiagnostics()
        {
            var sb = new StringBuilder();
            sb.Append("Links: " + linksAdded + " added, " + skippedNoMesh + " no NavMesh nearby, " + skippedInvalid + " rejected\n");
            string frameDesc = frame.method == "baked"
                ? "baked"
                : frame.method + ", " + frame.refsUsed + " refs, fit " + frame.rms.ToString("0.00") + " m";
            sb.Append("Model frame (" + frameDesc + (frame.mirrored ? ", MIRRORED" : "") +
                      "): origin " + V(frame.origin) + "  x " + V(frame.right) + "  y " + V(frame.planUp) + "  up " + V(frame.up) + "\n");
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            navMeshStaleWarning = "";   // reset before checks
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                sb.Append("NavMesh: EMPTY at runtime - bake CampusNavMesh again and save the scene.\n");
                navMeshStaleWarning = "The NavMesh is out of date. Stop Play, then use menu PathFinder > Bake + Save NavMesh.";
            }
            else
            {
                Vector3 mn = tri.vertices[0], mx = tri.vertices[0];
                var perLevel = new Dictionary<float, int>();
                foreach (float l in levelList) perLevel[l] = 0;
                foreach (var v in tri.vertices)
                {
                    mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
                    float rl = frame.RL(v);
                    foreach (float l in levelList) if (Mathf.Abs(rl - l) < 0.25f) perLevel[l]++;
                }
                sb.Append("NavMesh: " + tri.vertices.Length + " vertices, bounds " + V(mn) + " to " + V(mx) + "\n");
                sb.Append("Walkable vertices by level:");
                foreach (float l in levelList) sb.Append("  " + l.ToString("0.00") + "=" + perLevel[l]);
                sb.Append("\n");

                // ── Stale-NavMesh heuristic checks ────────────────────────────────
                // Check 1: if the NavMesh extends more than 40 m along frame.upUnit
                //          the bake was almost certainly done while the model was tilted.
                float upExtent = Vector3.Dot(mx - mn, frame.upUnit);
                bool staleByExtent = upExtent > 40f;

                // Check 2: no walkable vertex within 1.2 m (vertical) of RL 71.73
                //          (ground floor of Teaching Block – should always have coverage).
                const float targetRL = 71.73f;
                Vector3 probe = frame.ToWorld(0f, 0f, targetRL);
                NavMeshHit nmHit;
                bool hasGroundFloor = NavMesh.SamplePosition(probe, out nmHit, 6f, NavMesh.AllAreas) &&
                                      Mathf.Abs(frame.RL(nmHit.position) - targetRL) <= 1.2f;
                bool staleByMissingGF = !hasGroundFloor;

                if (staleByExtent || staleByMissingGF)
                {
                    navMeshStaleWarning = "The NavMesh is out of date. Stop Play, then use menu PathFinder > Bake + Save NavMesh.";
                    if (staleByExtent)
                        Debug.LogWarning("[PathFinder] NavMesh up-extent = " + upExtent.ToString("0.0") +
                                         " m (> 40 m threshold) – bake was likely done while model was tilted.");
                    if (staleByMissingGF)
                        Debug.LogWarning("[PathFinder] No walkable NavMesh vertex within 1.2 m of RL 71.73 " +
                                         "(Teaching Block GF) – NavMesh may be stale or misaligned.");
                }
            }
            if (data.startPoints.Length > 0)
            {
                var s0 = data.startPoints[0];
                DiagPoint(sb, s0.name, frame.ToWorld(s0.x, s0.y, s0.rl));
            }
            foreach (var d in data.destinations)
                if (d.floor == "TB_GF") { DiagPoint(sb, d.name + " (TB GF)", frame.ToWorld(d.x, d.y, d.rl)); break; }
            foreach (var d in data.destinations)
                if (d.floor == "TB_1F") { DiagPoint(sb, d.name + " (TB 1F)", frame.ToWorld(d.x, d.y, d.rl)); break; }
            diag = sb.ToString();
            Debug.Log("[PathFinder] Diagnostics\n" + diag);
        }

        void DiagPoint(StringBuilder sb, string label, Vector3 p)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(p, out hit, 60f, NavMesh.AllAreas))
                sb.Append(label + ": point " + V(p) + " -> nearest NavMesh " + V(hit.position) + " (" + Vector3.Distance(p, hit.position).ToString("0.0") + " m)\n");
            else
                sb.Append(label + ": point " + V(p) + " -> no NavMesh within 60 m\n");
        }

        // =====================================================================================
        Vector3 StartWorld()
        {
            return frame.ToWorld(startPoint.x, startPoint.y, startPoint.rl);
        }

        Vector3 CameraFloorPoint()
        {
            Transform c = arCamera != null ? arCamera.transform : transform;
            return c.position - Vector3.up * eyeHeight;
        }

        // Canonical mapping from model axes (x right, y north, z up) to Unity axes (x, y up, z north)
        static Vector3 Canon(Vector3 m) { return new Vector3(m.x, m.z, m.y); }
        static Vector3 CanonInv(Vector3 u) { return new Vector3(u.x, u.z, u.y); }

        Vector3 CampusToAR(Vector3 c) { return yawQ * Canon(frame.ToModel(c)) + arT; }
        Vector3 CampusDirToAR(Vector3 d) { return yawQ * Canon(frame.ToModelDir(d)); }
        Vector3 ARToCampus(Vector3 p) { return frame.FromModel(CanonInv(Quaternion.Inverse(yawQ) * (p - arT))); }

        [ContextMenu("Lay campus model flat")]
        public void LayCampusFlat()
        {
            Debug.LogWarning("[PathFinder] Lay campus model flat: this can give the wrong answer in Edit mode.");
            ResolveCampusRoot();
            if (campusRoot == null) { Debug.LogError("[PathFinder] Set Campus Root first."); return; }
            TextAsset ta = navData != null ? navData : Resources.Load<TextAsset>("PathFinderNavData");
            PFNavDataFile d = ta != null ? JsonUtility.FromJson<PFNavDataFile>(ta.text) : null;
            PFCampusFrame f = PFCampusFrame.Solve(campusRoot, d != null ? d.frameRefs : null);
            if (!f.valid) { Debug.LogError("[PathFinder] " + f.error); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(campusRoot, "Lay campus model flat");
#endif
            Quaternion fix = Quaternion.FromToRotation(f.upUnit, Vector3.up);
            campusRoot.rotation = fix * campusRoot.rotation;
            Debug.Log("[PathFinder] Campus model rotated by " + Vector3.Angle(f.upUnit, Vector3.up).ToString("0") +
                      " degrees; it is now flat (rotation " + campusRoot.eulerAngles + "). Now select CampusNavMesh > Clear > Bake, then save the scene.");
        }

        void OnDeepLinkActivated(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (data == null || !navReady || fatal.Length > 0)
            {
                pendingDeepLink = url;
                return;
            }
            ProcessDeepLink(url);
        }

        public void ProcessDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (data == null || !navReady)
            {
                pendingDeepLink = url;
                return;
            }
            if (fatal.Length > 0)
            {
                pendingDeepLink = url;
                return;
            }

            Debug.Log("[PathFinder] Processing deep link: " + url);

            string query = "";
            int qIdx = url.IndexOf('?');
            if (qIdx >= 0 && qIdx < url.Length - 1)
            {
                query = url.Substring(qIdx + 1);
            }

            string destId = null;
            string startName = null;

            if (!string.IsNullOrEmpty(query))
            {
                string[] pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    if (string.IsNullOrEmpty(pair)) continue;
                    int eq = pair.IndexOf('=');
                    string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                    string val = eq >= 0 ? pair.Substring(eq + 1) : "";
                    key = System.Uri.UnescapeDataString(key.Replace("+", " "));
                    val = System.Uri.UnescapeDataString(val.Replace("+", " "));

                    if (string.Equals(key, "dest", System.StringComparison.OrdinalIgnoreCase))
                        destId = val;
                    else if (string.Equals(key, "start", System.StringComparison.OrdinalIgnoreCase))
                        startName = val;
                }
            }

            PFDestination foundDest = null;
            if (!string.IsNullOrEmpty(destId) && data.destinations != null)
            {
                foreach (var d in data.destinations)
                {
                    if (string.Equals(d.id, destId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        foundDest = d;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(destId))
            {
                message = "Choose your destination";
                OpenPicker(Picker.Destination);
                return;
            }

            if (foundDest == null)
            {
                message = "Room not found: " + (destId ?? "");
                Debug.LogWarning("[PathFinder] Deep link destination not found: " + destId);
                OpenPicker(Picker.Destination);
                return;
            }

            PFStartPoint foundStart = null;
            if (!string.IsNullOrEmpty(startName) && data.startPoints != null)
            {
                foreach (var s in data.startPoints)
                {
                    if (string.Equals(s.name, startName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        foundStart = s;
                        break;
                    }
                }
            }

            destination = foundDest;
            if (foundStart != null)
            {
                startPoint = foundStart;
            }
            picker = Picker.None;

            RecomputeRoute();

            if (startPoint != null)
            {
                message = "Stand at " + startPoint.name + ", face the direction shown, then press ALIGN HERE.";
            }
            else
            {
                message = "Now choose your start point.";
            }
        }

        [ContextMenu("Test deep link")]
        public void TestDeepLink()
        {
            ProcessDeepLink("pathfinder://navigate?dest=TB_1F%3AStaff%20Room");
        }

        void RecomputeRoute()
        {
            if (destination == null) return;
            if (aligned) ComputeRoute(ARToCampus(CameraFloorPoint()), "your position");
            else if (startPoint != null) ComputeRoute(StartWorld(), startPoint.name);
        }

        void ComputeRoute(Vector3 fromCampus, string fromLabel)
        {
            ClearRouteVisuals();
            corners = null;
            Vector3 s, e;
            Vector3 destW = frame.ToWorld(destination.x, destination.y, destination.rl);
            if (!Snap(fromCampus, 3f, out s)) { message = "No walkable area near " + fromLabel + "."; return; }
            if (!Snap(destW, 3f, out e)) { message = "No walkable area near " + destination.name + "."; return; }
            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(s, e, NavMesh.AllAreas, path);
            if (!ok || path.status == NavMeshPathStatus.PathInvalid || path.corners.Length < 2)
            {
                message = "No route found from " + fromLabel + " to " + destination.name + ".";
                return;
            }
            partialRoute = path.status == NavMeshPathStatus.PathPartial;
            corners = path.corners;
            cum = PFRouteGuide.Cumulative(corners);
            totalLength = cum[cum.Length - 1];
            maneuvers = PFRouteGuide.Build(corners, frame, data.floors, linkRecords, destination);
            message = partialRoute
                ? "Only part of the route could be found (a connection may be missing in the model)."
                : "Route: " + Mathf.RoundToInt(totalLength) + " m to " + destination.name + ".";

            nextManeuverIdx = -1;
            spokenWithin8m = false;
            arrivedSpoken = false;
            string firstInstr = (maneuvers != null && maneuvers.Count > 0) ? maneuvers[0].text : "Follow the arrows";
            if (voice != null && destination != null)
            {
                voice.Say(PFVoice.Spoken("Route to " + destination.name + ": " + Mathf.RoundToInt(totalLength) + " m. " + firstInstr));
            }

            BuildRouteVisuals();
            ApplyRouteRoot();
            if (mapMode) FrameMapCamera();
        }

        void ClearRouteVisuals()
        {
            if (routeRoot == null) return;
            for (int i = routeRoot.childCount - 1; i >= 0; i--) Destroy(routeRoot.GetChild(i).gameObject);
            arrows.Clear(); arrowDist.Clear(); arrowRL.Clear(); arrowPos.Clear(); arrowDir.Clear();
            poles.Clear(); polePos.Clear(); poleHalf.Clear();
            beacon = null; mapLine = null; userDot = null;
        }

        void BuildRouteVisuals()
        {
            Vector3 up = frame.upUnit;
            float next = 0.6f;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Vector3 a = corners[i], b = corners[i + 1];
                float len = Vector3.Distance(a, b);
                if (len < 1e-3f) continue;
                Vector3 dir = (b - a) / len;
                float horiz = Vector3.ProjectOnPlane(b - a, up).magnitude;
                bool liftLike = horiz < 0.5f && len > 1.5f;
                if (liftLike)
                {
                    // vertical ride: show a tall marker instead of arrows
                    var pole = MakePrimitive(PrimitiveType.Cylinder, "PF_LiftMarker", destMat);
                    pole.localScale = new Vector3(0.35f, 1.0f, 0.35f);
                    poles.Add(pole); polePos.Add(a); poleHalf.Add(1.0f);
                    next = cum[i + 1] + arrowSpacing;
                    continue;
                }
                while (next <= cum[i + 1])
                {
                    float t = (next - cum[i]) / len;
                    Vector3 p = Vector3.Lerp(a, b, t) + up * 0.08f;
                    var go = new GameObject("PF_Arrow");
                    go.transform.SetParent(routeRoot, false);
                    arrowPos.Add(p);
                    arrowDir.Add(dir);
                    go.AddComponent<MeshFilter>().sharedMesh = chevron;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = arrowMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    arrows.Add(go.transform);
                    arrowDist.Add(next);
                    arrowRL.Add(frame.RL(p));
                    next += arrowSpacing;
                }
            }
            Vector3 end = corners[corners.Length - 1];
            beacon = MakePrimitive(PrimitiveType.Cylinder, "PF_Destination", destMat);
            beacon.localScale = new Vector3(0.3f, 1.2f, 0.3f);
            poles.Add(beacon); polePos.Add(end); poleHalf.Add(1.2f);

            var lineGo = new GameObject("PF_MapLine");
            lineGo.transform.SetParent(routeRoot, false);
            mapLine = lineGo.AddComponent<LineRenderer>();
            mapLine.useWorldSpace = false;
            mapLine.sharedMaterial = arrowMat;
            mapLine.widthMultiplier = 0.9f;
            mapLine.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++) mapLine.SetPosition(i, corners[i] + up * 0.6f);
            mapLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mapLine.enabled = mapMode;

            userDot = MakePrimitive(PrimitiveType.Sphere, "PF_You", userMat);
            userDot.localScale = Vector3.one * 1.6f;
            userDot.gameObject.SetActive(false);
        }

        Transform MakePrimitive(PrimitiveType type, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(routeRoot, false);
            return go.transform;
        }

        void ApplyRouteRoot()
        {
            if (routeRoot == null) return;
            bool hasRoute = corners != null;
            bool preview = !OnDevice && previewRouteInEditor && !aligned;
            routeRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            routeRoot.gameObject.SetActive(hasRoute && (mapMode || preview || aligned));
            if (mapLine != null) mapLine.enabled = mapMode;
            PlaceRouteItems(aligned && !mapMode);
        }

        /// <summary>Puts arrows and markers either in AR space (aligned) or in campus space (map / editor preview).</summary>
        void PlaceRouteItems(bool arSpace)
        {
            Vector3 cu = frame.upUnit;
            for (int i = 0; i < arrows.Count; i++)
            {
                if (arSpace)
                {
                    Vector3 d = CampusDirToAR(arrowDir[i]);
                    arrows[i].position = CampusToAR(arrowPos[i]);
                    if (d.sqrMagnitude > 1e-6f) arrows[i].rotation = Quaternion.LookRotation(d, Vector3.up);
                }
                else
                {
                    arrows[i].position = arrowPos[i];
                    arrows[i].rotation = Quaternion.LookRotation(arrowDir[i], cu);
                }
            }
            for (int i = 0; i < poles.Count; i++)
            {
                if (arSpace)
                {
                    poles[i].position = CampusToAR(polePos[i]) + Vector3.up * poleHalf[i];
                    poles[i].rotation = Quaternion.identity;
                }
                else
                {
                    poles[i].position = polePos[i] + cu * poleHalf[i];
                    poles[i].rotation = Quaternion.FromToRotation(Vector3.up, cu);
                }
            }
        }

        // =====================================================================================
        void Align()
        {
            if (startPoint == null) { message = "Choose your start point first."; return; }
            if (arCamera == null) { message = "No AR camera found."; return; }
            Vector3 sm = new Vector3(startPoint.x, startPoint.y, startPoint.rl - PFCampusFrame.RlDatum);
            Vector3 fm = Canon(new Vector3(startPoint.fx, startPoint.fy, 0f));
            Transform cam = arCamera.transform;
            Vector3 fc = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (fc.sqrMagnitude < 1e-4f) fc = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            fc.Normalize();
            Vector3 P = CameraFloorPoint();
            yawQ = Quaternion.AngleAxis(Vector3.SignedAngle(fm, fc, Vector3.up), Vector3.up);
            arT = P - yawQ * Canon(sm);
            aligned = true;
            alignedAt = startPoint.name;
            lastReroute = Time.time;
            if (destination != null) ComputeRoute(StartWorld(), startPoint.name);
            message = "Aligned at " + startPoint.name + ". If the arrows look off, use the nudge buttons.";
            if (voice != null) voice.Say("Aligned. Follow the arrows.");
            ApplyRouteRoot();
        }

        void Nudge(float yawDeg, float forward, float sideways, float upDown)
        {
            if (!aligned) return;
            Vector3 pivot = CameraFloorPoint();
            Quaternion r = Quaternion.AngleAxis(yawDeg, Vector3.up);
            yawQ = r * yawQ;
            arT = pivot + r * (arT - pivot);
            Transform cam = arCamera.transform;
            Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 s = Vector3.Cross(Vector3.up, f);
            arT += f * forward + s * sideways + Vector3.up * upDown;
            ApplyRouteRoot();
        }

        // =====================================================================================
        void Update()
        {
            if (data == null || frame == null || !frame.valid) return;
            UpdateArStatus();

            if (aligned && arCamera != null)
            {
                userCampus = ARToCampus(CameraFloorPoint());
                userRL = frame.RL(userCampus);
            }

            if (corners != null)
            {
                if (aligned)
                {
                    UpdateProgress();
                    if (!mapMode && deviation > rerouteDistance && Time.time - lastReroute > 4f)
                    {
                        lastReroute = Time.time;
                        if (voice != null) voice.Say("Recalculating route");
                        ComputeRoute(userCampus, "your position");
                    }
                }
                if (corners != null)
                {
                    if (mapMode) ShowArrowsNearLevel(mapFocusRL);
                    else if (aligned) UpdateArrowVisibility();
                    else ShowArrowsNearLevel(float.NaN);
                }
            }

            if (mapMode && userDot != null)
            {
                userDot.gameObject.SetActive(aligned);
                if (aligned) userDot.localPosition = userCampus + frame.upUnit * 1.0f;
            }

            if (OnDevice && arUnsupported && !autoMapDone && !mapMode)
            {
                autoMapDone = true;
                message = "AR is not available on this device - showing the 2D map instead.";
                SetMapMode(true);
            }
        }

        void UpdateArStatus()
        {
#if !PF_NO_ARFOUNDATION
            ARSessionState st = ARSession.state;
            arTracking = st == ARSessionState.SessionTracking;
            arUnsupported = st == ARSessionState.Unsupported;
            if (arTracking) arStatus = "AR tracking";
            else if (st == ARSessionState.Unsupported) arStatus = "AR not supported";
            else if (st == ARSessionState.NeedsInstall || st == ARSessionState.Installing) arStatus = "Installing AR services...";
            else if (st == ARSessionState.SessionInitializing)
                arStatus = "AR starting - move the phone slowly (" + ARSession.notTrackingReason + ")";
            else if (st == ARSessionState.None || st == ARSessionState.CheckingAvailability) arStatus = OnDevice ? "Checking AR..." : "AR not running (Editor preview)";
            else arStatus = "AR: " + st;
#else
            arTracking = false; arUnsupported = OnDevice; arStatus = "AR disabled";
#endif
        }

        void UpdateProgress()
        {
            Vector3 up = frame.upUnit;
            float best = float.MaxValue, bestDist = 0f;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Vector3 a = corners[i], seg = corners[i + 1] - a;
                float l2 = seg.sqrMagnitude;
                float t = l2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(userCampus - a, seg) / l2) : 0f;
                Vector3 q = a + seg * t;
                Vector3 d = q - userCampus;
                float dv = Vector3.Dot(d, up);
                float cost = Vector3.ProjectOnPlane(d, up).magnitude + 3f * Mathf.Abs(dv);
                if (cost < best) { best = cost; bestDist = cum[i] + Mathf.Sqrt(l2) * t; }
            }
            deviation = best;
            userDist = bestDist;

            if (maneuvers == null || maneuvers.Count == 0) return;

            float remaining = Mathf.Max(0f, totalLength - userDist);
            if (remaining < 3f)
            {
                if (!arrivedSpoken)
                {
                    arrivedSpoken = true;
                    if (voice != null && destination != null)
                    {
                        voice.Say("You have arrived at " + destination.name);
                    }
                }
                return;
            }

            int currentNextIdx = -1;
            for (int i = 0; i < maneuvers.Count; i++)
            {
                if (maneuvers[i].dist > userDist + 0.5f)
                {
                    currentNextIdx = i;
                    break;
                }
            }

            if (currentNextIdx < 0) return;

            if (nextManeuverIdx == -1)
            {
                nextManeuverIdx = currentNextIdx;
            }
            else if (currentNextIdx != nextManeuverIdx)
            {
                nextManeuverIdx = currentNextIdx;
                spokenWithin8m = false;
                if (voice != null && currentNextIdx < maneuvers.Count - 1)
                {
                    voice.Say(PFVoice.Spoken(maneuvers[currentNextIdx].text));
                }
            }

            PFManeuver nextM = maneuvers[nextManeuverIdx];
            float toManeuver = nextM.dist - userDist;
            if (toManeuver <= 8f && !spokenWithin8m && nextManeuverIdx < maneuvers.Count - 1)
            {
                spokenWithin8m = true;
                if (voice != null)
                {
                    if (nextM.vertical)
                    {
                        voice.Say(PFVoice.Spoken(nextM.text));
                    }
                    else
                    {
                        string action = nextM.text;
                        int commaIdx = action.IndexOf(", then go", System.StringComparison.OrdinalIgnoreCase);
                        if (commaIdx >= 0) action = action.Substring(0, commaIdx);
                        int dM = Mathf.Max(1, Mathf.RoundToInt(toManeuver));
                        voice.Say("In " + dM + " metres, " + action.ToLower());
                    }
                }
            }
        }

        void ShowArrowsNearLevel(float rl)
        {
            for (int i = 0; i < arrows.Count; i++)
            {
                bool show = float.IsNaN(rl) || Mathf.Abs(arrowRL[i] - rl) < 2.5f;
                if (arrows[i].gameObject.activeSelf != show) arrows[i].gameObject.SetActive(show);
            }
        }

        void UpdateArrowVisibility()
        {
            Vector3 up = frame.upUnit;
            for (int i = 0; i < arrows.Count; i++)
            {
                float h = Vector3.ProjectOnPlane(arrowPos[i] - userCampus, up).magnitude;
                bool show = arrowDist[i] >= userDist - 1.5f && h < arrowVisibleDistance && Mathf.Abs(arrowRL[i] - userRL) < 2.8f;
                if (arrows[i].gameObject.activeSelf != show) arrows[i].gameObject.SetActive(show);
            }
        }

        string CurrentInstruction(out string sub)
        {
            sub = "";
            if (destination == null) return "Choose a destination.";
            if (corners == null) return message;
            if (!aligned)
            {
                if (startPoint == null) return "Choose your start point.";
                sub = Mathf.RoundToInt(totalLength) + " m route" + (partialRoute ? " (partial)" : "") + " · first: " +
                      (maneuvers.Count > 0 ? maneuvers[0].text : "");
                if (OnDevice)
                    return "Walk to: " + startPoint.name + ". " + startPoint.description +
                           " Hold the phone upright at chest height and press ALIGN.";
                return "Route preview (Editor): see the Scene view or press MAP.";
            }
            float remaining = Mathf.Max(0f, totalLength - userDist);
            if (remaining < 2.5f) { sub = destination.building + " · " + destination.floorName; return "You have arrived at " + destination.name + "."; }
            PFManeuver nextM = new PFManeuver { dist = totalLength, text = "" };
            for (int i = 0; i < maneuvers.Count; i++)
                if (maneuvers[i].dist > userDist + 0.5f) { nextM = maneuvers[i]; break; }
            float toNext = nextM.dist - userDist;
            string where = float.IsNaN(userRL) ? "" : " · you are on " + PFRouteGuide.FloorName(userRL, data.floors, destination.building);
            sub = Mathf.RoundToInt(remaining) + " m to " + destination.name + where;
            if (toNext < 5f) return nextM.text;
            return "In " + Mathf.RoundToInt(toNext) + " m: " + nextM.text;
        }

        // =====================================================================================
        void SetMapMode(bool on)
        {
            mapMode = on;
            if (on)
            {
                if (mapCam == null)
                {
                    var go = new GameObject("PF_MapCamera");
                    mapCam = go.AddComponent<Camera>();
                    mapCam.orthographic = true;
                    mapCam.clearFlags = CameraClearFlags.SolidColor;
                    mapCam.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 1f);
                    mapCam.nearClipPlane = 0.5f;
                    mapCam.farClipPlane = 600f;
                }
                mapCam.depth = (arCamera != null ? arCamera.depth : 0f) + 10f;
                mapCam.enabled = true;
                if (aligned && !float.IsNaN(userRL)) mapFocusRL = NearestLevel(userRL);
                else if (startPoint != null) mapFocusRL = NearestLevel(startPoint.rl);
                else if (destination != null) mapFocusRL = NearestLevel(destination.rl);
                else mapFocusRL = 71.73f;
                ApplyMapVisibility();
                FrameMapCamera();
            }
            else
            {
                if (mapCam != null) mapCam.enabled = false;
                if (OnDevice && hideModelOnDevice) SetCampusVisible(false);
                else SetCampusVisible(true);
                if (userDot != null) userDot.gameObject.SetActive(false);
            }
            ApplyRouteRoot();
        }

        float NearestLevel(float rl)
        {
            float best = rl, d = float.MaxValue;
            foreach (float l in levelList) if (Mathf.Abs(l - rl) < d) { d = Mathf.Abs(l - rl); best = l; }
            return best;
        }

        void StepMapFloor(int dir)
        {
            int idx = levelList.IndexOf(NearestLevel(mapFocusRL));
            idx = Mathf.Clamp(idx + dir, 0, levelList.Count - 1);
            mapFocusRL = levelList[idx];
            ApplyMapVisibility();
        }

        void ApplyMapVisibility()
        {
            foreach (var r in campusRenderers)
            {
                if (r == null) continue;
                float rl = rendererRL[r];
                bool show;
                if (float.IsNaN(rl)) show = true;
                else show = rl <= mapFocusRL + 0.5f && !(rendererIsRoof[r] && rl >= mapFocusRL - 0.5f);
                r.enabled = show;
            }
        }

        void FrameMapCamera()
        {
            if (mapCam == null) return;
            Vector3 up = frame.upUnit, pu = frame.planUpUnit, pr = frame.rightUnit;
            var pts = new List<Vector3>();
            if (corners != null) pts.AddRange(corners);
            if (aligned) pts.Add(userCampus);
            if (startPoint != null) pts.Add(StartWorld());
            if (destination != null) pts.Add(frame.ToWorld(destination.x, destination.y, destination.rl));
            if (pts.Count == 0) { pts.Add(frame.ToWorld(-60f, -60f, 70f)); pts.Add(frame.ToWorld(60f, 60f, 70f)); }
            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            foreach (var p in pts)
            {
                float a = Vector3.Dot(p - frame.origin, pr), b = Vector3.Dot(p - frame.origin, pu);
                minR = Mathf.Min(minR, a); maxR = Mathf.Max(maxR, a); minU = Mathf.Min(minU, b); maxU = Mathf.Max(maxU, b);
            }
            Vector3 centre = frame.origin + pr * (minR + maxR) * 0.5f + pu * (minU + maxU) * 0.5f;
            float halfH = (maxU - minU) * 0.5f + 8f;
            float halfW = (maxR - minR) * 0.5f + 8f;
            float aspect = Mathf.Max(0.2f, (float)Screen.width / Mathf.Max(1, Screen.height));
            mapCam.orthographicSize = Mathf.Max(halfH, halfW / aspect) * 1.6f;   // room for the UI panels
            mapCam.transform.position = centre + up * 200f;
            mapCam.transform.rotation = Quaternion.LookRotation(-up, pu);
        }

        void ClearAll()
        {
            destination = null;
            corners = null;
            maneuvers.Clear();
            ClearRouteVisuals();
            message = "Cleared. Choose a destination.";
            ApplyRouteRoot();
        }

        // =====================================================================================
        // IMGUI interface (works on phones without any Canvas / EventSystem setup)
        void EnsureStyles()
        {
            U = Mathf.Max(16f, Mathf.Min(Screen.width, Screen.height) / 22f);
            if (sBox != null && sBig.fontSize == Mathf.RoundToInt(U * 0.85f)) return;
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.82f));
            bg.Apply();
            sBox = new GUIStyle(GUI.skin.box);
            sBox.normal.background = bg;
            sBox.padding = new RectOffset((int)(U * 0.4f), (int)(U * 0.4f), (int)(U * 0.3f), (int)(U * 0.3f));
            sBig = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(U * 0.85f), wordWrap = true, fontStyle = FontStyle.Bold };
            sBig.normal.textColor = Color.white;
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(U * 0.6f), wordWrap = true };
            sSmall.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
            sTitle = new GUIStyle(sBig) { fontSize = Mathf.RoundToInt(U * 0.8f), wordWrap = false };
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(U * 0.7f), wordWrap = true };
            sBtnSmall = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(U * 0.55f) };
            sItem = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(U * 0.62f), wordWrap = true, alignment = TextAnchor.MiddleLeft };
        }

        void OnGUI()
        {
            EnsureStyles();
            Rect safe = Screen.safeArea;
            Rect area = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);
            float pad = U * 0.35f;

            if (fatal.Length > 0)
            {
                GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, U * 12f), sBox);
                GUILayout.Label("PathFinder setup problem", sBig);
                GUILayout.Label(fatal, sSmall);
                GUILayout.EndArea();
                return;
            }
            if (data == null) return;

            if (picker != Picker.None) { DrawPicker(area, pad); return; }

            // ---- top panel
            string sub;
            string instr = CurrentInstruction(out sub);
            float topPanelH = U * 8.2f;
            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, topPanelH), sBox);
            string top = arStatus + (aligned ? "  ·  aligned at " + alignedAt : "  ·  not aligned") + (mapMode ? "  ·  MAP" : "");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ Back", sBtnSmall, GUILayout.Width(U * 4.2f), GUILayout.Height(U * 1.6f)))
            {
                if (!string.IsNullOrEmpty(webAppUrl)) Application.OpenURL(webAppUrl);
                else Debug.LogWarning("[PathFinder] webAppUrl is not set.");
            }
            GUILayout.Label(top, sSmall);
            GUILayout.EndHorizontal();

            GUILayout.Label(instr, sBig);
            if (sub.Length > 0) GUILayout.Label(sub, sSmall);
            if (message.Length > 0) GUILayout.Label(message, sSmall);
            GUILayout.EndArea();

            // ---- Stale NavMesh warning (top of screen, above the top panel)
            if (navMeshStaleWarning.Length > 0)
            {
                GUIStyle sWarn = new GUIStyle(sBig);
                sWarn.normal.textColor = new Color(1f, 0.35f, 0.1f, 1f);
                sWarn.wordWrap = true;
                float warnH = U * 3.5f;
                GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, warnH), sBox);
                GUILayout.Label(navMeshStaleWarning, sWarn);
                GUILayout.EndArea();
            }

            if (showDiag && diag.Length > 0)
            {
                GUILayout.BeginArea(new Rect(area.x + pad, area.y + U * 8.6f, area.width - 2 * pad, area.height * 0.42f), sBox);
                GUILayout.Label("Diagnostics (tap INFO to hide)", sSmall);
                GUILayout.Label(diag, sSmall);
                GUILayout.EndArea();
            }


            // ---- bottom panel
            float bh = U * 2.0f;
            int rows = 3 + (aligned ? 1 : 0) + (mapMode ? 1 : 0);
            float panelH = rows * (bh + U * 0.25f) + U * 0.8f;
            GUILayout.BeginArea(new Rect(area.x + pad, area.yMax - panelH - pad, area.width - 2 * pad, panelH), sBox);
            string dName = destination != null ? destination.name + "  (" + destination.building + ", " + destination.floorName + ")" : "tap to choose";
            if (GUILayout.Button("Destination: " + dName, sBtn, GUILayout.Height(bh))) OpenPicker(Picker.Destination);
            string sName = startPoint != null ? startPoint.name : "tap to choose";
            if (GUILayout.Button("Start point: " + sName, sBtn, GUILayout.Height(bh))) OpenPicker(Picker.Start);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(aligned ? "RE-ALIGN" : "ALIGN HERE", sBtn, GUILayout.Height(bh))) Align();
            if (GUILayout.Button(mapMode ? "AR VIEW" : "MAP", sBtn, GUILayout.Height(bh))) SetMapMode(!mapMode);
            string vLabel = (voice != null && voice.voiceOn) ? "VOICE ON" : "VOICE OFF";
            if (GUILayout.Button(vLabel, sBtn, GUILayout.Height(bh)))
            {
                if (voice != null) voice.SetVoice(!voice.voiceOn);
            }
            if (GUILayout.Button("CLEAR", sBtn, GUILayout.Height(bh))) ClearAll();
            if (GUILayout.Button("INFO", sBtn, GUILayout.Height(bh))) showDiag = !showDiag;
            GUILayout.EndHorizontal();
            if (aligned)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Turn L", sBtnSmall, GUILayout.Height(bh))) Nudge(-3f, 0, 0, 0);
                if (GUILayout.Button("Turn R", sBtnSmall, GUILayout.Height(bh))) Nudge(3f, 0, 0, 0);
                if (GUILayout.Button("Fwd", sBtnSmall, GUILayout.Height(bh))) Nudge(0, 0.5f, 0, 0);
                if (GUILayout.Button("Back", sBtnSmall, GUILayout.Height(bh))) Nudge(0, -0.5f, 0, 0);
                if (GUILayout.Button("Up", sBtnSmall, GUILayout.Height(bh))) Nudge(0, 0, 0, 0.1f);
                if (GUILayout.Button("Down", sBtnSmall, GUILayout.Height(bh))) Nudge(0, 0, 0, -0.1f);
                GUILayout.EndHorizontal();
            }
            if (mapMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Floor down", sBtnSmall, GUILayout.Height(bh))) StepMapFloor(-1);
                GUILayout.Label("Level " + mapFocusRL.ToString("0.00"), sSmall, GUILayout.Height(bh));
                if (GUILayout.Button("Floor up", sBtnSmall, GUILayout.Height(bh))) StepMapFloor(1);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        void OpenPicker(Picker p)
        {
            picker = p;
            page = 0;
        }

        void DrawPicker(Rect area, float pad)
        {
            Rect r = new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, area.height - 2 * pad);
            GUILayout.BeginArea(r, sBox);
            sTitle.wordWrap = false;
            float bh = U * 1.8f;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close", sBtnSmall, GUILayout.Width(U * 4f), GUILayout.Height(bh))) picker = Picker.None;
            GUILayout.Label(picker == Picker.Destination ? "Where do you want to go?" : "Where are you starting?", sTitle);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(message))
            {
                GUIStyle sMsg = new GUIStyle(sSmall);
                sMsg.normal.textColor = new Color(1f, 0.45f, 0.45f);
                GUILayout.Label(message, sMsg);
            }

            float used = bh + U * 0.6f;
            if (picker == Picker.Destination)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(filterBuilding == "", Sel(filterBuilding == "", "All"), sBtnSmall, GUILayout.Height(bh)) && filterBuilding != "") { filterBuilding = ""; filterFloor = ""; page = 0; }
                if (GUILayout.Toggle(filterBuilding == "Teaching Block", Sel(filterBuilding == "Teaching Block", "Teaching"), sBtnSmall, GUILayout.Height(bh)) && filterBuilding != "Teaching Block") { filterBuilding = "Teaching Block"; filterFloor = ""; page = 0; }
                if (GUILayout.Toggle(filterBuilding == "Admin Block", Sel(filterBuilding == "Admin Block", "Admin"), sBtnSmall, GUILayout.Height(bh)) && filterBuilding != "Admin Block") { filterBuilding = "Admin Block"; filterFloor = ""; page = 0; }
                GUILayout.EndHorizontal();
                used += bh + U * 0.3f;
                if (filterBuilding != "")
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Toggle(filterFloor == "", Sel(filterFloor == "", "All floors"), sBtnSmall, GUILayout.Height(bh)) && filterFloor != "") { filterFloor = ""; page = 0; }
                    foreach (var f in data.floors)
                    {
                        if (f.building != filterBuilding) continue;
                        if (GUILayout.Toggle(filterFloor == f.id, Sel(filterFloor == f.id, f.shortName), sBtnSmall, GUILayout.Height(bh)) && filterFloor != f.id) { filterFloor = f.id; page = 0; }
                    }
                    GUILayout.EndHorizontal();
                    used += bh + U * 0.3f;
                }
            }

            float itemH = U * 2.3f;
            float listH = r.height - used - bh - U * 1.6f;
            int perPage = Mathf.Max(3, Mathf.FloorToInt(listH / (itemH + U * 0.25f)));

            int count;
            if (picker == Picker.Destination)
            {
                var items = new List<PFDestination>();
                foreach (var d in data.destinations)
                {
                    if (filterBuilding != "" && d.building != filterBuilding) continue;
                    if (filterFloor != "" && d.floor != filterFloor) continue;
                    items.Add(d);
                }
                count = items.Count;
                int pages = Mathf.Max(1, (count + perPage - 1) / perPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                DrawPager(pages, bh);
                for (int i = page * perPage; i < Mathf.Min(count, (page + 1) * perPage); i++)
                {
                    var d = items[i];
                    string label = d.name + "\n" + d.building + " · " + d.floorName + (d.approximate ? " (approx.)" : "");
                    if (GUILayout.Button(label, sItem, GUILayout.Height(itemH)))
                    {
                        destination = d;
                        picker = Picker.None;
                        RecomputeRoute();
                        if (startPoint == null && !aligned) message = "Now choose your start point.";
                    }
                }
            }
            else
            {
                count = data.startPoints.Length;
                int pages = Mathf.Max(1, (count + perPage - 1) / perPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                DrawPager(pages, bh);
                for (int i = page * perPage; i < Mathf.Min(count, (page + 1) * perPage); i++)
                {
                    var s = data.startPoints[i];
                    if (GUILayout.Button(s.name + (s.approximate ? "  (approx.)" : ""), sItem, GUILayout.Height(itemH)))
                    {
                        startPoint = s;
                        picker = Picker.None;
                        if (aligned) message = "Walk to " + s.name + " and press RE-ALIGN.";
                        else RecomputeRoute();
                    }
                }
            }
            GUILayout.EndArea();
        }

        static string Sel(bool on, string t)
        {
            return on ? "[" + t + "]" : t;
        }

        void DrawPager(int pages, float bh)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = page > 0;
            if (GUILayout.Button("< Prev", sBtnSmall, GUILayout.Height(bh))) page--;
            GUI.enabled = true;
            GUILayout.Label("Page " + (page + 1) + " / " + pages, sSmall, GUILayout.Height(bh));
            GUI.enabled = page < pages - 1;
            if (GUILayout.Button("Next >", sBtnSmall, GUILayout.Height(bh))) page++;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // Scene-view gizmo: the current route in campus space (useful in the Editor)
        void OnDrawGizmos()
        {
            if (corners == null || corners.Length < 2) return;
            Gizmos.color = Color.yellow;
            for (int i = 0; i < corners.Length - 1; i++) Gizmos.DrawLine(corners[i] + Vector3.up * 0.3f, corners[i + 1] + Vector3.up * 0.3f);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(corners[corners.Length - 1], 0.5f);
        }
    }
}
