# minecraft-remake-csharp

A from-scratch, Minecraft-style voxel game written in **C# / .NET 10** with **OpenTK** (OpenGL 3.3 core).
No Mojang assets are used: every texture, the font and the UI are generated in code.

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
