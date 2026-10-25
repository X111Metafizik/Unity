# LADİR — Unity reference flight

A **32 × 32 km Windows prototype**, built with Unity **6000.0.80f1**, URP 17.0.4, Burst and Jobs. The browser game is maintained separately in [LADIR](https://github.com/X111Metafizik/ladir).

This is the first migration slice: canonical terrain and water from `fixtures/river-v42.planet`, the supplied Starlight Voyager model, flight, landing, boost, orbit camera and repair. The exported world has an **8,000 km diameter / 4,000 km radius**. This scene covers one finite reference region; it does not render the entire sphere.

## Open and fly

1. Clone this repository and add its root folder in Unity Hub, using **6000.0.80f1**. Use a short local path such as `C:/Projects/Unity` to avoid Windows/Mono path-length errors.
2. Let packages and assets import. Open **Assets/Ladir/Scenes/ReferenceFlight.unity**. The **LADIR → 1 · Create or open reference flight** menu can recreate the setup.
3. Press **Play**. Initial terrain stays visible while fine terrain streams in.

| Input | Action |
| --- | --- |
| Mouse position | Turn and pitch |
| A / D | Strafe |
| Space | Boost, 1,800 km/h |
| S / B | Brake / cut speed; release for 700 km/h cruise |
| C | Toggle landing assistant / take off |
| Ctrl + right mouse drag | Orbit around the ship |
| Mouse wheel | Camera distance |
| 2 | Repair 50% over 5 seconds, 30-second cooldown |
| R / Escape | Reset flight / pause flight |

## Data and geometry

- **256 files**, each a 2 × 2 km tile with 257 × 257 samples. Finest spacing is **7.8125 m**. A separate overview uses 62.5 m samples. This is not a claim of identical sampling density to the browser's adaptive mesh.
- The exporter calls the existing `sampleTerrain` and `biomeColor`; it does not create an independent Unity terrain generator. Float32 height, water-level and material-color samples are saved with SHA-256 checksums, seed, generator identity and metric coordinates.
- Unity X/Z follow the reference region. Rendered Y is altitude above sea level minus a local spherical-sag approximation, `r² / (2R)`. This local coordinate system is not the completed planetary floating-origin system.
- Terrain grids have 16, 32, 64, 128 or 256 cells. Fine edges follow their coarser neighbor's chord. A new set of meshes becomes visible together, after its edge contracts are ready. Old valid meshes remain visible during preparation.
- File/decompression tasks are limited to three; Burst geometry jobs to four; CPU mesh submission targets a 2 ms frame budget, allowing at least one mesh to make progress. An individual large upload can exceed that budget and is measured.
- Fine sample cache has a 96 MiB budget. Immutable CPU collision uses the currently displayed terrain triangles, including stitched edges. Water is clipped against the sampled bed; flat submerged blocks are merged.
- Predictive tile loading follows two seconds of ship velocity. Boost does not reduce near detail. LOD currently uses metric distance with hysteresis; full screen-error LOD and geomorphing are later work.

## Reproduce

The regional package and extracted ship are committed. **Playing or building in Unity does not require Node.js or the web project.**

Optional package integrity checks (Node.js 24+, no npm dependencies):

```sh
npm test
```

To export again, clone the [web source](https://github.com/X111Metafizik/ladir) separately, use source commit `34841d6abc1c1c12251c2ace18ffb853895db16f`, and run `npm ci` in that checkout. Then run from this Unity repository:

```sh
npm run export:region -- --source "../ladir"
```

This command replaces the regional package and extracted ship with samples from that web checkout. Generation may take minutes; it is offline, while the native player reads the committed package. Set `LADIR_WEB_SOURCE` to the web checkout path before `npm test` to enable the optional canonical generator parity test. Without it, that one test is explicitly skipped; package and shared-edge checks still run.

In Unity, **LADIR → 2 · Validate data and LOD** checks every tile checksum, shared edges, overview/fine parity, Burst jobs, water geometry, corrupt data rejection and physical scale. **LADIR → 3 · Build Windows prototype** runs validation and produces a Windows build. With the matching editor on PATH:

```text
Unity.exe -batchmode -nographics -projectPath "C:/Projects/Unity" -executeMethod Ladir.Editor.PrototypeSetup.Build --ladir-output "C:/Projects/Unity/Builds/Windows" -logFile "C:/Projects/unity-build.log"
Ladir.exe --ladir-benchmark --ladir-output "C:/short/ladir-results"
Ladir.exe --ladir-control-smoke --ladir-output "C:/short/ladir-controls"
```

The benchmark records startup and lake, river, canyon, mountain and boost-release camera routes at 1440 × 1000, with VSync disabled and 4× MSAA. CPU and GPU timings of zero mean the timing API was unavailable. Upload timing is CPU submission, not measured GPU copy time. Task latencies overlap. Screenshot frames are included in the first diagnostic run, so screenshot capture can contribute long frames.

Use a **visible player window** for rendering benchmarks. A hidden window can skip rendering and produces invalid GPU/FPS conclusions. `--ladir-no-screenshots` disables capture overhead. The independent control smoke test creates virtual Input System devices inside the game; it verifies boost, cruise, landing, takeoff, impact damage, repair, orbit and pause without sending operating-system input.

## Boundaries of this prototype

The web version's full planet, global warp, enemies, trees and volumetric clouds are **not ported yet**. Browser and native FPS are therefore not an equal-feature comparison. Distance-based LOD swaps can still change relief abruptly; there is no geomorph yet. The scene boundary is explicit. The initial flight controller is kinematic and samples terrain triangles; it is not a full Rigidbody aircraft model. Its scripted benchmark bypasses input/collision and must not be described as an interactive flight-physics test.

Source assets were extracted from the already supplied GLB; see [model provenance](Assets/Ladir/Source/model-provenance.json) and [credits](THIRD_PARTY_NOTICES.md). Read the [implementation and performance report (Turkish)](docs/UNITY_PROTOTYPE.md). Results and screenshots are recorded under [evidence/unity-v1](evidence/unity-v1/).
