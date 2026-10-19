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

All graphics are drawn at runtime with GDI+. There are no image assets except the action icons.

## Build

Requirements: Windows 10+, .NET Framework 4.8, Visual Studio 2022 or the .NET SDK.

```
dotnet build sd-mini-game.sln -c Release
```

The output is in `mini_game/bin/Release/net48/`.

## Install

1. Quit the Stream Deck app.
2. Copy everything in `mini_game/bin/Release/net48/` to
   `%APPDATA%\Elgato\StreamDeck\Plugins\com.odiestudio.minigame.sdPlugin\`.
3. Start the Stream Deck app. The actions are listed under **Sunset Arcade**.

## Tools

| Tool | What it does |
|---|---|
| `tools/make-icons.ps1` | Draws the category, action list and plugin icons into `mini_game/Images/`, and the 288 px store icon into `marketplace/` |
| `tools/shots/` | Renders real game frames with the game code and lays them out as the Marketplace thumbnail and gallery images in `marketplace/`. Run `dotnet run -c Release` in that folder |

The Marketplace name, description and image list are in `marketplace/listing.md`.

## Third-party libraries

| Library | License |
|---|---|
| [StreamDeck-Tools](https://github.com/BarRaider/streamdeck-tools) | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |
| [Fody](https://github.com/Fody/Fody) / [Costura.Fody](https://github.com/Fody/Costura) | MIT |
| [NLog](https://github.com/NLog/NLog) (via StreamDeck-Tools) | BSD-3-Clause |
| [CommandLineParser](https://github.com/commandlineparser/commandline) (via StreamDeck-Tools) | MIT |

## License

[MIT](LICENSE)
