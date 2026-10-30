import navDataJson from "./PathFinderNavData.json";

export interface NavDestination {
  id: string;
  name: string;
  type: string;
  floor: string;
  building: string;
  floorName: string;
  x: number;
  y: number;
  rl: number;
  approximate?: boolean;
}

export interface NavStartPoint {
  name: string;
  description: string;
  floor: string;
  x: number;
  y: number;
  rl: number;
  fx?: number;
  fy?: number;
  approximate?: boolean;
}

export interface NavFloor {
  id: string;
  name: string;
  shortName: string;
  building: string;
  rl: number;
}

export const navData = navDataJson as {
  version: number;
  floors: NavFloor[];
  destinations: NavDestination[];
  links: any[];
  startPoints: NavStartPoint[];
};

export const navDestinations = navData.destinations;
export const navStartPoints = navData.startPoints;
export const navFloors = navData.floors;

export const ANDROID_PACKAGE_NAME = "com.pathfinder.campusnavigation";

export function buildArIntentUrl(destId: string, startName?: string, fallbackUrl?: string): string {
  const encodedDest = encodeURIComponent(destId);
  const startParam = startName ? `&start=${encodeURIComponent(startName)}` : "";
  const fallback = encodeURIComponent(
    fallbackUrl || (typeof window !== "undefined" ? `${window.location.origin}/install-ar` : "http://localhost:3000/install-ar")
  );
  return `intent://navigate?dest=${encodedDest}${startParam}#Intent;scheme=pathfinder;package=${ANDROID_PACKAGE_NAME};S.browser_fallback_url=${fallback};end`;
}

export function isAndroidDevice(): boolean {
  if (typeof navigator === "undefined") return false;
  return /android/i.test(navigator.userAgent);
}
