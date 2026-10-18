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
| Left / Right / Middle click | break / place or use / pick block |
| 1-9, scroll | hotbar |
| E | creative inventory (torch, flint & steel, water source...) |
| F1 / F2 / F3 / F11 | hide HUD / screenshot / debug / fullscreen |
| Esc | pause |

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
