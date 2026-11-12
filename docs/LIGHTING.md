# Classic lighting (build-152)

Every map is lit by its own original settings: sun colour and strength, time of day, ambient (shadow) colour and
strength, drifting cloud shadows on the ground and its skybox. The lighting maths is the 2003 game's, so colours
keep the original's saturation and contrast. Build-11's fixed preview rig (blue-grey ambient at 0.70, white sun at
0.85, baked terrain shadows lifted to about half brightness) is gone.

Refresh the map caches once: `python tools/map_lighting.py` (Bilbo's Bluffs, every multiplayer map and the Sandbox),
and `python tools/map_lighting.py converted/prototype --source "extracted/Levels/Helms Deep.wor"` for Helm's Deep.
It writes each manifest's `lighting` block plus `map/clouds.png` and `map/sky-*.png`; nothing else is re-exported.
A cache without the block falls back to Rings.exe's defaults (noon, white sun, ambient 0.3, no clouds or sky).

## Where the settings come from

`tools/map_lighting.py` names fields `world_scene.py` already read. Meanings come from the editor's dialogs
(RingsEditor.exe resources: Global illumination, Sun/Moon light keyframe, Environment Settings) and from Rings.exe:

| Setting | Source | Rings.exe |
|---|---|---|
| Ambient colour and intensity (mostly 0.3-0.4) | world tail, time-of-day block words 1-2 | read at 0x56d412 into world +0x81c4; defaults 0x565c2d |
| Sunrise 0.25, sunset 0.75, day 3600 s (same on every map) | words 3-5 | same block |
| Initial time of day (0.22-0.93) | word 6 | same block |
| Shadow length (0 on the underground Balrog map) | word 7 | same block; not used yet |
| Sun/Moon colour and intensity (1.0-2.2) | the block's light keyframe (one per map) | `TimeOfDaySetSunLight` 0x53cef7 scales colour by intensity |
| Sun position | from time of day and map size | 0x53c80a: south edge (source y = 0), height = map length, aimed at the centre; east at sunrise, west at sunset, the moon back at night |
| Humidity and wind min/max/initial, wind direction | terrain environment block | 0x5691d7 reads them to world +0x8114..+0x8194 |
| Clouds on | environment flag | world +0x8140, constructor default 1 (0x5627f8); clear on Lothlorien, Wasteland, Cirith Udun, River of Deceit, Helm's Deep |
| Fog-of-war colour and alpha | environment block | world +0x8088/+0x808c; exported, not used yet |
| Skybox and Sky Color | world tail final record | `Skyboxes.lqr`: one side texture, a top and a bottom |
| Grass tint, height, on/off | world tail grass record | exported, not used yet |

## The lighting model

Direct3D 9 lit the original in gamma space: a surface's lit colour was `clamp(ambient + diffuse × sun × N·L, 0, 1)`,
and the texture was multiplied by it. Godot lights in linear space, where the same numbers come out much brighter and
flatter: build-11 showed an unlit side at about 63% of its texture (the original: about 30%). The classic shader
(`Rendering/ClassicLighting.cs`) computes the original's sum in `light()` and converts it to linear once, so a texel
shows on screen at `texture × clamp(ambient + diffuse × sun × N·L × shadow)`.

- A terrain vertex colour is that `diffuse`: the editor's painted colour and precomputed shadows. It scales the sun's
  light, not the texture, so the darkest baked shadow still shows the ambient colour (the editor guide: "think of
  the ambient color as the color of the shadows"). Multiplying it into the texture instead halved the ground's
  brightness against the retail captures.
- Models have no vertex colour; their diffuse is white.
- Cast shadows are Godot's shadow maps (the retail captures ran with stencil shadows on). A shadowed texel keeps the
  ambient.
- The terrain and editor decals are multiplied by twice the cloud texel, saturating at 1 (`Textures/baseclouds.tga`,
  the terrain materials' MODULATE2X stage), scrolled along the wind. Clouds shade the ground and never brighten it.
- Emission (team colours) is added after lighting, as before. Linear tonemapping, no glow, no specular.
- Materials the game derives per instance (Ring/camouflage ghosts, gilded Sandbox dummies), effects and the menu scene
  keep Godot's own lighting with the map's ambient and sun colours.

Two cloud numbers are not decoded and were chosen against the three retail Bilbo's Bluffs captures (local-008, 016,
018): the cloud map covers 96 m and drifts at 0.02 m/s per percent of initial wind.

Build-154: build-152 let clouds brighten too, at twice the texel with a gain that made an average texel neutral.
The owner found the original less bright. Measured by surface, retail grass never has a bright tail: its brightest
tenth sits just under build-152's cloud-free render, while build-152's clouds pushed it to 0.51. Saturating the cloud
stage at 1 (darken only) puts the ground back in the retail range. A version that multiplied the doubled texel into
the lit colour before clamping still brightened partly lit ground (grass median 0.267). Retail cloud shadows remain
a little darker than ours: grass 10th percentile 0.133-0.145 against 0.165.

| Ground brightness (gamma) | mean | 10% | 50% | 90% | grass 50% | grass 90% |
|---|---|---|---|---|---|---|
| Retail local-008/016/018 | 0.153-0.175 | 0.081-0.088 | 0.140-0.163 | 0.230-0.276 | 0.192-0.231 | 0.290-0.357 |
| Build-151 preview lighting | 0.134 | 0.055 | 0.110 | 0.192 | 0.173 | 0.275 |
| Build-152, no clouds | 0.166 | 0.099 | 0.162 | 0.216 | 0.227 | 0.302 |
| Build-152 | 0.192 | 0.078 | 0.165 | 0.341 | 0.251 | 0.514 |
| Build-154 | 0.163 | 0.094 | 0.156 | 0.217 | 0.227 | 0.310 |

The ground columns measure everything above the console (1920x1080, x 500-1900, y 0-760); the grass columns take
the HSV value of its green pixels (hue 60-150, saturation over 0.35). Our builds share the start camera, which shows
more cliff and distant grass than the captures. Captures: `converted/lighting-build152/` (`compare-bilbo.png`:
retail, build-151, build-152; `compare-bilbo-154.png`: retail, build-152, build-154; `maps.png`: Wasteland, Dust and
Blood, Lorien and the Bilbo's Bluffs overview with its skybox, all build-152).

## Not done

- Time of day stands still (the block's flag byte is 0 on every map; scripts can move the sun in the original).
- Humidity-driven cloud cover, rain and the Low/High light level weather colours.
- Placed dynamic lights (campaign maps: Dol Guldur has 18), grass, skydomes, water reflections.
- Shadow length, and per-vertex (Gouraud) shading of models; the shader lights per pixel.
- Material ambient factors from `Materials/*.lmt` (read as raw words; the model uses 1).
- The fog-of-war overlay could take the map's own fog colour from the `lighting.fog_of_war` block.
