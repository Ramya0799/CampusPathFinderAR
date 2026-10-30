// BuildingManager.cs
// Manages building-level logic, entrance nodes, and room queries.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public class BuildingManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CampusManager _campusManager;

        // Public properties
        public BuildingData CurrentBuilding => _campusManager != null ? _campusManager.ActiveBuilding : null;
        public string CurrentBuildingId => CurrentBuilding != null ? CurrentBuilding.id : string.Empty;

        // Events
        public event Action<BuildingData> OnBuildingSelected;
        public event Action<string> OnBuildingError;

        private void Awake()
        {
            if (_campusManager == null)
            {
                _campusManager = GetComponent<CampusManager>() ?? FindFirstObjectByType<CampusManager>();
            }
        }

        private void OnEnable()
        {
            if (_campusManager != null)
            {
                _campusManager.OnBuildingChanged += HandleBuildingChanged;
            }
        }

        private void OnDisable()
        {
            if (_campusManager != null)
            {
                _campusManager.OnBuildingChanged -= HandleBuildingChanged;
            }
        }

        private void HandleBuildingChanged(BuildingData building)
        {
            OnBuildingSelected?.Invoke(building);
        }

        public bool SelectBuilding(string buildingId)
        {
            if (_campusManager == null)
            {
                OnBuildingError?.Invoke("CampusManager dependency missing.");
                return false;
            }

            bool success = _campusManager.SelectBuilding(buildingId);
            if (!success)
            {
                OnBuildingError?.Invoke($"Building '{buildingId}' could not be selected.");
            }
            return success;
        }

        public List<FloorData> GetAvailableFloors()
        {
            if (CurrentBuilding == null || CurrentBuilding.floors == null)
            {
                return new List<FloorData>();
            }
            return new List<FloorData>(CurrentBuilding.floors);
        }

        public List<RoomData> GetAllRooms()
        {
            var results = new List<RoomData>();
            if (CurrentBuilding == null || CurrentBuilding.floors == null) return results;

            foreach (var floor in CurrentBuilding.floors)
            {
                if (floor.rooms != null)
                {
                    results.AddRange(floor.rooms);
                }
            }
            return results;
        }

        public IndoorNodeData GetPrimaryEntrance()
        {
            if (CurrentBuilding == null || CurrentBuilding.floors == null) return null;

            // Search for nodeType == Entrance on ground floor
            var groundFloor = CurrentBuilding.floors.Find(f => f.floorLevel == 0) ?? CurrentBuilding.floors[0];
            if (groundFloor != null && groundFloor.nodes != null)
            {
                return groundFloor.nodes.Find(n => n.nodeType == NodeType.Entrance) ?? (groundFloor.nodes.Count > 0 ? groundFloor.nodes[0] : null);
            }
            return null;
        }
    }
}
