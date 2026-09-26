# 选择海报

默认使用官网原构图，以 1920×1080 画面右边缘中点 `(1920,540)` 为锚点
等比放大 10%，再整体左移 50 屏幕像素；在该版基础上以画面下边缘中点
`(960,1080)` 为锚点缩小 5%，最后向右平移 42 屏幕像素以隐藏围巾截口。
当前人物位置 `(1052.18409092,36.76727273)`，缩放 `0.956175`。
原构图位置为 `(1135,35)`，缩放 `0.915`；锚点通过真实 AnimatedBg 变换反算。
四个放大构图 A/B/C/D 未采用。围巾额外延长量由 420 收短为 140 素材像素，
保留原画布坐标的上下轮廓宽度规律；领口接触处至源 x568 不动。
贴图、UV、网格拓扑、骨骼权重不变，8 秒循环摆幅维持 `±1.2°`。

“忍者杀手设置 → 外观 → 漫画版选择海报”默认关闭。
开启时使用原漫画人物与原手部动画；背景和粒子不随切换重启。
只影响选择界面，不影响战斗形态、头像、朝向或音效。

共享朱红背景去除试作的 Spine 余烬点，采用原版铁甲战士 `ash2` / `ash3`
两层 CPUParticles2D，参数、色带和前景层序均与原版相同。

来源与哈希：`NinjaSlayer/art/character_select/SOURCE.json`。
`import_poster.py` 从本地已确认生产目录重新导入；不会安装或上传。

验证：

```powershell
python tools/character-select/verify_poster.py
dotnet test Tests/NinjaSlayer.LogicTests/NinjaSlayer.LogicTests.csproj -c Release -p:NinjaSlayerHostChannel=stable --filter FullyQualifiedName~NinjaSlayerSettingsTests
dotnet build NinjaSlayer.csproj -c Debug -p:NinjaSlayerHostChannel=stable
```

原生 UI 冒烟测试先正常导入项目并导出 `build/select-poster-runtime/NinjaSlayer.pck`，
再编译 `tools/smoke-harness/NinjaSlayer.SelectPosterProbe/SelectPosterProbe.csproj`，
运行 `preview.ps1`。它使用独立 APPDATA 和游戏文件的只读硬链接；替换测试 DLL/PCK
之前会先断开该副本的硬链接，不写实际安装目录。测试会关闭隔离环境的遥测，
检查默认值、12 次即时切换、重新创建场景、原手部动画、粒子顺序及设置页导航。

原生测试还会保存下侧锚点（包含最终右移）的误差及围巾两个摆动极值的实机截图。
原生测试后先运行 `python tools/character-select/check_scarf_bounds.py`，
以实测场景变换检查 8 秒循环的 961 个相位，要求围巾截断边界始终在屏幕右侧之外；
再运行 `python tools/character-select/verify_poster.py --require-runtime`。
安装和发布由用户之后统一进行。
