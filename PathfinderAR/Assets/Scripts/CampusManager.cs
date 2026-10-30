// CampusManager.cs
// Central coordinator for campus building and floor data.
// Loads data from JSON (Resources or API) and provides fast indexed lookup.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public class CampusManager : MonoBehaviour
    {
        [Header("Data Source")]
        [Tooltip("Resource file name inside Assets/Resources (without .json extension)")]
        [SerializeField] private string _resourcePath = "campus_data";
        [SerializeField] private bool _loadOnAwake = true;

        [Header("State (Inspector Read-Only)")]
        [SerializeField] private string _loadedCampusName;
        [SerializeField] private int _buildingCount;
        [SerializeField] private string _activeBuildingId;
        [SerializeField] private string _activeFloorId;

        // Public accessors
        public CampusData Campus { get; private set; }
        public BuildingData ActiveBuilding { get; private set; }
        public FloorData ActiveFloor { get; private set; }
        public bool IsLoaded => Campus != null;

        // Events
        public event Action<CampusData> OnCampusLoaded;
        public event Action<BuildingData> OnBuildingChanged;
        public event Action<FloorData> OnFloorChanged;
        public event Action<string> OnLoadError;

        // Fast lookup caches
        private readonly Dictionary<string, BuildingData> _buildingLookup = new Dictionary<string, BuildingData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FloorData> _floorLookup = new Dictionary<string, FloorData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoomData> _roomLookup = new Dictionary<string, RoomData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IndoorNodeData> _nodeLookup = new Dictionary<string, IndoorNodeData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MarkerAnchorData> _markerLookup = new Dictionary<string, MarkerAnchorData>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (_loadOnAwake)
            {
                LoadFromResources();
            }
        }

        public bool LoadFromResources()
        {
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(_resourcePath);
                if (asset == null)
                {
                    string msg = $"[CampusManager] Could not find TextAsset at Resources/{_resourcePath}.json";
                    Debug.LogWarning(msg);
                    OnLoadError?.Invoke(msg);
                    return false;
                }
                return LoadFromJson(asset.text);
            }
            catch (Exception ex)
            {
                string msg = $"[CampusManager] Exception loading Resources/{_resourcePath}: {ex.Message}";
                Debug.LogError(msg);
                OnLoadError?.Invoke(msg);
                return false;
            }
        }

        public bool LoadFromJson(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                OnLoadError?.Invoke("JSON text is null or empty.");
                return false;
            }

            try
            {
                CampusData data = JsonUtility.FromJson<CampusData>(jsonText);
                if (data == null || data.buildings == null)
                {
                    OnLoadError?.Invoke("Parsed CampusData or buildings list is null.");
                    return false;
                }

                Campus = data;
                _loadedCampusName = data.campusName;
                _buildingCount = data.buildings.Count;

                RebuildIndices();

                Debug.Log($"[CampusManager] Loaded {data.campusName} with {_buildingCount} buildings.");
                OnCampusLoaded?.Invoke(data);

                // Auto-select first building and floor if available
                if (data.buildings.Count > 0)
                {
                    SelectBuilding(data.buildings[0].id);
                }

                return true;
            }
            catch (Exception ex)
            {
                string msg = $"[CampusManager] Error parsing JSON: {ex.Message}";
                Debug.LogError(msg);
                OnLoadError?.Invoke(msg);
                return false;
            }
        }

        private void RebuildIndices()
        {
            _buildingLookup.Clear();
            _floorLookup.Clear();
            _roomLookup.Clear();
            _nodeLookup.Clear();
            _markerLookup.Clear();

            if (Campus == null || Campus.buildings == null) return;

            foreach (var b in Campus.buildings)
            {
                if (string.IsNullOrEmpty(b.id)) continue;
                _buildingLookup[b.id] = b;

                if (b.floors == null) continue;
                foreach (var f in b.floors)
                {
                    if (string.IsNullOrEmpty(f.id)) continue;
                    _floorLookup[f.id] = f;

                    if (f.rooms != null)
                    {
                        foreach (var r in f.rooms)
                        {
                            if (!string.IsNullOrEmpty(r.id)) _roomLookup[r.id] = r;
                        }
                    }

                    if (f.nodes != null)
                    {
                        foreach (var n in f.nodes)
                        {
                            if (!string.IsNullOrEmpty(n.id)) _nodeLookup[n.id] = n;
                        }
                    }

                    if (f.markers != null)
                    {
                        foreach (var m in f.markers)
                        {
                            if (!string.IsNullOrEmpty(m.markerId)) _markerLookup[m.markerId] = m;
                        }
                    }
                }
            }

            if (Campus.globalMarkers != null)
            {
                foreach (var m in Campus.globalMarkers)
                {
                    if (!string.IsNullOrEmpty(m.markerId)) _markerLookup[m.markerId] = m;
                }
            }
        }

        // ── Query Methods ───────────────────────────────────────────────────

        public BuildingData GetBuilding(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _buildingLookup.TryGetValue(id, out var building);
            return building;
        }

        public FloorData GetFloor(string floorId)
        {
            if (string.IsNullOrEmpty(floorId)) return null;
            _floorLookup.TryGetValue(floorId, out var floor);
            return floor;
        }

        public RoomData GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            _roomLookup.TryGetValue(roomId, out var room);
            return room;
        }

        public IndoorNodeData GetNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            _nodeLookup.TryGetValue(nodeId, out var node);
            return node;
        }

        public MarkerAnchorData GetMarker(string markerId)
        {
            if (string.IsNullOrEmpty(markerId)) return null;
            _markerLookup.TryGetValue(markerId, out var marker);
            return marker;
        }

        // ── Selection Methods ───────────────────────────────────────────────

        public bool SelectBuilding(string buildingId)
        {
            var b = GetBuilding(buildingId);
            if (b == null)
            {
                Debug.LogWarning($"[CampusManager] Building '{buildingId}' not found.");
                return false;
            }

            ActiveBuilding = b;
            _activeBuildingId = b.id;
            OnBuildingChanged?.Invoke(b);

            // Default to ground floor or first floor in list
            if (b.floors != null && b.floors.Count > 0)
            {
                var defaultFloor = b.floors.Find(f => f.floorLevel == 0) ?? b.floors[0];
                SelectFloor(defaultFloor.id);
            }
            return true;
        }

        public bool SelectFloor(string floorId)
        {
            var f = GetFloor(floorId);
            if (f == null)
            {
                Debug.LogWarning($"[CampusManager] Floor '{floorId}' not found.");
                return false;
            }

            // Sync building if different
            if (ActiveBuilding == null || !string.Equals(ActiveBuilding.id, f.buildingId, StringComparison.OrdinalIgnoreCase))
            {
                var parentBuilding = GetBuilding(f.buildingId);
                if (parentBuilding != null)
                {
                    ActiveBuilding = parentBuilding;
                    _activeBuildingId = parentBuilding.id;
                    OnBuildingChanged?.Invoke(parentBuilding);
                }
            }

            ActiveFloor = f;
            _activeFloorId = f.id;
            OnFloorChanged?.Invoke(f);
            return true;
        }
    }
}
