# PathFinder: Smart Campus Navigation & AR System

PathFinder is an end-to-end smart campus navigation system designed for Alva's Institute of Engineering & Technology (AIET), Mijar, Moodbidri. It combines an interactive, multi-language campus web portal with a native Android augmented reality (AR) app powered by Unity 6, ARCore, and NavMesh navigation.

---

## Architecture Overview

```
PathFinder/
├── PathFinderAR-main/          # Web Application & API Server
│   ├── frontend/               # React 19 + TypeScript + Vite + Tailwind CSS
│   │   ├── src/pages/          # Map, Destinations, AR handoff, AI Assistant
│   │   └── src/features/       # Turn guidance, voice assistant, Three.js scenes
│   ├── backend/                # Express + tRPC + SQLite / Drizzle ORM
│   ├── shared/                 # Route calculation, graph definitions, navData
│   └── scripts/                # NavData synchronization utilities
│
├── PathfinderAR/               # Unity 6 Native Android AR Application
│   ├── Assets/
│   │   ├── Models/             # 3D campus & block FBX models
│   │   ├── Scripts/PathFinder/ # PFCampusNavigator, PFVoice, NavMesh routing
│   │   ├── Resources/          # Campus navigation anchors & data JSON
│   │   └── Plugins/Android/    # AndroidManifest (GameActivity & ARCore required)
│   └── ProjectSettings/        # OpenXR, AR Foundation, and Android settings
│
├── ArchitecturalAssets/        # Architectural floor plans and elevation drawings
└── package.json                # Root workspace configuration
```

---

## Key Features

1. **Native Android AR Navigation (`PathfinderAR`)**:
   - Unity 6 with Google ARCore & AR Foundation.
   - 3D spatial alignment to real-world campus landmarks.
   - Indoor & multi-floor NavMesh routing with lift and staircase connectors.
   - Native Android TextToSpeech (`PFVoice`) turn-by-turn spoken guidance.
   - Seamless deep linking from the web app (`pathfinder://navigate?dest=<id>`).

2. **Campus Web Application (`PathFinderAR-main`)**:
   - Interactive 2D campus map with live GPS course tracking.
   - Instant search across 55+ campus destinations, departments, and labs.
   - Alvas Robo AI Campus Assistant supporting 7 Indian languages (English, Kannada, Tulu, Hindi, Tamil, Telugu, Malayalam).
   - Seamless handoff to the native Android AR app for ARCore-enabled devices.
   - Full HTTPS LAN support with self-signed SSL certificate for on-device testing.

---

## Getting Started

### Web Application

```bash
cd PathFinderAR-main
pnpm install
pnpm dev
```
The server will boot on `https://localhost:3000` (or `https://<LAN-IP>:3000`).

### Unity AR Application

1. Open `PathfinderAR` in **Unity 6 (6000.3.27f1)**.
2. Select the `Assets/PathFinderAR.unity` scene.
3. Switch platform to **Android**.
4. Build and run to an ARCore-compatible Android phone connected via USB.
