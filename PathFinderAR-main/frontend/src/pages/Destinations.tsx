import { TopNav } from "@/components/TopNav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/_core/hooks/useAuth";
import { trpc } from "@/lib/trpc";
import { campusBuildings, campusEdges, campusNodes, destinations as landmarkDestinations, getNode, type Destination, type DestinationCategory } from "@shared/pathfinder";
import { calculateRoute } from "@shared/routing";
import { readRouteSelection, routeSelectionSearch, routableSourceNodes, writeRouteSelection } from "@shared/route-selection";
import { Building2, ChevronRight, Clock3, Compass, Copy, Heart, Info, MapPin, QrCode, Route, Search, Share2, ShieldCheck, SlidersHorizontal, Sparkles, Trash2, X } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import React, { useEffect, useMemo, useState } from "react";
import { useLocation } from "wouter";
import { buildArIntentUrl, isAndroidDevice, navDestinations, navStartPoints, type NavDestination } from "@shared/navData";
import { toast } from "sonner";

const categories = [
  "All",
  "Academic",
  "Administration",
  "Facility",
  "Accommodation",
  "Transport",
  "Campus",
  "Faculty Cabins",
] as const;

function mapNavTypeToCategory(type: string): DestinationCategory {
  switch (type.toLowerCase()) {
    case "classroom":
    case "meeting":
      return "Academic";
    case "staff":
      return "Faculty Cabins";
    case "office":
      return "Administration";
    case "food":
    case "toilet":
    case "lift":
      return "Facility";
    default:
      return "Campus";
  }
}

// Build unified destination list: official PathFinderNavData architectural rooms + landmark destinations
const allDestinations: Destination[] = [
  ...landmarkDestinations,
  ...navDestinations.map((room) => ({
    id: room.id,
    nodeId: room.building === "Admin Block" ? "admin-office" : "cse-entry",
    name: room.name,
    category: mapNavTypeToCategory(room.type),
    buildingId: room.building === "Admin Block" ? "admin" : "cse",
    buildingName: room.building,
    floorName: room.floorName,
    description: `${room.name} located on ${room.floorName} of ${room.building}.`,
    roomNumber: room.id.split(":")[1] || undefined,
    accessibilityInfo: "Step-free corridor & lift access",
    isActive: true,
  })),
];

export default function Destinations() {
  const [, setLocation] = useLocation();
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [category, setCategory] = useState<string>("All");
  const [building, setBuilding] = useState<string>("All");
  const [selectedStartPoint, setSelectedStartPoint] = useState<string>("");
  const [nonAndroidNotice, setNonAndroidNotice] = useState<{ open: boolean; roomName: string; roomId: string } | null>(null);
  const [routeSelection, setRouteSelection] = useState(() => readRouteSelection(sessionStorage, window.location.search));
  const sourceNodeId = routeSelection.sourceNodeId;
  const [recent, setRecent] = useState<string[]>(["Central Library", "AI Lab", "Cafeteria", "Staff Room"]);
  const [tagFilter, setTagFilter] = useState("");
  const [annotationDrafts, setAnnotationDrafts] = useState<Record<number, { note: string; tags: string }>>({});
  const [shareAnnotationChoice, setShareAnnotationChoice] = useState<Record<number, boolean>>({});
  const [shareUrls, setShareUrls] = useState<Record<number, string>>({});
  const [copiedShareId, setCopiedShareId] = useState<number | null>(null);
  const { user } = useAuth();
  const utils = trpc.useUtils();
  const favoriteRoutes = trpc.favorites.list.useQuery(undefined, { enabled: Boolean(user) });
  const saveFavorite = trpc.favorites.save.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const removeFavorite = trpc.favorites.remove.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const updateFavorite = trpc.favorites.update.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const createShare = trpc.favorites.createShare.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });
  const revokeShare = trpc.favorites.revokeShare.useMutation({ onSuccess: () => utils.favorites.list.invalidate() });

  const availableBuildings = useMemo(() => {
    const names = Array.from(new Set(allDestinations.map((d) => d.buildingName))).filter(Boolean).sort();
    return ["All", ...names];
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedQuery(query), 200);
    return () => window.clearTimeout(timer);
  }, [query]);

  const filtered = useMemo(() => {
    const q = debouncedQuery.trim().toLowerCase();
    return allDestinations.filter((d) => {
      const matchesBuilding = building === "All" || d.buildingName === building;
      const matchesCategory = category === "All" || d.category === category;
      const matchesQuery =
        !q ||
        d.name.toLowerCase().includes(q) ||
        d.id.toLowerCase().includes(q) ||
        d.buildingName.toLowerCase().includes(q) ||
        d.floorName.toLowerCase().includes(q) ||
        d.description.toLowerCase().includes(q);
      return d.isActive && matchesBuilding && matchesCategory && matchesQuery;
    });
  }, [debouncedQuery, category, building]);

  const sourceNode = sourceNodeId === "current-location" ? { name: "Current Location", id: "current-location" } : getNode(sourceNodeId);

  const updateSource = (nextSourceNodeId: string) => {
    const next = writeRouteSelection({ sourceNodeId: nextSourceNodeId, destinationId: routeSelection.destinationId }, sessionStorage);
    setRouteSelection(next);
  };

  const navigate = (destinationId: string, name: string) => {
    setRecent((current) => [name, ...current.filter((item) => item !== name)].slice(0, 4));
    const next = writeRouteSelection({ sourceNodeId, destinationId }, sessionStorage);
    setRouteSelection(next);
    setLocation(`/map${routeSelectionSearch(next)}`);
  };

  const toggleFavorite = (destinationId: string, name: string) => {
    if (!user) { setLocation("/account"); return; }
    const existing = favoriteRoutes.data?.find((item) => item.sourceNodeExternalId === sourceNodeId && item.destinationExternalId === destinationId && !item.accessibleOnly);
    if (existing) { removeFavorite.mutate({ favoriteId: existing.id }); return; }
    saveFavorite.mutate({ label: `${sourceNode?.name ?? "Start"} → ${name}`, sourceNodeExternalId: sourceNodeId, destinationExternalId: destinationId, accessibleOnly: false });
  };

  const reuseFavorite = (favorite: NonNullable<typeof favoriteRoutes.data>[number]) => {
    const destination = allDestinations.find((item) => item.id === favorite.destinationExternalId);
    if (!destination) return;
    updateSource(favorite.sourceNodeExternalId);
    navigate(destination.id, destination.name);
  };

  const saveAnnotations = (favorite: NonNullable<typeof favoriteRoutes.data>[number]) => {
    const draft = annotationDrafts[favorite.id] ?? { note: favorite.note ?? "", tags: favorite.tags.join(", ") };
    updateFavorite.mutate({ favoriteId: favorite.id, note: draft.note.trim() || undefined, tags: draft.tags.split(",").map((tag) => tag.trim()).filter(Boolean) });
  };

  const createShareLink = (favorite: NonNullable<typeof favoriteRoutes.data>[number]) => {
    createShare.mutate({ favoriteId: favorite.id, includeAnnotations: Boolean(shareAnnotationChoice[favorite.id]) }, {
      onSuccess: ({ token }) => setShareUrls((current) => ({ ...current, [favorite.id]: `${window.location.origin}/shared/${token}` })),
    });
  };

  const copyShareLink = async (favoriteId: number) => {
    const url = shareUrls[favoriteId];
    if (!url) return;
    try {
      await navigator.clipboard.writeText(url);
      setCopiedShareId(favoriteId);
    } catch {
      window.prompt("Copy this secure route link:", url);
    }
  };

  const handleNavigateAR = (destinationId: string, destinationName: string) => {
    setRecent((current) => [destinationName, ...current.filter((item) => item !== destinationName)].slice(0, 4));
    if (isAndroidDevice()) {
      const intentUrl = buildArIntentUrl(destinationId, selectedStartPoint);
      window.location.href = intentUrl;
    } else {
      toast.info("AR navigation needs the PathFinder AR app on an Android phone with ARCore.", {
        description: `Destination: ${destinationName} (${destinationId})`,
        duration: 6000,
      });
      setNonAndroidNotice({
        open: true,
        roomName: destinationName,
        roomId: destinationId,
      });
    }
  };

  const visibleFavorites = favoriteRoutes.data?.filter((favorite) => !tagFilter.trim() || favorite.tags.some((tag) => tag.includes(tagFilter.trim().toLocaleLowerCase())));

  return (
    <div className="min-h-screen bg-slate-50">
      <TopNav />
      <main className="container py-10 sm:py-14">
        <div className="grid gap-8 lg:grid-cols-[.82fr_1.18fr]">
          <aside className="lg:sticky lg:top-24 lg:h-fit">
            <p className="text-sm font-bold uppercase tracking-[0.18em] text-amber-700">AIET route planner</p>
            <h1 className="mt-3 font-display text-4xl font-bold tracking-tight sm:text-5xl">Where are you starting from?</h1>
            <p className="mt-4 max-w-md text-lg leading-8 text-slate-600">
              Set your source first, then choose an AIET building, facility, department, laboratory, or room as your destination.
            </p>

            {/* Starting Point Selection (2D Map Route Source) */}
            <div className="mt-6 rounded-2xl border border-amber-200 bg-gradient-to-br from-amber-50 to-white p-5 shadow-sm">
              <label className="text-sm font-bold text-slate-800" htmlFor="source-select">Your source / starting point</label>
              <select
                id="source-select"
                aria-label="Starting point"
                value={sourceNodeId}
                onChange={(event) => updateSource(event.target.value)}
                className="mt-2 h-11 w-full rounded-lg border border-amber-200 bg-white px-3 text-sm font-semibold text-slate-800"
              >
                <option value="current-location">📍 Current Location</option>
                {routableSourceNodes().map((node) => (
                  <option key={node.id} value={node.id}>
                    {node.name} · {node.buildingName} · {node.floorName}
                  </option>
                ))}
              </select>
              <p className="mt-2 text-xs leading-5 text-slate-500">
                Current route source: <strong className="text-slate-700">{sourceNode?.name}</strong>. You can change it at any time.
              </p>
            </div>

            {/* Optional AR Start Point Selection */}
            <div className="mt-5 rounded-2xl border border-amber-200 bg-white p-5 shadow-sm">
              <label className="text-sm font-bold text-slate-800 flex items-center gap-1.5" htmlFor="ar-start-select">
                <Compass className="h-4 w-4 text-amber-600" />
                <span>AR Start Point (Optional)</span>
              </label>
              <p className="mt-1 text-xs leading-5 text-slate-500">
                Choose an initial calibration position to open the AR app ready to align.
              </p>
              <select
                id="ar-start-select"
                aria-label="Starting calibration point"
                value={selectedStartPoint}
                onChange={(event) => setSelectedStartPoint(event.target.value)}
                className="mt-2 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-xs font-semibold text-slate-800"
              >
                <option value="">None (Choose inside AR app)</option>
                {navStartPoints.map((sp) => (
                  <option key={sp.name} value={sp.name}>
                    {sp.name} ({sp.floor})
                  </option>
                ))}
              </select>
              {selectedStartPoint && (
                <p className="mt-2 text-xs text-amber-800 bg-amber-50 rounded-lg p-2 border border-amber-200">
                  Target start: <strong>{selectedStartPoint}</strong>
                </p>
              )}
            </div>

            {/* Destination Search & Filters */}
            <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <label className="text-sm font-semibold text-slate-800" htmlFor="destination-search">Search destinations</label>
              <div className="relative mt-2">
                <Search className="absolute left-3 top-3 h-4 w-4 text-slate-400" />
                <Input
                  id="destination-search"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="e.g., AI Lab, Library, Staff Room..."
                  className="h-10 pl-9"
                />
                {query && (
                  <button
                    onClick={() => setQuery("")}
                    className="absolute right-2 top-2 rounded p-1 text-slate-400 hover:bg-slate-100"
                    aria-label="Clear destination search"
                  >
                    <X className="h-4 w-4" />
                  </button>
                )}
              </div>

              <div className="mt-5">
                <p className="flex items-center gap-2 text-sm font-semibold">
                  <SlidersHorizontal className="h-4 w-4" /> Filter by category
                </p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {categories.map((item) => (
                    <button
                      key={item}
                      onClick={() => setCategory(item)}
                      className={`rounded-full px-3 py-1.5 text-xs font-semibold transition ${
                        category === item ? "bg-slate-950 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"
                      }`}
                    >
                      {item === "Faculty Cabins" ? "Staff Directory / Cabins" : item}
                    </button>
                  ))}
                </div>
              </div>

              <div className="mt-5">
                <label className="text-sm font-semibold" htmlFor="building-filter">Building</label>
                <select
                  id="building-filter"
                  value={building}
                  onChange={(event) => setBuilding(event.target.value)}
                  className="mt-2 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm"
                >
                  {availableBuildings.map((item) => (
                    <option key={item}>{item}</option>
                  ))}
                </select>
              </div>
            </div>

            <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-5">
              <p className="text-sm font-semibold text-slate-800">Recently searched</p>
              <div className="mt-3 flex flex-wrap gap-2">
                {recent.map((item) => (
                  <button
                    key={item}
                    onClick={() => setQuery(item)}
                    className="rounded-full border border-slate-200 px-3 py-1.5 text-xs font-medium text-slate-600 hover:border-amber-300 hover:bg-amber-50 hover:text-amber-800"
                  >
                    {item}
                  </button>
                ))}
              </div>
            </div>

            {/* Saved Routes Section */}
            <section className="mt-5 rounded-2xl border border-rose-100 bg-gradient-to-br from-rose-50 to-white p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-semibold text-slate-800">Saved routes</p>
                  <p className="mt-1 text-xs leading-5 text-slate-500">Organize private routes, or issue a revocable link and QR code for someone else.</p>
                </div>
                <Heart className="h-4 w-4 text-rose-600" />
              </div>
              {!user ? (
                <Button variant="outline" size="sm" className="mt-4 border-rose-200 text-rose-800 hover:bg-rose-100" onClick={() => setLocation("/account")}>
                  Sign in to save routes
                </Button>
              ) : (
                <div className="mt-4 space-y-3">
                  <Input
                    aria-label="Filter saved routes by tag"
                    value={tagFilter}
                    onChange={(event) => setTagFilter(event.target.value)}
                    placeholder="Filter by tag, e.g. class"
                    className="h-9 border-rose-200 bg-white text-xs"
                  />
                  {visibleFavorites?.map((favorite) => {
                    const draft = annotationDrafts[favorite.id] ?? { note: favorite.note ?? "", tags: favorite.tags.join(", ") };
                    const shareUrl = shareUrls[favorite.id];
                    return (
                      <div key={favorite.id} className="rounded-xl border border-rose-100 bg-white p-3">
                        <div className="flex items-start gap-2">
                          <button type="button" onClick={() => reuseFavorite(favorite)} className="min-w-0 flex-1 text-left">
                            <span className="block truncate text-sm font-semibold text-slate-800">{favorite.label}</span>
                            <span className="block text-xs text-slate-500">Open on live map</span>
                          </button>
                          <button
                            type="button"
                            aria-label={`Remove ${favorite.label}`}
                            onClick={() => removeFavorite.mutate({ favoriteId: favorite.id })}
                            className="rounded-lg p-2 text-rose-600 transition hover:bg-rose-50"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                        <div className="mt-3 space-y-2 border-t border-rose-50 pt-3">
                          <textarea
                            aria-label={`Note for ${favorite.label}`}
                            value={draft.note}
                            onChange={(event) => setAnnotationDrafts((current) => ({ ...current, [favorite.id]: { ...draft, note: event.target.value } }))}
                            maxLength={600}
                            rows={2}
                            placeholder="Private reminder, meeting point, or access note"
                            className="w-full resize-none rounded-lg border border-rose-100 px-2.5 py-2 text-xs leading-5 outline-none transition focus:border-rose-400"
                          />
                          <Input
                            aria-label={`Tags for ${favorite.label}`}
                            value={draft.tags}
                            onChange={(event) => setAnnotationDrafts((current) => ({ ...current, [favorite.id]: { ...draft, tags: event.target.value } }))}
                            maxLength={149}
                            placeholder="Tags, separated by commas"
                            className="h-8 border-rose-100 text-xs"
                          />
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex min-w-0 flex-wrap gap-1">
                              {favorite.tags.map((tag) => (
                                <span key={tag} className="rounded-full bg-rose-50 px-2 py-0.5 text-[10px] font-semibold text-rose-700">
                                  #{tag}
                                </span>
                              ))}
                            </div>
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              disabled={updateFavorite.isPending}
                              onClick={() => saveAnnotations(favorite)}
                              className="h-7 shrink-0 border-rose-200 px-2 text-xs text-rose-800 hover:bg-rose-50"
                            >
                              Save details
                            </Button>
                          </div>
                        </div>
                        <div className="mt-3 rounded-lg border border-sky-100 bg-sky-50/70 p-2.5">
                          <div className="flex items-center justify-between gap-2">
                            <span className="inline-flex items-center gap-1 text-xs font-semibold text-sky-900">
                              <Share2 className="h-3.5 w-3.5" />Share route
                            </span>
                            {favorite.activeShare && (
                              <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-emerald-700">
                                <ShieldCheck className="h-3.5 w-3.5" />Active
                              </span>
                            )}
                          </div>
                          <label className="mt-2 flex items-center gap-2 text-[11px] leading-4 text-sky-800">
                            <input
                              aria-label={`Include notes and tags for ${favorite.label}`}
                              type="checkbox"
                              checked={Boolean(shareAnnotationChoice[favorite.id])}
                              onChange={(event) => setShareAnnotationChoice((current) => ({ ...current, [favorite.id]: event.target.checked }))}
                              className="h-3.5 w-3.5 rounded border-sky-300"
                            />
                            Include my note and tags in this link
                          </label>
                          <div className="mt-2 flex flex-wrap gap-1.5">
                            <Button
                              type="button"
                              size="sm"
                              disabled={createShare.isPending}
                              onClick={() => createShareLink(favorite)}
                              className="h-7 bg-sky-700 px-2 text-xs hover:bg-sky-800"
                            >
                              {favorite.activeShare ? "Replace link" : "Generate link"}
                            </Button>
                            {favorite.activeShare && (
                              <Button
                                type="button"
                                size="sm"
                                variant="outline"
                                disabled={revokeShare.isPending}
                                onClick={() => {
                                  revokeShare.mutate({ favoriteId: favorite.id });
                                  setShareUrls((current) => {
                                    const next = { ...current };
                                    delete next[favorite.id];
                                    return next;
                                  });
                                }}
                                className="h-7 border-sky-200 px-2 text-xs text-sky-800 hover:bg-sky-100"
                              >
                                Revoke
                              </Button>
                            )}
                          </div>
                          {shareUrl && (
                            <div className="mt-3 grid gap-3 sm:grid-cols-[1fr_auto]">
                              <div className="min-w-0">
                                <p className="truncate rounded border border-sky-100 bg-white px-2 py-1.5 text-[10px] text-slate-600">{shareUrl}</p>
                                <div className="mt-2 flex gap-1.5">
                                  <Button
                                    type="button"
                                    size="sm"
                                    variant="outline"
                                    onClick={() => copyShareLink(favorite.id)}
                                    className="h-7 border-sky-200 px-2 text-xs text-sky-800 hover:bg-sky-100"
                                  >
                                    <Copy className="mr-1 h-3 w-3" />
                                    {copiedShareId === favorite.id ? "Copied" : "Copy link"}
                                  </Button>
                                  <a href={shareUrl} target="_blank" rel="noreferrer" className="inline-flex h-7 items-center rounded-md border border-sky-200 px-2 text-xs font-medium text-sky-800 hover:bg-sky-100">
                                    Open
                                  </a>
                                </div>
                              </div>
                              <div className="grid h-24 w-24 place-items-center rounded-lg bg-white p-2">
                                <QRCodeSVG value={shareUrl} size={80} level="M" title={`QR code for ${favorite.label}`} />
                              </div>
                            </div>
                          )}
                        </div>
                      </div>
                    );
                  })}
                  {Boolean(favoriteRoutes.data?.length) && !visibleFavorites?.length && (
                    <p className="rounded-xl border border-dashed border-rose-200 bg-white/70 p-3 text-xs leading-5 text-slate-600">
                      No saved route matches that tag.
                    </p>
                  )}
                  {!favoriteRoutes.data?.length && (
                    <p className="rounded-xl border border-dashed border-rose-200 bg-white/70 p-3 text-xs leading-5 text-slate-600">
                      Save a route from a destination card to find it here later.
                    </p>
                  )}
                </div>
              )}
            </section>
          </aside>

          {/* Main Section: Destination & Room Cards */}
          <section>
            <div className="flex items-end justify-between gap-4">
              <div>
                <p className="text-sm font-medium text-slate-500">Available destinations from {sourceNode?.name}</p>
                <h2 className="font-display text-2xl font-bold">{filtered.length} destinations found</h2>
              </div>
              <Badge variant="outline" className="hidden gap-1 border-amber-200 bg-amber-50 text-amber-800 sm:flex">
                <Sparkles className="h-3.5 w-3.5" />
                Navigate in AR supported
              </Badge>
            </div>

            {/* Non-Android Notice Banner (Requirement 2) */}
            {nonAndroidNotice && nonAndroidNotice.open && (
              <div className="mt-4 rounded-xl border border-amber-300 bg-amber-50 p-4 text-xs text-amber-950 shadow-sm flex items-start justify-between gap-3 animate-in fade-in">
                <div className="flex items-start gap-2.5">
                  <Info className="h-5 w-5 shrink-0 text-amber-700 mt-0.5" />
                  <div>
                    <p className="font-bold text-sm text-amber-900">
                      AR navigation needs the PathFinder AR app on an Android phone with ARCore.
                    </p>
                    <p className="mt-1 text-slate-700 leading-relaxed">
                      To navigate to <strong className="font-semibold text-slate-900">{nonAndroidNotice.roomName}</strong> in 3D augmented reality, open this link on your Android device or install the PathFinder AR application.
                    </p>
                    <p className="mt-2 text-[11px] text-slate-500">
                      Target room ID: <code className="font-mono bg-white px-1.5 py-0.5 rounded border border-amber-200 text-amber-900 font-semibold">{nonAndroidNotice.roomId}</code>
                    </p>
                  </div>
                </div>
                <button onClick={() => setNonAndroidNotice(null)} className="text-slate-400 hover:text-slate-700">
                  <X className="h-4 w-4" />
                </button>
              </div>
            )}

            <div className="mt-5 grid gap-3">
              {filtered.map((destination) => {
                let preview;
                try {
                  const routingSource = sourceNodeId === "current-location" ? "gate" : sourceNodeId;
                  preview = calculateRoute(campusNodes, campusEdges, routingSource, destination.nodeId);
                } catch {
                  preview = undefined;
                }
                const isSaved = Boolean(
                  favoriteRoutes.data?.find(
                    (item) => item.sourceNodeExternalId === sourceNodeId && item.destinationExternalId === destination.id && !item.accessibleOnly
                  )
                );

                return (
                  <article
                    key={destination.id}
                    className="group rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:border-amber-300 hover:shadow-lg hover:shadow-amber-900/5"
                  >
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                      <div className="flex gap-4">
                        {destination.imageUrl ? (
                          <div className="relative h-20 w-24 shrink-0 overflow-hidden rounded-xl border border-slate-200/80 bg-slate-100 shadow-sm sm:h-24 sm:w-32">
                            <img
                              src={destination.imageUrl}
                              alt={destination.name}
                              loading="lazy"
                              className="h-full w-full object-cover transition duration-300 group-hover:scale-105"
                              onError={(e) => {
                                (e.currentTarget as HTMLElement).style.display = "none";
                              }}
                            />
                            {destination.roomNumber && (
                              <span className="absolute bottom-1 right-1 rounded bg-black/60 px-1.5 py-0.5 text-[9px] font-bold text-white backdrop-blur-sm">
                                {destination.roomNumber}
                              </span>
                            )}
                          </div>
                        ) : (
                          <div className="grid h-16 w-16 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-amber-100 to-amber-200 text-amber-900 sm:h-20 sm:w-20">
                            <Building2 className="h-6 w-6" />
                          </div>
                        )}
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <h3 className="font-display text-xl font-bold">{destination.name}</h3>
                            <Badge className="bg-slate-100 text-slate-700 hover:bg-slate-100">
                              {destination.category}
                            </Badge>
                            <span className="font-mono text-[11px] text-slate-400">
                              {destination.id}
                            </span>
                          </div>
                          <p className="mt-1 text-sm text-slate-600">{destination.description}</p>
                          <div className="mt-3 rounded-xl bg-slate-50 px-3 py-2 text-xs font-medium text-slate-600">
                            <span className="font-bold text-amber-600">S</span> {sourceNode?.name}{" "}
                            <span className="mx-1 text-slate-400">→</span>
                            <span className="font-bold text-cyan-700">D</span> {destination.name}
                          </div>
                          <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs font-medium text-slate-500">
                            <span className="inline-flex items-center gap-1">
                              <MapPin className="h-3.5 w-3.5" />
                              {destination.buildingName} · {destination.floorName}
                            </span>
                            <span className="inline-flex items-center gap-1">
                              <Route className="h-3.5 w-3.5" />
                              {preview ? `${preview.totalDistance} m` : "Distance estimated in AR"}
                            </span>
                            <span className="inline-flex items-center gap-1">
                              <Clock3 className="h-3.5 w-3.5" />
                              {preview ? `${Math.max(1, Math.ceil(preview.estimatedTime / 60))} min walk` : "Live walk guide"}
                            </span>
                          </div>
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center shrink-0 gap-2">
                        {/* Navigate in AR Button (Requirement 2) */}
                        <Button
                          onClick={() => handleNavigateAR(destination.id, destination.name)}
                          className="bg-gradient-to-r from-amber-600 to-amber-700 text-white font-bold hover:from-amber-700 hover:to-amber-800 shadow-sm flex items-center gap-1.5"
                        >
                          <Sparkles className="h-4 w-4 text-amber-200" />
                          <span>Navigate in AR</span>
                        </Button>

                        <Button
                          variant="outline"
                          size="icon"
                          aria-label={`${isSaved ? "Remove" : "Save"} route to ${destination.name}`}
                          onClick={() => toggleFavorite(destination.id, destination.name)}
                          className={isSaved ? "border-rose-300 bg-rose-50 text-rose-700 hover:bg-rose-100" : "border-slate-200"}
                        >
                          <Heart className={`h-4 w-4 ${isSaved ? "fill-current" : ""}`} />
                        </Button>

                        <Button
                          onClick={() => navigate(destination.id, destination.name)}
                          variant="outline"
                          className="border-slate-300 font-semibold hover:bg-slate-100"
                        >
                          View route <ChevronRight className="ml-1 h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  </article>
                );
              })}

              {!filtered.length && (
                <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center">
                  <Search className="mx-auto h-8 w-8 text-slate-400" />
                  <h3 className="mt-3 font-display text-xl font-bold">No matching destination</h3>
                  <p className="mt-1 text-sm text-slate-600">Try another name, category, or building filter.</p>
                </div>
              )}
            </div>
          </section>
        </div>
      </main>
    </div>
  );
}
