import { TopNav } from "@/components/TopNav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  campusPanoramaScenes,
  getPanoramaScene,
  PanoramaCategory,
  PanoramaHotspot,
  PanoramaScene,
} from "@shared/panorama";
import { campusNodes, getNode, destinations } from "@shared/pathfinder";
import {
  ArrowLeft,
  Camera,
  ChevronLeft,
  ChevronRight,
  Compass,
  Eye,
  Info,
  Layers,
  Map as MapIcon,
  Maximize2,
  Minimize2,
  Navigation,
  RotateCw,
  Search,
  Share2,
  Smartphone,
  Sparkles,
  X,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation } from "wouter";

declare global {
  interface Window {
    pannellum?: any;
    DeviceOrientationEvent?: any;
  }
}

export default function CampusStreetView() {
  const [, setLocation] = useLocation();
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<any>(null);

  // Read URL query parameters for initial scene or destination
  const initialSceneId = useMemo(() => {
    const params = new URLSearchParams(window.location.search);
    const sceneParam = params.get("scene") || params.get("node") || params.get("location");
    if (sceneParam) {
      const match = getPanoramaScene(sceneParam);
      if (match) return match.id;
      const matchDest = campusPanoramaScenes.find((s) => s.destinationId === sceneParam);
      if (matchDest) return matchDest.id;
    }
    const destParam = params.get("destination") || params.get("to");
    if (destParam) {
      const match = campusPanoramaScenes.find((s) => s.destinationId === destParam);
      if (match) return match.id;
    }
    return "gate";
  }, []);

  const [currentSceneId, setCurrentSceneId] = useState<string>(initialSceneId);
  const [activeInfoHotspot, setActiveInfoHotspot] = useState<PanoramaHotspot | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isGyroActive, setIsGyroActive] = useState(false);
  const [hasGyroSupport, setHasGyroSupport] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [autoRotate, setAutoRotate] = useState(false);
  const [showMiniMap, setShowMiniMap] = useState(true);
  const [currentYaw, setCurrentYaw] = useState(0);
  const [currentPitch, setCurrentPitch] = useState(0);
  const [selectedCategory, setSelectedCategory] = useState<"All" | PanoramaCategory>("All");
  const [searchQuery, setSearchQuery] = useState("");

  const currentScene = useMemo(() => {
    return getPanoramaScene(currentSceneId) || campusPanoramaScenes[0];
  }, [currentSceneId]);

  const currentNode = useMemo(() => {
    return getNode(currentScene.nodeId);
  }, [currentScene]);

  // Check gyroscope capability on mount
  useEffect(() => {
    if (typeof window !== "undefined" && window.DeviceOrientationEvent) {
      setHasGyroSupport(true);
    }
  }, []);

  // Initialize or update Pannellum viewer when scene changes
  useEffect(() => {
    let isCancelled = false;
    setIsLoading(true);
    setLoadError(null);
    setActiveInfoHotspot(null);

    const initViewer = () => {
      if (!window.pannellum || !containerRef.current || isCancelled) return;

      try {
        // Destroy existing viewer if present
        if (viewerRef.current) {
          try {
            viewerRef.current.destroy();
          } catch (e) {
            // ignore cleanup errors
          }
          viewerRef.current = null;
        }

        // Clear container
        containerRef.current.innerHTML = "";

        // Build hotspots configuration for Pannellum
        const pnHsp = currentScene.hotspots.map((hs) => {
          if (hs.type === "scene" && hs.targetSceneId) {
            return {
              pitch: hs.pitch,
              yaw: hs.yaw,
              type: "scene",
              text: hs.text,
              sceneId: hs.targetSceneId,
              clickHandlerFunc: () => {
                if (hs.targetSceneId) {
                  switchScene(hs.targetSceneId);
                }
              },
            };
          } else {
            return {
              pitch: hs.pitch,
              yaw: hs.yaw,
              type: "info",
              text: hs.text,
              clickHandlerFunc: () => {
                setActiveInfoHotspot(hs);
              },
            };
          }
        });

        // Initialize Pannellum viewer
        const pnlmConfig: any = {
          type: "equirectangular",
          panorama: currentScene.panoramaUrl,
          autoLoad: true,
          autoRotate: autoRotate ? -2 : 0,
          compass: false,
          showZoomCtrl: false,
          showFullscreenCtrl: false,
          yaw: currentScene.defaultYaw,
          pitch: currentScene.defaultPitch,
          hfov: currentScene.defaultHfov || 95,
          minHfov: currentScene.minHfov || 40,
          maxHfov: currentScene.maxHfov || 120,
          hotSpots: pnHsp,
        };

        const viewer = window.pannellum.viewer(containerRef.current, pnlmConfig);

        viewerRef.current = viewer;

        viewer.on("load", () => {
          if (!isCancelled) {
            setIsLoading(false);
            try {
              viewer.resize();
            } catch {}
          }
        });

        // Trigger resize on animation frames and layout completion
        requestAnimationFrame(() => {
          try {
            viewer.resize();
          } catch {}
        });
        setTimeout(() => {
          try {
            viewer.resize();
          } catch {}
        }, 150);
        setTimeout(() => {
          try {
            viewer.resize();
          } catch {}
        }, 500);

        viewer.on("error", (err: any) => {
          if (!isCancelled) {
            setIsLoading(false);
            setLoadError(
              typeof err === "string" ? err : "Failed to load 360° panorama image. Please check your network."
            );
          }
        });

        // Observe container resize
        let ro: ResizeObserver | null = null;
        if (typeof ResizeObserver !== "undefined" && containerRef.current) {
          ro = new ResizeObserver(() => {
            if (viewerRef.current) {
              try {
                viewerRef.current.resize();
              } catch {}
            }
          });
          ro.observe(containerRef.current);
        }

        // Track user looking direction for mini-map compass indicator
        const updateHeading = () => {
          if (viewerRef.current) {
            try {
              setCurrentYaw(viewerRef.current.getYaw());
              setCurrentPitch(viewerRef.current.getPitch());
            } catch {
              // ignore
            }
          }
        };

        const interval = setInterval(updateHeading, 200);

        return () => {
          clearInterval(interval);
          if (ro) {
            ro.disconnect();
          }
        };
      } catch (err: any) {
        if (!isCancelled) {
          setIsLoading(false);
          setLoadError(err?.message || "Failed to initialize 360° street view.");
        }
      }
    };

    // If pannellum script is already loaded
    if (window.pannellum) {
      initViewer();
    } else {
      // Poll briefly for script load
      const checkTimer = setInterval(() => {
        if (window.pannellum) {
          clearInterval(checkTimer);
          initViewer();
        }
      }, 100);
      return () => {
        clearInterval(checkTimer);
        isCancelled = true;
      };
    }

    return () => {
      isCancelled = true;
      if (viewerRef.current) {
        try {
          viewerRef.current.destroy();
        } catch {
          // ignore
        }
        viewerRef.current = null;
      }
    };
  }, [currentSceneId]);

  const switchScene = (sceneId: string) => {
    setCurrentSceneId(sceneId);
    window.history.replaceState(null, "", `/tour?scene=${encodeURIComponent(sceneId)}`);
  };

  const toggleAutoRotate = () => {
    if (!viewerRef.current) return;
    const nextState = !autoRotate;
    setAutoRotate(nextState);
    if (nextState) {
      viewerRef.current.startAutoRotate(-2);
    } else {
      viewerRef.current.stopAutoRotate();
    }
  };

  const handleZoom = (delta: number) => {
    if (!viewerRef.current) return;
    const currentHfov = viewerRef.current.getHfov();
    const target = Math.max(40, Math.min(120, currentHfov + delta));
    viewerRef.current.setHfov(target);
  };

  const toggleFullscreen = () => {
    if (!containerRef.current) return;
    if (!document.fullscreenElement) {
      containerRef.current.parentElement?.requestFullscreen().then(() => {
        setIsFullscreen(true);
      }).catch(() => {});
    } else {
      document.exitFullscreen().then(() => {
        setIsFullscreen(false);
      }).catch(() => {});
    }
  };

  // Toggle phone gyroscope orientation
  const toggleGyroscope = async () => {
    if (!viewerRef.current) return;

    if (isGyroActive) {
      try {
        viewerRef.current.stopOrientation();
      } catch {}
      setIsGyroActive(false);
      return;
    }

    // iOS 13+ requires user permission prompt
    if (
      typeof (window as any).DeviceOrientationEvent !== "undefined" &&
      typeof (window as any).DeviceOrientationEvent.requestPermission === "function"
    ) {
      try {
        const permission = await (window as any).DeviceOrientationEvent.requestPermission();
        if (permission === "granted") {
          viewerRef.current.startOrientation();
          setIsGyroActive(true);
        } else {
          alert("Device motion permission was denied.");
        }
      } catch (e) {
        console.error("Orientation permission error:", e);
      }
    } else {
      try {
        viewerRef.current.startOrientation();
        setIsGyroActive(true);
      } catch (e) {
        console.error("Gyroscope error:", e);
      }
    }
  };

  // Share current viewpoint URL
  const handleShare = () => {
    if (navigator.share) {
      navigator.share({
        title: `${currentScene.title} - AIET 360° Virtual Tour`,
        text: currentScene.description,
        url: window.location.href,
      }).catch(() => {});
    } else {
      navigator.clipboard.writeText(window.location.href);
      alert("Virtual tour link copied to clipboard!");
    }
  };

  // Filter scenes based on selected category and search query
  const filteredScenes = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return campusPanoramaScenes.filter((scene) => {
      const matchesCategory =
        selectedCategory === "All" || scene.category === selectedCategory;
      const matchesQuery =
        !q ||
        scene.title.toLowerCase().includes(q) ||
        scene.subtitle.toLowerCase().includes(q) ||
        scene.buildingName.toLowerCase().includes(q);
      return matchesCategory && matchesQuery;
    });
  }, [selectedCategory, searchQuery]);

  // Distinct nodes for radar map
  const radarNodes = useMemo(() => {
    const seenNodes = new Set<string>();
    const list: PanoramaScene[] = [];
    for (const scene of campusPanoramaScenes) {
      if (!seenNodes.has(scene.nodeId)) {
        seenNodes.add(scene.nodeId);
        list.push(scene);
      }
    }
    return list;
  }, []);

  return (
    <div className="flex h-screen w-screen flex-col bg-slate-950 text-white overflow-hidden">
      <TopNav />

      {/* Main Street View Virtual Tour Container */}
      <div className="relative flex-1 w-full h-[calc(100vh-64px)] overflow-hidden bg-black select-none">
        {/* Pannellum WebGL 360 Canvas Container */}
        <div
          ref={containerRef}
          id="pannellum-viewer-container"
          className="absolute inset-0 h-full w-full cursor-grab active:cursor-grabbing"
          style={{ touchAction: "none" }}
        />

        {/* Top Floating Header & Location Info HUD */}
        <div className="absolute top-4 left-4 right-4 z-20 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 pointer-events-none">
          {/* Location Title & Breadcrumb */}
          <div className="pointer-events-auto flex items-center gap-3 rounded-2xl border border-white/20 bg-slate-950/80 p-2.5 pr-4 shadow-2xl backdrop-blur-md">
            <Link
              href="/map"
              className="flex h-9 w-9 items-center justify-center rounded-xl bg-white/10 text-white transition hover:bg-white/20 active:scale-95"
              title="Return to Campus Map"
            >
              <ArrowLeft className="h-4 w-4" />
            </Link>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-amber-400">
                  {currentScene.buildingName}
                </span>
                <span className="text-[10px] text-slate-400">•</span>
                <span className="text-[10px] text-slate-300">
                  {currentScene.floorName}
                </span>
                {currentScene.badge && (
                  <Badge variant="outline" className="border-amber-400/40 bg-amber-400/10 text-[9px] text-amber-300 py-0 px-1.5 font-semibold">
                    {currentScene.badge}
                  </Badge>
                )}
              </div>
              <h1 className="text-sm font-extrabold text-white tracking-tight sm:text-base">
                {currentScene.title}
              </h1>
            </div>
          </div>

          {/* Quick Action Buttons */}
          <div className="pointer-events-auto flex items-center gap-2 self-end sm:self-auto">
            {/* Direct Map Navigation Link */}
            <Link
              href={`/map?destination=${encodeURIComponent(currentScene.destinationId || currentScene.nodeId)}`}
              className="flex items-center gap-1.5 rounded-xl border border-blue-400/30 bg-blue-600/90 px-3 py-2 text-xs font-bold text-white shadow-lg backdrop-blur-md transition hover:bg-blue-600 active:scale-95"
            >
              <Navigation className="h-3.5 w-3.5" />
              <span>Navigate Here</span>
            </Link>

            {/* Unity AR Hub Quick Switch */}
            <Link
              href="/unity-ar"
              className="flex items-center gap-1.5 rounded-xl border border-amber-400/40 bg-amber-500/90 px-3 py-2 text-xs font-bold text-slate-950 shadow-lg backdrop-blur-md transition hover:bg-amber-400 active:scale-95"
            >
              <Camera className="h-3.5 w-3.5" />
              <span className="hidden sm:inline">Unity AR</span>
            </Link>

            {/* Share Viewpoint Button */}
            <button
              onClick={handleShare}
              className="flex h-9 w-9 items-center justify-center rounded-xl border border-white/20 bg-slate-900/80 text-white shadow-lg backdrop-blur-md transition hover:bg-white/20 active:scale-95"
              title="Share Location"
              aria-label="Share location"
            >
              <Share2 className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Loading Overlay */}
        {isLoading && (
          <div className="absolute inset-0 z-30 flex flex-col items-center justify-center bg-slate-950/80 backdrop-blur-sm">
            <div className="relative flex items-center justify-center">
              <div className="h-16 w-16 animate-spin rounded-full border-4 border-amber-400/20 border-t-amber-400" />
              <Eye className="absolute h-6 w-6 text-amber-400 animate-pulse" />
            </div>
            <p className="mt-4 text-sm font-bold text-white">Loading AIET 360° Street View...</p>
            <p className="mt-1 text-xs text-slate-400">{currentScene.title}</p>
          </div>
        )}

        {/* Load Error Notification */}
        {loadError && (
          <div className="absolute inset-0 z-20 flex flex-col items-center justify-center bg-slate-950/90 p-6 text-center">
            <div className="rounded-xl border border-red-500/30 bg-red-500/10 p-6 max-w-md">
              <p className="text-base font-bold text-red-400">Unable to load viewpoint</p>
              <p className="mt-2 text-xs text-slate-300">{loadError}</p>
              <div className="mt-4 flex justify-center gap-3">
                <Button
                  onClick={() => switchScene(currentSceneId)}
                  className="bg-amber-500 hover:bg-amber-600 text-slate-950 font-semibold text-xs"
                >
                  Retry Loading
                </Button>
                <Link
                  href="/map"
                  className="rounded-lg border border-white/20 bg-white/10 px-4 py-2 text-xs font-semibold text-white"
                >
                  Return to Map
                </Link>
              </div>
            </div>
          </div>
        )}

        {/* Informational Hotspot Modal Popup */}
        {activeInfoHotspot && (
          <div className="absolute bottom-32 left-1/2 z-30 w-[92%] max-w-md -translate-x-1/2 transform rounded-2xl border border-white/20 bg-slate-900/95 p-5 shadow-2xl backdrop-blur-xl animate-in fade-in slide-in-from-bottom-6">
            <div className="flex items-start justify-between gap-3">
              <div className="flex items-center gap-2 text-amber-400 font-bold text-sm">
                <Sparkles className="h-4 w-4" />
                <span>{activeInfoHotspot.infoTitle || activeInfoHotspot.text}</span>
              </div>
              <button
                onClick={() => setActiveInfoHotspot(null)}
                className="rounded-full bg-white/10 p-1 text-slate-400 hover:bg-white/20 hover:text-white"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            <p className="mt-2 text-xs leading-relaxed text-slate-300">
              {activeInfoHotspot.infoDescription || "Point of interest on AIET campus grounds."}
            </p>
            <div className="mt-3 flex items-center justify-between border-t border-white/10 pt-3">
              <span className="text-[10px] uppercase font-bold tracking-wider text-slate-400">
                Verified Campus Landmark
              </span>
              <button
                onClick={() => setActiveInfoHotspot(null)}
                className="rounded-lg bg-amber-400/20 px-3 py-1 text-xs font-semibold text-amber-300 hover:bg-amber-400/30"
              >
                Close
              </button>
            </div>
          </div>
        )}

        {/* Floating Controls Sidebar (Right Side) */}
        <div className="absolute right-4 top-20 z-20 flex flex-col gap-2">
          {/* Zoom In */}
          <button
            onClick={() => handleZoom(-15)}
            className="flex h-10 w-10 items-center justify-center rounded-xl border border-white/15 bg-slate-900/80 shadow-lg backdrop-blur-md transition hover:bg-white/20 hover:text-amber-400 active:scale-95"
            title="Zoom In"
            aria-label="Zoom in"
          >
            <ZoomIn className="h-4 w-4" />
          </button>

          {/* Zoom Out */}
          <button
            onClick={() => handleZoom(15)}
            className="flex h-10 w-10 items-center justify-center rounded-xl border border-white/15 bg-slate-900/80 shadow-lg backdrop-blur-md transition hover:bg-white/20 hover:text-amber-400 active:scale-95"
            title="Zoom Out"
            aria-label="Zoom out"
          >
            <ZoomOut className="h-4 w-4" />
          </button>

          {/* Auto Rotate Toggle */}
          <button
            onClick={toggleAutoRotate}
            className={`flex h-10 w-10 items-center justify-center rounded-xl border shadow-lg backdrop-blur-md transition active:scale-95 ${
              autoRotate
                ? "border-amber-400 bg-amber-400/30 text-amber-300"
                : "border-white/15 bg-slate-900/80 text-white hover:bg-white/20"
            }`}
            title="Toggle Auto-Rotation"
            aria-label="Toggle auto rotation"
          >
            <RotateCw className={`h-4 w-4 ${autoRotate ? "animate-spin" : ""}`} />
          </button>

          {/* Device Orientation / Gyroscope Mode for Mobile */}
          {hasGyroSupport && (
            <button
              onClick={toggleGyroscope}
              className={`flex h-10 w-10 items-center justify-center rounded-xl border shadow-lg backdrop-blur-md transition active:scale-95 ${
                isGyroActive
                  ? "border-cyan-400 bg-cyan-400/30 text-cyan-300 ring-2 ring-cyan-400/50"
                  : "border-white/15 bg-slate-900/80 text-white hover:bg-white/20"
              }`}
              title="Motion & Gyroscope Mode (Look around with phone)"
              aria-label="Toggle phone gyroscope rotation"
            >
              <Smartphone className={`h-4 w-4 ${isGyroActive ? "animate-bounce" : ""}`} />
            </button>
          )}

          {/* Fullscreen Toggle */}
          <button
            onClick={toggleFullscreen}
            className="flex h-10 w-10 items-center justify-center rounded-xl border border-white/15 bg-slate-900/80 shadow-lg backdrop-blur-md transition hover:bg-white/20 hover:text-amber-400 active:scale-95"
            title="Toggle Fullscreen"
            aria-label="Toggle fullscreen"
          >
            {isFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
          </button>

          {/* Mini-Map Toggle */}
          <button
            onClick={() => setShowMiniMap(!showMiniMap)}
            className={`flex h-10 w-10 items-center justify-center rounded-xl border shadow-lg backdrop-blur-md transition active:scale-95 ${
              showMiniMap
                ? "border-amber-400/60 bg-amber-400/20 text-amber-300"
                : "border-white/15 bg-slate-900/80 text-white hover:bg-white/20"
            }`}
            title="Toggle Campus Radar Mini-Map"
            aria-label="Toggle mini map"
          >
            <Compass className="h-4 w-4" />
          </button>
        </div>

        {/* Interactive Campus Radar Mini-Map Overlay (Left Side) */}
        {showMiniMap && (
          <div className="absolute left-4 top-20 z-20 w-44 rounded-2xl border border-white/20 bg-slate-950/85 p-3 shadow-2xl backdrop-blur-md sm:w-56 animate-in fade-in">
            <div className="flex items-center justify-between pb-2 border-b border-white/10">
              <span className="flex items-center gap-1.5 text-[11px] font-bold text-amber-400">
                <Compass className="h-3.5 w-3.5" />
                <span>Campus Radar</span>
              </span>
              <span className="text-[10px] text-slate-400 font-mono">
                {Math.round(currentYaw)}°
              </span>
            </div>

            {/* Schematic Node Layout */}
            <div className="relative mt-3 h-36 w-full rounded-xl bg-slate-900/90 border border-white/10 overflow-hidden p-2">
              <div className="absolute inset-0 bg-[radial-gradient(#38bdf8_1px,transparent_1px)] [background-size:12px_12px] opacity-20" />

              {/* Campus Nodes on Radar */}
              {radarNodes.map((scene) => {
                const isSelected = scene.nodeId === currentScene.nodeId;
                const node = campusNodes.find((n) => n.id === scene.nodeId);
                const x = node ? `${node.xCoordinate}%` : "50%";
                const y = node ? `${node.yCoordinate}%` : "50%";

                return (
                  <button
                    key={scene.id}
                    onClick={() => switchScene(scene.id)}
                    style={{ left: x, top: y }}
                    title={scene.title}
                    className={`absolute -translate-x-1/2 -translate-y-1/2 group transition-transform ${
                      isSelected ? "z-10 scale-125" : "hover:scale-110 opacity-70 hover:opacity-100"
                    }`}
                  >
                    <div
                      className={`h-3 w-3 rounded-full border shadow-md transition ${
                        isSelected
                          ? "border-amber-400 bg-amber-400 shadow-amber-400/50 ring-2 ring-amber-400/30"
                          : "border-cyan-400 bg-cyan-600 hover:border-white"
                      }`}
                    />
                    {/* Direction field of view cone on active node */}
                    {isSelected && (
                      <div
                        style={{ transform: `rotate(${currentYaw}deg)` }}
                        className="pointer-events-none absolute -top-4 -left-4 h-11 w-11 origin-center"
                      >
                        <div className="h-full w-full rounded-full border-t-2 border-amber-400/80 bg-amber-400/10 [clip-path:polygon(50%_50%,20%_0%,80%_0%)]" />
                      </div>
                    )}
                  </button>
                );
              })}
            </div>

            <p className="mt-2 text-[10px] text-slate-400 text-center">
              Tap radar dots to navigate campus
            </p>
          </div>
        )}

        {/* Bottom Location Selector & Category Browser */}
        <div className="absolute bottom-3 left-0 right-0 z-20 px-3 sm:px-4">
          <div className="mx-auto max-w-5xl rounded-2xl border border-white/20 bg-slate-950/90 p-3 shadow-2xl backdrop-blur-xl">
            {/* Header with Title, Category Tabs, and Search */}
            <div className="mb-2.5 flex flex-wrap items-center justify-between gap-2 border-b border-white/10 pb-2">
              <div className="flex items-center gap-1.5">
                <Layers className="h-4 w-4 text-amber-400" />
                <span className="font-extrabold text-xs sm:text-sm text-white">
                  AIET Campus Locations
                </span>
                <span className="rounded-full bg-white/10 px-2 py-0.5 text-[10px] font-bold text-amber-300">
                  {filteredScenes.length} / {campusPanoramaScenes.length}
                </span>
              </div>

              {/* Category Filter Pills */}
              <div className="flex items-center gap-1 overflow-x-auto text-xs scrollbar-none">
                {(["All", "Landmarks", "Academic", "Faculty Cabins"] as const).map((cat) => (
                  <button
                    key={cat}
                    onClick={() => setSelectedCategory(cat)}
                    className={`rounded-lg px-2.5 py-1 text-xs font-semibold transition ${
                      selectedCategory === cat
                        ? "bg-amber-400 text-slate-950 shadow-md shadow-amber-400/30"
                        : "bg-white/5 text-slate-300 hover:bg-white/10 hover:text-white"
                    }`}
                  >
                    {cat}
                  </button>
                ))}
              </div>

              {/* Quick Search Input */}
              <div className="relative flex items-center">
                <Search className="absolute left-2.5 h-3.5 w-3.5 text-slate-400" />
                <input
                  type="text"
                  placeholder="Search location or cabin..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="h-7 w-36 sm:w-48 rounded-lg border border-white/15 bg-white/5 pl-8 pr-6 text-xs text-white placeholder-slate-400 focus:border-amber-400 focus:outline-none focus:ring-1 focus:ring-amber-400"
                />
                {searchQuery && (
                  <button
                    onClick={() => setSearchQuery("")}
                    className="absolute right-2 text-slate-400 hover:text-white"
                  >
                    <X className="h-3 w-3" />
                  </button>
                )}
              </div>
            </div>

            {/* Horizontal Scrollable Thumbnails */}
            <div className="flex gap-2 overflow-x-auto pb-1 scrollbar-thin scrollbar-thumb-white/20">
              {filteredScenes.map((scene) => {
                const isActive = scene.id === currentSceneId;
                return (
                  <button
                    key={scene.id}
                    onClick={() => switchScene(scene.id)}
                    className={`group relative flex shrink-0 items-center gap-2 rounded-xl border p-1.5 pr-3 text-left transition-all ${
                      isActive
                        ? "border-amber-400 bg-amber-400/20 text-white shadow-lg shadow-amber-500/20 ring-1 ring-amber-400/50"
                        : "border-white/10 bg-white/5 text-slate-300 hover:border-white/25 hover:bg-white/10"
                    }`}
                  >
                    <div className="relative h-11 w-16 overflow-hidden rounded-lg bg-slate-800 shrink-0">
                      <img
                        src={scene.panoramaUrl}
                        alt={scene.title}
                        className="h-full w-full object-cover transition-transform group-hover:scale-110"
                        loading="lazy"
                      />
                      {isActive && (
                        <div className="absolute inset-0 bg-amber-400/25 ring-2 ring-amber-400" />
                      )}
                    </div>
                    <div className="min-w-0 max-w-[140px] sm:max-w-[180px]">
                      <p className="text-xs font-bold leading-tight truncate text-white">
                        {scene.title}
                      </p>
                      <p className="text-[10px] text-slate-400 truncate">
                        {scene.buildingName}
                      </p>
                      {scene.badge && (
                        <span className="inline-block mt-0.5 rounded bg-amber-400/15 px-1.5 py-0.2 text-[9px] font-semibold text-amber-300">
                          {scene.badge}
                        </span>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
