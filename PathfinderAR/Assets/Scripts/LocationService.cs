// LocationService.cs
// Wraps Unity's Input.location and Input.compass with clean status reporting.
//
// GPS LIMITATIONS — READ BEFORE USING:
//   • GPS accuracy outdoors is typically 3–15 m; good enough for campus navigation.
//   • GPS is unreliable or absent INDOORS.  Do not claim GPS provides indoor positioning.
//   • First fix can take 10–60 s after permission is granted.
//   • Heading from Input.compass is magnetic north; it may drift near metal structures.

using System;
using System.Collections;
using UnityEngine;

namespace PathFinderAR
{
    public enum LocationStatus
    {
        Idle,
        Requesting,
        Active,
        LowAccuracy,
        Failed,
        NotSupported
    }

    public class LocationService : MonoBehaviour
    {
        [Header("Configuration")]
        public PathFinderConfig Config;

        [Header("Status (read-only in Inspector)")]
        [SerializeField] private LocationStatus _status = LocationStatus.Idle;
        [SerializeField] private float  _latitude;
        [SerializeField] private float  _longitude;
        [SerializeField] private float  _accuracyMeters;
        [SerializeField] private float  _compassHeading;
        [SerializeField] private bool   _compassAvailable;

        // Public read-only properties
        public LocationStatus Status         => _status;
        public float          Latitude       => _latitude;
        public float          Longitude      => _longitude;
        public float          AccuracyMeters => _accuracyMeters;
        public float          CompassHeading => _compassHeading;
        public bool           CompassAvailable => _compassAvailable;
        public bool           IsReady        => _status == LocationStatus.Active || _status == LocationStatus.LowAccuracy;

        // ── Events ─────────────────────────────────────────────────────────
        public event Action<float, float, float> OnLocationUpdated;  // lat, lng, accuracy
        public event Action<float>               OnHeadingUpdated;   // degrees
        public event Action<string>              OnError;

        private const float DesiredAccuracyMeters = 5f;
        private const float UpdateDistanceMeters  = 1f;

        private void Start()
        {
            StartCoroutine(StartLocationServices());
        }

        private IEnumerator StartLocationServices()
        {
            // ── Compass ────────────────────────────────────────────────────
            _compassAvailable = SystemInfo.supportsGyroscope;
            if (_compassAvailable)
            {
                Input.compass.enabled = true;
                Input.gyro.enabled    = true;
            }

            // ── Location permission ────────────────────────────────────────
            if (!Input.location.isEnabledByUser)
            {
                _status = LocationStatus.NotSupported;
                string msg = "Location services are disabled on this device. Enable them in Settings.";
                Debug.LogWarning("[LocationService] " + msg);
                OnError?.Invoke(msg);
                yield break;
            }

            _status = LocationStatus.Requesting;
            Input.location.Start(DesiredAccuracyMeters, UpdateDistanceMeters);

            // Wait up to 20 seconds for the first fix
            int waited = 0;
            while (Input.location.status == LocationServiceStatus.Initializing && waited < 20)
            {
                yield return new WaitForSeconds(1f);
                waited++;
            }

            if (Input.location.status == LocationServiceStatus.Failed)
            {
                _status = LocationStatus.Failed;
                string msg = "Location initialisation failed. Make sure GPS is enabled and you are outdoors.";
                Debug.LogError("[LocationService] " + msg);
                OnError?.Invoke(msg);
                yield break;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                _status = LocationStatus.Failed;
                OnError?.Invoke("Location timed out. Try again outdoors.");
                yield break;
            }

            Debug.Log("[LocationService] GPS running.");
            StartCoroutine(PollLocation());
        }

        private IEnumerator PollLocation()
        {
            while (true)
            {
                if (Input.location.status == LocationServiceStatus.Running)
                {
                    var loc = Input.location.lastData;
                    _latitude       = loc.latitude;
                    _longitude      = loc.longitude;
                    _accuracyMeters = loc.horizontalAccuracy;

                    float minAccuracy = Config != null ? Config.MinAcceptableGpsAccuracy : 25f;
                    _status = _accuracyMeters <= minAccuracy ? LocationStatus.Active : LocationStatus.LowAccuracy;

                    OnLocationUpdated?.Invoke(_latitude, _longitude, _accuracyMeters);
                }

                if (_compassAvailable)
                {
                    _compassHeading = Input.compass.trueHeading;
                    if (_compassHeading == 0f)
                        _compassHeading = Input.compass.magneticHeading;
                    OnHeadingUpdated?.Invoke(_compassHeading);
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void OnDisable()
        {
            Input.location.Stop();
        }
    }
}
