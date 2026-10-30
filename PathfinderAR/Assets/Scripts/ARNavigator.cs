// ARNavigator.cs
// Main navigation controller. Orchestrates:
//   1. GPS + compass input (LocationService)
//   2. Route fetch from PathFinder API (PathFinderAPIClient)
//   3. ENU coordinate conversion (GeoUtils)
//   4. WaypointMarker placement and updates
//   5. HUD overlay (distance, bearing arrow, instructions)
//
// Uses only UnityEngine.UI (Text, Button) - no TextMeshPro dependency.
//
// Scene requirements (all present in PathFinderAR.unity):
//   AR Session, XR Origin (with Camera Offset child), Main Camera
//
// Add this script to a new empty GameObject called "AR Navigator".

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PathFinderAR
{
    public class ARNavigator : MonoBehaviour
    {
        // ── Inspector fields ───────────────────────────────────────────────

        [Header("Core Dependencies")]
        public PathFinderConfig    Config;
        public PathFinderAPIClient APIClient;
        public LocationService     Location;

        [Header("Destination (set before pressing Navigate)")]
        [Tooltip("Campus node ID for the start, e.g. 'gate'")]
        public string StartNodeId       = "gate";
        [Tooltip("Campus node ID for the destination, e.g. 'library'")]
        public string DestinationNodeId = "library";

        [Header("Modular Architecture (Phases 2 & 3)")]
        public CampusManager       Campus;
        public BuildingManager     Building;
        public FloorManager        Floor;
        public LocalizationManager Localization;
        public RouteManager        Route;
        public ARArrowManager      ArrowManager;
        public NavigationUIManager NavigationUI;

        [Header("HUD UI References")]
        public Text          InstructionText;   // UnityEngine.UI.Text
        public Text          DistanceText;
        public Text          StatusText;
        public RectTransform DirectionArrow;    // Rotated in screen-Z to show turn direction
        public Button        StartButton;
        public Button        RetryButton;

        // ── Private state ──────────────────────────────────────────────────

        private RouteResponse   _route;
        private RouteWaypoint[] _waypoints;
        private int             _currentWaypointIndex;
        private bool            _navigating;

        // ENU origin — first GPS fix used as (0,0,0) for all markers
        private bool   _originSet;
        private double _originLat;
        private double _originLng;

        // AR markers (one per waypoint)
        private readonly List<WaypointMarker> _markers = new List<WaypointMarker>();

        // Device compass heading in degrees (0 = North)
        private float _heading;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (Config    == null) Debug.LogError("[ARNavigator] PathFinderConfig not assigned.");
            if (APIClient == null) Debug.LogError("[ARNavigator] PathFinderAPIClient not assigned.");
            if (Location  == null) Debug.LogError("[ARNavigator] LocationService not assigned.");

            // Discover modular Phase 2/3 managers if not explicitly assigned
            if (Campus == null) Campus = FindFirstObjectByType<CampusManager>();
            if (Building == null) Building = FindFirstObjectByType<BuildingManager>();
            if (Floor == null) Floor = FindFirstObjectByType<FloorManager>();
            if (Localization == null) Localization = FindFirstObjectByType<LocalizationManager>();
            if (Route == null) Route = FindFirstObjectByType<RouteManager>();
            if (ArrowManager == null) ArrowManager = FindFirstObjectByType<ARArrowManager>();
            if (NavigationUI == null) NavigationUI = FindFirstObjectByType<NavigationUIManager>();

            // Register Deep Link listener for seamless web-to-app AR handoff
            Application.deepLinkActivated += OnDeepLinkActivated;
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                OnDeepLinkActivated(Application.absoluteURL);
            }
        }

        private void OnDeepLinkActivated(string url)
        {
            Debug.Log($"[ARNavigator] Deep link triggered: {url}");
            if (string.IsNullOrEmpty(url)) return;

            string start = ExtractQueryParam(url, "start");
            string dest  = ExtractQueryParam(url, "dest");

            if (!string.IsNullOrEmpty(start)) StartNodeId = start;
            if (!string.IsNullOrEmpty(dest))  DestinationNodeId = dest;

            Debug.Log($"[ARNavigator] Deep Link Route: {StartNodeId} → {DestinationNodeId}");
            BeginNavigation();
        }

        private string ExtractQueryParam(string url, string paramName)
        {
            int queryIdx = url.IndexOf('?');
            if (queryIdx < 0) return null;
            string queryString = url.Substring(queryIdx + 1);
            string[] pairs = queryString.Split('&');
            foreach (var pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Equals(paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(kv[1].Trim());
                }
            }
            return null;
        }

        private void Start()
        {
            SetStatus("Waiting for GPS…");

            if (APIClient != null)
            {
                APIClient.OnRouteSuccess += OnRouteReceived;
                APIClient.OnRouteError   += OnRouteError;
            }

            if (Location != null)
            {
                Location.OnLocationUpdated += OnLocationUpdated;
                Location.OnHeadingUpdated  += OnHeadingUpdated;
                Location.OnError           += OnLocationError;
            }

            if (StartButton != null)
                StartButton.onClick.AddListener(BeginNavigation);

            if (RetryButton != null)
            {
                RetryButton.onClick.AddListener(RetryRoute);
                RetryButton.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLinkActivated;

            if (APIClient != null)
            {
                APIClient.OnRouteSuccess -= OnRouteReceived;
                APIClient.OnRouteError   -= OnRouteError;
            }
            if (Location != null)
            {
                Location.OnLocationUpdated -= OnLocationUpdated;
                Location.OnHeadingUpdated  -= OnHeadingUpdated;
                Location.OnError           -= OnLocationError;
            }
        }

        // ── Public controls ────────────────────────────────────────────────

        public void BeginNavigation()
        {
            if (string.IsNullOrWhiteSpace(StartNodeId) || string.IsNullOrWhiteSpace(DestinationNodeId))
            {
                SetStatus("Set StartNodeId and DestinationNodeId first.");
                return;
            }
            ClearMarkers();
            _navigating           = false;
            _currentWaypointIndex = 0;
            _originSet            = false;

            SetStatus($"Fetching route: {StartNodeId} → {DestinationNodeId}…");
            if (RetryButton != null) RetryButton.gameObject.SetActive(false);
            APIClient.CalculateRoute(StartNodeId, DestinationNodeId);
        }

        public void RetryRoute() => BeginNavigation();

        // ── API callbacks ──────────────────────────────────────────────────

        private void OnRouteReceived(RouteResponse route)
        {
            _route     = route;
            _waypoints = route.waypoints;
            _navigating = true;

            SetStatus($"Route: {route.waypoints.Length} stops, {Mathf.RoundToInt(route.totalDistance)}m (~{Mathf.RoundToInt(route.estimatedTime)}s walk)");
            string firstInstruction = (route.instructions != null && route.instructions.Length > 0)
                ? route.instructions[0]
                : $"Head toward {_waypoints[0].name}";
            SetInstruction(firstInstruction);

            PlaceMarkers();
        }

        private void OnRouteError(string errorMessage)
        {
            Debug.LogError("[ARNavigator] " + errorMessage);
            SetStatus("Route error: " + errorMessage);
            if (RetryButton != null) RetryButton.gameObject.SetActive(true);
        }

        // ── Location callbacks ─────────────────────────────────────────────

        private void OnLocationUpdated(float lat, float lng, float accuracy)
        {
            float minAcc = Config != null ? Config.MinAcceptableGpsAccuracy : 25f;
            if (accuracy > minAcc)
            {
                SetStatus($"GPS signal weak (±{accuracy:F0}m). Move outdoors.");
                return;
            }

            // Set ENU origin on first good fix
            if (!_originSet)
            {
                _originLat = lat;
                _originLng = lng;
                _originSet = true;
                Debug.Log($"[ARNavigator] ENU origin: {lat:F6}, {lng:F6}");
                RepositionAllMarkers();
                SetStatus($"GPS ready (±{accuracy:F0}m). Tap Navigate to begin.");
            }

            if (!_navigating || _waypoints == null) return;

            // Slide the XR Origin in world space so camera = GPS position
            MoveXROriginToGPS((double)lat, (double)lng);

            // Waypoint arrival check
            CheckArrival((double)lat, (double)lng);

            // Direction arrow
            UpdateArrow((double)lat, (double)lng);

            // Distance readout
            if (_currentWaypointIndex < _waypoints.Length)
            {
                var   wp   = _waypoints[_currentWaypointIndex];
                float dist = GeoUtils.HaversineMeters(lat, lng, wp.latitude, wp.longitude);
                if (DistanceText != null)
                    DistanceText.text = $"{Mathf.RoundToInt(dist)} m";
            }
        }

        private void OnHeadingUpdated(float heading) => _heading = heading;

        private void OnLocationError(string msg) => SetStatus("GPS error: " + msg);

        // ── Marker management ──────────────────────────────────────────────

        private void PlaceMarkers()
        {
            ClearMarkers();
            if (_waypoints == null) return;

            for (int i = 0; i < _waypoints.Length; i++)
            {
                var go     = new GameObject($"WP_{i}_{_waypoints[i].id}");
                var marker = go.AddComponent<WaypointMarker>();
                marker.SetWaypointName(_waypoints[i].name);
                marker.SetActive(i == _currentWaypointIndex);
                _markers.Add(marker);

                if (_originSet) PositionMarker(marker, _waypoints[i]);
            }
        }

        private void RepositionAllMarkers()
        {
            if (_waypoints == null) return;
            for (int i = 0; i < _markers.Count && i < _waypoints.Length; i++)
                PositionMarker(_markers[i], _waypoints[i]);
        }

        private void PositionMarker(WaypointMarker marker, RouteWaypoint wp)
        {
            if (!_originSet) return;
            float h   = Config != null ? Config.MarkerHeight : 1.5f;
            Vector3 e = GeoUtils.LatLngToENU(_originLat, _originLng, wp.latitude, wp.longitude);
            marker.transform.position = new Vector3(e.x, h, e.z);
        }

        private void ClearMarkers()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m.gameObject);
            _markers.Clear();
        }

        // ── Navigation logic ───────────────────────────────────────────────

        private void MoveXROriginToGPS(double lat, double lng)
        {
            var xrGO = GameObject.Find("XR Origin");
            if (xrGO == null) return;
            Vector3 enu = GeoUtils.LatLngToENU(_originLat, _originLng, lat, lng);
            xrGO.transform.position = new Vector3(enu.x, 0f, enu.z);
        }

        private void CheckArrival(double lat, double lng)
        {
            if (_currentWaypointIndex >= _waypoints.Length) return;

            var   wp     = _waypoints[_currentWaypointIndex];
            float dist   = GeoUtils.HaversineMeters(lat, lng, wp.latitude, wp.longitude);
            float radius = Config != null ? Config.WaypointArrivalRadius : 8f;

            if (dist > radius) return;

            // Mark current as arrived
            if (_currentWaypointIndex < _markers.Count)
            {
                _markers[_currentWaypointIndex].SetActive(false);
                _markers[_currentWaypointIndex].SetArrived(true);
            }

            _currentWaypointIndex++;

            if (_currentWaypointIndex >= _waypoints.Length)
            {
                _navigating = false;
                SetInstruction($"You have arrived at {_waypoints[_waypoints.Length - 1].name}.");
                SetStatus("Navigation complete. ✓");
                if (DistanceText != null) DistanceText.text = "0 m";
                return;
            }

            // Activate next marker
            if (_currentWaypointIndex < _markers.Count)
                _markers[_currentWaypointIndex].SetActive(true);

            // Next instruction
            string[] instr    = _route?.instructions;
            string   nextInstr = (instr != null && _currentWaypointIndex < instr.Length)
                ? instr[_currentWaypointIndex]
                : $"Continue toward {_waypoints[_currentWaypointIndex].name}";
            SetInstruction(nextInstr);
        }

        private void UpdateArrow(double lat, double lng)
        {
            if (DirectionArrow == null || _currentWaypointIndex >= _waypoints.Length) return;
            var   wp  = _waypoints[_currentWaypointIndex];
            float tgt = GeoUtils.BearingDegrees(lat, lng, wp.latitude, wp.longitude);
            float rel = GeoUtils.RelativeBearing(tgt, _heading);
            // Negative Z rotation because UI positive rotation is counter-clockwise
            DirectionArrow.localRotation = Quaternion.Euler(0f, 0f, -rel);
        }

        // ── HUD helpers ────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (StatusText != null) StatusText.text = msg;
            Debug.Log("[ARNavigator] " + msg);
        }

        private void SetInstruction(string msg)
        {
            if (InstructionText != null) InstructionText.text = msg;
        }
    }
}
