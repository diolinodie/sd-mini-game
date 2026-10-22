# Sunset Arcade, Node.js version

Port of the C# plugin in `../mini_game/` to the official Elgato Node.js SDK (`@elgato/streamdeck`). This is the released version on `main`; the C# version stays on the `c#` branch.

| Game | Status |
|---|---|
| Snake | ported, tested on device |
| Breakout | ported, tested on device |
| Foosball | ported, tested on device |
| Sunset Racer | ported, tested on device |
| Sunset Racer 4P / Racer Map | ported, tested on device |

Still to do before merging: port `tools/shots`.

## Layout

| Path | What |
|---|---|
| `src/games/` | Game logic and drawing. No Stream Deck code, so it also runs in a browser or a test |
| `src/actions/` | Stream Deck actions: map dial / touch events to a game, send frames as `setFeedback` |
| `src/gfx/` | `Svg`, a tiny GDI+-style drawing API that outputs SVG. Frames go to the touch strip as `data:image/svg+xml;base64,...`, so there are no native dependencies. Stream Deck draws SVG with Qt (SVG Tiny 1.2, no `clipPath`), so transforms and clipping are applied in `Svg` and only absolute coordinates are written |
| `src/games/net-random.ts` | Same algorithm as .NET `System.Random(seed)`, so the racer tracks are identical to the C# version |
| `com.odiestudio.minigame.sdPlugin/` | Plugin folder: manifest, images, layouts, property inspectors. `bin/` is the build output |

## UUID

The plugin UUID is `com.odiestudio.minigame` (`src/ids.ts`, `manifest.json` and the folder name), the same as the C# version. The action UUIDs and the settings keys and types (all strings) also match the C# `PluginSettings`, so users who update keep their dials, settings and high scores. Don't change them.

Because the UUID is the same, the C# version and this one can't be installed at the same time.

## Build and run

Requirements: Node.js 20+, Stream Deck 7.1+.

```
npm install
npm run build
npx streamdeck link com.odiestudio.minigame.sdPlugin   # once, links the folder into Stream Deck
npx streamdeck restart com.odiestudio.minigame
```

`npm run watch` rebuilds and restarts the plugin on every change. `npm run validate` checks the manifest.
