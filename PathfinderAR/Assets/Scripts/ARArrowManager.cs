// ARArrowManager.cs
// Spawns, aligns, and animates 3D directional arrows along corridor routes.
// Generates a procedural 3D chevron if no custom prefab is assigned.

using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public class ARArrowManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private RouteManager _routeManager;
        [SerializeField] private LocalizationManager _localizationManager;
        [SerializeField] private FloorManager _floorManager;

        [Header("Arrow Visuals")]
        [Tooltip("Optional custom 3D arrow prefab. If unassigned, procedural chevrons are generated.")]
        [SerializeField] private GameObject _customArrowPrefab;
        [Tooltip("Spacing between arrows in metres along the corridor path")]
        public float ArrowSpacingMeters = 1.8f;
        [Tooltip("Vertical height above the floor plane")]
        public float ArrowHeightOffset = 0.08f;
        [SerializeField] private Color _arrowColor = new Color(0.06f, 0.75f, 0.95f, 0.92f); // Cyan

        [Header("State (Inspector Read-Only)")]
        [SerializeField] private int _activeArrowCount = 0;
        [SerializeField] private bool _arrowsVisible = false;

        private readonly List<GameObject> _arrowPool = new List<GameObject>();
        private Mesh _proceduralChevronMesh;
        private Material _arrowMaterial;

        private void Awake()
        {
            if (_routeManager == null) _routeManager = GetComponent<RouteManager>() ?? FindFirstObjectByType<RouteManager>();
            if (_localizationManager == null) _localizationManager = GetComponent<LocalizationManager>() ?? FindFirstObjectByType<LocalizationManager>();
            if (_floorManager == null) _floorManager = GetComponent<FloorManager>() ?? FindFirstObjectByType<FloorManager>();

            InitializeMaterial();
        }

        private void OnEnable()
        {
            if (_routeManager != null)
            {
                _routeManager.OnRouteStarted += HandleRouteStarted;
                _routeManager.OnWaypointReached += HandleWaypointReached;
                _routeManager.OnDestinationArrived += HandleDestinationArrived;
                _routeManager.OnNavigationStopped += HandleNavigationStopped;
            }

            if (_localizationManager != null)
            {
                _localizationManager.OnTrackingConfidenceChanged += HandleConfidenceChanged;
            }

            if (_floorManager != null)
            {
                _floorManager.OnFloorTransitionStarted += HandleFloorTransitionStarted;
                _floorManager.OnFloorTransitionCompleted += HandleFloorTransitionCompleted;
            }
        }

        private void OnDisable()
        {
            if (_routeManager != null)
            {
                _routeManager.OnRouteStarted -= HandleRouteStarted;
                _routeManager.OnWaypointReached -= HandleWaypointReached;
                _routeManager.OnDestinationArrived -= HandleDestinationArrived;
                _routeManager.OnNavigationStopped -= HandleNavigationStopped;
            }

            if (_localizationManager != null)
            {
                _localizationManager.OnTrackingConfidenceChanged -= HandleConfidenceChanged;
            }

            if (_floorManager != null)
            {
                _floorManager.OnFloorTransitionStarted -= HandleFloorTransitionStarted;
                _floorManager.OnFloorTransitionCompleted -= HandleFloorTransitionCompleted;
            }

            ClearArrows();
        }

        private void InitializeMaterial()
        {
            if (_arrowMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                _arrowMaterial = new Material(shader)
                {
                    color = _arrowColor
                };
            }
        }

        private void HandleRouteStarted(List<IndoorNodeData> waypoints)
        {
            RefreshArrows();
        }

        private void HandleWaypointReached(int index, IndoorNodeData node)
        {
            RefreshArrows();
        }

        private void HandleDestinationArrived(IndoorNodeData node)
        {
            ClearArrows();
        }

        private void HandleNavigationStopped()
        {
            ClearArrows();
        }

        private void HandleConfidenceChanged(TrackingConfidence confidence)
        {
            bool shouldShow = confidence >= TrackingConfidence.Normal;
            SetArrowsVisibility(shouldShow);
        }

        private void HandleFloorTransitionStarted(string targetFloorId, IndoorNodeData transitionNode)
        {
            // Suspend arrows during staircase traversal until arrival on new floor is confirmed
            SetArrowsVisibility(false);
        }

        private void HandleFloorTransitionCompleted(FloorData newFloor)
        {
            RefreshArrows();
        }

        public void RefreshArrows()
        {
            ClearArrows();

            if (_routeManager == null || !_routeManager.IsNavigating) return;
            if (_floorManager != null && _floorManager.IsTransitioningFloors) return;
            if (_localizationManager != null && !_localizationManager.IsLocalized) return;

            var waypoints = _routeManager.Waypoints;
            int startIdx = _routeManager.CurrentWaypointIndex;
            if (waypoints == null || startIdx >= waypoints.Count) return;

            // Generate arrow points along the remaining waypoint segments on current floor
            string currentFloorId = _floorManager != null ? _floorManager.CurrentFloorId : "";

            for (int i = startIdx; i < waypoints.Count - 1; i++)
            {
                var wpA = waypoints[i];
                var wpB = waypoints[i + 1];

                // If segment crosses a floor transition (staircase), stop rendering arrows beyond this floor
                if (wpA.floorId != wpB.floorId && !string.IsNullOrEmpty(currentFloorId) && wpA.floorId != currentFloorId)
                {
                    break;
                }

                Vector3 posA = _localizationManager != null
                    ? _localizationManager.MapToWorldPosition(wpA.localX, wpA.localY, wpA.localZ)
                    : new Vector3(wpA.localX, wpA.localY, wpA.localZ);

                Vector3 posB = _localizationManager != null
                    ? _localizationManager.MapToWorldPosition(wpB.localX, wpB.localY, wpB.localZ)
                    : new Vector3(wpB.localX, wpB.localY, wpB.localZ);

                posA.y += ArrowHeightOffset;
                posB.y += ArrowHeightOffset;

                float segmentDistance = Vector3.Distance(posA, posB);
                Vector3 direction = (posB - posA).normalized;
                Quaternion rotation = direction != Vector3.zero ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;

                int arrowCount = Mathf.Max(1, Mathf.FloorToInt(segmentDistance / ArrowSpacingMeters));
                Camera cam = Camera.main;
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

                for (int j = 0; j <= arrowCount; j++)
                {
                    float t = (float)j / Mathf.Max(1, arrowCount);
                    Vector3 arrowPos = Vector3.Lerp(posA, posB, t);
                    
                    // Proximity culling: Do not spawn arrows directly in front of or inside the camera lens
                    if (cam != null && Vector3.Distance(camPos, arrowPos) < 1.4f)
                    {
                        continue;
                    }

                    SpawnArrow(arrowPos, rotation);
                }
            }

            _activeArrowCount = _arrowPool.Count;
            SetArrowsVisibility(true);
        }

        private void SpawnArrow(Vector3 position, Quaternion rotation)
        {
            GameObject arrowObj;
            if (_customArrowPrefab != null)
            {
                arrowObj = Instantiate(_customArrowPrefab, position, rotation, transform);
            }
            else
            {
                arrowObj = new GameObject("AR_Chevron");
                arrowObj.transform.SetPositionAndRotation(position, rotation);
                arrowObj.transform.SetParent(transform);

                MeshFilter mf = arrowObj.AddComponent<MeshFilter>();
                MeshRenderer mr = arrowObj.AddComponent<MeshRenderer>();

                mf.sharedMesh = GetOrCreateChevronMesh();
                mr.sharedMaterial = _arrowMaterial;
            }

            _arrowPool.Add(arrowObj);
        }

        private Mesh GetOrCreateChevronMesh()
        {
            if (_proceduralChevronMesh != null) return _proceduralChevronMesh;

            Mesh mesh = new Mesh { name = "Procedural_Chevron" };
            // Define 2D arrow pointing forward (+Z), flat on XZ plane
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0.0f,  0.0f,  0.40f),  // 0 Tip
                new Vector3(0.25f, 0.0f, -0.20f),  // 1 Right outer
                new Vector3(0.10f, 0.0f, -0.15f),  // 2 Right inner
                new Vector3(0.0f,  0.0f, -0.05f),  // 3 Center notch
                new Vector3(-0.10f, 0.0f, -0.15f), // 4 Left inner
                new Vector3(-0.25f, 0.0f, -0.20f)  // 5 Left outer
            };

            int[] triangles = new int[]
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 5
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _proceduralChevronMesh = mesh;
            return _proceduralChevronMesh;
        }

        public void SetArrowsVisibility(bool visible)
        {
            _arrowsVisible = visible;
            foreach (var arrow in _arrowPool)
            {
                if (arrow != null) arrow.SetActive(visible);
            }
        }

        public void ClearArrows()
        {
            foreach (var arrow in _arrowPool)
            {
                if (arrow != null) Destroy(arrow);
            }
            _arrowPool.Clear();
            _activeArrowCount = 0;
            _arrowsVisible = false;
        }
    }
}
