import { TopNav } from "@/components/TopNav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { bearingDegrees, evaluateRouteDeviation, haversineDistanceMeters, normalizeRelativeBearing, calculateRoute } from "@shared/routing";
import { deriveRouteProgress } from "@shared/route-progress";
import { campusEdges, campusNodes, DEVIATION_THRESHOLD_METERS, destinations, getNode } from "@shared/pathfinder";
import { AlertTriangle, ArrowUp, Camera, CheckCircle2, ChevronLeft, Compass, FlipHorizontal, Layers, LocateFixed, Map as MapIcon, Navigation, RotateCcw, ShieldAlert, Smartphone, SwitchCamera, Volume2, VolumeX, X, MapPin, Sparkles } from "lucide-react";
import { buildArIntentUrl, isAndroidDevice, navDestinations } from "@shared/navData";
import { toast } from "sonner";
import React from "react";
import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import { Link } from "wouter";

import { ARRouteScene } from "@/features/ar/ARRouteScene";
import { ARVoiceAssistant, type WaypointNode } from "@/features/ar/voiceAssistant";
import { computeTurnGuidance, type TurnGuidanceResult } from "@/features/ar/turnGuidance";
import { useLanguage } from "@/contexts/LanguageContext";
import { readLiveNavigationSnapshot, writeLiveNavigationSnapshot } from "@shared/live-navigation";
import { FloorPlanModal } from "@/components/FloorPlanModal";


type CameraStatus = "idle" | "loading" | "active" | "error";
type LocationStatus = "idle" | "requesting" | "active" | "signal-lost" | "denied" | "error";
type OrientationStatus = "idle" | "unsupported" | "requesting" | "granted" | "denied";
type LocationState = { latitude: number; longitude: number; accuracy: number; timestamp: number; speed?: number; heading?: number };
type OrientationEventWithPermission = typeof DeviceOrientationEvent & { requestPermission?: () => Promise<"granted" | "denied"> };
type CompassEvent = DeviceOrientationEvent & { webkitCompassHeading?: number };

const stopStream = (stream: MediaStream | null) => {
  stream?.getTracks().forEach((track) => track.stop());
};

export default function ARNavigation() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const locationWatchRef = useRef<number | null>(null);
  const lastRerouteAt = useRef(0);

  const sceneRef = useRef<ARRouteScene | null>(null);
  const voiceAssistantRef = useRef<ARVoiceAssistant | null>(null);

  const [cameraStatus, setCameraStatus] = useState<CameraStatus>("idle");
  const [facingMode, setFacingMode] = useState<"environment" | "user">("environment");
  const [isMirrored, setIsMirrored] = useState(false);
  const [locationStatus, setLocationStatus] = useState<LocationStatus>("idle");
  const [orientationStatus, setOrientationStatus] = useState<OrientationStatus>("idle");
  const [showFloorPlan, setShowFloorPlan] = useState(false);
  const liveSnapshot = useMemo(() => readLiveNavigationSnapshot(), []);
  const [location, setLocation] = useState<LocationState | undefined>(() => {
    if (liveSnapshot?.position && liveSnapshot.position.latitude !== 0 && liveSnapshot.position.longitude !== 0) {
      return {
        latitude: liveSnapshot.position.latitude,
        longitude: liveSnapshot.position.longitude,
        accuracy: liveSnapshot.position.accuracy ?? 12,
        speed: liveSnapshot.position.speed,
        heading: liveSnapshot.position.heading,
        timestamp: liveSnapshot.updatedAt ?? Date.now(),
      };
    }
    return undefined;
  });
  const [compassHeading, setCompassHeading] = useState<number | null>(null);
  const [gpsHeading, setGpsHeading] = useState<number | null>(() => liveSnapshot?.position?.heading ?? null);
  const [headingSource, setHeadingSource] = useState<"compass" | "gps" | "none">("none");
  const [headingReliable, setHeadingReliable] = useState(false);
  const prevPositionRef = useRef<LocationState | null>(null);

  // Unified heading: compass sensor takes precedence; if unavailable or stationary, fall back to GPS course
  const heading = useMemo(() => {
    if (compassHeading !== null) return compassHeading;
    if (gpsHeading !== null) return gpsHeading;
    return null;
  }, [compassHeading, gpsHeading]);

  useEffect(() => {
    if (compassHeading !== null) {
      setHeadingSource("compass");
    } else if (gpsHeading !== null) {
      setHeadingSource("gps");
    } else {
      setHeadingSource("none");
    }
  }, [compassHeading, gpsHeading]);

  const [sourceNodeId, setSourceNodeId] = useState(() => liveSnapshot?.sourceNodeId || sessionStorage.getItem("aiet-source") || "current-location");
  const [destinationId, setDestinationId] = useState(() => liveSnapshot?.destinationId || sessionStorage.getItem("pathfinder-destination") || "d7");
  const [currentWaypointIndex, setCurrentWaypointIndex] = useState(() => liveSnapshot?.currentWaypointIndex ?? 0);
  const [error, setError] = useState("");
  const [deviationMessage, setDeviationMessage] = useState("");
  const [voiceEnabled, setVoiceEnabled] = useState(true);
  const [hasPulse, setHasPulse] = useState(false);
  const [isArrived, setIsArrived] = useState(false);

  const { language } = useLanguage();

  const destination = useMemo(() => {
    const match = destinations.find((item) => item.id === destinationId);
    if (match) return match;
    const navMatch = navDestinations.find((item) => item.id === destinationId);
    if (navMatch) {
      return {
        id: navMatch.id,
        nodeId: navMatch.building === "Admin Block" ? "admin-office" : "cse-entry",
        name: navMatch.name,
        category: "Academic" as const,
        buildingId: navMatch.building === "Admin Block" ? "admin" : "cse",
        buildingName: navMatch.building,
        floorName: navMatch.floorName,
        description: `${navMatch.name} located on ${navMatch.floorName} of ${navMatch.building}.`,
        roomNumber: navMatch.id.split(":")[1] || undefined,
        accessibilityInfo: "Step-free corridor & lift access",
        isActive: true,
      };
    }
    return destinations[0];
  }, [destinationId]);

  const navDataDestination = useMemo(() => {
    return (
      navDestinations.find((item) => item.id === destinationId) ||
      navDestinations.find((item) => item.id === destination.id)
    );
  }, [destinationId, destination.id]);

  const isAndroid = useMemo(() => isAndroidDevice(), []);
  const isHttps = useMemo(() => {
    if (typeof window === "undefined") return false;
    return window.location.protocol === "https:" || window.isSecureContext === true;
  }, []);

  const handleOpenInPathFinderAR = useCallback(() => {
    if (!isAndroid) {
      toast.info("AR navigation needs the PathFinder AR app on an Android phone with ARCore.", {
        description: navDataDestination ? `Destination: ${destination.name} (${navDataDestination.id})` : `Destination: ${destination.name}`,
        duration: 6000,
      });
      setError("AR navigation needs the PathFinder AR app on an Android phone with ARCore.");
      return;
    }
    if (navDataDestination) {
      const intentUrl = buildArIntentUrl(navDataDestination.id);
      window.location.href = intentUrl;
    } else {
      window.location.href = "intent://navigate#Intent;scheme=pathfinder;package=com.pathfinder.campusnavigation;end";
    }
  }, [navDataDestination, isAndroid, destination.name]);

  // Auto-snap to nearest accessible campus node from live GPS
  const liveNearestNode = useMemo(() => {
    if (!location) return null;
    return campusNodes
      .filter((n) => n.isAccessible && n.isActive)
      .reduce((closest, node) => {
        return haversineDistanceMeters(location, node) < haversineDistanceMeters(location, closest) ? node : closest;
      }, campusNodes[0]);
  }, [location]);

  const activeStartNodeId = useMemo(() => {
    if (liveNearestNode) return liveNearestNode.id;
    if (sourceNodeId && sourceNodeId !== "current-location") return sourceNodeId;
    return "gate";
  }, [liveNearestNode, sourceNodeId]);

  const route = useMemo(() => {
    if (activeStartNodeId === destination.nodeId) {
      return {
        algorithm: "dijkstra" as const,
        nodeIds: [activeStartNodeId],
        totalDistance: 0,
        estimatedTime: 0,
        visitedNodeCount: 1,
        executionTimeMs: 0,
      };
    }
    try {
      return calculateRoute(campusNodes, campusEdges, activeStartNodeId, destination.nodeId);
    } catch {
      return {
        algorithm: "dijkstra" as const,
        nodeIds: [activeStartNodeId, destination.nodeId],
        totalDistance: 0,
        estimatedTime: 0,
        visitedNodeCount: 2,
        executionTimeMs: 0,
      };
    }
  }, [destination.nodeId, activeStartNodeId]);

  const routeNodes = useMemo(() => route.nodeIds.map(getNode).filter((node): node is NonNullable<typeof node> => Boolean(node)), [route.nodeIds]);
  const progress = useMemo(() => location
    ? deriveRouteProgress(location, routeNodes, currentWaypointIndex)
    : { currentWaypointIndex, totalWaypoints: routeNodes.length, remainingDistance: route.totalDistance, progressPercent: 0, isComplete: false, distanceToCurrentWaypoint: 0, arrivalThreshold: 18 },
  [currentWaypointIndex, location, route.totalDistance, routeNodes]);
  
  const activeIndex = Math.max(currentWaypointIndex, progress.currentWaypointIndex);
  const nextWaypointIndex = Math.min(activeIndex + 1, Math.max(0, routeNodes.length - 1));
  const nextWaypoint = routeNodes[nextWaypointIndex] ?? routeNodes[0];
  const distanceToNext = location && nextWaypoint ? Math.round(haversineDistanceMeters(location, nextWaypoint)) : null;
  const targetBearing = nextWaypoint ? bearingDegrees(location ?? routeNodes[Math.min(activeIndex, routeNodes.length - 1)] ?? campusNodes[0], nextWaypoint) : 0;
  const relativeBearing = heading === null ? 0 : normalizeRelativeBearing(targetBearing, heading);
  const arrowRotation = heading === null ? 0 : Math.max(-80, Math.min(80, relativeBearing));
  const directionLabel = heading === null ? "Align your device for direction" : Math.abs(relativeBearing) <= 18 ? "Continue straight" : Math.abs(relativeBearing) > 145 ? "Turn around" : relativeBearing > 0 ? "Turn right" : "Turn left";
  const instruction = progress.isComplete ? `You have reached ${destination.name}.` : nextWaypoint ? `Continue toward ${nextWaypoint.name}.` : "Route ready.";

  // Dynamic Turn Guidance for HUD
  const guidance: TurnGuidanceResult = useMemo(() => {
    const origin = location ?? (routeNodes[0] ? { latitude: routeNodes[0].latitude, longitude: routeNodes[0].longitude } : null);
    if (!origin || !nextWaypoint) {
      return {
        bearingToNext: targetBearing,
        relativeAngle: relativeBearing,
        turnType: "straight",
        actionText: directionLabel,
        detailedInstruction: instruction,
        distanceToNextMeters: distanceToNext ?? 0,
        isCloseToTurn: (distanceToNext ?? 999) <= 8,
      };
    }
    return computeTurnGuidance(origin, nextWaypoint, heading);
  }, [location, nextWaypoint, heading, targetBearing, relativeBearing, directionLabel, instruction, distanceToNext]);

  // Automated Rerouting Callback for Voice Assistant
  const handleAutomatedReroute = useCallback(async (): Promise<boolean> => {
    if (!location) return false;
    const nearestSafeNode = campusNodes.filter((node) => node.isAccessible).reduce((closest, node) => 
      haversineDistanceMeters(location, node) < haversineDistanceMeters(location, closest) ? node : closest, campusNodes[0]
    );
    try {
      lastRerouteAt.current = Date.now();
      setSourceNodeId(nearestSafeNode.id);
      setCurrentWaypointIndex(0);
      setDeviationMessage(`Route updated from ${nearestSafeNode.name}.`);
      return true;
    } catch {
      return false;
    }
  }, [location]);

  // Initialize Voice Assistant
  useEffect(() => {
    const assistant = new ARVoiceAssistant({
      enabled: voiceEnabled,
      language,
      onRerouteRequest: handleAutomatedReroute,
    });
    voiceAssistantRef.current = assistant;

    if (route && destination) {
      assistant.reset(destination.name, route.totalDistance, route.estimatedTime);
    }

    return () => {
      assistant.stop();
      voiceAssistantRef.current = null;
    };
  }, [voiceEnabled, language, destination.name, route?.totalDistance, route?.estimatedTime, handleAutomatedReroute]);

  // Initialize Three.js Scene when canvas is mounted
  useEffect(() => {
    if (!canvasRef.current || cameraStatus !== "active") return;

    if (!sceneRef.current) {
      try {
        sceneRef.current = new ARRouteScene(canvasRef.current);
      } catch (err) {
        console.warn("Could not initialize 3D AR Scene:", err);
      }
    }

    const scene = sceneRef.current;
    if (scene && routeNodes.length > 0) {
      scene.setWaypoints(routeNodes.map((n) => ({ id: n.id, name: n.name, latitude: n.latitude, longitude: n.longitude })));
    }

    const handleResize = () => {
      if (canvasRef.current && sceneRef.current) {
        sceneRef.current.resize(canvasRef.current.clientWidth, canvasRef.current.clientHeight);
      }
    };
    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
    };
  }, [cameraStatus, routeNodes]);

  // Update Three.js and Voice Assistant on Location/Heading changes
  useEffect(() => {
    if (sceneRef.current) {
      if (location) {
        sceneRef.current.setUserPosition(location);
      }
      if (heading !== null) {
        sceneRef.current.setHeading(heading);
      }
      sceneRef.current.setActiveWaypointIndex(activeIndex);
    }

    if (progress.isComplete || (distanceToNext !== null && distanceToNext <= 5 && nextWaypointIndex === routeNodes.length - 1)) {
      setIsArrived(true);
    }

    if (voiceAssistantRef.current && location && routeNodes.length > 0) {
      voiceAssistantRef.current.processUpdate(
        location,
        activeIndex,
        routeNodes.map((n) => ({ id: n.id, name: n.name, latitude: n.latitude, longitude: n.longitude })),
        guidance,
        progress.isComplete || isArrived
      );
    }

    // Persist live navigation snapshot
    if (location) {
      writeLiveNavigationSnapshot({
        active: true,
        sourceNodeId,
        destinationId,
        currentWaypointIndex: activeIndex,
        totalWaypoints: routeNodes.length,
        progressPercent: progress.progressPercent,
        remainingDistance: progress.remainingDistance,
        currentInstruction: instruction,
        currentVoiceInstruction: guidance.actionText,
        isComplete: progress.isComplete || isArrived,
        positionSource: "gps",
        position: {
          latitude: location.latitude,
          longitude: location.longitude,
          accuracy: location.accuracy,
          heading: heading ?? undefined,
        },
        updatedAt: Date.now(),
      });
    }
  }, [location, heading, activeIndex, routeNodes, guidance, progress.isComplete, progress.progressPercent, progress.remainingDistance, instruction, distanceToNext, nextWaypointIndex, isArrived, sourceNodeId, destinationId]);

  // Waypoint approach pulse
  useEffect(() => {
    if (guidance.isCloseToTurn) {
      setHasPulse(true);
      const timer = setTimeout(() => setHasPulse(false), 2000);
      return () => clearTimeout(timer);
    }
  }, [guidance.isCloseToTurn]);

  const setLocationFromPosition = (position: GeolocationPosition) => {
    const next: LocationState = {
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      accuracy: position.coords.accuracy,
      speed: position.coords.speed ?? undefined,
      heading: position.coords.heading ?? undefined,
      timestamp: position.timestamp,
    };

    // 1. Native GPS heading from device if moving
    if (typeof position.coords.heading === "number" && !isNaN(position.coords.heading) && position.coords.heading >= 0) {
      setGpsHeading(Math.round(position.coords.heading));
      setHeadingReliable(true);
    } else if (prevPositionRef.current) {
      // 2. GPS course derived from consecutive fixes if moved >= 2.5 meters
      const distMoved = haversineDistanceMeters(prevPositionRef.current, next);
      if (distMoved >= 2.5) {
        const course = bearingDegrees(prevPositionRef.current, next);
        setGpsHeading(Math.round((course + 360) % 360));
        setHeadingReliable(true);
      }
    }

    prevPositionRef.current = next;
    setLocation(next);
    setLocationStatus("active");
  };

  const handleLocationError = (locationError: GeolocationPositionError) => {
    if (locationError.code === 1) {
      setLocationStatus("denied");
      setError("Location permission was denied. AR alignment is unavailable; use the map for reliable turn-by-turn guidance.");
    } else if (locationError.code === 2) {
      setLocationStatus("signal-lost");
      setError("Live location is temporarily unavailable. The route remains visible while PathFinder waits for recovery.");
    } else {
      setLocationStatus("error");
      setError("Location timed out. Try again outdoors or continue with map navigation.");
    }
  };

  const requestLocation = () => {
    if (!navigator.geolocation) {
      setLocationStatus("error");
      setError("Geolocation is unavailable in this browser. Open map navigation to continue.");
      return;
    }
    setLocationStatus("requesting");
    navigator.geolocation.getCurrentPosition(setLocationFromPosition, handleLocationError, { enableHighAccuracy: true, timeout: 10000, maximumAge: 5000 });
    if (locationWatchRef.current !== null) navigator.geolocation.clearWatch(locationWatchRef.current);
    locationWatchRef.current = navigator.geolocation.watchPosition(setLocationFromPosition, handleLocationError, { enableHighAccuracy: true, timeout: 15000, maximumAge: 3000 });
  };

  const enableOrientation = async (): Promise<boolean> => {
    if (typeof window === "undefined" || !("DeviceOrientationEvent" in window)) {
      setOrientationStatus("unsupported");
      return false;
    }
    setOrientationStatus("requesting");
    try {
      const eventWithPermission = DeviceOrientationEvent as OrientationEventWithPermission;
      if (typeof eventWithPermission.requestPermission === "function") {
        const response = await eventWithPermission.requestPermission();
        if (response === "granted") {
          setOrientationStatus("granted");
          return true;
        } else {
          setOrientationStatus("denied");
          return false;
        }
      } else {
        setOrientationStatus("granted");
        return true;
      }
    } catch {
      setOrientationStatus("denied");
      return false;
    }
  };

  const startCameraStream = async (targetFacing?: "environment" | "user") => {
    if (!window.isSecureContext && window.location.hostname !== "localhost") {
      setCameraStatus("error");
      setError("Camera access requires HTTPS. Open map navigation to continue on this connection.");
      return;
    }
    if (!navigator.mediaDevices?.getUserMedia) {
      setCameraStatus("error");
      setError("Camera access is unavailable in this browser. Open map navigation to continue.");
      return;
    }
    const desiredFacing = targetFacing === "user" || targetFacing === "environment" ? targetFacing : facingMode;
    setCameraStatus("loading");
    setError("");
    stopStream(streamRef.current);
    requestLocation();
    void enableOrientation();

    try {
      let stream: MediaStream;
      if (desiredFacing === "environment") {
        try {
          // 1. Try environment with HD constraints
          stream = await navigator.mediaDevices.getUserMedia({
            video: {
              facingMode: { ideal: "environment" },
              width: { ideal: 1920 },
              height: { ideal: 1080 },
            },
            audio: false,
          });
        } catch (err) {
          if (err instanceof DOMException && err.name === "NotAllowedError") {
            throw err;
          }
          // 2. Fallback to basic ideal environment
          stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: { ideal: "environment" } },
            audio: false,
          });
        }
      } else {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: "user" } },
          audio: false,
        });
      }

      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        try {
          await videoRef.current.play();
        } catch {}
      }

      const videoTrack = stream.getVideoTracks?.()?.[0];
      const actualSettings = videoTrack?.getSettings?.() as MediaTrackSettings | undefined;
      const actualFacing = actualSettings?.facingMode;
      if (actualFacing === "user" || actualFacing === "environment") {
        setFacingMode(actualFacing);
        setIsMirrored(actualFacing === "user");
      } else {
        setFacingMode(desiredFacing);
        setIsMirrored(desiredFacing === "user");
      }
      setCameraStatus("active");
    } catch (cameraError) {
      setCameraStatus("error");
      const denied = cameraError instanceof DOMException && cameraError.name === "NotAllowedError";
      setError(
        denied
          ? "Camera permission was denied. Allow camera access or use map navigation as the reliable fallback."
          : "Camera could not be started. Check browser permissions and HTTPS, then try again."
      );
      stopStream(streamRef.current);
      streamRef.current = null;
    }
  };

  const enableCamera = () => {
    void enableOrientation();
    void startCameraStream();
  };

  useEffect(() => {
    if (sessionStorage.getItem("pathfinder-ar-autostart") === "true") {
      sessionStorage.removeItem("pathfinder-ar-autostart");
      enableCamera();
    }
  }, []);

  const handleManualReroute = () => {
    void handleAutomatedReroute();
  };

  const toggleCameraFacing = async () => {
    const nextFacing: "environment" | "user" = facingMode === "environment" ? "user" : "environment";
    setFacingMode(nextFacing);
    setIsMirrored(nextFacing === "user");
    await startCameraStream(nextFacing);
  };

  const toggleMirror = () => {
    setIsMirrored((prev) => !prev);
  };

  const exitCamera = () => {
    stopStream(streamRef.current);
    streamRef.current = null;
    if (locationWatchRef.current !== null && navigator.geolocation) navigator.geolocation.clearWatch(locationWatchRef.current);
    locationWatchRef.current = null;
    if (sceneRef.current) {
      sceneRef.current.destroy();
      sceneRef.current = null;
    }
    if (voiceAssistantRef.current) {
      voiceAssistantRef.current.stop();
    }
    setCameraStatus("idle");
  };

  useEffect(() => {
    if (orientationStatus !== "granted" || typeof window === "undefined") return;

    const handler = (event: DeviceOrientationEvent) => {
      const compassEvent = event as CompassEvent;
      // 1. iOS Safari native webkitCompassHeading (0 = North, clockwise)
      if (typeof compassEvent.webkitCompassHeading === "number" && !isNaN(compassEvent.webkitCompassHeading)) {
        setCompassHeading(Math.round((compassEvent.webkitCompassHeading + 360) % 360));
        setHeadingReliable(true);
        return;
      }

      // 2. Android deviceorientationabsolute or absolute deviceorientation
      if (typeof event.alpha === "number" && !isNaN(event.alpha)) {
        // Standard W3C compass calculation: clockwise from North
        const androidHeading = Math.round((360 - event.alpha + 360) % 360);
        setCompassHeading(androidHeading);
        setHeadingReliable(Boolean(event.absolute));
      }
    };

    const hasAbsolute = "ondeviceorientationabsolute" in window;
    if (hasAbsolute) {
      window.addEventListener("deviceorientationabsolute", handler as EventListener, true);
    }
    window.addEventListener("deviceorientation", handler, true);

    return () => {
      if (hasAbsolute) {
        window.removeEventListener("deviceorientationabsolute", handler as EventListener, true);
      }
      window.removeEventListener("deviceorientation", handler, true);
    };
  }, [orientationStatus]);

  useEffect(() => {
    if (!location || !routeNodes.length) return;
    const nextProgress = deriveRouteProgress(location, routeNodes, currentWaypointIndex);
    if (nextProgress.currentWaypointIndex > currentWaypointIndex) setCurrentWaypointIndex(nextProgress.currentWaypointIndex);
    if (!cameraStatus || cameraStatus !== "active") return;
    const deviation = evaluateRouteDeviation(location, routeNodes, DEVIATION_THRESHOLD_METERS, lastRerouteAt.current, 15000);
    if (!deviation.shouldReroute) {
      if (!deviation.isOffRoute) setDeviationMessage("");
      return;
    }
    const nearestSafeNode = campusNodes.filter((node) => node.isAccessible).reduce((closest, node) => haversineDistanceMeters(location, node) < haversineDistanceMeters(location, closest) ? node : closest, campusNodes[0]);
    lastRerouteAt.current = Date.now();
    setSourceNodeId(nearestSafeNode.id);
    setCurrentWaypointIndex(0);
    setDeviationMessage(`Route updated from ${nearestSafeNode.name}.`);
  }, [cameraStatus, currentWaypointIndex, location, routeNodes]);

  useEffect(() => () => {
    stopStream(streamRef.current);
    if (locationWatchRef.current !== null && navigator.geolocation) navigator.geolocation.clearWatch(locationWatchRef.current);
    if (sceneRef.current) {
      sceneRef.current.destroy();
      sceneRef.current = null;
    }
    if (voiceAssistantRef.current) {
      voiceAssistantRef.current.stop();
    }
  }, []);

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-slate-950 text-white font-sans select-none">
      {/* Live Camera Video Feed */}
      <video
        ref={videoRef}
        playsInline
        muted
        autoPlay
        style={{ transform: isMirrored ? "scaleX(-1)" : "none" }}
        className={`absolute inset-0 h-full w-full object-cover transition-transform duration-300 ${
          cameraStatus === "active" ? "opacity-100" : "opacity-0"
        }`}
        aria-label={`Live ${facingMode === "environment" ? "rear" : "front"} camera preview`}
      />

      {/* Three.js 3D WebGL Canvas Layer */}
      <canvas
        ref={canvasRef}
        className={`absolute inset-0 h-full w-full pointer-events-none z-10 ${
          cameraStatus === "active" ? "block" : "hidden"
        }`}
      />

      {/* Subtle Vignette Gradient Overlay */}
      <div
        className={`absolute inset-0 pointer-events-none z-10 ${
          cameraStatus === "active"
            ? "bg-gradient-to-b from-slate-950/75 via-transparent to-slate-950/85"
            : "ar-fallback"
        }`}
      />

      {/* Camera Idle / Error / Requesting Permission Screen */}
      {(cameraStatus === "idle" || cameraStatus === "loading" || cameraStatus === "error") && (
        <div className="absolute inset-0 z-30 flex items-center justify-center p-6 bg-slate-950/90 backdrop-blur-md">
          <div className="max-w-md text-center p-8 rounded-3xl border border-white/10 bg-slate-900/90 shadow-2xl">
            <div className="mx-auto grid h-16 w-16 place-items-center rounded-2xl bg-blue-600 text-white shadow-xl shadow-blue-500/25">
              {cameraStatus === "loading" ? (
                <Navigation className="h-8 w-8 animate-pulse" />
              ) : (
                <Camera className="h-8 w-8" />
              )}
            </div>
            <h1 className="mt-5 font-display text-2xl font-bold text-white">
              {cameraStatus === "loading"
                ? "Starting 3D AR camera guidance…"
                : `Follow your route to ${destination.name}`}
            </h1>
            <p className="mt-3 text-sm leading-6 text-slate-300">
              Interactive 3D Three.js camera guidance with real-world compass lock, turn-by-turn HUD, continuous voice assistant, and automatic off-route recalculation.
            </p>
            {!navDataDestination && (
              <p role="status" className="mt-4 rounded-xl border border-amber-400/30 bg-amber-500/10 p-3 text-sm font-medium text-amber-200">
                This place isn't in the AR model yet - choose the nearest room in the app.
              </p>
            )}
            <div className="mt-6 flex flex-col gap-3">
              {(navDataDestination || isAndroid) && (
                <Button
                  onClick={handleOpenInPathFinderAR}
                  className="h-12 w-full rounded-xl bg-gradient-to-r from-amber-600 to-amber-700 px-6 font-bold text-white shadow-lg shadow-amber-600/30 hover:from-amber-700 hover:to-amber-800"
                >
                  <Sparkles className="mr-2 h-4 w-4 text-amber-200" />
                  Open in PathFinder AR
                </Button>
              )}
              <div className="flex flex-col sm:flex-row justify-center gap-3">
                <Link
                  href="/map"
                  className="inline-flex h-12 flex-1 items-center justify-center rounded-xl border border-white/20 bg-white/5 px-5 text-sm font-semibold text-white transition hover:bg-white/10"
                >
                  <MapIcon className="mr-2 h-4 w-4" />
                  Open map navigation
                </Link>
                {cameraStatus !== "loading" && typeof window !== "undefined" && Boolean(window.isSecureContext) && (
                  <Button
                    onClick={enableCamera}
                    variant="outline"
                    className="h-12 flex-1 rounded-xl border-white/20 bg-white/5 px-5 text-sm font-semibold text-white shadow-none hover:bg-white/10"
                  >
                    <Camera className="mr-2 h-4 w-4" />
                    Enable camera & location
                  </Button>
                )}
              </div>
            </div>
            {error && (
              <p role="alert" className="mt-4 rounded-xl border border-rose-300/30 bg-rose-500/10 p-3 text-xs text-rose-200">
                {error}
              </p>
            )}
          </div>
        </div>
      )}

      {/* TOP FLOATING BAR: Back Button + Maneuver Banner + Utilities */}
      <div className="absolute left-4 right-4 top-4 z-20 flex items-center justify-between gap-3 pointer-events-none">
        {/* Back Button */}
        <Link
          href="/map"
          onClick={exitCamera}
          className="pointer-events-auto grid h-10 w-10 sm:h-11 sm:w-11 place-items-center rounded-2xl border border-white/20 bg-slate-900/80 text-white shadow-xl backdrop-blur-md transition hover:bg-slate-800 active:scale-95"
          aria-label="Return to map"
          title="Return to map"
        >
          <ChevronLeft className="h-6 w-6 stroke-[2.5]" />
        </Link>

        {/* Dynamic Center Navigation HUD Banner */}
        <div className="pointer-events-auto flex items-center gap-2 rounded-2xl border border-white/20 bg-slate-900/85 px-3.5 py-2 shadow-xl backdrop-blur-md text-xs font-semibold text-white max-w-[calc(100vw-12rem)] sm:max-w-md truncate">
          <div className="rounded-full bg-blue-600/30 border border-blue-400 px-2.5 py-0.5 text-[11px] font-bold text-blue-300">
            AR route · {destination.name}
          </div>
          <div className="min-w-0 truncate">
            <span className="text-blue-200 font-bold mr-1">Next instruction:</span>
            <span className="truncate">{guidance.detailedInstruction || guidance.actionText}</span>
          </div>
          <div className="flex items-center gap-1.5 border-l border-white/15 pl-2 shrink-0">
            <span className="text-[11px] text-slate-400">
              {heading === null
                ? "heading unavailable"
                : `${headingSource === "gps" ? "GPS course" : "heading"} ${heading}°`}
            </span>
            {headingSource === "gps" && (
              <span className="rounded-full bg-emerald-500/20 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-300">
                Using GPS direction
              </span>
            )}
          </div>
        </div>

        {/* Hidden Accessible Elements for Screen Readers & Tests */}
        <div className="sr-only">
          <button onClick={requestLocation}>Enable live location</button>
          <p>
            {location
              ? `GPS connected · ±${Math.round(location.accuracy)} m`
              : "No live position shared yet."}
          </p>
        </div>

        {/* Top-Right Quick Camera Utilities */}
        <div className="pointer-events-auto flex items-center gap-1.5 sm:gap-2">
          {/* Switch Camera: Rear / Front */}
          <button
            type="button"
            onClick={toggleCameraFacing}
            title={`Switch camera (${facingMode === "environment" ? "rear" : "front"})`}
            className="grid h-10 w-10 place-items-center rounded-2xl border border-white/20 bg-slate-900/80 text-white shadow-xl backdrop-blur-md hover:bg-slate-800 active:scale-95 transition"
            aria-label="Switch camera"
          >
            <SwitchCamera className="h-4 w-4 text-blue-400" />
          </button>

          {/* Flip / Mirror View */}
          <button
            type="button"
            onClick={toggleMirror}
            title="Flip camera horizontally"
            aria-label="Flip camera horizontally"
            className={`grid h-10 w-10 place-items-center rounded-2xl border transition active:scale-95 shadow-xl backdrop-blur-md ${
              isMirrored
                ? "bg-blue-600 border-blue-400 text-white"
                : "border-white/20 bg-slate-900/80 text-white hover:bg-slate-800"
            }`}
          >
            <FlipHorizontal className="h-4 w-4" />
          </button>

          {/* Floor Plan Button */}
          <button
            type="button"
            onClick={() => setShowFloorPlan(true)}
            title="Floor Plan"
            className="grid h-10 w-10 place-items-center rounded-2xl border border-white/20 bg-slate-900/80 text-white shadow-xl backdrop-blur-md hover:bg-slate-800 active:scale-95 transition"
            aria-label="Floor plan"
          >
            <Layers className="h-4 w-4 text-cyan-400" />
          </button>

          {/* Voice Guidance Toggle */}
          <button
            type="button"
            onClick={() => setVoiceEnabled((v) => !v)}
            title={voiceEnabled ? "Mute Voice" : "Enable Voice"}
            className={`grid h-10 w-10 place-items-center rounded-2xl border transition active:scale-95 shadow-xl backdrop-blur-md ${
              voiceEnabled
                ? "bg-blue-600/30 border-blue-400 text-blue-300"
                : "border-white/20 bg-slate-900/80 text-slate-400 hover:bg-slate-800"
            }`}
            aria-label="Voice toggle"
          >
            {voiceEnabled ? <Volume2 className="h-4 w-4" /> : <VolumeX className="h-4 w-4" />}
          </button>
        </div>
      </div>

      {/* RIGHT FLOATING SECTION: Destination Photo Landmark Card (RVCE style) */}
      <div className="absolute right-4 top-16 sm:top-20 z-20 w-52 sm:w-64 pointer-events-none animate-in fade-in slide-in-from-right-4 duration-300">
        <div className="pointer-events-auto rounded-2xl border border-white/20 bg-slate-900/85 p-2 sm:p-2.5 shadow-2xl backdrop-blur-md overflow-hidden">
          <div className="relative h-24 sm:h-32 w-full overflow-hidden rounded-xl bg-slate-950 shadow-inner">
            <img
              src={destination.imageUrl || "/central-library.jpg"}
              alt={destination.name}
              className="h-full w-full object-cover transition duration-300 hover:scale-105"
              onError={(e) => {
                (e.target as HTMLImageElement).src = "/central-library.jpg";
              }}
            />
            <div className="absolute bottom-1.5 left-1.5 rounded-lg bg-slate-950/75 px-2 py-0.5 text-[10px] font-bold text-white backdrop-blur flex items-center gap-1">
              <span className="h-2 w-2 rounded-full bg-blue-500 animate-pulse" />
              <span>{destination.category || "Destination"}</span>
            </div>
          </div>
          <div className="pt-2 pb-0.5 px-1">
            <h3 className="font-bold text-white text-xs sm:text-sm leading-snug truncate">
              {destination.name}
            </h3>
            <p className="text-[10px] sm:text-xs text-slate-400 mt-0.5 truncate">
              {destination.buildingName || "Campus Grounds"} • {destination.floorName || "Ground Floor"}
            </p>
          </div>
        </div>
      </div>

      {/* CENTER HOLOGRAPHIC ROTATING AR DIRECTION ARROW */}
      {cameraStatus === "active" && (
        <div className="absolute left-1/2 top-[32%] sm:top-[30%] -translate-x-1/2 text-center z-20 pointer-events-none">
          <div
            className={`ar-arrow transition-transform duration-200 ${
              hasPulse ? "scale-110 shadow-blue-400/50" : ""
            } ${heading === null ? "opacity-60" : ""}`}
            style={{ transform: `rotate(${arrowRotation}deg)` }}
            aria-label={
              heading === null
                ? "Direction unavailable until compass is ready"
                : `Direction ${Math.round(targetBearing)} degrees`
            }
          >
            ➜
          </div>
          <p className="mt-3.5 rounded-full bg-slate-900/90 px-4 py-2 text-center text-xs sm:text-sm font-bold backdrop-blur-md border border-white/20 text-blue-300 shadow-xl">
            {guidance.actionText}
          </p>
          {heading === null && (
            <span className="mt-2 inline-block rounded-full bg-amber-500/20 px-3 py-1 text-[11px] font-semibold text-amber-200 border border-amber-400/30 backdrop-blur-md">
              Move phone in a figure-8 to calibrate compass
            </span>
          )}
        </div>
      )}

      {/* BOTTOM FLOATING ACTION BAR: Telemetry Strip + 2 RVCE-Style Action Buttons */}
      <div className="absolute bottom-6 left-4 right-4 z-20 mx-auto max-w-xl flex flex-col gap-2.5 pointer-events-none">
        {/* Telemetry Strip */}
        <div className="pointer-events-auto flex items-center justify-between rounded-2xl border border-white/20 bg-slate-900/85 px-4 py-2.5 shadow-xl backdrop-blur-md text-xs font-semibold text-slate-300">
          <div className="flex items-center gap-1.5 text-white font-bold">
            <Navigation className="h-4 w-4 text-blue-400" />
            <span>
              {distanceToNext === null ? progress.remainingDistance : distanceToNext} m
            </span>
          </div>
          <div className="flex items-center gap-1 text-slate-400 text-xs">
            <span>Remaining:</span>
            <strong className="text-white font-bold">{progress.remainingDistance} m</strong>
          </div>
          <div className="flex items-center gap-1 text-slate-400 text-xs">
            <span>Walking:</span>
            <strong className="text-blue-400 font-bold">
              {Math.max(1, Math.ceil(progress.remainingDistance / 1.4 / 60))} min
            </strong>
          </div>
        </div>

        {/* Off-Route Alert if deviated */}
        {deviationMessage && (
          <div className="pointer-events-auto rounded-xl border border-amber-300/30 bg-amber-500/20 px-3 py-2 text-xs text-amber-200 flex items-center gap-2 backdrop-blur-md">
            <AlertTriangle className="h-4 w-4 text-amber-300 shrink-0" />
            <span className="truncate">{deviationMessage}</span>
          </div>
        )}

        {/* Primary 2-Button Row (Exact RVCE layout in AR Mode): [ Map View ] and [ Recalculate ] */}
        <div className="grid grid-cols-2 gap-3.5 pointer-events-auto">
          <Button
            onClick={() => {
              exitCamera();
              window.location.href = "/map";
            }}
            className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
          >
            <MapIcon className="h-5 w-5" />
            Map View
          </Button>
          <Button
            onClick={handleManualReroute}
            aria-label="Recalculate route"
            className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
          >
            <RotateCcw className="h-5 w-5" />
            Recalculate
          </Button>
        </div>
      </div>

      {/* Destination Arrival Modal */}
      {isArrived && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-slate-950/85 p-6 backdrop-blur-md">
          <div className="w-full max-w-sm rounded-3xl border border-blue-500/40 bg-slate-900 p-6 text-center shadow-2xl">
            <div className="mx-auto grid h-16 w-16 place-items-center rounded-2xl bg-blue-600 text-white mb-4 animate-bounce">
              <CheckCircle2 className="h-10 w-10" />
            </div>
            <h2 className="text-2xl font-bold font-display text-white">You have arrived!</h2>
            <p className="mt-1 text-sm text-blue-300 font-semibold">{destination.name}</p>
            <p className="mt-2 text-xs text-slate-400">
              You reached your destination using PathFinder AR navigation.
            </p>
            <Button
              onClick={() => {
                exitCamera();
                window.location.href = "/map";
              }}
              className="mt-6 w-full bg-blue-600 font-bold text-white hover:bg-blue-700 py-6 text-base rounded-xl"
            >
              Back to Map
            </Button>
          </div>
        </div>
      )}

      {/* Interactive Official Architectural Floor Plan Dialog */}
      <FloorPlanModal
        isOpen={showFloorPlan}
        onClose={() => setShowFloorPlan(false)}
        defaultPlan={destination.buildingId === "central" || destination.nodeId === "library" ? "library" : "cse"}
        highlightRoom={destination.name}
        onSelectDestination={(nodeId, destId) => {
          setSourceNodeId(nodeId);
          setDestinationId(destId);
        }}
      />
    </div>
  );
}
