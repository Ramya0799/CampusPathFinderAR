// NavigationUIManager.cs
// Manages the AR navigation HUD, status badges, multi-floor prompts,
// arrival dialogs, and user control buttons.

using UnityEngine;
using UnityEngine.UI;

namespace PathFinderAR
{
    public class NavigationUIManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CampusManager _campusManager;
        [SerializeField] private FloorManager _floorManager;
        [SerializeField] private LocalizationManager _localizationManager;
        [SerializeField] private RouteManager _routeManager;

        [Header("Header HUD")]
        public Text BuildingText;
        public Text FloorText;
        public Text StatusText;
        public Image StatusBadgeImage;

        [Header("Instruction Card")]
        public GameObject InstructionPanel;
        public Text InstructionText;
        public Text NextWaypointText;
        public Text DistanceText;
        public RectTransform DirectionArrow;

        [Header("Multi-Floor Transition Banner")]
        public GameObject FloorTransitionBanner;
        public Text TransitionMessageText;
        public Button ConfirmNewFloorButton;

        [Header("Arrival Dialog")]
        public GameObject ArrivalModal;
        public Text ArrivalTitleText;
        public Text ArrivalRoomText;
        public Button DismissArrivalButton;

        [Header("Controls")]
        public Button StartButton;
        public Button StopButton;
        public Button RelocalizeButton;
        public Button Toggle2DMapButton;

        private void Awake()
        {
            if (_campusManager == null) _campusManager = GetComponent<CampusManager>() ?? FindFirstObjectByType<CampusManager>();
            if (_floorManager == null) _floorManager = GetComponent<FloorManager>() ?? FindFirstObjectByType<FloorManager>();
            if (_localizationManager == null) _localizationManager = GetComponent<LocalizationManager>() ?? FindFirstObjectByType<LocalizationManager>();
            if (_routeManager == null) _routeManager = GetComponent<RouteManager>() ?? FindFirstObjectByType<RouteManager>();

            // Setup button listeners
            if (StartButton != null) StartButton.onClick.AddListener(HandleStartClicked);
            if (StopButton != null) StopButton.onClick.AddListener(HandleStopClicked);
            if (RelocalizeButton != null) RelocalizeButton.onClick.AddListener(HandleRelocalizeClicked);
            if (DismissArrivalButton != null) DismissArrivalButton.onClick.AddListener(HandleDismissArrivalClicked);
            if (ConfirmNewFloorButton != null) ConfirmNewFloorButton.onClick.AddListener(HandleConfirmNewFloorClicked);

            if (FloorTransitionBanner != null) FloorTransitionBanner.SetActive(false);
            if (ArrivalModal != null) ArrivalModal.SetActive(false);
        }

        private void OnEnable()
        {
            if (_campusManager != null)
            {
                _campusManager.OnBuildingChanged += HandleBuildingChanged;
                _campusManager.OnFloorChanged += HandleFloorChanged;
            }

            if (_floorManager != null)
            {
                _floorManager.OnFloorTransitionStarted += HandleFloorTransitionStarted;
                _floorManager.OnFloorTransitionCompleted += HandleFloorTransitionCompleted;
            }

            if (_localizationManager != null)
            {
                _localizationManager.OnLocalized += HandleLocalized;
                _localizationManager.OnTrackingConfidenceChanged += HandleConfidenceChanged;
            }

            if (_routeManager != null)
            {
                _routeManager.OnRouteStarted += HandleRouteStarted;
                _routeManager.OnWaypointReached += HandleWaypointReached;
                _routeManager.OnDestinationArrived += HandleDestinationArrived;
                _routeManager.OnNavigationStopped += HandleNavigationStopped;
            }
        }

        private void OnDisable()
        {
            if (_campusManager != null)
            {
                _campusManager.OnBuildingChanged -= HandleBuildingChanged;
                _campusManager.OnFloorChanged -= HandleFloorChanged;
            }

            if (_floorManager != null)
            {
                _floorManager.OnFloorTransitionStarted -= HandleFloorTransitionStarted;
                _floorManager.OnFloorTransitionCompleted -= HandleFloorTransitionCompleted;
            }

            if (_localizationManager != null)
            {
                _localizationManager.OnLocalized -= HandleLocalized;
                _localizationManager.OnTrackingConfidenceChanged -= HandleConfidenceChanged;
            }

            if (_routeManager != null)
            {
                _routeManager.OnRouteStarted -= HandleRouteStarted;
                _routeManager.OnWaypointReached -= HandleWaypointReached;
                _routeManager.OnDestinationArrived -= HandleDestinationArrived;
                _routeManager.OnNavigationStopped -= HandleNavigationStopped;
            }
        }

        private void Update()
        {
            if (_routeManager != null && _routeManager.IsNavigating)
            {
                UpdateLiveRouteDisplay();
            }
        }

        private void UpdateLiveRouteDisplay()
        {
            if (DistanceText != null)
            {
                DistanceText.text = $"{_routeManager.DistanceToNextMeters:F1} m";
            }

            if (NextWaypointText != null && _routeManager.NextWaypoint != null)
            {
                NextWaypointText.text = $"Next: {_routeManager.NextWaypoint.name}";
            }

            // Rotate direction arrow toward next waypoint relative to camera forward
            if (DirectionArrow != null && Camera.main != null && _routeManager.CurrentWaypoint != null)
            {
                Vector3 targetWorld = _localizationManager != null
                    ? _localizationManager.MapToWorldPosition(_routeManager.CurrentWaypoint.localX, _routeManager.CurrentWaypoint.localY, _routeManager.CurrentWaypoint.localZ)
                    : new Vector3(_routeManager.CurrentWaypoint.localX, _routeManager.CurrentWaypoint.localY, _routeManager.CurrentWaypoint.localZ);

                Vector3 directionToTarget = targetWorld - Camera.main.transform.position;
                directionToTarget.y = 0;

                Vector3 forward = Camera.main.transform.forward;
                forward.y = 0;

                if (directionToTarget != Vector3.zero && forward != Vector3.zero)
                {
                    float angle = Vector3.SignedAngle(forward, directionToTarget, Vector3.up);
                    DirectionArrow.localRotation = Quaternion.Euler(0, 0, -angle);
                }
            }
        }

        // ── Callbacks ───────────────────────────────────────────────────────

        private void HandleBuildingChanged(BuildingData building)
        {
            if (BuildingText != null) BuildingText.text = building.name;
        }

        private void HandleFloorChanged(FloorData floor)
        {
            if (FloorText != null) FloorText.text = floor.name;
        }

        private void HandleConfidenceChanged(TrackingConfidence confidence)
        {
            if (StatusText == null) return;

            switch (confidence)
            {
                case TrackingConfidence.Normal:
                    StatusText.text = "Tracking Normal • AR Active";
                    if (StatusBadgeImage != null) StatusBadgeImage.color = new Color(0.1f, 0.8f, 0.4f); // Emerald
                    break;
                case TrackingConfidence.Calibrating:
                    StatusText.text = "Calibrating AR Origin…";
                    if (StatusBadgeImage != null) StatusBadgeImage.color = new Color(0.9f, 0.7f, 0.1f); // Amber
                    break;
                case TrackingConfidence.Relocalizing:
                    StatusText.text = "Relocalizing • Scan Reference Marker";
                    if (StatusBadgeImage != null) StatusBadgeImage.color = new Color(0.9f, 0.4f, 0.1f); // Orange
                    break;
                default:
                    StatusText.text = "Tracking Unavailable";
                    if (StatusBadgeImage != null) StatusBadgeImage.color = new Color(0.8f, 0.2f, 0.2f); // Red
                    break;
            }
        }

        private void HandleLocalized(MarkerAnchorData marker, Vector3 offset, Quaternion rotation)
        {
            if (StatusText != null) StatusText.text = $"Localized • {marker.markerId}";
            if (StatusBadgeImage != null) StatusBadgeImage.color = new Color(0.06f, 0.75f, 0.95f); // Cyan
        }

        private void HandleFloorTransitionStarted(string targetFloorId, IndoorNodeData transitionNode)
        {
            if (FloorTransitionBanner != null)
            {
                FloorTransitionBanner.SetActive(true);
                if (TransitionMessageText != null)
                {
                    TransitionMessageText.text = $"Please ascend stairs to {targetFloorId}. Tap confirm when reached.";
                }
            }
        }

        private void HandleFloorTransitionCompleted(FloorData newFloor)
        {
            if (FloorTransitionBanner != null)
            {
                FloorTransitionBanner.SetActive(false);
            }
        }

        private void HandleRouteStarted(System.Collections.Generic.List<IndoorNodeData> waypoints)
        {
            if (InstructionPanel != null) InstructionPanel.SetActive(true);
            if (StartButton != null) StartButton.gameObject.SetActive(false);
            if (StopButton != null) StopButton.gameObject.SetActive(true);

            if (InstructionText != null && waypoints.Count > 0)
            {
                InstructionText.text = $"Follow corridor to {waypoints[0].name}";
            }
        }

        private void HandleWaypointReached(int index, IndoorNodeData node)
        {
            if (InstructionText != null && _routeManager != null && _routeManager.NextWaypoint != null)
            {
                InstructionText.text = $"Continue toward {_routeManager.NextWaypoint.name}";
            }
        }

        private void HandleDestinationArrived(IndoorNodeData node)
        {
            if (ArrivalModal != null)
            {
                ArrivalModal.SetActive(true);
                if (ArrivalTitleText != null) ArrivalTitleText.text = "Destination Reached!";
                if (ArrivalRoomText != null) ArrivalRoomText.text = node.name;
            }

            if (InstructionPanel != null) InstructionPanel.SetActive(false);
            if (StartButton != null) StartButton.gameObject.SetActive(true);
            if (StopButton != null) StopButton.gameObject.SetActive(false);
        }

        private void HandleNavigationStopped()
        {
            if (InstructionPanel != null) InstructionPanel.SetActive(false);
            if (StartButton != null) StartButton.gameObject.SetActive(true);
            if (StopButton != null) StopButton.gameObject.SetActive(false);
            if (FloorTransitionBanner != null) FloorTransitionBanner.SetActive(false);
        }

        // ── Button Handlers ─────────────────────────────────────────────────

        private void HandleStartClicked()
        {
            if (_routeManager != null)
            {
                // Default demonstration: Navigate from Academic Block Entrance to ML Lab
                _routeManager.CalculateLocalRoute("cse-g-entry", "cse-g-lab-door");
            }
        }

        private void HandleStopClicked()
        {
            if (_routeManager != null) _routeManager.StopNavigation();
        }

        private void HandleRelocalizeClicked()
        {
            if (_localizationManager != null) _localizationManager.RequestRelocalization();
        }

        private void HandleConfirmNewFloorClicked()
        {
            if (_floorManager != null && !string.IsNullOrEmpty(_floorManager.TargetTransitionFloorId))
            {
                _floorManager.CompleteFloorTransition(_floorManager.TargetTransitionFloorId);
            }
        }

        private void HandleDismissArrivalClicked()
        {
            if (ArrivalModal != null) ArrivalModal.SetActive(false);
        }
    }
}
