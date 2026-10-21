# minecraft-remake-csharp

A from-scratch, Minecraft-style voxel game written in **C# / .NET 10** with **OpenTK** (OpenGL 3.3 core).
No Mojang assets are bundled: every default texture, the font and the UI are generated in code (you can load your own resource packs).

> Not affiliated with or endorsed by Mojang AB / Microsoft.

## Run

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK. Tested on macOS (Apple Silicon).

## Controls

| Key | Action |
| --- | --- |
| W A S D | move |
| Ctrl / double-tap W | sprint |
| Shift | sneak (also fly down) |
| Space / double-tap Space | jump / toggle flying |
| Left / Right / Middle click | break / place or use / pick block (hold right click with a bow to draw, release to shoot) |
| 1-9, scroll | hotbar |
| E | creative inventory (torch, flint & steel, water source...) |
| F1 / F2 / F3 / F11 | hide HUD / screenshot / debug / fullscreen |
| Esc | pause |

## TNT, bow and arrows

- **TNT** is lit with flint & steel, by adjacent fire, or by another explosion (chain reactions). Primed TNT pops up, falls, flashes white and blows up after 4 seconds.
- Explosions use Minecraft's algorithm: 1352 rays that lose strength by each block's blast resistance (stone and bricks resist, dirt and leaves don't, water absorbs it), power 4, knockback for you and other TNT, and a bloom flash with smoke and sparks.
- **Bow:** hold right click to draw (the FOV tightens, like vanilla), release to shoot. Arrows fly with vanilla physics (gravity 0.05, drag 0.99, 0.6 in water) and stick into blocks.
  The first arrow item in your hotbar picks the ammo; with none you shoot plain arrows. An **Explosive Arrow** detonates on impact (power 3).
- Breaking any block throws a burst of textured fragments like Minecraft.
- Try `/give tnt`, `/give bow`, `/give explosive_arrow`, `/summon tnt ~ ~ ~ 40` or `/explode ~ ~ ~ 6`.

## Commands

Press `T` (chat) or `/` (command). Tab completes commands, items and arguments; Up/Down browse history.

`/help` `/give` `/clear` `/time set|add|query` `/gamemode creative|spectator` `/tp` `/setblock` `/fill` (replace/destroy/hollow/keep/outline)
`/seed` `/say` `/me` `/kill` `/gamerule doDaylightCycle|doFireTick` `/locate biome` `/reload` `/save-all` `/fly`
and `/flightspeed <0.1-20|reset>` to change how fast you fly (saved in your options).

## Shader-pack look

An optional (default on) HDR pipeline inspired by shader packs like BSL: sun/moon shadow maps with soft edges, warm sun and blue sky
ambient, bloom and glowing torches/fire with flickering light, god rays, filmic tone mapping, and water with animated waves, Fresnel
reflections (sky + screen-space reflections of the shore and trees), refraction, depth absorption, foam and sun glints.
Everything is configurable under **Options > Shader Settings** (shadow quality, reflections, bloom) or can be switched off for the vanilla look.

## Earth mode

**Create New World > World Type: Earth** generates the real planet instead of random terrain. Pick a start location (type `latitude, longitude`
or cycle the presets) and a scale (1 block = 1, 2, 4, 8, 16 or 32 m).

- **Terrain and oceans:** real heights and sea-floor depths from AWS Terrain Tiles (SRTM/Copernicus + bathymetry). Sea level, beaches and big flat lakes follow the real map.
- **Materials:** NASA MODIS land cover picks forest (spruce for needleleaf), grassland, cropland, shrub, desert, barren rock, snow and ice. Steep ground in dry regions (Grand Canyon, Utah, Arizona) is banded terracotta / red sandstone, and steep ground elsewhere is bare stone.
- **Map features:** buildings (walls, windows, doors, flat roofs, real heights), roads, paths, railways, bridges, rivers, streams, canals and lakes from OpenStreetMap, served as OpenFreeMap vector tiles (fast and free, no key). Roads are Asphalt, footpaths Concrete or Gravel.
- **Place name:** the top right shows the city and country you are in plus your latitude/longitude.
- **Map:** a minimap sits under the place name (top right) with a red arrow for where you face; **N** toggles it, **-** / **=** zoom it. **M** opens the full world map with country, state, city, neighbourhood, sea and street names: drag to pan, scroll or +/- to zoom, click to drop a marker, double-click (or Enter / the Teleport button) to go there, and type any place, address or postcode in the search box. The map is drawn from the same OpenFreeMap data as the buildings, so it works at every zoom from the whole planet to single streets.
- **Teleport:** `/tp london`, `/tp grand canyon`, `/tp Kyoto` (any place name), `/tp hx4 8dy` (UK postcode, looked up exactly), `/tp 90210` (US zip), `/tp 10 Downing Street, London` (addresses) or `/tp 48.85 2.35`. `/tp <x> <y> <z>` still works for local coordinates, and `/where` prints where you are. A teleport re-centres the world (your edits stay where you built them).
- **Downloads and caches:** data is fetched on demand and cached in `earthcache/` next to your saves; the first visit to a place needs internet. Buildings, roads and rivers appear a moment after the terrain (chunks regenerate when the map tile arrives).
- **Limits:** the world is 192 blocks tall, so heights are 1:1 for as much range as fits (about 100 blocks of hill above the local valley floor; 90 above sea level near the coast) and only the extreme relief above that is eased in. The vertical reference is fitted to the terrain around where you start or teleport, so inland places sit on the valley floor and hills get the full height. Land below sea level (e.g. the Netherlands) is flooded. Roofs are flat, interiors are empty, and very large lakes or rivers that OpenStreetMap stores as relations are only covered when the elevation data shows them as flat water.
- Map data: (c) OpenStreetMap contributors via OpenFreeMap / OpenMapTiles; land cover from NASA GIBS (MODIS); elevation from AWS Terrain Tiles; place names from Nominatim.
- Debug: `--earthdump lat,lon,scale` prints a height map; `--earthtp lat,lon,scale lat,lon` tests a teleport.

## Resource packs

The game loads **Minecraft Java Edition resource packs** the same way Minecraft does:

1. Open **Options > Resource Packs...** and click **Open Pack Folder** (it is created for you).
2. Drop packs in as `.zip` files or unzipped folders (`pack.mcmeta` + `assets/minecraft/textures/block|item/*.png`).
3. Click a pack to activate it. Packs higher in the *Selected* list override the ones below; use the arrows to reorder.

Supported: 16x up to 128x textures, animated textures (`*.png.mcmeta` with `frametime` / `frames`), biome-tinted grass,
leaves and water, the grass side overlay, and both the modern (`block/`) and legacy (`blocks/`) texture names.
Without a pack the built-in generated textures are used.

**One-click open-source pack:** the pack screen has a *Download Open-Source Pack (Luanti, CC BY-SA)* button. It fetches the
default textures of [Luanti's Minetest Game](https://github.com/luanti-org/minetest_game) (CC BY-SA 3.0) straight from GitHub,
converts them into a Minecraft-layout pack (gray biome-tinted grass/leaves/water, ores composited on stone, animated water and fire)
and activates it. The credits and licence text are saved inside the pack folder. Nothing from it is committed to this repo.
 The pack folder lives in your user data directory
(`~/Library/Application Support/McClone/resourcepacks` on macOS), so no third-party art ever ends up in this repo.

## Features

- Procedural terrain: continents, biomes, caves, ores, trees, cacti, flowers
- Chunked world streamed on background threads, face-culling mesher with smooth lighting and ambient occlusion
- Vanilla tick physics (20 TPS) for walking, sprinting, sneaking, swimming and flying
- Two light channels: sky light and block light (torches, fire) with warm torch tinting
- Torches (floor + wall mounted), flint & steel and spreading fire that burns planks, logs, leaves and grass
- Vanilla-style water: source/flowing/falling levels, infinite sources, flow towards drops, sloped surfaces, currents
- Day/night cycle, sun, moon, stars, clouds, fog
- Minecraft-style menus: title screen with live panorama, world select/create, options, pause, creative inventory
- Saves block edits, player and time per world
