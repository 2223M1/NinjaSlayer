# NinjaSlayer

《杀戮尖塔 2》的忍者杀手角色 Mod，包含完整角色机制、卡牌、遗物、事件、动画与 FMOD 音效。

## 安装

1. 订阅并启用 [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)。
2. 在 Steam 创意工坊订阅 NinjaSlayer，或从 [GitHub Releases](https://github.com/2223M1/NinjaSlayer/releases) 下载最新版本并解压到游戏的 `mods` 目录。
3. 启动游戏，在 Mod 管理器中启用 `STS2-RitsuLib` 和 `NinjaSlayer`。

## 兼容性

- Slay the Spire 2: `0.109.x`
- RitsuLib: `0.4.62` 或更高兼容版本
- 当前提供简体中文本地化

## Development

**Prerequisites**

- [.NET SDK 9](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Godot 4.5.1 Mono](https://godotengine.org/download/archive/4.5.1-stable/)
- Slay the Spire 2 `0.109.x`

```powershell
git clone https://github.com/2223M1/NinjaSlayer.git
cd NinjaSlayer
dotnet restore .\NinjaSlayer.csproj
dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal
```

普通构建只编译代码。完整测试、打包、安装、版本发布和受保护实机验证流程见 [开发与发布指南](Docs/development.md)。

测试版可从干净且已推送的 `main` 一键发布到 GitHub 和 Steam：

```powershell
.\tools\release\Publish-QuickRelease.ps1 -Version 0.1.2 -Confirm
```

## Links

- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3761570842)
- [Card Catalog](Docs/card-catalog.md)
- [Privacy](Docs/privacy.md)

本仓库未声明开源许可证。
