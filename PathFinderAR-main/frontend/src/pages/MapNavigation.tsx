import { CampusMap, MapPosition } from "@/components/CampusMap";
import { TopNav } from "@/components/TopNav";
import { Search as SearchComponent } from "@/components/Search";
import { useAuth } from "@/_core/hooks/useAuth";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { trpc } from "@/lib/trpc";
import { clearAssistantRouteLaunch, readAssistantRouteLaunch, resolveMapRouteLaunchState } from "@/lib/assistantRouteLaunch";
import { speakText } from "@/lib/browserSpeech";
import { useLanguage } from "@/contexts/LanguageContext";
import { createLocalizedVoiceGuidance } from "@shared/voice-guidance";
import { calculateRoute, createRouteInstructions, evaluateRouteDeviation, haversineDistanceMeters } from "@shared/routing";
import { deriveRouteManeuvers, isWaypointReached } from "@shared/maneuvers";
import { StepByStepGuidancePanel } from "@/components/StepByStepGuidancePanel";
import { deriveRouteProgress } from "@shared/route-progress";
import { clearLiveNavigationSnapshot, writeLiveNavigationSnapshot } from "@shared/live-navigation";
import { assessLocationSample, locationQualityForAge, type LocationQuality } from "@shared/location-quality";
import { campusEdges, campusNodes, DEVIATION_THRESHOLD_METERS, destinations, getNode, REROUTE_COOLDOWN_MS } from "@shared/pathfinder";
import { normalizeRouteSelection, readRouteSelection, routeSelectionSearch, routableSourceNodes, routeDestination, writeRouteSelection } from "@shared/route-selection";
import {
  AlertTriangle,
  Camera,
  CheckCircle2,
  ChevronLeft,
  Compass,
  Eye,
  Heart,
  LocateFixed,
  Map as MapIcon,
  Navigation,
  Pause,
  Play,
  RefreshCw,
  Route,
  Search,
  ShieldAlert,
  Sparkles,
  Volume2,
  VolumeX,
  X,
  Zap,
  Radio,
  Wifi,
  Layers,
  MapPin,
} from "lucide-react";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation } from "wouter";
import { FloorPlanModal } from "@/components/FloorPlanModal";

type LocationState = Omit<MapPosition, "accuracy"> & { accuracy: number; speed?: number; timestamp?: number };

export default function MapNavigation() {
  const [, setLocation] = useLocation();
  const [assistantLaunch] = useState(() => readAssistantRouteLaunch());
  const [mapLaunch] = useState(() => {
    const persisted = readRouteSelection(sessionStorage, window.location.search);
    return resolveMapRouteLaunchState(assistantLaunch, persisted);
  });
  const [routeSelection, setRouteSelection] = useState(() =>
    normalizeRouteSelection(assistantLaunch ?? readRouteSelection(sessionStorage, window.location.search))
  );
  const destinationId = routeSelection.destinationId;
  const sourceNodeId = routeSelection.sourceNodeId;
  const [position, setPosition] = useState<LocationState | undefined>();
  const [locationMessage, setLocationMessage] = useState("Location not yet shared. Route preview is not a live position.");
  const [locationQuality, setLocationQuality] = useState<LocationQuality>("unavailable");
  const [tracking, setTracking] = useState(false);
  const [accessibleOnly, setAccessibleOnly] = useState(() => mapLaunch.accessibleOnly);
  const [active, setActive] = useState(false);
  const [currentStep, setCurrentStep] = useState(0);
  const [offRoute, setOffRoute] = useState(false);
  const [voiceOn, setVoiceOn] = useState(() => mapLaunch.voiceOn);
  const [persistedRouteId, setPersistedRouteId] = useState<number | null>(null);
  const [deviationCount, setDeviationCount] = useState(0);
  const [rerouteStartNodeId, setRerouteStartNodeId] = useState<string | null>(null);
  const [locateTrigger, setLocateTrigger] = useState(0);
  const [gpsAutoDetected, setGpsAutoDetected] = useState(false);
  const [gpsPermissionStatus, setGpsPermissionStatus] = useState<"prompting" | "granted" | "denied" | "unsupported" | "idle">("idle");
  const [searchModalOpen, setSearchModalOpen] = useState(false);
  const [showFloorPlan, setShowFloorPlan] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const searchDropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (searchDropdownRef.current && !searchDropdownRef.current.contains(e.target as Node)) {
        setIsSearchOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Default to using live Current Location unless user explicitly selects a manual fixed starting node
  const [useCurrentLocation, setUseCurrentLocation] = useState<boolean>(() => {
    const params = new URLSearchParams(window.location.search);
    const fromParam = params.get("from") ?? params.get("source");
    return !fromParam || fromParam === "current-location" || fromParam === "gate";
  });

  const lastRerouteAt = useRef<number | undefined>(undefined);
  const lastProgressPersistedAt = useRef(0);
  const announcedVoiceWaypoints = useRef<Set<number>>(new Set());
  const announcedVoiceRouteKey = useRef<string | undefined>(undefined);
  const { user } = useAuth();
  const { language } = useLanguage();
  const assistantLaunchHandled = useRef(false);

  const destination = routeDestination(destinationId) ?? destinations[0];

  const filteredDestinations = useMemo(() => {
    const activeItems = destinations.filter((item) => item.isActive);
    if (!searchQuery.trim()) return activeItems.slice(0, 10);
    const q = searchQuery.toLowerCase();
    return activeItems.filter(
      (item) =>
        item.name.toLowerCase().includes(q) ||
        (item.buildingName && item.buildingName.toLowerCase().includes(q)) ||
        (item.category && item.category.toLowerCase().includes(q)) ||
        (item.description && item.description.toLowerCase().includes(q))
    );
  }, [searchQuery]);

  // Nearest campus node to the user's live GPS position
  const liveNearestNode = useMemo(() => {
    if (!position) return null;
    return campusNodes
      .filter((n) => !accessibleOnly || n.isAccessible)
      .reduce((closest, node) => {
        return haversineDistanceMeters(position, node) < haversineDistanceMeters(position, closest) ? node : closest;
      }, campusNodes[0]);
  }, [position, accessibleOnly]);

  const startNodeId = useMemo(() => {
    if (rerouteStartNodeId) return rerouteStartNodeId;
    if (useCurrentLocation && liveNearestNode) return liveNearestNode.id;
    if (sourceNodeId === "current-location") return liveNearestNode?.id ?? "gate";
    return sourceNodeId;
  }, [rerouteStartNodeId, useCurrentLocation, liveNearestNode, sourceNodeId]);

  const sourceNode = useMemo(() => {
    if (useCurrentLocation) {
      if (position) {
        const nearest = liveNearestNode ?? getNode(startNodeId) ?? campusNodes[0];
        return {
          ...nearest,
          name: `Current Location (${nearest.name})`,
          latitude: position.latitude,
          longitude: position.longitude,
        };
      }
      const refNode = getNode(startNodeId) ?? campusNodes[0];
      return {
        ...refNode,
        name: "Current Location",
      };
    }
    return getNode(startNodeId);
  }, [useCurrentLocation, position, liveNearestNode, startNodeId]);

  const route = useMemo(() => {
    if (startNodeId === destination.nodeId) {
      return {
        algorithm: "dijkstra" as const,
        nodeIds: [startNodeId],
        totalDistance: 0,
        estimatedTime: 0,
        visitedNodeCount: 1,
        executionTimeMs: 0,
      };
    }
    try {
      return calculateRoute(campusNodes, campusEdges, startNodeId, destination.nodeId, "auto", accessibleOnly);
    } catch {
      return null;
    }
  }, [destination.nodeId, accessibleOnly, startNodeId]);

  const routeNodes = useMemo(
    () => route?.nodeIds.map(getNode).filter((node): node is NonNullable<typeof node> => Boolean(node)) ?? [],
    [route]
  );

  const instructions = useMemo(() => (route ? createRouteInstructions(campusNodes, route.nodeIds) : []), [route]);
  const maneuvers = useMemo(() => (route ? deriveRouteManeuvers(campusNodes, route.nodeIds) : []), [route]);

  const routeError = !route
    ? "No walking route is currently available between these locations."
    : startNodeId === destination.nodeId
    ? "Starting point and destination are the same location."
    : undefined;

  const liveProgress = useMemo(
    () =>
      position
        ? deriveRouteProgress(position, routeNodes, currentStep)
        : {
            currentWaypointIndex: currentStep,
            totalWaypoints: routeNodes.length,
            remainingDistance: Math.max(
              0,
              Math.round((route?.totalDistance ?? 0) * (1 - currentStep / Math.max(1, routeNodes.length - 1)))
            ),
            progressPercent: Math.round((currentStep / Math.max(1, routeNodes.length - 1)) * 100),
            isComplete: false,
            distanceToCurrentWaypoint: 0,
            arrivalThreshold: 18,
          },
    [position, routeNodes, currentStep, route?.totalDistance]
  );

  const activeWaypointIndex = Math.max(currentStep, liveProgress.currentWaypointIndex);
  const activeManeuverIndex = Math.min(activeWaypointIndex, Math.max(0, maneuvers.length - 1));
  const currentInstruction =
    maneuvers[activeManeuverIndex]?.instruction ??
    instructions[Math.min(activeWaypointIndex, instructions.length - 1)] ??
    "Route ready.";

  const voiceLanguage = assistantLaunch?.language ?? language;
  const localizedVoiceInstructions = useMemo(
    () => createLocalizedVoiceGuidance(routeNodes.map((node) => node.name), voiceLanguage),
    [routeNodes, voiceLanguage]
  );
  const voiceRouteKey = route?.nodeIds.join("|") ?? "unavailable";
  const activeVoiceWaypointIndex = liveProgress.isComplete
    ? Math.max(0, localizedVoiceInstructions.length - 1)
    : Math.min(activeWaypointIndex, Math.max(0, localizedVoiceInstructions.length - 1));
  const currentVoiceInstruction = localizedVoiceInstructions[activeVoiceWaypointIndex] ?? currentInstruction;

  const startSession = trpc.routing.startSession.useMutation({
    onSuccess: (result) => setPersistedRouteId(result.routeId),
  });
  const completeSession = trpc.routing.completeSession.useMutation();
  const updateProgress = trpc.routing.updateProgress.useMutation();
  const recordDeviation = trpc.routing.recordDeviation.useMutation();
  const utils = trpc.useUtils();
  const favoriteRoutes = trpc.favorites.list.useQuery(undefined, { enabled: Boolean(user) });
  const saveFavorite = trpc.favorites.save.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const removeFavorite = trpc.favorites.remove.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const currentFavorite = favoriteRoutes.data?.find(
    (item) =>
      item.sourceNodeExternalId === sourceNodeId &&
      item.destinationExternalId === destinationId &&
      item.accessibleOnly === accessibleOnly
  );

  const applyGpsPosition = (next: LocationState) => {
    setPosition((previous) => {
      const assessment = assessLocationSample(previous, next);
      if (!assessment.accepted || !assessment.sample) {
        setLocationQuality(assessment.quality);
        setLocationMessage(
          assessment.reason === "implausible-jump"
            ? "GPS jump ignored. Waiting for a stable position."
            : assessment.reason === "low-accuracy"
            ? "GPS accuracy is too low for reliable route progress. Move outdoors."
            : "Waiting for a newer GPS position."
        );
        return previous;
      }
      setLocationQuality(assessment.quality);
      setLocationMessage(
        assessment.quality === "low-accuracy"
          ? `Live GPS connected, but accuracy is low (±${Math.round(assessment.sample.accuracy)} m). Move outdoors.`
          : `Live GPS connected • ±${Math.round(assessment.sample.accuracy)} m`
      );
      return assessment.sample;
    });
  };

  const requestLocation = () => {
    if (!navigator.geolocation) {
      setLocationQuality("unavailable");
      setLocationMessage("Geolocation is not supported in this browser. Map guidance remains available.");
      return;
    }
    setLocationQuality("unavailable");
    setLocationMessage("Requesting live GPS permission…");
    navigator.geolocation.getCurrentPosition(
      (result) => {
        applyGpsPosition({
          latitude: result.coords.latitude,
          longitude: result.coords.longitude,
          accuracy: result.coords.accuracy,
          heading: result.coords.heading ?? undefined,
          speed: result.coords.speed ?? undefined,
          timestamp: result.timestamp,
        });
      },
      (error) => {
        const messages: Record<number, string> = {
          1: "Location permission was denied. Map guidance remains available; no simulated position is shown.",
          2: "Location is currently unavailable. Try moving outdoors or use map guidance.",
          3: "Location request timed out. Please try again.",
        };
        setLocationQuality(error.code === 1 ? "unavailable" : "signal-lost");
        setLocationMessage(messages[error.code] ?? "Location could not be obtained.");
      },
      { enableHighAccuracy: true, timeout: 10000, maximumAge: 5000 }
    );
  };

  // Continuous live GPS tracking on the map (always active)
  useEffect(() => {
    if (!navigator.geolocation) {
      setGpsPermissionStatus("unsupported");
      setLocationQuality("unavailable");
      setLocationMessage("Geolocation is not supported in this browser. Map guidance remains available.");
      return;
    }

    setGpsPermissionStatus("prompting");
    const watcher = navigator.geolocation.watchPosition(
      (pos) => {
        setGpsPermissionStatus("granted");
        const fix: LocationState = {
          latitude: pos.coords.latitude,
          longitude: pos.coords.longitude,
          accuracy: pos.coords.accuracy,
          heading: pos.coords.heading ?? undefined,
          speed: pos.coords.speed ?? undefined,
          timestamp: pos.timestamp,
        };
        applyGpsPosition(fix);

        // Snap to nearest campus node if using Current Location
        const nearestNode = campusNodes
          .filter((n) => !accessibleOnly || n.isAccessible)
          .reduce((closest, node) => {
            return haversineDistanceMeters(fix, node) < haversineDistanceMeters(fix, closest) ? node : closest;
          }, campusNodes[0]);

        setGpsAutoDetected(true);
        setLocationMessage(
          `Live location active • Near ${nearestNode.name} (±${Math.round(pos.coords.accuracy)} m)`
        );
      },
      (err) => {
        if (err.code === 1) {
          setGpsPermissionStatus("denied");
          setLocationQuality("unavailable");
          setLocationMessage("Location permission denied. Map preview shown from Main Gate.");
        } else {
          setLocationQuality("signal-lost");
          setLocationMessage("Waiting for GPS signal recovery…");
        }
      },
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 3000 }
    );

    return () => {
      navigator.geolocation.clearWatch(watcher);
    };
  }, [accessibleOnly]);

  // Voice cues
  useEffect(() => {
    if (active && announcedVoiceRouteKey.current !== voiceRouteKey) {
      announcedVoiceWaypoints.current.clear();
      announcedVoiceRouteKey.current = voiceRouteKey;
      if (currentStep !== 0) {
        setCurrentStep(0);
        return;
      }
    }
    if (!voiceOn || !active || !currentVoiceInstruction || announcedVoiceWaypoints.current.has(activeVoiceWaypointIndex))
      return;
    announcedVoiceWaypoints.current.add(activeVoiceWaypointIndex);
    speakText(currentVoiceInstruction, voiceLanguage);
  }, [voiceOn, active, voiceRouteKey, currentStep, activeVoiceWaypointIndex, currentVoiceInstruction, voiceLanguage]);

  // Off-route detection and auto-recalculation
  useEffect(() => {
    if (!active || !position) return;
    const deviation = evaluateRouteDeviation(
      position,
      routeNodes,
      DEVIATION_THRESHOLD_METERS,
      lastRerouteAt.current,
      REROUTE_COOLDOWN_MS
    );
    if (deviation.shouldReroute) {
      const nearestSafeNode = campusNodes
        .filter((node) => node.isAccessible)
        .reduce((closest, node) =>
          haversineDistanceMeters(position, node) < haversineDistanceMeters(position, closest) ? node : closest,
          campusNodes[0]
        );
      setOffRoute(true);
      setRerouteStartNodeId(nearestSafeNode.id);
      setLocationMessage(`You're off the planned route. Rerouting from ${nearestSafeNode.name}.`);
      if (voiceOn) {
        speakText(`You're off route. Rerouting from ${nearestSafeNode.name}.`, voiceLanguage);
      }
      lastRerouteAt.current = Date.now();
      setDeviationCount((count) => count + 1);
      if (persistedRouteId) {
        recordDeviation.mutate({
          routeId: persistedRouteId,
          latitude: position.latitude,
          longitude: position.longitude,
          distanceFromRoute: deviation.distanceFromRoute,
          rerouted: true,
        });
      }
    } else if (!deviation.isOffRoute) {
      setOffRoute(false);
    }
  }, [active, position, routeNodes, persistedRouteId, voiceOn, voiceLanguage]);

  // Live GPS proximity auto-advancement
  useEffect(() => {
    if (!active || !position || routeNodes.length === 0) return;
    const targetWaypoint = routeNodes[Math.min(currentStep + 1, routeNodes.length - 1)];
    if (targetWaypoint && currentStep < routeNodes.length - 1) {
      if (isWaypointReached(position, targetWaypoint, 8)) {
        setCurrentStep((step) => Math.min(routeNodes.length - 1, step + 1));
      }
    }
  }, [active, position, currentStep, routeNodes]);

  // Sync snapshot
  useEffect(() => {
    if (!active || !position) {
      if (!active) clearLiveNavigationSnapshot();
      return;
    }
    writeLiveNavigationSnapshot({
      active: true,
      sourceNodeId: startNodeId,
      destinationId,
      currentWaypointIndex: activeWaypointIndex,
      totalWaypoints: liveProgress.totalWaypoints,
      progressPercent: liveProgress.progressPercent,
      remainingDistance: liveProgress.remainingDistance,
      currentInstruction,
      currentVoiceInstruction,
      isComplete: liveProgress.isComplete,
      positionSource: "gps",
      position: { ...position, accuracy: position.accuracy ?? 999 },
      updatedAt: Date.now(),
    });
  }, [
    active,
    position,
    startNodeId,
    destinationId,
    activeWaypointIndex,
    liveProgress.totalWaypoints,
    liveProgress.progressPercent,
    liveProgress.remainingDistance,
    currentInstruction,
    currentVoiceInstruction,
    liveProgress.isComplete,
  ]);

  // Persist session progress
  useEffect(() => {
    if (!active || !persistedRouteId || !user || !position || updateProgress.isPending) return;
    const now = Date.now();
    if (!liveProgress.isComplete && now - lastProgressPersistedAt.current < 5000) return;
    lastProgressPersistedAt.current = now;
    updateProgress.mutate({
      routeId: persistedRouteId,
      currentWaypointIndex: activeWaypointIndex,
      totalWaypoints: liveProgress.totalWaypoints,
      remainingDistance: liveProgress.remainingDistance,
      navigationState: liveProgress.isComplete ? "completed" : "active",
    });
  }, [
    active,
    persistedRouteId,
    user,
    position,
    activeWaypointIndex,
    liveProgress.totalWaypoints,
    liveProgress.remainingDistance,
    liveProgress.isComplete,
    updateProgress,
  ]);

  const startNavigation = () => {
    if (!route || routeError) return;
    announcedVoiceWaypoints.current.clear();
    announcedVoiceRouteKey.current = voiceRouteKey;
    setActive(true);
    setTracking(true);
    setCurrentStep(0);
    setDeviationCount(0);
    setRerouteStartNodeId(null);
    lastProgressPersistedAt.current = 0;
    setLocationQuality(position ? locationQuality : "unavailable");
    if (!position) {
      setLocationMessage(
        "Navigation started as a route preview. Tap Locate and grant GPS permission for live tracking."
      );
    }
    if (user) {
      startSession.mutate({
        algorithm: route.algorithm,
        totalDistance: route.totalDistance,
        estimatedTime: route.estimatedTime,
        startLatitude: position?.latitude,
        startLongitude: position?.longitude,
        waypoints: routeNodes.map((node, index) => ({
          latitude: node.latitude,
          longitude: node.longitude,
          instruction: instructions[index],
        })),
      });
    }
  };

  useEffect(() => {
    if (!mapLaunch.autoStart || assistantLaunchHandled.current) return;
    assistantLaunchHandled.current = true;
    clearAssistantRouteLaunch();
    startNavigation();
  }, [mapLaunch]);

  const finishNavigation = () => {
    setActive(false);
    setTracking(false);
    clearLiveNavigationSnapshot();
    if (persistedRouteId && user) {
      completeSession.mutate({
        routeId: persistedRouteId,
        completionStatus: liveProgress.isComplete || activeWaypointIndex >= instructions.length - 1 ? "completed" : "stopped",
        deviationCount,
        actualDistance: (route?.totalDistance ?? 0) - liveProgress.remainingDistance,
      });
    }
    setPersistedRouteId(null);
  };

  const toggleFavorite = () => {
    if (!route || routeError) return;
    if (!user) {
      setLocation("/account");
      return;
    }
    if (currentFavorite) {
      removeFavorite.mutate({ favoriteId: currentFavorite.id });
      return;
    }
    saveFavorite.mutate({
      label: `${sourceNode?.name ?? "Start"} → ${destination.name}`,
      sourceNodeExternalId: sourceNodeId,
      destinationExternalId: destinationId,
      accessibleOnly,
    });
  };

  const applySelection = (nextSourceNodeId: string, nextDestinationId: string) => {
    const next = writeRouteSelection({ sourceNodeId: nextSourceNodeId, destinationId: nextDestinationId }, sessionStorage);
    setRouteSelection(next);
    window.history.replaceState({}, "", `/map${routeSelectionSearch(next)}`);
    setRerouteStartNodeId(null);
    setOffRoute(false);
    setCurrentStep(0);
    clearLiveNavigationSnapshot();
  };

  const launchFavorite = (favorite: NonNullable<typeof favoriteRoutes.data>[number]) => {
    setAccessibleOnly(favorite.accessibleOnly);
    applySelection(favorite.sourceNodeExternalId, favorite.destinationExternalId);
  };

  const handleRecalculate = () => {
    if (!route) return;
    let fromNodeId = sourceNodeId;
    if (position) {
      const nearestSafe = campusNodes
        .filter((n) => !accessibleOnly || n.isAccessible)
        .reduce((closest, n) => {
          return haversineDistanceMeters(position, n) < haversineDistanceMeters(position, closest) ? n : closest;
        }, campusNodes[0]);
      fromNodeId = nearestSafe.id;
    }
    setRerouteStartNodeId(fromNodeId);
    setOffRoute(false);
    setCurrentStep(0);
    announcedVoiceWaypoints.current.clear();
    announcedVoiceRouteKey.current = undefined;
    const nodeName = getNode(fromNodeId)?.name ?? "current location";
    setLocationMessage(`Route recalculated from ${nodeName}.`);
    if (voiceOn) {
      speakText(`Route recalculated from ${nodeName}.`, voiceLanguage);
    }
  };

  const handleToggleVoice = () => {
    const next = !voiceOn;
    setVoiceOn(next);
    if (!next) {
      if (typeof window !== "undefined" && "speechSynthesis" in window) {
        window.speechSynthesis.cancel();
      }
    }
  };

  const handleLocateClick = () => {
    setLocateTrigger((prev) => prev + 1);
    requestLocation();
  };

  const handleOpenAR = () => {
    const effectiveStart =
      useCurrentLocation && liveNearestNode
        ? liveNearestNode.id
        : sourceNodeId === "current-location"
        ? liveNearestNode?.id ?? "current-location"
        : startNodeId;
    sessionStorage.setItem("aiet-source", effectiveStart);
    sessionStorage.setItem("pathfinder-destination", destinationId);
    writeLiveNavigationSnapshot({
      active: true,
      sourceNodeId: effectiveStart,
      destinationId,
      currentWaypointIndex: activeWaypointIndex,
      totalWaypoints: liveProgress.totalWaypoints,
      progressPercent: liveProgress.progressPercent,
      remainingDistance: liveProgress.remainingDistance,
      currentInstruction,
      currentVoiceInstruction,
      isComplete: liveProgress.isComplete,
      positionSource: "gps",
      position: {
        latitude: position?.latitude ?? 0,
        longitude: position?.longitude ?? 0,
        accuracy: position?.accuracy ?? 12,
        heading: position?.heading,
        speed: position?.speed,
      },
      updatedAt: Date.now(),
    });
    setLocation("/ar");
  };

  const speedDisplay =
    position?.speed !== undefined && position.speed > 0.1
      ? `${(position.speed * 3.6).toFixed(1)} km/h`
      : active
      ? "3.6 km/h"
      : "0 km/h";

  const totalDistanceMeters = active ? liveProgress.remainingDistance : (route?.totalDistance ?? 0);
  const totalMinutes = Math.max(1, Math.ceil((route?.estimatedTime ?? 60) / 60));

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-slate-100">
      {/* Full-Bleed Campus Map */}
      <CampusMap
        nodes={campusNodes}
        route={routeNodes}
        source={sourceNode}
        destination={getNode(destination.nodeId)}
        userPosition={position}
        onLocate={handleLocateClick}
        locateTrigger={locateTrigger}
        onLocationFound={(pos) => applyGpsPosition({ ...pos, accuracy: pos.accuracy ?? 12 })}
        isNavigating={active}
        className="h-full w-full"
      />

      {/* TOP FLOATING BAR: Back Button & Status on Left, Search Bar & Controls on Right */}
      <div className="absolute left-4 right-4 top-4 z-[500] flex items-center justify-between gap-3 pointer-events-none">
        <div className="flex items-center gap-2 pointer-events-auto">
          {/* Back Button */}
          <Link
            href="/destinations"
            className="grid h-10 w-10 sm:h-11 sm:w-11 place-items-center rounded-2xl border border-slate-200/90 bg-white/95 text-slate-700 shadow-md backdrop-blur-md transition hover:bg-slate-50 hover:shadow-lg active:scale-95"
            aria-label="Back to destinations"
            title="Back to destinations"
          >
            <ChevronLeft className="h-6 w-6 stroke-[2.5]" />
          </Link>

          {/* Status Pill Badge */}
          <div
            className={`rounded-full px-3 py-1.5 text-xs font-bold shadow-md backdrop-blur-md flex items-center gap-1.5 ${
              active
                ? "bg-emerald-500 text-white animate-pulse"
                : "bg-white/95 text-blue-700 border border-slate-200/80"
            }`}
          >
            <span>{active ? "Navigation in progress" : "Route preview"}</span>
            {route && (
              <span className="font-semibold text-xs border-l border-current/25 pl-1.5">
                {totalDistanceMeters >= 1000
                  ? `${(totalDistanceMeters / 1000).toFixed(1)} km`
                  : `${totalDistanceMeters} m`}
              </span>
            )}
          </div>
        </div>

        {/* Hidden Accessible Form Controls for Tests & Screen Readers */}
        <div className="sr-only">
          <label htmlFor="map-source-select">Starting point</label>
          <select
            id="map-source-select"
            aria-label="Starting point"
            value={useCurrentLocation ? "current-location" : sourceNodeId}
            onChange={(e) => {
              if (e.target.value === "current-location") {
                setUseCurrentLocation(true);
                setRerouteStartNodeId(null);
                applySelection("current-location", destinationId);
              } else {
                setUseCurrentLocation(false);
                setRerouteStartNodeId(null);
                applySelection(e.target.value, destinationId);
              }
            }}
          >
            <option value="current-location">
              Current Location {liveNearestNode ? `(${liveNearestNode.name})` : "(Auto-detecting…)"}
            </option>
            {routableSourceNodes().map((node) => (
              <option key={node.id} value={node.id}>
                {node.name} • {node.buildingName} • {node.floorName}
              </option>
            ))}
          </select>

          <label htmlFor="map-destination-select">Destination</label>
          <select
            id="map-destination-select"
            aria-label="Destination"
            value={destinationId}
            onChange={(e) => applySelection(sourceNodeId, e.target.value)}
          >
            {destinations
              .filter((item) => item.isActive)
              .map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
          </select>
        </div>

        {/* Right Top Controls: RVCE Search Bar + Quick Actions */}
        <div className="flex items-center gap-2 pointer-events-auto">
          {/* RVCE Search Bar with Dropdown */}
          <div ref={searchDropdownRef} className="relative">
            <div className="flex items-center gap-2.5 rounded-2xl border border-slate-200/90 bg-white/95 px-3.5 py-2 sm:py-2.5 shadow-md backdrop-blur-md w-52 sm:w-72 md:w-80 transition focus-within:ring-2 focus-within:ring-blue-500/20">
              <Search className="h-4 w-4 text-slate-400 shrink-0" />
              <input
                type="text"
                placeholder="Search..."
                value={searchQuery}
                onChange={(e) => {
                  setSearchQuery(e.target.value);
                  setIsSearchOpen(true);
                }}
                onFocus={() => setIsSearchOpen(true)}
                className="w-full bg-transparent text-xs sm:text-sm font-semibold text-slate-900 placeholder:text-slate-400 outline-none"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => {
                    setSearchQuery("");
                    setIsSearchOpen(false);
                  }}
                  className="text-slate-400 hover:text-slate-600"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
            </div>

            {/* Instant Dropdown Results */}
            {isSearchOpen && (
              <div className="absolute right-0 top-12 sm:top-14 z-[600] w-72 sm:w-80 max-h-72 overflow-y-auto rounded-2xl border border-slate-200 bg-white/95 p-1.5 shadow-2xl backdrop-blur-md">
                <div className="px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider text-slate-400">
                  Campus Destinations ({filteredDestinations.length})
                </div>
                {filteredDestinations.length === 0 ? (
                  <div className="px-3 py-4 text-center text-xs text-slate-400 font-medium">
                    No destinations matching "{searchQuery}"
                  </div>
                ) : (
                  filteredDestinations.map((d) => (
                    <button
                      key={d.id}
                      type="button"
                      onClick={() => {
                        applySelection(sourceNodeId, d.id);
                        setSearchQuery("");
                        setIsSearchOpen(false);
                      }}
                      className={`flex w-full items-center gap-2.5 rounded-xl px-3 py-2 text-left transition text-xs ${
                        d.id === destinationId ? "bg-blue-50 text-blue-900 font-bold" : "hover:bg-slate-50 text-slate-700"
                      }`}
                    >
                      <div className="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-blue-100/70 text-blue-600 font-bold">
                        <MapPin className="h-3.5 w-3.5" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="font-bold text-slate-900 truncate text-xs sm:text-sm">{d.name}</p>
                        <p className="text-[11px] text-slate-400 truncate">
                          {d.buildingName || "Campus Grounds"} • {d.floorName || "Ground Level"}
                        </p>
                      </div>
                    </button>
                  ))
                )}
              </div>
            )}
          </div>

          {/* Quick Buttons: Accessible, Floor Plan, Locate */}
          <Button
            size="sm"
            variant="outline"
            onClick={() => setAccessibleOnly((val) => !val)}
            className={`hidden sm:inline-flex h-9 sm:h-10 rounded-2xl border-slate-200/90 bg-white/95 px-3 text-xs font-semibold shadow-md backdrop-blur-md ${
              accessibleOnly ? "border-cyan-400 bg-cyan-50 text-cyan-800 font-bold" : "text-slate-600"
            }`}
            title="Toggle Accessible Step-Free Routes"
          >
            Accessible
          </Button>
          <Button
            size="sm"
            variant="outline"
            onClick={() => setShowFloorPlan(true)}
            className="hidden sm:inline-flex h-9 sm:h-10 rounded-2xl border-cyan-300 bg-cyan-50/95 px-3 text-xs font-semibold text-cyan-800 shadow-md backdrop-blur-md hover:bg-cyan-100"
            title="View architectural floor plan"
          >
            <Layers className="mr-1.5 h-3.5 w-3.5 text-cyan-600" />
            Floor Plan
          </Button>
          <Button
            size="sm"
            variant="outline"
            onClick={handleLocateClick}
            className="h-9 sm:h-10 rounded-2xl border-slate-200/90 bg-white/95 px-3 text-slate-700 shadow-md backdrop-blur-md hover:bg-slate-50"
            title="Locate me on map"
          >
            <LocateFixed className="mr-1.5 h-3.5 w-3.5" />
            <span className="hidden sm:inline">Locate</span>
          </Button>
        </div>
      </div>

      {/* RIGHT FLOATING SECTION: Destination Photo Landmark Card (RVCE style) */}
      <div className="absolute right-4 top-16 sm:top-20 z-[490] w-56 sm:w-64 pointer-events-none animate-in fade-in slide-in-from-right-4 duration-300">
        <div className="pointer-events-auto rounded-2xl border border-slate-200/90 bg-white/95 p-2 sm:p-2.5 shadow-xl backdrop-blur-md overflow-hidden transition-all duration-300 hover:shadow-2xl">
          <div className="relative h-28 sm:h-32 w-full overflow-hidden rounded-xl bg-slate-900 shadow-inner">
            <img
              src={destination.imageUrl || "/central-library.jpg"}
              alt={destination.name}
              className="h-full w-full object-cover transition duration-300 hover:scale-105"
              onError={(e) => {
                (e.target as HTMLImageElement).src = "/central-library.jpg";
              }}
            />
            <div className="absolute bottom-2 left-2 rounded-lg bg-slate-950/70 px-2 py-0.5 text-[10px] font-bold text-white backdrop-blur flex items-center gap-1">
              <MapPin className="h-3 w-3 text-blue-400" />
              <span>{destination.category || "Campus Landmark"}</span>
            </div>
          </div>
          <div className="pt-2 pb-0.5 px-1">
            <h3 className="font-bold text-slate-900 text-xs sm:text-sm leading-snug truncate">
              {destination.buildingName || destination.name}
            </h3>
            <p className="text-[10px] sm:text-xs text-slate-500 mt-0.5 truncate">
              {destination.name} • {destination.floorName || "Ground Floor"}
            </p>
          </div>
        </div>
      </div>

      {/* LEFT FLOATING SECTION: Turn Guidance & Telemetry (Active Navigation Mode - Screenshot 2) */}
      {active && (
        <div className="absolute left-4 top-16 sm:top-20 z-[490] flex flex-col gap-2.5 w-[calc(100vw-2rem)] sm:max-w-xs md:max-w-sm pointer-events-none animate-in fade-in slide-in-from-left-4 duration-300">
          {/* Telemetry Card matching Screenshot 2 */}
          <div className="flex flex-col gap-2 pointer-events-auto rounded-2xl border border-slate-200/90 bg-white/95 p-3.5 shadow-xl backdrop-blur-md text-xs font-semibold text-slate-700">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5 text-slate-900 font-bold text-sm">
                <Zap className="h-4 w-4 text-blue-600 fill-blue-600" />
                <span>{speedDisplay}</span>
              </div>
              <div className="flex items-center gap-1 text-slate-500 text-xs">
                <Radio className="h-3.5 w-3.5 text-emerald-500" />
                <span>Avg: {speedDisplay}</span>
              </div>
              <button
                type="button"
                onClick={finishNavigation}
                className="grid h-6 w-6 place-items-center rounded-full bg-slate-100 text-slate-500 hover:bg-rose-50 hover:text-rose-600 transition"
                title="Stop Navigation"
                aria-label="Stop navigation"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>

            <div className="flex items-center justify-between border-t border-slate-100 pt-2 text-slate-500 text-xs">
              <span className="flex items-center gap-1 font-medium">
                <Wifi className="h-3.5 w-3.5 text-blue-500" />
                <span>GPS: {position ? `+/-${Math.round(position.accuracy)}m` : "+/-12m"}</span>
              </span>
              {route && (
                <span className="font-bold text-blue-700 text-xs sm:text-sm">
                  {totalDistanceMeters >= 1000
                    ? `${(totalDistanceMeters / 1000).toFixed(1)} km`
                    : `${totalDistanceMeters} m`}{" "}
                  • {totalMinutes} min
                </span>
              )}
            </div>

            {/* Animated Progress Bar */}
            <div
              role="progressbar"
              aria-label={`Route progress: ${liveProgress.progressPercent}%`}
              aria-valuenow={liveProgress.progressPercent}
              aria-valuemin={0}
              aria-valuemax={100}
              className="h-1.5 w-full rounded-full bg-slate-100 overflow-hidden shadow-inner mt-0.5"
            >
              <div
                className="h-full rounded-full bg-blue-600 transition-[width] duration-300"
                style={{ width: `${liveProgress.progressPercent}%` }}
              />
            </div>
          </div>

          {/* Maneuvers Scrollable List */}
          <div className="pointer-events-auto">
            <StepByStepGuidancePanel
              maneuvers={maneuvers}
              activeStepIndex={activeManeuverIndex}
              position={position}
              isNavigating={active}
            />
          </div>
        </div>
      )}

      {/* BOTTOM FLOATING ACTION BAR: Exact RVCE 2-button layout in both states */}
      <div className="absolute bottom-6 left-4 right-4 z-[500] mx-auto max-w-xl flex flex-col gap-2.5 pointer-events-none">
        {/* Subtle Utilities Ribbon */}
        <div className="flex items-center justify-between px-2 pointer-events-auto">
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={handleToggleVoice}
              className={`h-8 rounded-full border-slate-200/90 bg-white/95 px-3 shadow-md backdrop-blur-md text-xs font-semibold ${
                voiceOn ? "text-blue-700 font-bold" : "text-slate-500"
              }`}
            >
              {voiceOn ? <Volume2 className="mr-1.5 h-3.5 w-3.5 text-blue-600" /> : <VolumeX className="mr-1.5 h-3.5 w-3.5 text-slate-400" />}
              Voice {voiceOn ? "on" : "off"}
            </Button>
            <Link
              href={`/tour?scene=${destination.id || destination.nodeId || "gate"}`}
              className="inline-flex h-8 items-center rounded-full border border-slate-200/90 bg-white/95 px-3 text-xs font-semibold text-slate-700 shadow-md backdrop-blur-md hover:bg-slate-50 transition"
            >
              <Eye className="mr-1.5 h-3.5 w-3.5 text-amber-500" />
              360° View
            </Link>
          </div>

          {active && (
            <Button
              size="sm"
              onClick={() => setCurrentStep((step) => Math.min(routeNodes.length - 1, step + 1))}
              aria-label="Mark next waypoint reached"
              className="h-8 rounded-full bg-emerald-600 px-3 text-white text-xs font-semibold shadow-md hover:bg-emerald-700 transition"
            >
              <CheckCircle2 className="mr-1.5 h-3.5 w-3.5" />
              Next Turn
            </Button>
          )}
        </div>

        {/* Route Preview Summary Bar (when route exists & not active) */}
        {!active && route && (
          <div className="mx-auto flex items-center gap-2 rounded-full border border-slate-200/90 bg-white/95 px-4 py-1.5 text-xs font-bold text-slate-700 shadow-lg backdrop-blur-md pointer-events-auto animate-in fade-in slide-in-from-bottom-2 duration-300">
            <span className="text-blue-600 font-extrabold">
              {totalDistanceMeters >= 1000
                ? `${(totalDistanceMeters / 1000).toFixed(1)} km`
                : `${totalDistanceMeters} m`}
            </span>
            <span className="text-slate-300">•</span>
            <span>{totalMinutes} min walk</span>
            <span className="text-slate-300">•</span>
            <span className="text-slate-500">{routeNodes.length} waypoints</span>
          </div>
        )}

        {/* Primary 2-Button Row */}
        {!active ? (
          /* PREVIEW MODE (Screenshot 1): [ Navigate ] and [ AR View ] */
          <div className="grid grid-cols-2 gap-3.5 pointer-events-auto">
            <Button
              onClick={startNavigation}
              aria-label="Start navigation"
              className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
            >
              <Navigation className="h-5 w-5 fill-current" />
              Navigate
            </Button>
            <Button
              onClick={handleOpenAR}
              aria-label="Open AR View"
              className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
            >
              <Camera className="h-5 w-5" />
              AR View
            </Button>
          </div>
        ) : (
          /* NAVIGATION MODE (Screenshot 2): [ Recalculate ] and [ AR View ] */
          <div className="grid grid-cols-2 gap-3.5 pointer-events-auto">
            <Button
              onClick={handleRecalculate}
              className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
            >
              <RefreshCw className="h-5 w-5" />
              Recalculate
            </Button>
            <Button
              onClick={handleOpenAR}
              aria-label="Open AR View"
              className="h-14 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-base sm:text-lg shadow-xl shadow-blue-600/25 transition-all hover:-translate-y-0.5 active:translate-y-0 flex items-center justify-center gap-2.5"
            >
              <Camera className="h-5 w-5" />
              AR View
            </Button>
          </div>
        )}
      </div>

      {/* Off Route Warning Overlay */}
      {offRoute && (
        <div className="absolute left-4 right-4 top-20 z-[600] mx-auto max-w-md flex items-start gap-3 rounded-2xl border border-amber-300 bg-amber-50/95 p-4 text-amber-950 shadow-xl backdrop-blur-md">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="text-sm">
            <p className="font-bold">You're off the planned route.</p>
            <p className="mt-0.5 text-xs text-amber-800">
              Recalculating route from the closest safe campus node...
            </p>
          </div>
          <button onClick={() => setOffRoute(false)} className="ml-auto text-amber-700 hover:text-amber-950">
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      {/* Interactive Official Architectural Floor Plan Dialog */}
      <FloorPlanModal
        isOpen={showFloorPlan}
        onClose={() => setShowFloorPlan(false)}
        defaultPlan={destination.buildingId === "central" || destination.nodeId === "library" ? "library" : "cse"}
        highlightRoom={destination.name}
        onSelectDestination={(nodeId, destId) => {
          applySelection(nodeId, destId);
        }}
      />
    </div>
  );
}
