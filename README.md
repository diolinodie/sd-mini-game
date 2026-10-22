# Sunset Arcade

Mini games for the Elgato **Stream Deck+**, played on the dials, the touch strip and the keys.

| Action | Where | How to play |
|---|---|---|
| **Breakout** | 1 dial | Rotate to move the paddle, press to launch / pause, long-press the strip to restart |
| **Snake** | 1 dial | Rotate to turn, press to start / pause |
| **Foosball** | all 4 dials | 2 players. RED uses dial 1 + 3, BLUE uses dial 2 + 4. Rotate to move rods, press to kick, hold to lift |
| **Sunset Racer** | 1 dial | Retro pseudo-3D racer. Rotate to steer, press the dial for a speed boost, 3 per race (gas is automatic). 5 stages, reach each checkpoint before time runs out |
| **Sunset Racer 4P** | all 4 dials | 4-player split-screen race, dial n is player Pn. Press to join, the race starts 10 s after the first join |
| **Racer Map** | keys | Live top-down map of the race with positions and ranking. Put it on several keys and they join into one big map. Press to zoom on the leader |

All graphics are drawn at runtime as SVG. There are no image assets except the action icons.

## Layout

| Path | What |
|---|---|
| `node/` | The plugin, built on the official Elgato Node.js SDK. This is the version on the Marketplace. See [node/README.md](node/README.md) for the source layout |
| `mini_game/` | The original C# (.NET Framework 4.8) plugin. No longer maintained on `main`; the `c#` branch has its last release. The game code is still used by `tools/shots` to render the Marketplace images |
| `marketplace/` | Marketplace name, description, release notes and images, see `marketplace/listing.md` |
| `tools/` | Icon and screenshot generators |

## Build and install

Requirements: Node.js 20+, Stream Deck 7.1+ on Windows 10+ or macOS 12+.

```
cd node
npm install
npm run build
npx streamdeck link com.odiestudio.minigame.sdPlugin   # once, links the folder into Stream Deck
npx streamdeck restart com.odiestudio.minigame
```

The actions are listed under **Sunset Arcade**. `npm run watch` rebuilds and restarts the plugin on every change, `npm run validate` checks the manifest, and `npx streamdeck pack com.odiestudio.minigame.sdPlugin` writes the `.streamDeckPlugin` file to `node/dist/`.

## Tools

| Tool | What it does |
|---|---|
| `tools/make-icons.ps1` | Draws the category, action list and plugin icons into `mini_game/Images/`, and the 288 px store icon into `marketplace/`. Copy the icons to `node/com.odiestudio.minigame.sdPlugin/Images/` afterwards |
| `tools/shots/` | Renders real game frames with the C# game code and lays them out as the Marketplace thumbnail and gallery images in `marketplace/`. Needs the .NET SDK on Windows. Run `dotnet run -c Release` in that folder |

## Third-party libraries

| Library | License |
|---|---|
| [@elgato/streamdeck](https://github.com/elgatosf/streamdeck) | MIT |
| [@elgato/utils](https://github.com/elgatosf/utils) | MIT |

## License

[MIT](LICENSE)
