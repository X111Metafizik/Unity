# LADİR — native Planet Studio port

Unity **6000.0.80f1 / URP 17** source port of the [LADİR browser project](https://github.com/X111Metafizik/ladir), based on web commit `34841d6abc1c1c12251c2ace18ffb853895db16f`.

**Status: implementation and portable parity tests are present; native visual acceptance is pending.** The new global scene has not yet passed a Windows player build or rendered visual QA. Do not reuse the older finite prototype's FPS numbers for this scene.

## Open the new scene

1. Open this repository in Unity 6000.0.80f1 and allow import to finish.
2. Choose **LADIR → 4 · Create or open full Planet Studio**.
3. The setup creates `Assets/Ladir/Scenes/PlanetStudio.unity`, its URP cloud renderer, materials and UI Toolkit panel. Press **Play**, then **Uçuş modu**.
4. Existing unsaved scenes are preserved. Save them before switching scenes.

The default package is the original `river-v42.planet`: **8,000 km diameter / 4,000 km radius**, with the original stamp height arrays, erosion, rivers, lakes and world identity. Gameplay and surface sampling are native C#. Generating a **new seed** currently invokes the bundled, unchanged original authoring generator using **Node.js 24+**; opening and playing existing packages does not require Node or a browser.

## Implemented in this port

- Double-precision spherical world, floating render origin, six-face quadtree, background terrain jobs, parent coverage, horizon culling, upload budget and destination prefetch.
- C# terrain/stamp/hydrology/biome sampler, shared metric surface for flight and water, clipped shores, original mineral texture, original three tree species and deterministic tree placement. Tree roots bind to the displayed LOD triangle.
- Native editor and flight UI: seed/presets, appearance, inspection, exploration markers, surface selection, package opening/saving, route preview, flight readouts, radar, target labels and eight skill slots.
- Supplied Voyager mesh, 700 km/h cruise, 1,800 km/h boost, landing/takeoff, gravity-assisted dives, swept terrain collisions, orbit camera, enemies and pooled projectiles/explosions.
- Warp distances 1,000 / 3,000 / 6,000 / 9,000 / 12,000 km, three-second charge, animated travel and deceleration; repair 50% over five seconds with a 30-second cooldown.
- Half-resolution procedural clouds using the original 64³ noise asset, depth-aware compositing, density-selected lightning, bright compact thrusters, navigation lights, speed streaks and a transparent warp tunnel.

These are source implementations, not a claim of pixel-identical or performance-validated rendering. See [remaining acceptance work](docs/PLANET_PORT.md).

## Controls

| Input | Action |
| --- | --- |
| Mouse | Flight steering; editor surface navigation |
| Space / S | Boost / brake |
| A / D; double tap | Strafe; evasive roll |
| B / W | Hover / cruise |
| Left / right mouse | Gun / guided missile |
| Ctrl + right drag | Ship orbit camera |
| Shift | Select target |
| C | Land / take off |
| 1 + wheel | Select warp distance during charge |
| 2 | Repair |
| Delete / Esc | Cockpit view / pause |
| H | Toggle HUD |

## Validation

The committed [evidence](evidence/planet-port) records:

| Check | Result |
| --- | --- |
| Terrain, six world types, shared faces and hydrology | 2,118 cases, zero failures; maximum height difference 6.96e-11 m |
| Original tree placement | 8 tiles / 2,439 trees match; maximum root difference 3.26e-10 m |
| Flight, landing, takeoff, warp, repair, navigation and package checks | 1,531 assertions passed (includes time-series samples) |
| C# save reopened by the unchanged web reader | Same canonical world hash |
| Existing regional package/model tests | 3 passed, no skips |
| Unity API compile check | C# 9 against installed Unity 6 assemblies: zero errors/warnings |
| New native player build / GPU visuals / FPS | **Not validated**; local import ran out of memory and disk space |

With .NET 9 and Node 24, after Unity package import:

```powershell
./tools/test-native-kernel.ps1
$env:LADIR_WEB_SOURCE = 'C:/Projects/ladir'
npm.cmd test
```

`tools/compile-unity` is an additional API/syntax check. It is **not** the Unity compiler, a shader test, or a player build.

Native build command once resources are available:

```text
Unity.exe -batchmode -nographics -projectPath "C:/Projects/Unity" -executeMethod Ladir.Editor.PlanetSetup.Build -logFile "C:/Projects/planet-build.log"
Builds/Planet/Ladir.exe --planet-smoke "C:/Projects/planet-smoke"
```

The visible smoke run captures editor, flight and orbital views and writes timing/error evidence. No new-scene screenshots or FPS results are fabricated in this repository.

## Existing reference prototype

The independent 32 × 32 km baked prototype and its Burst/Jobs pipeline remain available through **LADIR → 1 · Create or open reference flight**. Its instructions are preserved in [REFERENCE_FLIGHT_README.md](docs/REFERENCE_FLIGHT_README.md); its old benchmark remains separate.

[Asset credits](THIRD_PARTY_NOTICES.md) · [Migration notes](docs/PLANET_PORT.md) · [Original comparison](docs/TEST_KARSILASTIRMASI.md)
