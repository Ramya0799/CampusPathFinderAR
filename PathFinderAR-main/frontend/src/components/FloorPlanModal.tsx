import React, { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Compass,
  Layers,
  MapPin,
  Maximize2,
  Minimize2,
  Navigation,
  Sparkles,
  X,
  Building2,
  BookOpen,
  Cpu,
  Eye,
  CheckCircle2,
} from "lucide-react";

export type FloorPlanType = "cse" | "library";

interface FloorPlanModalProps {
  isOpen: boolean;
  onClose: () => void;
  defaultPlan?: FloorPlanType;
  highlightRoom?: string;
  onSelectDestination?: (nodeId: string, destId: string) => void;
}

const FLOOR_PLANS = [
  {
    id: "cse" as FloorPlanType,
    name: "CSE Academic Block",
    subName: "Ground Floor & ML / Web Tech Lab",
    building: "Computing & Innovation Block",
    imageUrl: "/Images/CSE Academic Block Floor Plan.png",
    areaSqm: "155.55 Sqm (ML Lab) • 1,200 Sqm (Wing)",
    dimensions: "18.52 m × 9.15 m",
    highlights: [
      { name: "Machine Learning & Web Tech Lab", code: "C-108", size: "155.55 Sqm", desc: "Dual-column workstation layout, projector screen, whiteboard", nodeId: "cse-lab", destId: "d6" },
      { name: "Server Room", code: "SR-1", size: "18.5 Sqm", desc: "Rack infrastructure, door D2, high-security server cluster", nodeId: "cse-lab", destId: "d6" },
      { name: "HOD Entrance & Faculty Chamber", code: "C-102", size: "48 Sqm", desc: "Department leadership suite & faculty reception", nodeId: "cs-dept", destId: "d5" },
      { name: "Central Atrium & Fountain Courtyard", code: "ATRIUM", size: "320 Sqm", desc: "Circulation core connecting East & West wings to Alva's Fountain", nodeId: "square", destId: "d17" },
    ],
    arStatus: "Indoor Spatial Mesh Synced • Step-by-Step Waypoint Guidance Active",
  },
  {
    id: "library" as FloorPlanType,
    name: "Library & Info Centre",
    subName: "Ground & First Floor Layouts",
    building: "Central Learning Block",
    imageUrl: "/Images/Library Floor Plan.png",
    areaSqm: "450 Sqm per Floor • 4-Storey Elevation",
    dimensions: "36.0 m × 12.5 m",
    highlights: [
      { name: "Main Reading & Lending Section", code: "L-102", size: "280 Sqm", desc: "Ground floor open reading hall, circulation desk, lending stacks", nodeId: "library", destId: "d10" },
      { name: "Book Stacks & Rear Entry", code: "L-105", size: "95 Sqm", desc: "Reference book stacks, rear corridor access, librarian desk", nodeId: "library", destId: "d10" },
      { name: "Digital Library & Periodicals", code: "L-201", size: "160 Sqm", desc: "First floor e-learning terminals, journal archives, periodicals", nodeId: "seminar", destId: "d11" },
      { name: "Curved Reference & AV Study", code: "L-205", size: "120 Sqm", desc: "Modern acoustic curved partition wall with quiet research cubicles", nodeId: "seminar", destId: "d11" },
    ],
    arStatus: "Multi-Floor Stairway Anchored • Heading Lock: Library Portal Axis",
  },
];

export function FloorPlanModal({
  isOpen,
  onClose,
  defaultPlan = "cse",
  highlightRoom,
  onSelectDestination,
}: FloorPlanModalProps) {
  const [selectedPlanId, setSelectedPlanId] = useState<FloorPlanType>(defaultPlan);
  const [isZoomed, setIsZoomed] = useState(false);

  const activePlan = FLOOR_PLANS.find((p) => p.id === selectedPlanId) ?? FLOOR_PLANS[0];

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-4xl max-h-[92vh] overflow-y-auto rounded-3xl border border-cyan-500/30 bg-slate-950/95 p-4 sm:p-6 text-white shadow-2xl backdrop-blur-xl">
        <DialogHeader className="border-b border-white/10 pb-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2">
                <Badge className="border border-cyan-400/40 bg-cyan-500/20 text-cyan-300">
                  <Sparkles className="mr-1 h-3 w-3" />
                  Official AIET Architectural Blueprint
                </Badge>
                <Badge variant="outline" className="border-white/20 text-slate-300">
                  <Building2 className="mr-1 h-3 w-3 text-cyan-400" />
                  {activePlan.building}
                </Badge>
              </div>
              <DialogTitle className="mt-2 text-2xl font-bold font-display text-white">
                {activePlan.name} • {activePlan.subName}
              </DialogTitle>
              <DialogDescription className="text-xs text-slate-400 mt-1">
                Authentic architectural floor plan provided by Alva's Institute of Engineering & Technology (AIET). Connected to live AR spatial positioning and turn-by-turn guidance.
              </DialogDescription>
            </div>

            {/* Plan Switcher Tabs */}
            <div className="flex items-center rounded-2xl border border-white/10 bg-slate-900/80 p-1">
              {FLOOR_PLANS.map((plan) => (
                <button
                  key={plan.id}
                  onClick={() => setSelectedPlanId(plan.id)}
                  className={`rounded-xl px-3 py-1.5 text-xs font-semibold transition ${
                    selectedPlanId === plan.id
                      ? "bg-cyan-500 text-slate-950 shadow-md shadow-cyan-500/30"
                      : "text-slate-400 hover:text-white"
                  }`}
                >
                  {plan.id === "cse" ? (
                    <span className="flex items-center gap-1.5">
                      <Cpu className="h-3.5 w-3.5" />
                      CSE Block
                    </span>
                  ) : (
                    <span className="flex items-center gap-1.5">
                      <BookOpen className="h-3.5 w-3.5" />
                      Library
                    </span>
                  )}
                </button>
              ))}
            </div>
          </div>
        </DialogHeader>

        {/* Blueprint Visual Display Container */}
        <div className="relative mt-4 overflow-hidden rounded-2xl border border-white/15 bg-slate-900 shadow-inner group">
          <div className={`transition-all duration-300 ${isZoomed ? "max-h-[70vh] overflow-auto cursor-zoom-out" : "max-h-[460px] overflow-hidden"}`}>
            <img
              src={activePlan.imageUrl}
              alt={activePlan.name}
              className={`w-full object-contain transition-transform duration-300 ${isZoomed ? "scale-125" : "scale-100 group-hover:scale-[1.02]"}`}
            />
          </div>

          {/* AR Sync Overlay Banner */}
          <div className="absolute top-3 left-3 right-3 flex flex-wrap items-center justify-between gap-2 pointer-events-none">
            <div className="pointer-events-auto flex items-center gap-2 rounded-full border border-cyan-400/40 bg-slate-950/85 px-3 py-1 text-[11px] font-bold text-cyan-300 shadow-lg backdrop-blur">
              <span className="h-2 w-2 rounded-full bg-cyan-400 animate-pulse" />
              <span>{activePlan.arStatus}</span>
            </div>

            <button
              onClick={() => setIsZoomed((z) => !z)}
              className="pointer-events-auto rounded-full border border-white/20 bg-slate-950/80 p-2 text-white hover:bg-slate-900 active:scale-95 transition shadow-lg"
              title={isZoomed ? "Zoom out" : "Zoom in"}
            >
              {isZoomed ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
            </button>
          </div>

          <div className="absolute bottom-3 left-3 right-3 flex items-center justify-between rounded-xl border border-white/10 bg-slate-950/90 px-3 py-2 text-xs text-slate-300 backdrop-blur">
            <span className="font-mono text-cyan-300 font-semibold">{activePlan.dimensions}</span>
            <span className="text-slate-400">{activePlan.areaSqm}</span>
          </div>
        </div>

        {/* Architectural Room Highlights & AR Navigation Direct Launch */}
        <div className="mt-5">
          <h3 className="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-1.5">
            <Layers className="h-4 w-4" />
            Architectural Zones & AR Destination Nodes
          </h3>
          <div className="mt-3 grid grid-cols-1 sm:grid-cols-2 gap-3">
            {activePlan.highlights.map((room) => {
              const isMatch = highlightRoom && room.name.toLowerCase().includes(highlightRoom.toLowerCase());
              return (
                <div
                  key={room.name}
                  className={`flex flex-col justify-between rounded-2xl border p-3.5 transition ${
                    isMatch
                      ? "border-cyan-400 bg-cyan-500/15 ring-2 ring-cyan-400/30"
                      : "border-white/10 bg-slate-900/60 hover:border-cyan-500/30 hover:bg-slate-900"
                  }`}
                >
                  <div>
                    <div className="flex items-center justify-between gap-2">
                      <span className="rounded-lg bg-cyan-500/20 px-2 py-0.5 font-mono text-[11px] font-bold text-cyan-300 border border-cyan-400/30">
                        {room.code}
                      </span>
                      <span className="text-[11px] text-slate-400 font-medium">{room.size}</span>
                    </div>
                    <h4 className="mt-2 text-sm font-bold text-white">{room.name}</h4>
                    <p className="mt-1 text-xs text-slate-300 leading-relaxed">{room.desc}</p>
                  </div>

                  <div className="mt-3 pt-2.5 border-t border-white/10 flex items-center justify-between">
                    <span className="text-[10px] text-slate-400 flex items-center gap-1">
                      <CheckCircle2 className="h-3 w-3 text-cyan-400" />
                      AR Mesh Aligned
                    </span>
                    {onSelectDestination && (
                      <Button
                        size="sm"
                        onClick={() => {
                          onSelectDestination(room.nodeId, room.destId);
                          onClose();
                        }}
                        className="h-7 text-xs bg-cyan-500 text-slate-950 hover:bg-cyan-400 font-bold px-2.5 rounded-lg"
                      >
                        <Navigation className="mr-1 h-3 w-3" />
                        Navigate Here
                      </Button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Footer info banner */}
        <div className="mt-5 rounded-2xl border border-white/10 bg-slate-900/40 p-3.5 text-xs text-slate-400 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Compass className="h-4 w-4 text-cyan-400 shrink-0" />
            <span>
              Matches physical Alva's Education Foundation engineering blueprint boards displayed in AIET Academic & Library blocks.
            </span>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={onClose}
            className="text-slate-300 hover:text-white"
          >
            Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
