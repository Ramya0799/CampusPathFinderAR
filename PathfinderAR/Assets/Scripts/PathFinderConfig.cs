// PathFinderConfig.cs
// Central configuration for the PathFinder API connection.
//
// ── HOW TO SET YOUR SERVER ADDRESS ──────────────────────────────────────────
// On a physical Android device, "localhost" and "127.0.0.1" refer to the
// device itself — NOT your PC.  Use your PC's LAN IP address instead.
//
// Steps:
//   1. On Windows run: ipconfig
//      Look for "IPv4 Address" under your Wi-Fi adapter, e.g. 192.168.1.42
//   2. Change BaseUrl below to: "http://192.168.1.42:3000"
//   3. Your PC and phone must be on the SAME Wi-Fi network.
//   4. Windows Firewall: allow inbound TCP on port 3000
//      (or whatever port the server prints on startup).
//
// Android cleartext HTTP:
//   Plain http:// is blocked by Android 9+ by default.
//   This project's AndroidManifest.xml enables cleartext traffic for
//   development only via a network security config (see res/xml/network_security_config.xml).
//   Remove or narrow that config before any production deployment.
// ────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace PathFinderAR
{
    [CreateAssetMenu(fileName = "PathFinderConfig", menuName = "PathFinder/Config")]
    public class PathFinderConfig : ScriptableObject
    {
        [Header("API Server")]
        [Tooltip("Base URL of the PathFinder backend. Do NOT use localhost on a physical device.")]
        public string BaseUrl = "http://162.15.13.228:3000";

        [Header("Timeouts")]
        [Tooltip("Seconds to wait for an API response before giving up.")]
        public float ApiTimeoutSeconds = 10f;

        [Header("Navigation")]
        [Tooltip("Metres from a waypoint to consider it reached.")]
        public float WaypointArrivalRadius = 8f;

        [Tooltip("Metres off-route before an error hint is shown.")]
        public float DeviationThresholdMeters = 20f;

        [Tooltip("Minimum GPS accuracy (metres) before we trust the position.")]
        public float MinAcceptableGpsAccuracy = 25f;

        [Header("AR Visuals")]
        [Tooltip("World scale: metres per Unity unit. Leave at 1 for 1:1 real-world scale.")]
        public float MetersPerUnit = 1f;

        [Tooltip("Height (Unity units) at which waypoint markers float above ground.")]
        public float MarkerHeight = 1.5f;
    }
}
