// CampusDataModels.cs
// Canonical C# data models for data-driven indoor and outdoor campus navigation.
// Supports multi-building, multi-floor, room, node, edge, and localization marker definitions.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderAR
{
    public enum LocalizationMode
    {
        OutdoorGPS,
        VisualMarker,
        ManualAnchor
    }

    public enum TrackingConfidence
    {
        None,
        Low,
        Calibrating,
        Normal,
        Relocalizing
    }

    public enum NodeType
    {
        Entrance,
        CorridorJunction,
        RoomDoor,
        RoomInterior,
        Staircase,
        Elevator,
        OutdoorGateway,
        MarkerAnchor
    }

    // ── Campus Top-Level Structure ──────────────────────────────────────────

    [Serializable]
    public class CampusData
    {
        public string schemaVersion = "1.0";
        public string campusId = "aiet-mijar";
        public string campusName = "Alva's Institute of Engineering & Technology";
        public string campusAddress = "Shobhavana Campus, Mijar, Moodbidri, DK - 574225";
        public List<BuildingData> buildings = new List<BuildingData>();
        public List<MarkerAnchorData> globalMarkers = new List<MarkerAnchorData>();
    }

    // ── Building Structure ──────────────────────────────────────────────────

    [Serializable]
    public class BuildingData
    {
        public string id;                   // e.g. "library", "cse"
        public string name;                 // e.g. "AIET Central Library", "CSE Academic Block"
        public string code;                 // e.g. "LIB", "CSE"
        public string description;
        public double latitude;             // Building entrance WGS84 coordinates
        public double longitude;
        public List<FloorData> floors = new List<FloorData>();
    }

    // ── Floor Structure ─────────────────────────────────────────────────────

    [Serializable]
    public class FloorData
    {
        public string id;                   // e.g. "central-g", "central-f1", "cse-g"
        public string buildingId;           // References BuildingData.id
        public string name;                 // e.g. "Ground Floor", "First Floor"
        public int floorLevel;              // 0 = Ground, 1 = 1st Floor, -1 = Basement
        public float elevationMeters;       // Vertical Y offset in Unity meters (e.g. 0.0, 3.8)
        public string floorPlanImage;       // Relative filename in Images (e.g. "Library Floor Plan.png")
        public float widthMeters;           // Approximate metric width
        public float lengthMeters;          // Approximate metric length
        public bool isVerifiedDimensions;   // True if established from physical survey
        public List<RoomData> rooms = new List<RoomData>();
        public List<IndoorNodeData> nodes = new List<IndoorNodeData>();
        public List<IndoorEdgeData> edges = new List<IndoorEdgeData>();
        public List<MarkerAnchorData> markers = new List<MarkerAnchorData>();
    }

    // ── Room Structure ──────────────────────────────────────────────────────

    [Serializable]
    public class RoomData
    {
        public string id;                   // e.g. "C-108", "L-102"
        public string floorId;
        public string name;                 // e.g. "Machine Learning & Web Technology Laboratory"
        public string category;             // "Laboratory", "Reading Section", "Server Room", "Classroom"
        public string roomNumber;           // e.g. "C-108", "L-102"
        public float areaSqm;               // e.g. 155.55
        public float lengthMeters;          // e.g. 18.52
        public float widthMeters;           // e.g. 9.15
        public bool isVerifiedDimensions;   // True if verified from blueprint legend
        public string accessDoorId;         // Door label e.g. "D1"
        public bool isRestricted;           // True for Server Room, electrical closets
        public string destinationNodeId;    // References IndoorNodeData.id for routing
        public string notes;
    }

    // ── Graph Nodes & Edges ─────────────────────────────────────────────────

    [Serializable]
    public class IndoorNodeData
    {
        public string id;                   // Unique e.g. "cse-g-lab-door"
        public string floorId;
        public string name;
        public NodeType nodeType;
        public float localX;                // Metric coordinate in metres (East / Right)
        public float localY;                // Vertical elevation (typically floor elevation)
        public float localZ;                // Metric coordinate in metres (North / Forward)
        public bool isStaircase;            // True if this node connects across floors
        public bool isElevator;
        public string transitionTargetFloorId; // Floor reached when taking this stair/elevator
        public string transitionTargetNodeId;  // Paired node on target floor
        public bool isAccessible;           // Step-free accessible
        public bool isVerified;             // False for preliminary estimated coordinates
    }

    [Serializable]
    public class IndoorEdgeData
    {
        public string id;                   // e.g. "edge-cse-01"
        public string sourceNodeId;
        public string targetNodeId;
        public float distanceMeters;        // Walkable distance in metres
        public bool isBidirectional = true;
        public bool isAccessible = true;
        public bool isStaircaseTransition = false;
        public bool isVerified = false;     // True if physically measured
    }

    // ── Visual Localization Marker Anchor ───────────────────────────────────

    [Serializable]
    public class MarkerAnchorData
    {
        public string markerId;             // e.g. "MARKER_CSE_G01"
        public string buildingId;
        public string floorId;
        public string physicalLocation;     // e.g. "Ground floor entrance foyer notice board"
        public float physicalWidthMeters = 0.20f; // Printed marker physical width in metres (e.g. 20cm)
        public float originX;               // Map coordinate corresponding to marker center
        public float originY;
        public float originZ;
        public float headingCorrectionDeg;  // Offset between marker normal and floor North
        public bool isVerified;
    }

    // ── Navigation State Container ──────────────────────────────────────────

    [Serializable]
    public class ActiveNavigationSession
    {
        public string sessionId;
        public string buildingId;
        public string targetFloorId;
        public string destinationRoomId;
        public string destinationNodeId;
        public string startNodeId;
        public LocalizationMode localizationMode;
        public TrackingConfidence trackingConfidence;
        public List<IndoorNodeData> activeRoute = new List<IndoorNodeData>();
        public int currentWaypointIndex = 0;
        public float remainingDistanceMeters = 0f;
        public bool isFloorTransitionActive = false;
        public string targetFloorTransitionName = "";
        public bool isArrived = false;
        public bool isPaused = false;
    }
}
