// PathFinderAPIClient.cs
// Calls the PathFinder REST endpoints using UnityWebRequest.
// Handles HTTP errors, JSON parse errors, and network timeouts.
//
// Endpoints used:
//   POST /api/routes/calculate   — route calculation
//   GET  /api/destinations       — destination list
//
// Both endpoints are defined in PathFinderAR-main/backend/restRoutes.ts.
// The request/response types mirror PathFinderModels.cs exactly.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PathFinderAR
{
    public class PathFinderAPIClient : MonoBehaviour
    {
        [Header("Configuration")]
        public PathFinderConfig Config;

        // ── Public events ──────────────────────────────────────────────────
        public event Action<RouteResponse>          OnRouteSuccess;
        public event Action<string>                 OnRouteError;
        public event Action<DestinationListResponse> OnDestinationsSuccess;
        public event Action<string>                 OnDestinationsError;

        // ── Route calculation ──────────────────────────────────────────────

        /// <summary>
        /// Calls POST /api/routes/calculate.
        /// startNodeId and destinationNodeId must be campus graph node IDs
        /// (e.g. "gate", "library", "cse-lab") — NOT destination IDs like "d1".
        /// See campusNodes in PathFinderAR-main/shared/pathfinder.ts for all valid IDs.
        /// </summary>
        public void CalculateRoute(string startNodeId, string destinationNodeId, bool accessibleOnly = false)
        {
            if (Config == null) { OnRouteError?.Invoke("PathFinderConfig is not assigned."); return; }
            StartCoroutine(PostRouteRequest(startNodeId, destinationNodeId, accessibleOnly));
        }

        private IEnumerator PostRouteRequest(string startNodeId, string destNodeId, bool accessibleOnly)
        {
            string url  = Config.BaseUrl.TrimEnd('/') + "/api/routes/calculate";
            var    body = new RouteRequest
            {
                startNodeId      = startNodeId,
                destinationNodeId = destNodeId,
                algorithm        = "auto",
                accessibleOnly   = accessibleOnly
            };
            string json     = JsonUtility.ToJson(body);
            byte[] bodyRaw  = Encoding.UTF8.GetBytes(json);

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = Mathf.CeilToInt(Config.ApiTimeoutSeconds);

            Debug.Log($"[PathFinder] POST {url} body={json}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                string msg = $"Network error: {req.error}. Is the server running? Is the device on the same Wi-Fi?";
                Debug.LogError("[PathFinder] " + msg);
                OnRouteError?.Invoke(msg);
                yield break;
            }

            string responseText = req.downloadHandler.text;
            Debug.Log($"[PathFinder] Response {req.responseCode}: {responseText}");

            if (req.responseCode >= 400)
            {
                // Try to extract the server's error message
                string serverMsg = responseText;
                try
                {
                    var err = JsonUtility.FromJson<RouteErrorResponse>(responseText);
                    if (!string.IsNullOrEmpty(err?.error)) serverMsg = err.error;
                }
                catch { /* ignore parse error */ }
                OnRouteError?.Invoke($"Server error {req.responseCode}: {serverMsg}");
                yield break;
            }

            RouteResponse route;
            try
            {
                route = JsonUtility.FromJson<RouteResponse>(responseText);
            }
            catch (Exception ex)
            {
                OnRouteError?.Invoke($"Failed to parse route response: {ex.Message}");
                yield break;
            }

            if (route == null || route.waypoints == null || route.waypoints.Length == 0)
            {
                OnRouteError?.Invoke("Route response contained no waypoints.");
                yield break;
            }

            Debug.Log($"[PathFinder] Route OK: {route.waypoints.Length} waypoints, {route.totalDistance}m, {route.estimatedTime}s");
            OnRouteSuccess?.Invoke(route);
        }

        // ── Destination list ───────────────────────────────────────────────

        /// <summary>
        /// Calls GET /api/destinations (optionally filtered by query string).
        /// </summary>
        public void FetchDestinations(string query = "")
        {
            if (Config == null) { OnDestinationsError?.Invoke("PathFinderConfig is not assigned."); return; }
            StartCoroutine(GetDestinations(query));
        }

        private IEnumerator GetDestinations(string query)
        {
            string url = Config.BaseUrl.TrimEnd('/') + "/api/destinations";
            if (!string.IsNullOrWhiteSpace(query))
                url += "?query=" + UnityWebRequest.EscapeURL(query);

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = Mathf.CeilToInt(Config.ApiTimeoutSeconds);

            Debug.Log($"[PathFinder] GET {url}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                string msg = $"Network error: {req.error}";
                Debug.LogError("[PathFinder] " + msg);
                OnDestinationsError?.Invoke(msg);
                yield break;
            }

            string responseText = req.downloadHandler.text;

            if (req.responseCode >= 400)
            {
                OnDestinationsError?.Invoke($"Server error {req.responseCode}: {responseText}");
                yield break;
            }

            DestinationListResponse result;
            try
            {
                result = JsonUtility.FromJson<DestinationListResponse>(responseText);
            }
            catch (Exception ex)
            {
                OnDestinationsError?.Invoke($"Failed to parse destinations: {ex.Message}");
                yield break;
            }

            Debug.Log($"[PathFinder] Destinations OK: {result?.total} items");
            OnDestinationsSuccess?.Invoke(result);
        }
    }
}
