// RouteManager.cs
// Manages indoor graph route calculation, waypoint progression,
// arrival detection, and floor transition handoffs.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public class RouteManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CampusManager _campusManager;
        [SerializeField] private FloorManager _floorManager;
        [SerializeField] private LocalizationManager _localizationManager;

        [Header("Settings")]
        [Tooltip("Arrival radius in metres to advance to the next indoor waypoint")]
        public float IndoorArrivalRadiusMeters = 2.5f;
        [Tooltip("Arrival radius in metres for final destination")]
        public float DestinationArrivalRadiusMeters = 3.0f;

        [Header("State (Inspector Read-Only)")]
        [SerializeField] private bool _isNavigating = false;
        [SerializeField] private int _currentWaypointIndex = 0;
        [SerializeField] private string _destinationNodeId;
        [SerializeField] private float _distanceToNextMeters = 0f;
        [SerializeField] private float _totalRemainingMeters = 0f;

        // Active route waypoints
        private readonly List<IndoorNodeData> _routeWaypoints = new List<IndoorNodeData>();

        // Public properties
        public bool IsNavigating => _isNavigating;
        public int CurrentWaypointIndex => _currentWaypointIndex;
        public int TotalWaypoints => _routeWaypoints.Count;
        public IndoorNodeData CurrentWaypoint => _currentWaypointIndex < _routeWaypoints.Count ? _routeWaypoints[_currentWaypointIndex] : null;
        public IndoorNodeData NextWaypoint => (_currentWaypointIndex + 1) < _routeWaypoints.Count ? _routeWaypoints[_currentWaypointIndex + 1] : null;
        public IndoorNodeData FinalDestination => _routeWaypoints.Count > 0 ? _routeWaypoints[_routeWaypoints.Count - 1] : null;
        public float DistanceToNextMeters => _distanceToNextMeters;
        public float TotalRemainingMeters => _totalRemainingMeters;
        public IReadOnlyList<IndoorNodeData> Waypoints => _routeWaypoints;

        // Events
        public event Action<List<IndoorNodeData>> OnRouteStarted;
        public event Action<int, IndoorNodeData> OnWaypointReached;
        public event Action<IndoorNodeData> OnDestinationArrived;
        public event Action OnNavigationStopped;
        public event Action<string> OnRouteError;

        private void Awake()
        {
            if (_campusManager == null) _campusManager = GetComponent<CampusManager>() ?? FindFirstObjectByType<CampusManager>();
            if (_floorManager == null) _floorManager = GetComponent<FloorManager>() ?? FindFirstObjectByType<FloorManager>();
            if (_localizationManager == null) _localizationManager = GetComponent<LocalizationManager>() ?? FindFirstObjectByType<LocalizationManager>();
        }

        private void Update()
        {
            if (!_isNavigating || _routeWaypoints.Count == 0) return;

            // Get user's position from Main Camera in world coordinates
            Camera cam = Camera.main;
            if (cam == null) return;

            UpdateNavigationProgress(cam.transform.position);
        }

        public void SetRoute(List<IndoorNodeData> waypoints, string destinationNodeId = "")
        {
            if (waypoints == null || waypoints.Count == 0)
            {
                OnRouteError?.Invoke("Cannot set an empty route.");
                return;
            }

            _routeWaypoints.Clear();
            _routeWaypoints.AddRange(waypoints);
            _currentWaypointIndex = 0;
            _destinationNodeId = destinationNodeId;
            _isNavigating = true;

            RecalculateRemainingDistance(Vector3.zero);

            Debug.Log($"[RouteManager] Route started with {_routeWaypoints.Count} waypoints toward '{_destinationNodeId}'.");
            OnRouteStarted?.Invoke(_routeWaypoints);
        }

        /// <summary>
        /// Offline graph solver: Finds shortest path between two nodes using Dijkstra's algorithm.
        /// </summary>
        public bool CalculateLocalRoute(string startNodeId, string targetNodeId, bool accessibleOnly = false)
        {
            if (_campusManager == null || !_campusManager.IsLoaded)
            {
                OnRouteError?.Invoke("Campus data not loaded.");
                return false;
            }

            IndoorNodeData startNode = _campusManager.GetNode(startNodeId);
            IndoorNodeData targetNode = _campusManager.GetNode(targetNodeId);

            if (startNode == null || targetNode == null)
            {
                OnRouteError?.Invoke($"Invalid node pair: start='{startNodeId}', target='{targetNodeId}'.");
                return false;
            }

            // Gather all nodes and edges across the campus dataset
            var allNodes = new Dictionary<string, IndoorNodeData>();
            var adjList = new Dictionary<string, List<(string targetId, float distance, bool accessible, bool isStair)>>();

            foreach (var building in _campusManager.Campus.buildings)
            {
                foreach (var floor in building.floors)
                {
                    foreach (var n in floor.nodes)
                    {
                        allNodes[n.id] = n;
                        if (!adjList.ContainsKey(n.id)) adjList[n.id] = new List<(string, float, bool, bool)>();
                    }

                    foreach (var e in floor.edges)
                    {
                        if (!adjList.ContainsKey(e.sourceNodeId)) adjList[e.sourceNodeId] = new List<(string, float, bool, bool)>();
                        adjList[e.sourceNodeId].Add((e.targetNodeId, e.distanceMeters, e.isAccessible, e.isStaircaseTransition));

                        if (e.isBidirectional)
                        {
                            if (!adjList.ContainsKey(e.targetNodeId)) adjList[e.targetNodeId] = new List<(string, float, bool, bool)>();
                            adjList[e.targetNodeId].Add((e.sourceNodeId, e.distanceMeters, e.isAccessible, e.isStaircaseTransition));
                        }
                    }
                }
            }

            // Dijkstra algorithm
            var distances = new Dictionary<string, float>();
            var previous = new Dictionary<string, string>();
            var unvisited = new HashSet<string>(allNodes.Keys);

            foreach (var nodeId in allNodes.Keys)
            {
                distances[nodeId] = float.MaxValue;
            }
            distances[startNodeId] = 0f;

            while (unvisited.Count > 0)
            {
                string closestNode = null;
                float smallestDistance = float.MaxValue;

                foreach (var nodeId in unvisited)
                {
                    if (distances[nodeId] < smallestDistance)
                    {
                        smallestDistance = distances[nodeId];
                        closestNode = nodeId;
                    }
                }

                if (closestNode == null || smallestDistance == float.MaxValue) break;
                if (closestNode == targetNodeId) break;

                unvisited.Remove(closestNode);

                if (adjList.TryGetValue(closestNode, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (!unvisited.Contains(neighbor.targetId)) continue;
                        if (accessibleOnly && !neighbor.accessible) continue;

                        float alt = distances[closestNode] + neighbor.distance;
                        if (alt < distances[neighbor.targetId])
                        {
                            distances[neighbor.targetId] = alt;
                            previous[neighbor.targetId] = closestNode;
                        }
                    }
                }
            }

            if (!previous.ContainsKey(targetNodeId) && startNodeId != targetNodeId)
            {
                OnRouteError?.Invoke($"No navigable path between '{startNodeId}' and '{targetNodeId}'.");
                return false;
            }

            // Reconstruct path
            var path = new List<IndoorNodeData>();
            string curr = targetNodeId;
            while (curr != null)
            {
                if (allNodes.TryGetValue(curr, out var n)) path.Insert(0, n);
                if (curr == startNodeId) break;
                previous.TryGetValue(curr, out curr);
            }

            SetRoute(path, targetNodeId);
            return true;
        }

        private void UpdateNavigationProgress(Vector3 userWorldPosition)
        {
            if (_currentWaypointIndex >= _routeWaypoints.Count) return;

            IndoorNodeData targetWp = _routeWaypoints[_currentWaypointIndex];
            Vector3 targetWorldPos = _localizationManager != null
                ? _localizationManager.MapToWorldPosition(targetWp.localX, targetWp.localY, targetWp.localZ)
                : new Vector3(targetWp.localX, targetWp.localY, targetWp.localZ);

            _distanceToNextMeters = Vector3.Distance(userWorldPosition, targetWorldPos);

            bool isFinal = _currentWaypointIndex == _routeWaypoints.Count - 1;
            float threshold = isFinal ? DestinationArrivalRadiusMeters : IndoorArrivalRadiusMeters;

            if (_distanceToNextMeters <= threshold)
            {
                AdvanceWaypoint();
            }
            else
            {
                RecalculateRemainingDistance(userWorldPosition);
            }
        }

        public void AdvanceWaypoint()
        {
            if (!_isNavigating || _currentWaypointIndex >= _routeWaypoints.Count) return;

            IndoorNodeData completedWaypoint = _routeWaypoints[_currentWaypointIndex];
            Debug.Log($"[RouteManager] Waypoint reached: {completedWaypoint.name} (Index {_currentWaypointIndex})");
            OnWaypointReached?.Invoke(_currentWaypointIndex, completedWaypoint);

            // Check if this waypoint triggers a multi-floor transition
            if (completedWaypoint.isStaircase && !string.IsNullOrEmpty(completedWaypoint.transitionTargetFloorId) && _floorManager != null)
            {
                _floorManager.BeginFloorTransition(completedWaypoint.transitionTargetFloorId, completedWaypoint);
            }

            _currentWaypointIndex++;

            if (_currentWaypointIndex >= _routeWaypoints.Count)
            {
                _isNavigating = false;
                Debug.Log($"[RouteManager] Final destination reached: {completedWaypoint.name}");
                OnDestinationArrived?.Invoke(completedWaypoint);
            }
        }

        private void RecalculateRemainingDistance(Vector3 userWorldPosition)
        {
            if (_currentWaypointIndex >= _routeWaypoints.Count)
            {
                _totalRemainingMeters = 0f;
                return;
            }

            float sum = _distanceToNextMeters;
            for (int i = _currentWaypointIndex; i < _routeWaypoints.Count - 1; i++)
            {
                var curr = _routeWaypoints[i];
                var next = _routeWaypoints[i + 1];
                Vector3 p1 = new Vector3(curr.localX, curr.localY, curr.localZ);
                Vector3 p2 = new Vector3(next.localX, next.localY, next.localZ);
                sum += Vector3.Distance(p1, p2);
            }
            _totalRemainingMeters = sum;
        }

        public void StopNavigation()
        {
            _isNavigating = false;
            _routeWaypoints.Clear();
            _currentWaypointIndex = 0;
            _totalRemainingMeters = 0f;
            Debug.Log("[RouteManager] Navigation stopped.");
            OnNavigationStopped?.Invoke();
        }
    }
}
