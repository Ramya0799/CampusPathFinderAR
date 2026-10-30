// PathFinderModels.cs
// Data models that exactly mirror the /api/routes/calculate and /api/destinations response shapes.
// Do NOT change field names — JsonUtility uses them verbatim.

using System;

namespace PathFinderAR
{
    // ─── /api/routes/calculate ────────────────────────────────────────────────

    [Serializable]
    public class RouteRequest
    {
        public string startNodeId;
        public string destinationNodeId;
        public string algorithm = "auto";   // "auto" | "dijkstra" | "astar"
        public bool   accessibleOnly = false;
    }

    [Serializable]
    public class RouteWaypoint
    {
        public string id;
        public string name;
        public string buildingName;
        public string floorName;
        public string nodeType;
        public double latitude;
        public double longitude;
        public float  xCoordinate;
        public float  yCoordinate;
        public bool   isAccessible;
        public bool   isActive;
    }

    [Serializable]
    public class RouteProcessing
    {
        public int   visitedNodeCount;
        public float executionTimeMs;
    }

    [Serializable]
    public class RouteResponse
    {
        public string         routeId;
        public string         algorithm;       // "Dijkstra" or "A*"
        public RouteWaypoint[] waypoints;
        public float          totalDistance;   // metres
        public float          estimatedTime;   // seconds
        public string[]       instructions;
        public RouteProcessing processing;
        public string         source;
    }

    // ─── /api/routes/calculate error ─────────────────────────────────────────

    [Serializable]
    public class RouteErrorResponse
    {
        public string error;
    }

    // ─── /api/destinations ────────────────────────────────────────────────────

    [Serializable]
    public class DestinationItem
    {
        public string id;          // e.g. "d1"
        public string nodeId;      // e.g. "gate"  ← used as startNodeId / destinationNodeId
        public string name;
        public string category;
        public string buildingName;
        public string floorName;
        public string description;
        public string roomNumber;
        public string accessibilityInfo;
        public string imageUrl;
        public bool   isActive;
    }

    [Serializable]
    public class DestinationListResponse
    {
        public int               total;
        public DestinationItem[] destinations;
        public string            source;
    }
}
