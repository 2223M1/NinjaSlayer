<div align="center">
  <img src="Workshop/image.png" width="256" alt="NinjaSlayer project icon">
  <h1>NinjaSlayer</h1>
  <p>A Ninja Slayer character mod for Slay the Spire 2</p>
  <p><a href="README.md">简体中文</a> | <strong>English</strong></p>
  <!-- compatibility-badges:start -->
  <p>
    <img src="https://img.shields.io/badge/C%23-.NET-512BD4?logo=dotnet&amp;logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 9">
    <img src="https://img.shields.io/badge/Godot-4.5.1-478CBF?logo=godotengine&amp;logoColor=white" alt="Godot 4.5.1">
    <img src="https://img.shields.io/badge/Slay%20the%20Spire%202-0.107.1%20%7C%200.110.0-B51F24" alt="Slay the Spire 2 0.107.1 and 0.110.0">
    <img src="https://img.shields.io/badge/RitsuLib-0.5.1-2D7D9A" alt="RitsuLib 0.5.1">
    <a href="https://github.com/2223M1/NinjaSlayer/releases/latest"><img src="https://img.shields.io/github/v/release/2223M1/NinjaSlayer?display_name=tag&amp;sort=semver" alt="GitHub Release"></a>
  </p>
  <!-- compatibility-badges:end -->
</div>

NinjaSlayer brings the combat style of Ninja Slayer to Slay the Spire 2. Built on RitsuLib, it delivers a complete playable character experience centered on karate, shuriken, chado, Naraku, and Ninja Soul, together with custom presentation, events, and audio.

## Highlights

- Cohesive character mechanics with multiple combat routes grounded in the Ninja Slayer theme.
- Custom finishers, character animation, boss death presentation, companion visuals, and FMOD audio.
- Explicit safeguards and validation for saves, multiplayer, game-version contracts, and mod compatibility.
- A separately maintained [card catalog](Docs/card-catalog.md) without duplicating content inventories on the project home page.

## Installation

### Steam Workshop

1. Subscribe to and enable [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295).
2. Subscribe to [NinjaSlayer](https://steamcommunity.com/sharedfiles/filedetails/?id=3761570842).
3. Launch the game and enable both `STS2-RitsuLib` and `NinjaSlayer` in the mod manager.

### Manual installation

1. Download the latest build from [GitHub Releases](https://github.com/2223M1/NinjaSlayer/releases).
2. Extract the archive into the Slay the Spire 2 `mods` directory.
3. Install and enable a compatible RitsuLib version separately, then enable NinjaSlayer.

## Compatibility And Language

<!-- compatibility:start -->
| Component | Supported version |
|---|---|
| Slay the Spire 2 | stable public `0.107.1`; preview beta `0.110.0` |
| RitsuLib | build baseline and minimum dependency `0.5.1`; Workshop installs receive its current release automatically |
| .NET | `9.0` |
| Godot | `4.5.1 Mono` |
| In-game language | Primarily Simplified Chinese at present |

Each GitHub Release contains separate host-specific stable and preview archives. The public Workshop item always receives stable; a preview item link will be added after its one-time creation. Never enable both NinjaSlayer items together.
<!-- compatibility:end -->

This English README documents the project and installation process. It does not indicate complete English localization in the game.

## Development

Development requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0), [Godot 4.5.1 Mono](https://godotengine.org/download/archive/4.5.1-stable/), and compatible game files.

```powershell
git clone https://github.com/2223M1/NinjaSlayer.git
cd NinjaSlayer
dotnet restore .\NinjaSlayer.csproj
dotnet build .\NinjaSlayer.csproj --no-restore -c Release -v:minimal
```

An ordinary build never installs or publishes the mod automatically. See the [development and release guide](Docs/development.md) for tests, real-game contracts, packaging, local installation, and release boundaries.

## Links

- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3761570842)
- [GitHub Releases](https://github.com/2223M1/NinjaSlayer/releases)
- [Issue tracker](https://github.com/2223M1/NinjaSlayer/issues)
- [Card catalog](Docs/card-catalog.md)
- [Privacy notice](Docs/privacy.md)

## Author And Acknowledgements

Maintained by [2223M1](https://github.com/2223M1).

Thanks to the maintainers of [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib), the STS2 modding tutorials, and the modding community for their framework, documentation, and technical discussion.

## Rights Notice

This is an unofficial fan-made mod. Slay the Spire 2, Ninja Slayer, and all related names, characters, artwork, and other third-party material remain the property of their respective rights holders.

This repository does not declare an open-source license. Public visibility of the source code does not grant permission to copy, modify, redistribute, or create derivative releases. Obtain explicit permission from the maintainer and applicable rights holders before using project material.
