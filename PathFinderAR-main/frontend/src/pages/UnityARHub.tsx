import { TopNav } from "@/components/TopNav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { campusNodes, destinations, getNode } from "@shared/pathfinder";
import {
  Activity,
  AlertCircle,
  ArrowRight,
  Camera,
  CheckCircle2,
  Compass,
  Download,
  ExternalLink,
  Info,
  Layers,
  MapPin,
  Play,
  QrCode,
  RefreshCw,
  Send,
  Smartphone,
  Sparkles,
  Wifi,
} from "lucide-react";
import React, { useEffect, useMemo, useState } from "react";
import { Link } from "wouter";
import { FloorPlanModal } from "@/components/FloorPlanModal";

export default function UnityARHub() {
  const [startNodeId, setStartNodeId] = useState("gate");
  const [destinationNodeId, setDestinationNodeId] = useState("library");
  const [accessibleOnly, setAccessibleOnly] = useState(false);
  const [showFloorPlan, setShowFloorPlan] = useState(false);

  // API connectivity test state
  const [apiStatus, setApiStatus] = useState<"checking" | "online" | "offline">("checking");
  const [routeTesting, setRouteTesting] = useState(false);
  const [routeResult, setRouteResult] = useState<any>(null);
  const [routeError, setRouteError] = useState<string | null>(null);

  // LAN address detection
  const serverHost = typeof window !== "undefined" ? window.location.host : "localhost:3000";
  const serverOrigin = typeof window !== "undefined" ? window.location.origin : "http://localhost:3000";

  const deepLinkUrl = `pathfinder://navigate?start=${encodeURIComponent(
    startNodeId
  )}&dest=${encodeURIComponent(destinationNodeId)}&accessible=${accessibleOnly}`;

  const checkApiConnection = async () => {
    setApiStatus("checking");
    try {
      const res = await fetch("/api/destinations");
      if (res.ok) {
        setApiStatus("online");
      } else {
        setApiStatus("offline");
      }
    } catch {
      setApiStatus("offline");
    }
  };

  useEffect(() => {
    checkApiConnection();
  }, []);

  const testRouteCalculation = async () => {
    setRouteTesting(true);
    setRouteError(null);
    setRouteResult(null);

    try {
      const res = await fetch("/api/routes/calculate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          startNodeId,
          destinationNodeId,
          algorithm: "auto",
          accessibleOnly,
        }),
      });

      const data = await res.json();
      if (res.ok) {
        setRouteResult(data);
      } else {
        setRouteError(data.error || "Route calculation failed.");
      }
    } catch (err: any) {
      setRouteError(err.message || "Failed to reach backend endpoint.");
    } finally {
      setRouteTesting(false);
    }
  };

  const launchUnityDeepLink = () => {
    window.location.href = deepLinkUrl;
  };

  const selectedStartNode = getNode(startNodeId);
  const selectedDestNode = getNode(destinationNodeId);

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <TopNav />

      <main className="container mx-auto px-4 py-8 max-w-6xl">
        {/* Hero Section */}
        <div className="relative overflow-hidden rounded-3xl border border-white/10 bg-gradient-to-br from-slate-900 via-slate-900/90 to-purple-950/40 p-6 md:p-10 shadow-2xl backdrop-blur-xl">
          <div className="absolute top-0 right-0 -mt-10 -mr-10 h-64 w-64 rounded-full bg-cyan-500/10 blur-3xl" />
          <div className="absolute bottom-0 left-0 -mb-10 -ml-10 h-64 w-64 rounded-full bg-amber-500/10 blur-3xl" />

          <div className="relative z-10 flex flex-col md:flex-row items-start md:items-center justify-between gap-6">
            <div>
              <div className="inline-flex items-center gap-2 rounded-full border border-amber-400/30 bg-amber-400/10 px-3 py-1 text-xs font-bold text-amber-300">
                <Sparkles className="h-3.5 w-3.5" />
                <span>Unity 6 AR Foundation + ARCore</span>
              </div>
              <h1 className="mt-3 text-2xl md:text-4xl font-extrabold tracking-tight text-white">
                Unity AR Campus Navigator
              </h1>
              <p className="mt-2 text-sm md:text-base text-slate-300 max-w-2xl leading-relaxed">
                Experience real-time outdoor augmented reality wayfinding with dynamic 3D waypoint
                markers, compass alignment, and live backend route calculations.
              </p>
            </div>

            {/* Quick Status Pill */}
            <div className="flex flex-col gap-2 rounded-2xl border border-white/10 bg-white/5 p-4 backdrop-blur-md min-w-[220px]">
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-400">Backend API</span>
                <span className="flex items-center gap-1.5 font-bold">
                  {apiStatus === "checking" && (
                    <RefreshCw className="h-3 w-3 animate-spin text-amber-400" />
                  )}
                  {apiStatus === "online" && (
                    <span className="flex items-center gap-1 text-emerald-400">
                      <CheckCircle2 className="h-3.5 w-3.5" /> Online
                    </span>
                  )}
                  {apiStatus === "offline" && (
                    <span className="flex items-center gap-1 text-rose-400">
                      <AlertCircle className="h-3.5 w-3.5" /> Offline
                    </span>
                  )}
                </span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-400">Unity Project</span>
                <span className="text-cyan-300 font-semibold font-mono">/PathfinderAR</span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-400">Target Framework</span>
                <span className="text-slate-200 font-semibold">AR Foundation 6.3</span>
              </div>
            </div>
          </div>
        </div>

        {/* Two Column Layout: Route Dispatcher & Mobile Integration */}
        <div className="mt-8 grid grid-cols-1 lg:grid-cols-12 gap-8">
          {/* Left Column: Route Dispatcher & Verification (7 cols) */}
          <div className="lg:col-span-7 flex flex-col gap-6">
            {/* Route Dispatch Card */}
            <div className="rounded-2xl border border-white/10 bg-slate-900/60 p-6 backdrop-blur-md">
              <h2 className="text-lg font-bold text-white flex items-center gap-2">
                <Compass className="h-5 w-5 text-amber-400" />
                <span>Configure & Dispatch AR Route</span>
              </h2>
              <p className="mt-1 text-xs text-slate-400">
                Choose start and destination campus landmarks to preview waypoints and launch in Unity.
              </p>

              <div className="mt-5 grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Start Node Selector */}
                <div>
                  <label className="block text-xs font-semibold text-slate-300 mb-1.5">
                    Start Landmark (GPS Snap)
                  </label>
                  <select
                    value={startNodeId}
                    onChange={(e) => setStartNodeId(e.target.value)}
                    className="w-full rounded-xl border border-white/15 bg-slate-800 px-3 py-2.5 text-sm text-white outline-none focus:border-amber-400 focus:ring-1 focus:ring-amber-400"
                  >
                    {campusNodes
                      .filter((n) => n.isActive)
                      .map((node) => (
                        <option key={node.id} value={node.id}>
                          {node.name} ({node.buildingName})
                        </option>
                      ))}
                  </select>
                </div>

                {/* Destination Node Selector */}
                <div>
                  <label className="block text-xs font-semibold text-slate-300 mb-1.5">
                    Destination Landmark
                  </label>
                  <select
                    value={destinationNodeId}
                    onChange={(e) => setDestinationNodeId(e.target.value)}
                    className="w-full rounded-xl border border-white/15 bg-slate-800 px-3 py-2.5 text-sm text-white outline-none focus:border-cyan-400 focus:ring-1 focus:ring-cyan-400"
                  >
                    {campusNodes
                      .filter((n) => n.isActive)
                      .map((node) => (
                        <option key={node.id} value={node.id}>
                          {node.name} ({node.buildingName})
                        </option>
                      ))}
                  </select>
                </div>
              </div>

              {/* Accessible Only Checkbox */}
              <div className="mt-4 flex items-center gap-2">
                <input
                  type="checkbox"
                  id="unity-accessible"
                  checked={accessibleOnly}
                  onChange={(e) => setAccessibleOnly(e.target.checked)}
                  className="h-4 w-4 rounded border-white/20 bg-slate-800 text-amber-500 focus:ring-amber-400"
                />
                <label htmlFor="unity-accessible" className="text-xs text-slate-300 cursor-pointer">
                  Wheelchair / Step-free routes only (avoids staircases)
                </label>
              </div>

              {/* Action Buttons */}
              <div className="mt-6 flex flex-wrap gap-3">
                <Button
                  onClick={testRouteCalculation}
                  disabled={routeTesting}
                  className="bg-amber-500 hover:bg-amber-600 text-slate-950 font-bold text-xs flex items-center gap-1.5 shadow-lg shadow-amber-500/20"
                >
                  {routeTesting ? (
                    <RefreshCw className="h-4 w-4 animate-spin" />
                  ) : (
                    <Activity className="h-4 w-4" />
                  )}
                  <span>Test API Route Response</span>
                </Button>

                <Button
                  onClick={launchUnityDeepLink}
                  className="bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-600 hover:to-blue-700 text-white font-bold text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/20"
                >
                  <Send className="h-4 w-4" />
                  <span>Launch in Unity AR App</span>
                </Button>

                <Link
                  href={`/ar?destination=${destinationNodeId}`}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-white/20 bg-white/5 px-3 py-2 text-xs font-semibold text-slate-200 transition hover:bg-white/10"
                >
                  <Camera className="h-4 w-4 text-emerald-400" />
                  <span>Use Web AR Fallback</span>
                </Link>
              </div>

              {/* Deep Link Display */}
              <div className="mt-5 rounded-xl border border-white/10 bg-slate-950/60 p-3 text-xs">
                <span className="font-semibold text-slate-400 block mb-1">Generated App Intent / URI:</span>
                <code className="text-amber-300 break-all select-all font-mono">{deepLinkUrl}</code>
              </div>
            </div>

            {/* Test Route Output */}
            {routeError && (
              <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-rose-300 text-xs">
                <p className="font-bold flex items-center gap-1.5">
                  <AlertCircle className="h-4 w-4" /> Route Error
                </p>
                <p className="mt-1">{routeError}</p>
              </div>
            )}

            {routeResult && (
              <div className="rounded-2xl border border-emerald-500/30 bg-slate-900/70 p-5 backdrop-blur-md animate-in fade-in">
                <div className="flex items-center justify-between border-b border-white/10 pb-3">
                  <span className="text-sm font-bold text-emerald-400 flex items-center gap-1.5">
                    <CheckCircle2 className="h-4 w-4" /> Route Computed Successfully
                  </span>
                  <Badge variant="outline" className="border-emerald-500/40 text-emerald-300 text-xs">
                    {routeResult.algorithm} Algorithm
                  </Badge>
                </div>

                <div className="mt-4 grid grid-cols-3 gap-3 text-center">
                  <div className="rounded-xl bg-white/5 p-2.5">
                    <p className="text-[10px] text-slate-400 uppercase font-bold">Total Distance</p>
                    <p className="mt-1 text-base font-extrabold text-white">
                      {routeResult.totalDistance} m
                    </p>
                  </div>
                  <div className="rounded-xl bg-white/5 p-2.5">
                    <p className="text-[10px] text-slate-400 uppercase font-bold">Est. Walk Time</p>
                    <p className="mt-1 text-base font-extrabold text-amber-300">
                      {Math.ceil(routeResult.estimatedTime / 60)} min
                    </p>
                  </div>
                  <div className="rounded-xl bg-white/5 p-2.5">
                    <p className="text-[10px] text-slate-400 uppercase font-bold">Waypoints</p>
                    <p className="mt-1 text-base font-extrabold text-cyan-300">
                      {routeResult.waypoints?.length || 0}
                    </p>
                  </div>
                </div>

                {/* Waypoint Sequence */}
                <div className="mt-4">
                  <p className="text-xs font-semibold text-slate-300 mb-2">Turn-by-Turn Guidance:</p>
                  <ol className="space-y-1.5 text-xs text-slate-300 max-h-48 overflow-y-auto pr-2">
                    {routeResult.instructions?.map((inst: string, idx: number) => (
                      <li key={idx} className="flex items-start gap-2 rounded-lg bg-white/5 p-2">
                        <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-amber-400/20 text-[10px] font-bold text-amber-300">
                          {idx + 1}
                        </span>
                        <span>{inst}</span>
                      </li>
                    ))}
                  </ol>
                </div>
              </div>
            )}
          </div>

          {/* Right Column: Unity Integration Setup & Build Guide (5 cols) */}
          <div className="lg:col-span-5 flex flex-col gap-6">
            {/* LAN Configuration Card */}
            <div className="rounded-2xl border border-white/10 bg-slate-900/60 p-6 backdrop-blur-md">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Wifi className="h-4 w-4 text-cyan-400" />
                <span>LAN Setup for Physical Phone</span>
              </h3>
              <p className="mt-2 text-xs text-slate-300 leading-relaxed">
                Physical Android phones cannot connect to <code className="text-amber-300">localhost</code>. Connect your PC and phone to the same Wi-Fi network and configure:
              </p>

              <div className="mt-3 rounded-xl bg-slate-950 p-3 font-mono text-xs border border-white/10">
                <p className="text-slate-400 text-[11px]">// In PathfinderAR/Assets/Scripts/PathFinderConfig.cs</p>
                <p className="text-cyan-300 mt-1">public string BaseUrl = "{serverOrigin}";</p>
              </div>

              <p className="mt-3 text-[11px] text-slate-400">
                To find your PC's IP, open PowerShell and run <code className="text-slate-200">ipconfig</code>, then look for IPv4 Address (e.g. 192.168.1.xxx).
              </p>
            </div>

            {/* Build & Run Instructions */}
            <div className="rounded-2xl border border-white/10 bg-slate-900/60 p-6 backdrop-blur-md">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Smartphone className="h-4 w-4 text-emerald-400" />
                <span>How to Build & Run Unity Project</span>
              </h3>

              <ol className="mt-3 space-y-3 text-xs text-slate-300">
                <li className="flex items-start gap-2.5">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-cyan-400/20 text-[10px] font-bold text-cyan-300">
                    1
                  </span>
                  <div>
                    <strong className="text-white">Open in Unity 6:</strong> Launch Unity Hub and open the folder <code className="text-amber-300 font-mono">D:\PathFinder\PathfinderAR</code>.
                  </div>
                </li>
                <li className="flex items-start gap-2.5">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-cyan-400/20 text-[10px] font-bold text-cyan-300">
                    2
                  </span>
                  <div>
                    <strong className="text-white">Load Main Scene:</strong> In Project window, double click <code className="text-amber-300 font-mono">Assets/PathFinderAR.unity</code>.
                  </div>
                </li>
                <li className="flex items-start gap-2.5">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-cyan-400/20 text-[10px] font-bold text-cyan-300">
                    3
                  </span>
                  <div>
                    <strong className="text-white">In-Editor Testing:</strong> Press Play in Unity. The XR Simulation environment will emulate camera feed and location signals.
                  </div>
                </li>
                <li className="flex items-start gap-2.5">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-cyan-400/20 text-[10px] font-bold text-cyan-300">
                    4
                  </span>
                  <div>
                    <strong className="text-white">Build Android APK:</strong> Go to <span className="text-white font-semibold">File &gt; Build Settings</span>, select <span className="text-white font-semibold">Android</span>, and click <span className="text-white font-semibold">Build and Run</span> on your USB-connected device.
                  </div>
                </li>
              </ol>
            </div>

            {/* Comparison / Capabilities Matrix */}
            <div className="rounded-2xl border border-white/10 bg-slate-900/60 p-6 backdrop-blur-md">
              <h3 className="text-base font-bold text-white mb-3">
                AR Capabilities Comparison
              </h3>
              <div className="space-y-2 text-xs">
                <div className="flex items-center justify-between border-b border-white/10 pb-2">
                  <span className="text-slate-400">Outdoor Waypoint Markers</span>
                  <span className="font-semibold text-emerald-400">Unity & Web AR</span>
                </div>
                <div className="flex items-center justify-between border-b border-white/10 pb-2">
                  <span className="text-slate-400">6DoF SLAM Camera Tracking</span>
                  <span className="font-semibold text-cyan-300">Unity ARCore</span>
                </div>
                <div className="flex items-center justify-between border-b border-white/10 pb-2">
                  <span className="text-slate-400">Hardware Compass Heading</span>
                  <span className="font-semibold text-emerald-400">Unity & Web AR</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-slate-400">Zero-Install Browser Access</span>
                  <span className="font-semibold text-amber-300">Web AR (/ar)</span>
                </div>
              </div>
            </div>

            {/* Architectural Blueprint & Indoor AR Anchors */}
            <div className="rounded-2xl border border-cyan-500/30 bg-slate-900/60 p-6 backdrop-blur-md">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-base font-bold text-white flex items-center gap-2">
                  <Layers className="h-4 w-4 text-cyan-400" />
                  <span>Architectural Floor Plans & AR Mesh</span>
                </h3>
                <Badge className="border border-cyan-400/40 bg-cyan-500/20 text-cyan-300 text-[10px]">
                  AIET Official
                </Badge>
              </div>
              <p className="mt-1.5 text-xs text-slate-300 leading-relaxed">
                Physical engineering floor plans for the AIET CSE Academic Block (155.55 Sqm ML Lab) and Central Library are synchronized with indoor AR waypoint coordinates.
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Button
                  size="sm"
                  onClick={() => setShowFloorPlan(true)}
                  className="bg-cyan-500 text-slate-950 hover:bg-cyan-400 font-bold text-xs rounded-xl"
                >
                  <Layers className="mr-1.5 h-3.5 w-3.5" />
                  Inspect Blueprints & AR Anchors
                </Button>
                <Link
                  href="/ar"
                  className="inline-flex items-center rounded-xl border border-white/15 bg-slate-800/80 px-3 py-1.5 text-xs font-semibold text-white hover:bg-slate-700 transition"
                >
                  <Camera className="mr-1.5 h-3.5 w-3.5 text-cyan-300" />
                  Launch Web AR
                </Link>
              </div>
            </div>
          </div>
        </div>
      </main>

      {/* Interactive Official Architectural Floor Plan Dialog */}
      <FloorPlanModal
        isOpen={showFloorPlan}
        onClose={() => setShowFloorPlan(false)}
        defaultPlan="cse"
        onSelectDestination={(nodeId) => {
          setDestinationNodeId(nodeId);
        }}
      />
    </div>
  );
}
