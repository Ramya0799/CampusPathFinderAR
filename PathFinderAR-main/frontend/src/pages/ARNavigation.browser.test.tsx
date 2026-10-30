import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ARNavigation from "./ARNavigation";

const getUserMediaSpy = vi.hoisted(() => vi.fn());
const clearWatchSpy = vi.hoisted(() => vi.fn());
const watchPositionSpy = vi.hoisted(() => vi.fn());
const getCurrentPositionSpy = vi.hoisted(() => vi.fn());

vi.mock("@/components/TopNav", () => ({ TopNav: () => <div data-testid="top-nav" /> }));
vi.mock("wouter", async () => {
  const actual = await vi.importActual<typeof import("wouter")>("wouter");
  return { ...actual, useLocation: () => ["/ar", vi.fn()] };
});

function createStream() {
  const stop = vi.fn();
  return { getTracks: () => [{ stop }] } as unknown as MediaStream;
}

beforeEach(() => {
  sessionStorage.clear();
  sessionStorage.setItem("aiet-source", "gate");
  sessionStorage.setItem("pathfinder-destination", "d10");
  getUserMediaSpy.mockResolvedValue(createStream());
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia: getUserMediaSpy } });
  Object.defineProperty(navigator, "geolocation", { configurable: true, value: { getCurrentPosition: getCurrentPositionSpy, watchPosition: watchPositionSpy.mockReturnValue(31), clearWatch: clearWatchSpy } });
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(undefined);
  Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  getUserMediaSpy.mockReset();
  getCurrentPositionSpy.mockReset();
  watchPositionSpy.mockReset();
  clearWatchSpy.mockReset();
  sessionStorage.clear();
});

describe("AR navigation", () => {
  it("uses the selected route destination and starts live camera/location guidance", async () => {
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    render(<ARNavigation />);

    expect(screen.getByRole("heading", { name: /Follow your route to Central Library|Starting 3D AR camera guidance/i })).toBeTruthy();

    await waitFor(() => expect(getUserMediaSpy).toHaveBeenCalled());
    expect(watchPositionSpy).toHaveBeenCalled();
    expect(screen.getByText("AR route · Central Library")).toBeTruthy();
    expect(screen.getByText(/Next instruction/i)).toBeTruthy();
    expect(screen.getByText("heading unavailable")).toBeTruthy();
    expect(screen.queryByText("38 m")).toBeNull();
  });

  it("shows a permission-denied camera error and retains the map fallback", async () => {
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    getUserMediaSpy.mockRejectedValueOnce(new DOMException("denied", "NotAllowedError"));
    render(<ARNavigation />);

    expect((await screen.findByRole("alert")).textContent).toMatch(/Camera permission was denied/i);
    expect(screen.getByRole("link", { name: /Open map navigation/i }).getAttribute("href")).toBe("/map");
  });

  it("handles denied location permission without fabricating a position", async () => {
    const user = userEvent.setup();
    getCurrentPositionSpy.mockImplementation((_success: PositionCallback, error: PositionErrorCallback) => error?.({ code: 1, message: "denied", PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 } as GeolocationPositionError));
    render(<ARNavigation />);

    await user.click(screen.getByRole("button", { name: /Enable live location/i }));
    expect(await screen.findByText(/Location permission was denied/i)).toBeTruthy();
    expect(screen.getByText(/No live position shared yet/i)).toBeTruthy();
  });

  it("stops camera tracks and GPS watch when returning to the map", async () => {
    const user = userEvent.setup();
    const stream = createStream();
    getUserMediaSpy.mockResolvedValueOnce(stream);
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    render(<ARNavigation />);
    await waitFor(() => expect(screen.getByText("AR route · Central Library")).toBeTruthy());

    await user.click(screen.getByRole("link", { name: "Return to map" }));
    expect(clearWatchSpy).toHaveBeenCalledWith(31);
    expect((stream.getTracks()[0] as MediaStreamTrack).stop).toHaveBeenCalled();
  });

  it("updates the route overlay from a real GPS fix", async () => {
    let success: PositionCallback | undefined;
    watchPositionSpy.mockImplementation((callback: PositionCallback) => { success = callback; return 31; });
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    render(<ARNavigation />);

    await act(async () => success?.({ coords: { latitude: 13.0219, longitude: 74.98128, accuracy: 5, altitude: null, altitudeAccuracy: null, heading: null, speed: null, toJSON: () => ({}) }, timestamp: Date.now(), toJSON: () => ({}) } as GeolocationPosition));
    expect(await screen.findByText(/GPS connected · ±5 m/i)).toBeTruthy();
  });

  it("toggles camera facing mode and flips the camera horizontally", async () => {
    const user = userEvent.setup();
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    render(<ARNavigation />);
    await waitFor(() => expect(screen.getByText("AR route · Central Library")).toBeTruthy());

    const video = screen.getByLabelText(/Live rear camera preview/i) as HTMLVideoElement;
    expect(video.style.transform).toBe("none");

    // Toggle mirror horizontally
    const flipButton = screen.getByRole("button", { name: /Flip camera horizontally/i });
    await user.click(flipButton);
    expect(video.style.transform).toBe("scaleX(-1)");

    // Toggle switch camera to front
    const switchButton = screen.getByRole("button", { name: /Switch camera/i });
    await user.click(switchButton);
    await waitFor(() => {
      expect(getUserMediaSpy).toHaveBeenCalledWith({
        video: { facingMode: { ideal: "user" } },
        audio: false,
      });
    });
  });

  it("uses GPS-course heading fallback when moving without compass", async () => {
    let success: PositionCallback | undefined;
    watchPositionSpy.mockImplementation((callback: PositionCallback) => { success = callback; return 31; });
    sessionStorage.setItem("pathfinder-ar-autostart", "true");
    render(<ARNavigation />);

    // Simulate GPS fix with native course heading
    await act(async () => success?.({
      coords: { latitude: 13.0219, longitude: 74.98128, accuracy: 5, altitude: null, altitudeAccuracy: null, heading: 85, speed: 1.4, toJSON: () => ({}) },
      timestamp: Date.now(),
      toJSON: () => ({}),
    } as GeolocationPosition));

    expect(await screen.findByText(/GPS course 85°/i)).toBeTruthy();
    expect(screen.getByText("Using GPS direction")).toBeTruthy();
  });

  it("shows 'This place isn\'t in the AR model yet - choose the nearest room in the app.' for destinations without NavData ID on non-Android", async () => {
    sessionStorage.setItem("pathfinder-destination", "d10"); // Central Library
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    Object.defineProperty(navigator, "userAgent", { configurable: true, value: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" });
    render(<ARNavigation />);

    expect(screen.getByText(/This place isn't in the AR model yet - choose the nearest room in the app./i)).toBeTruthy();
    expect(screen.getByRole("link", { name: /Open map navigation/i })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Enable camera & location/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Open in PathFinder AR/i })).toBeNull();
  });

  it("shows 'Open in PathFinder AR' without dest on Android for destinations without NavData ID", async () => {
    const user = userEvent.setup();
    sessionStorage.setItem("pathfinder-destination", "d10"); // Central Library
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    Object.defineProperty(navigator, "userAgent", { configurable: true, value: "Mozilla/5.0 (Linux; Android 14; Pixel 8)" });

    delete (window as any).location;
    window.location = { href: "", origin: "http://localhost:3000", protocol: "http:" } as any;

    render(<ARNavigation />);

    expect(screen.getByText(/This place isn't in the AR model yet - choose the nearest room in the app./i)).toBeTruthy();
    const openArBtn = screen.getByRole("button", { name: /Open in PathFinder AR/i });
    expect(openArBtn).toBeTruthy();

    await user.click(openArBtn);
    expect(window.location.href).toBe("intent://navigate#Intent;scheme=pathfinder;package=com.pathfinder.campusnavigation;end");
  });

  it("shows message on non-Android when clicking 'Open in PathFinder AR'", async () => {
    const user = userEvent.setup();
    sessionStorage.setItem("pathfinder-destination", "TB_LG03:Classroom 1");
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    Object.defineProperty(navigator, "userAgent", { configurable: true, value: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" });

    delete (window as any).location;
    window.location = { href: "", origin: "http://localhost:3000", protocol: "http:" } as any;

    render(<ARNavigation />);

    expect(screen.queryByText(/AR not available for this place yet/i)).toBeNull();
    const openArBtn = screen.getByRole("button", { name: /Open in PathFinder AR/i });
    expect(openArBtn).toBeTruthy();

    await user.click(openArBtn);
    expect(window.location.href).toBe("");
    expect((await screen.findByRole("alert")).textContent).toMatch(/AR navigation needs the PathFinder AR app on an Android phone with ARCore/i);
  });

  it("launches intent URL on Android when clicking 'Open in PathFinder AR'", async () => {
    const user = userEvent.setup();
    sessionStorage.setItem("pathfinder-destination", "TB_LG03:Classroom 1");
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    Object.defineProperty(navigator, "userAgent", { configurable: true, value: "Mozilla/5.0 (Linux; Android 14; Pixel 8)" });

    delete (window as any).location;
    window.location = { href: "", origin: "http://localhost:3000", protocol: "http:" } as any;

    render(<ARNavigation />);

    const openArBtn = screen.getByRole("button", { name: /Open in PathFinder AR/i });
    await user.click(openArBtn);
    expect(window.location.href).toContain("intent://navigate?dest=TB_LG03%3AClassroom%201");
    expect(window.location.href).toContain("package=com.pathfinder.campusnavigation");
  });

  it("shows 'Enable camera & location' when isSecureContext is true and hides it when false", async () => {
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    const { unmount } = render(<ARNavigation />);
    expect(screen.getByRole("button", { name: /Enable camera & location/i })).toBeTruthy();
    unmount();

    Object.defineProperty(window, "isSecureContext", { configurable: true, value: false });
    render(<ARNavigation />);
    expect(screen.queryByRole("button", { name: /Enable camera & location/i })).toBeNull();
  });
});
