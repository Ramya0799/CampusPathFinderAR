// LocalizationManager.cs
// Manages indoor localization, visual reference-marker tracking via AR Foundation,
// and world coordinate calibration.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace PathFinderAR
{
    public class LocalizationManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CampusManager _campusManager;
        [SerializeField] private ARTrackedImageManager _trackedImageManager;
        [SerializeField] private ARSession _arSession;

        [Header("Calibration Parameters")]
        [Tooltip("Local convention: 1 Unity unit = 1.0 Metre")]
        public float MetersPerUnit = 1.0f;
        [Tooltip("Minimum confidence required before enabling directional guidance")]
        [SerializeField] private TrackingConfidence _minimumConfidenceForGuidance = TrackingConfidence.Normal;

        [Header("State (Inspector Read-Only)")]
        [SerializeField] private TrackingConfidence _currentConfidence = TrackingConfidence.None;
        [SerializeField] private LocalizationMode _currentMode = LocalizationMode.OutdoorGPS;
        [SerializeField] private string _activeMarkerId;
        [SerializeField] private Vector3 _calibratedOriginOffset = Vector3.zero;
        [SerializeField] private float _calibratedHeadingOffsetDeg = 0f;

        // Public properties
        public TrackingConfidence Confidence => _currentConfidence;
        public LocalizationMode Mode => _currentMode;
        public string ActiveMarkerId => _activeMarkerId;
        public bool IsLocalized => _currentConfidence >= _minimumConfidenceForGuidance;
        public Vector3 WorldOriginOffset => _calibratedOriginOffset;
        public float HeadingCorrectionDegrees => _calibratedHeadingOffsetDeg;

        // Events
        public event Action<MarkerAnchorData, Vector3, Quaternion> OnLocalized;
        public event Action<TrackingConfidence> OnTrackingConfidenceChanged;
        public event Action OnRelocalizationRequested;

        private void Awake()
        {
            if (_campusManager == null)
            {
                _campusManager = GetComponent<CampusManager>() ?? FindFirstObjectByType<CampusManager>();
            }

            if (_trackedImageManager == null)
            {
                _trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
            }

            if (_arSession == null)
            {
                _arSession = FindFirstObjectByType<ARSession>();
            }
        }

        private void OnEnable()
        {
            if (_trackedImageManager != null)
            {
                _trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            }

            ARSession.stateChanged += OnARSessionStateChanged;
        }

        private void OnDisable()
        {
            if (_trackedImageManager != null)
            {
                _trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            }

            ARSession.stateChanged -= OnARSessionStateChanged;
        }

        // ── AR Foundation Tracked Image Callback ─────────────────────────────

        private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
        {
            foreach (var trackedImage in eventArgs.added)
            {
                ProcessTrackedImage(trackedImage);
            }

            foreach (var trackedImage in eventArgs.updated)
            {
                if (trackedImage.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking)
                {
                    ProcessTrackedImage(trackedImage);
                }
                else if (trackedImage.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Limited)
                {
                    if (_currentConfidence == TrackingConfidence.Normal)
                    {
                        SetConfidence(TrackingConfidence.Low);
                    }
                }
            }
        }

        private void ProcessTrackedImage(ARTrackedImage trackedImage)
        {
            if (trackedImage == null || trackedImage.referenceImage == null) return;

            string imageName = trackedImage.referenceImage.name;
            Debug.Log($"[LocalizationManager] Reference marker detected: {imageName}");

            MarkerAnchorData marker = _campusManager != null ? _campusManager.GetMarker(imageName) : null;
            if (marker == null)
            {
                Debug.LogWarning($"[LocalizationManager] Detected image '{imageName}' is not registered in CampusManager dataset.");
                return;
            }

            // Calibrate world coordinates using the marker's physical pose in AR space
            ApplyMarkerCalibration(marker, trackedImage.transform.position, trackedImage.transform.rotation);
        }

        // ── AR Session Tracking State Callback ──────────────────────────────

        private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            switch (args.state)
            {
                case ARSessionState.SessionTracking:
                    if (_currentConfidence < TrackingConfidence.Normal && !string.IsNullOrEmpty(_activeMarkerId))
                    {
                        SetConfidence(TrackingConfidence.Normal);
                    }
                    break;
                case ARSessionState.SessionInitializing:
                    SetConfidence(TrackingConfidence.Calibrating);
                    break;
                case ARSessionState.NeedsInstall:
                case ARSessionState.Unsupported:
                case ARSessionState.None:
                    SetConfidence(TrackingConfidence.None);
                    break;
            }
        }

        // ── Coordinate Calibration ──────────────────────────────────────────

        /// <summary>
        /// Calibrates the virtual campus origin to the physical marker position and orientation.
        /// Documented convention:
        ///   X = East / Right
        ///   Y = Up
        ///   Z = North / Forward
        ///   1 Unit = 1 Metre
        /// </summary>
        public void ApplyMarkerCalibration(MarkerAnchorData marker, Vector3 markerArPosition, Quaternion markerArRotation)
        {
            if (marker == null) return;

            _activeMarkerId = marker.markerId;
            _currentMode = LocalizationMode.VisualMarker;

            // Compute offset between physical marker position in AR and expected map coordinate
            Vector3 expectedMapCoord = new Vector3(marker.originX, marker.originY, marker.originZ);
            _calibratedOriginOffset = markerArPosition - expectedMapCoord;
            _calibratedHeadingOffsetDeg = marker.headingCorrectionDeg;

            SetConfidence(TrackingConfidence.Normal);

            Debug.Log($"[LocalizationManager] Localized to marker '{marker.markerId}'. Origin Offset={_calibratedOriginOffset}, HeadingCorrection={_calibratedHeadingOffsetDeg}°");

            // Notify CampusManager of building and floor activation if applicable
            if (_campusManager != null && !string.IsNullOrEmpty(marker.floorId))
            {
                _campusManager.SelectFloor(marker.floorId);
            }

            OnLocalized?.Invoke(marker, _calibratedOriginOffset, markerArRotation);
        }

        /// <summary>
        /// Transforms a local floor metric coordinate (x, y, z) into Unity world position.
        /// </summary>
        public Vector3 MapToWorldPosition(float localX, float localY, float localZ)
        {
            Vector3 localPos = new Vector3(localX, localY, localZ) * MetersPerUnit;
            // Apply heading rotation if needed
            if (Mathf.Abs(_calibratedHeadingOffsetDeg) > 0.01f)
            {
                localPos = Quaternion.Euler(0, _calibratedHeadingOffsetDeg, 0) * localPos;
            }
            return localPos + _calibratedOriginOffset;
        }

        public void RequestRelocalization()
        {
            SetConfidence(TrackingConfidence.Relocalizing);
            _currentMode = LocalizationMode.VisualMarker;
            Debug.Log("[LocalizationManager] Relocalization requested. Please point camera at a registered marker.");
            OnRelocalizationRequested?.Invoke();
        }

        public void SetConfidence(TrackingConfidence confidence)
        {
            if (_currentConfidence != confidence)
            {
                _currentConfidence = confidence;
                OnTrackingConfidenceChanged?.Invoke(confidence);
            }
        }

        public void SetOutdoorGpsMode()
        {
            _currentMode = LocalizationMode.OutdoorGPS;
            _activeMarkerId = string.Empty;
            _calibratedOriginOffset = Vector3.zero;
            _calibratedHeadingOffsetDeg = 0f;
            SetConfidence(TrackingConfidence.Normal);
        }

        /// <summary>
        /// Simulation helper: Simulates marker detection for Unity Editor testing without physical camera.
        /// </summary>
        public bool SimulateMarkerLocalization(string markerId)
        {
            if (_campusManager == null) return false;
            var marker = _campusManager.GetMarker(markerId);
            if (marker == null)
            {
                Debug.LogWarning($"[LocalizationManager] Simulation marker '{markerId}' not found.");
                return false;
            }

            ApplyMarkerCalibration(marker, new Vector3(marker.originX, marker.originY, marker.originZ), Quaternion.identity);
            return true;
        }
    }
}
