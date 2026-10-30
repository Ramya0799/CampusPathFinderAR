import React, { useEffect, useRef } from "react";
import type { RouteManeuver, ManeuverType } from "@shared/maneuvers";
import type { MapPosition } from "./CampusMap";
import {
  ArrowUp,
  ArrowUpRight,
  ArrowUpLeft,
  CornerUpRight,
  CornerUpLeft,
  RotateCcw,
  Footprints,
  Layers,
  ChevronUp,
  ChevronDown,
  GitFork,
  CircleDot,
  MapPin,
  Check,
} from "lucide-react";

export type StepByStepGuidancePanelProps = {
  maneuvers: RouteManeuver[];
  activeStepIndex: number;
  position?: MapPosition & { speed?: number; accuracy?: number; timestamp?: number };
  isNavigating: boolean;
  onAdvanceStep?: () => void;
  className?: string;
};

function getManeuverIcon(maneuver: ManeuverType, instruction: string = "") {
  const lower = instruction.toLowerCase();
  if (lower.includes("circle") || lower.includes("roundabout")) {
    return <CircleDot className="h-3.5 w-3.5 text-blue-600" />;
  }
  if (lower.includes("fork") || lower.includes("branch")) {
    return <GitFork className="h-3.5 w-3.5 text-blue-600" />;
  }

  switch (maneuver) {
    case "straight":
      return <ArrowUp className="h-3.5 w-3.5 text-blue-600" />;
    case "slight-right":
      return <ArrowUpRight className="h-3.5 w-3.5 text-blue-600" />;
    case "turn-right":
      return <CornerUpRight className="h-3.5 w-3.5 text-blue-600" />;
    case "slight-left":
      return <ArrowUpLeft className="h-3.5 w-3.5 text-blue-600" />;
    case "turn-left":
      return <CornerUpLeft className="h-3.5 w-3.5 text-blue-600" />;
    case "u-turn":
      return <RotateCcw className="h-3.5 w-3.5 text-blue-600" />;
    case "staircase":
      return <Footprints className="h-3.5 w-3.5 text-blue-600" />;
    case "floor-change":
      return <Layers className="h-3.5 w-3.5 text-blue-600" />;
    case "arrive":
      return <MapPin className="h-3.5 w-3.5 text-emerald-600" />;
    case "depart":
      return <div className="h-2 w-2 rounded-full bg-blue-600" />;
    default:
      return <ArrowUp className="h-3.5 w-3.5 text-blue-600" />;
  }
}

export function StepByStepGuidancePanel({
  maneuvers,
  activeStepIndex,
  className = "",
}: StepByStepGuidancePanelProps) {
  const activeItemRef = useRef<HTMLDivElement | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement | null>(null);

  // Auto-scroll active maneuver into view smoothly
  useEffect(() => {
    if (typeof activeItemRef.current?.scrollIntoView === "function") {
      activeItemRef.current.scrollIntoView({
        behavior: "smooth",
        block: "nearest",
      });
    }
  }, [activeStepIndex]);

  const scrollUp = () => {
    scrollContainerRef.current?.scrollBy({ top: -100, behavior: "smooth" });
  };

  const scrollDown = () => {
    scrollContainerRef.current?.scrollBy({ top: 100, behavior: "smooth" });
  };

  return (
    <div
      className={`relative flex flex-col rounded-2xl border border-slate-200/90 bg-white/95 p-3 shadow-xl backdrop-blur-md ${className}`}
    >
      {/* Scroll up button if long */}
      {maneuvers.length > 4 && (
        <button
          onClick={scrollUp}
          className="mx-auto -mt-1 py-0.5 text-slate-400 hover:text-slate-600 transition"
          aria-label="Scroll maneuvers up"
        >
          <ChevronUp className="h-4 w-4" />
        </button>
      )}

      {/* Maneuvers Scrollable List */}
      <div
        ref={scrollContainerRef}
        className="max-h-[42vh] sm:max-h-[48vh] min-h-[140px] overflow-y-auto space-y-2 pr-1.5 scroll-smooth"
      >
        {maneuvers.length === 0 ? (
          <div className="py-6 text-center text-xs font-semibold text-slate-400">
            Select a destination to view turn guidance.
          </div>
        ) : (
          maneuvers.map((maneuver, index) => {
            const isActive = index === activeStepIndex;
            const isCompleted = index < activeStepIndex;

            return (
              <div
                key={`${maneuver.nodeId}-${index}`}
                ref={isActive ? activeItemRef : null}
                className={`group flex items-start gap-3 rounded-xl p-2 transition-all duration-200 ${
                  isActive
                    ? "bg-blue-50/90 ring-1 ring-blue-500/30 shadow-sm"
                    : isCompleted
                    ? "opacity-55 hover:opacity-85"
                    : "hover:bg-slate-50/90"
                }`}
              >
                {/* Direction Icon in Blue Tinted Rounded Square */}
                <div
                  className={`grid h-7 w-7 shrink-0 place-items-center rounded-lg shadow-xs transition-colors ${
                    isActive
                      ? "bg-blue-600 text-white"
                      : isCompleted
                      ? "bg-emerald-50 text-emerald-600"
                      : "bg-blue-50 text-blue-600"
                  }`}
                >
                  {isCompleted ? (
                    <Check className="h-3.5 w-3.5 text-emerald-600 stroke-[2.5]" />
                  ) : isActive ? (
                    <span className="text-white brightness-200 contrast-200">
                      {getManeuverIcon(maneuver.maneuver, maneuver.instruction)}
                    </span>
                  ) : (
                    getManeuverIcon(maneuver.maneuver, maneuver.instruction)
                  )}
                </div>

                {/* Instruction & Distance */}
                <div className="min-w-0 flex-1">
                  <p
                    className={`text-xs sm:text-[13px] font-bold leading-tight ${
                      isActive
                        ? "text-blue-950 font-bold"
                        : isCompleted
                        ? "text-slate-500"
                        : "text-slate-800"
                    }`}
                  >
                    {maneuver.instruction}
                  </p>
                  <p className="mt-1 text-[11px] font-medium text-slate-400">
                    {maneuver.distanceMeters > 0
                      ? maneuver.distanceMeters >= 1000
                        ? `${(maneuver.distanceMeters / 1000).toFixed(1)} km`
                        : `${maneuver.distanceMeters} m`
                      : "Destination reached"}
                  </p>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Scroll down button if long */}
      {maneuvers.length > 4 && (
        <button
          onClick={scrollDown}
          className="mx-auto -mb-1 py-0.5 text-slate-400 hover:text-slate-600 transition"
          aria-label="Scroll maneuvers down"
        >
          <ChevronDown className="h-4 w-4" />
        </button>
      )}
    </div>
  );
}
