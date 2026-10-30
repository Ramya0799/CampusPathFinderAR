// FloorManager.cs
// Manages the active floor, handles multi-floor staircase transitions,
// and enforces relocalization before resuming AR guidance.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public class FloorManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CampusManager _campusManager;

        [Header("State (Inspector Read-Only)")]
        [SerializeField] private string _activeFloorId;
        [SerializeField] private int _activeFloorLevel;
        [SerializeField] private bool _isTransitioningFloors = false;
        [SerializeField] private string _targetTransitionFloorId;

        // Public properties
        public FloorData CurrentFloor => _campusManager != null ? _campusManager.ActiveFloor : null;
        public string CurrentFloorId => CurrentFloor != null ? CurrentFloor.id : string.Empty;
        public int CurrentFloorLevel => CurrentFloor != null ? CurrentFloor.floorLevel : 0;
        public float CurrentElevationMeters => CurrentFloor != null ? CurrentFloor.elevationMeters : 0f;
        public bool IsTransitioningFloors => _isTransitioningFloors;
        public string TargetTransitionFloorId => _targetTransitionFloorId;

        // Events
        public event Action<FloorData> OnFloorSelected;
        public event Action<string, IndoorNodeData> OnFloorTransitionStarted; // targetFloorId, transitionNode
        public event Action<FloorData> OnFloorTransitionCompleted;
        public event Action<string> OnFloorError;

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
                _campusManager.OnFloorChanged += HandleFloorChanged;
            }
        }

        private void OnDisable()
        {
            if (_campusManager != null)
            {
                _campusManager.OnFloorChanged -= HandleFloorChanged;
            }
        }

        private void HandleFloorChanged(FloorData floor)
        {
            _activeFloorId = floor.id;
            _activeFloorLevel = floor.floorLevel;
            OnFloorSelected?.Invoke(floor);
        }

        public bool SelectFloor(string floorId)
        {
            if (_campusManager == null)
            {
                OnFloorError?.Invoke("CampusManager dependency missing.");
                return false;
            }

            bool success = _campusManager.SelectFloor(floorId);
            if (!success)
            {
                OnFloorError?.Invoke($"Floor '{floorId}' could not be selected.");
            }
            return success;
        }

        /// <summary>
        /// Begins a multi-floor transition (e.g. user reached a staircase or elevator).
        /// AR direction arrows should be temporarily paused until the user confirms arrival on the new floor.
        /// </summary>
        public void BeginFloorTransition(string targetFloorId, IndoorNodeData transitionNode)
        {
            if (string.IsNullOrEmpty(targetFloorId))
            {
                Debug.LogWarning("[FloorManager] Target floor ID is empty.");
                return;
            }

            _isTransitioningFloors = true;
            _targetTransitionFloorId = targetFloorId;

            Debug.Log($"[FloorManager] Transition started: {CurrentFloorId} → {targetFloorId} via {transitionNode?.name ?? "Stairs"}");
            OnFloorTransitionStarted?.Invoke(targetFloorId, transitionNode);
        }

        /// <summary>
        /// Confirms arrival on target floor after scanning the new floor's marker or manual confirmation.
        /// Resumes AR guidance.
        /// </summary>
        public bool CompleteFloorTransition(string newFloorId)
        {
            _isTransitioningFloors = false;
            _targetTransitionFloorId = string.Empty;

            bool success = SelectFloor(newFloorId);
            if (success)
            {
                Debug.Log($"[FloorManager] Transition complete. Now on floor: {CurrentFloor.name}");
                OnFloorTransitionCompleted?.Invoke(CurrentFloor);
            }
            return success;
        }

        public List<IndoorNodeData> GetNodesForCurrentFloor()
        {
            if (CurrentFloor == null || CurrentFloor.nodes == null)
            {
                return new List<IndoorNodeData>();
            }
            return CurrentFloor.nodes;
        }

        public List<IndoorEdgeData> GetEdgesForCurrentFloor()
        {
            if (CurrentFloor == null || CurrentFloor.edges == null)
            {
                return new List<IndoorEdgeData>();
            }
            return CurrentFloor.edges;
        }
    }
}
