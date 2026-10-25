# Sources and credits

- The terrain package samples the existing [LADIR web generator](https://github.com/X111Metafizik/ladir), using `fixtures/river-v42.planet` from commit `34841d6abc1c1c12251c2ace18ffb853895db16f`. Its seed, generator version and checksums are recorded in `Assets/StreamingAssets/Ladir/reference/manifest.json`.
- The Starlight Voyager mesh and textures come from the user-supplied `Meshy_AI_Starlight_Voyager_1020235536_texture.glb`, stored in the web project as `assets/starlight-voyager.glb`. See `Assets/Ladir/Source/model-provenance.json` for its source hash. Terrain stamp inputs also have user/third-party origins. This repository does not grant these assets a new license.
- Unity 6000.0.80f1, Universal Render Pipeline, Burst, Collections, Mathematics and Input System are Unity dependencies. Versions are pinned in `Packages/manifest.json` and `Packages/packages-lock.json`. Package source, editor binaries and imported Library caches are not committed; their respective licenses apply.
- The optional offline exporter uses Three.js from the separate web checkout (MIT). Three.js is not a Unity runtime dependency. See the web repository's [third-party notices](https://github.com/X111Metafizik/ladir/blob/main/THIRD_PARTY_NOTICES.md).
- The global Planet Studio port includes the unchanged web authoring generator, original mineral/volume noise data and exported procedural tree geometry. Their exact source commit and hashes are recorded in `Assets/StreamingAssets/Ladir/authoring/provenance.json` and `Assets/Ladir/Source/visual-provenance.json`. The C# terrain and flight implementations are derived from that same project; this port does not imply independent ownership of supplied assets.

- HUD font atlases use Liberation Sans and [Noto Sans Symbols 2](https://github.com/google/fonts/tree/main/ofl/notosanssymbols2), both under the SIL Open Font License 1.1. Source fonts and the accompanying copyright/license notices are in `Assets/Ladir/Source/Fonts`. The generated SDF atlases inherit those font licenses.

Public repository visibility does not grant an additional license to its code or assets.
