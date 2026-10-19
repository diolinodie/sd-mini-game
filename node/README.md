# Sunset Arcade, Node.js version

Port of the C# plugin in `../mini_game/` to the official Elgato Node.js SDK (`@elgato/streamdeck`). Work in progress on the `node-port` branch.

| Game | Status |
|---|---|
| Snake | ported, tested on device |
| Breakout | ported |
| Foosball | ported |
| Sunset Racer | ported |
| Sunset Racer 4P / Racer Map | ported |

Still to do before merging: test everything on a device, port `tools/shots`, switch to the release UUID.

## Layout

| Path | What |
|---|---|
| `src/games/` | Game logic and drawing. No Stream Deck code, so it also runs in a browser or a test |
| `src/actions/` | Stream Deck actions: map dial / touch events to a game, send frames as `setFeedback` |
| `src/gfx/` | `Svg`, a tiny GDI+-style drawing API that outputs SVG. Frames go to the touch strip as `data:image/svg+xml;base64,...`, so there are no native dependencies. Stream Deck draws SVG with Qt (SVG Tiny 1.2, no `clipPath`), so transforms and clipping are applied in `Svg` and only absolute coordinates are written |
| `src/games/net-random.ts` | Same algorithm as .NET `System.Random(seed)`, so the racer tracks are identical to the C# version |
| `com.odiestudio.minigame.dev.sdPlugin/` | Plugin folder: manifest, images, layouts, property inspectors. `bin/` is the build output |

## Dev UUID

During the port the plugin uses `com.odiestudio.minigame.dev` (see `src/ids.ts`, `manifest.json` and the folder name), shown as **Sunset Arcade (Node)**. This way it installs next to the released C# version and the two can be compared dial by dial.

For release, switch all three back to `com.odiestudio.minigame` so the action UUIDs match the C# version and users keep their settings and high scores. For the same reason the settings keys and types (all strings) must stay the same as the C# `PluginSettings`.

## Build and run

Requirements: Node.js 20+, Stream Deck 7.1+.

```
npm install
npm run build
npx streamdeck link com.odiestudio.minigame.dev.sdPlugin   # once, links the folder into Stream Deck
npx streamdeck restart com.odiestudio.minigame.dev
```

`npm run watch` rebuilds and restarts the plugin on every change. `npm run validate` checks the manifest.
