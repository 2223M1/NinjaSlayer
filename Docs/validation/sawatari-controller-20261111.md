# 沢渡事件手柄输入修复，2026-11-11

官网最新法语反馈 `8c1b0e08-afa7-4496-a559-2d5fbca16e1c` 于北京时间 2026-11-11 05:26 提交，报告首场绿色忍者事件的按钮无法正常通过手柄操作。公开记录为游戏 0.107.1、模组 1.2.2；私有附件确认 PS5 手柄及 Fast 动画速度。附件只保存在忽略的 `Infrastructure/telemetry-worker/feedback-downloads/`，不提交原始日志、截图或存档。

## 原因与修复

原版 `NCombatEventLayout.HideEventVisuals()` 将 `HasCombatStarted` 设为 true。此前模组返回沢渡事件选项时仅恢复节点可见性，没有恢复这一状态，因此 `ActiveScreenContext.GetCurrentScreen()` 仍返回嵌入战斗房间，默认焦点也仍指向战斗手牌区。按钮的 `AnimateIn()`／`EnableButton()` 只在入场结束后开放鼠标过滤，并不会自动恢复事件的手柄焦点。

- `SawatariEventUi.Show()` 在显示选项前，通过该布局的确切私有 setter 恢复事件界面状态。选项隐藏时继续由原版 `HideEventVisuals()` 恢复战斗状态。
- `SawatariEventOptionReadyPatch` 在当前沢渡页面第一个原版选项完成入场时通知 `ActiveScreenContext.Update()`，让原版控制器管理器选择当前最上层界面的默认焦点。它涵盖普通、快速、瞬间模式，不设置固定延时，也不强行聚焦被地图覆盖的事件。
- 回调只接受当前布局内可见的沢渡首选项，隐藏的旧页面、其他事件和后续按钮不会抢焦点。补丁属于原有 required 事务。

未改变事件出率、选项效果、卡牌、本地化、存档结构或资源；卡牌目录无需更新。

## 精确宿主依据

检查 `../Slay the Spire 2/Slay the Spire 2 v0.107.1/src/` 与 `v0.111.0/src/` 中以下成员，两版的布局状态、选项入场及事件／战斗界面选择契约相同：

- `Core/Nodes/Events/NCombatEventLayout.cs`：`HasCombatStarted` 私有 setter、`DefaultFocusedControl`、`HideEventVisuals()`。
- `Core/Nodes/Events/NEventLayout.cs`：`AddOptions()`、`AnimateButtonsIn()`。
- `Core/Nodes/Events/NEventOptionButton.cs`：`AnimateIn()`、`EnableButton()`、`OnRelease()`。
- `Core/Nodes/Screens/ScreenContext/ActiveScreenContext.cs`：`GetCurrentScreen()`、`FocusOnDefaultControl()`。
- `Core/Nodes/CommonUi/NControllerManager.cs`：`OnScreenContextChanged()`。

RitsuLib 的自定义事件和场景教程未定义这一私有布局切换契约。生产代码仅在沢渡 UI 本地绑定确切 setter；小型自动属性 getter 可能已被宿主调用方内联，直接恢复状态能同时保证界面选择和默认焦点正确，无须分别补丁两条读取路径。

稳定版控制器状态为 `IsUsingController`，预览版为 `InputType`／`IsUsingDirectionalNavigation`。该区别只出现在测试输入设置中，生产修复沿用原版界面通知，无新增宿主分支。

## 验证

基于 `d912c41bacd075b85ffdff9b6168edb53e6ae753` 的本地未发布修改，候选构建标记为 `1.2.3-dev.2+gd912c41b.dirty`。验证材料位于 `build/validation-sawatari-controller-20261111/`。

| 项目 | stable 0.107.1 | preview 0.111.0 |
| --- | --- | --- |
| 生产 Release 构建 | 通过，0 警告／0 错误 | 通过，0 警告／0 错误 |
| 沢渡手柄行为契约 | 通过，无 Godot 错误／警告 | 通过，无 Godot 错误／警告 |
| 生产补丁安装、故障注入及回滚 | 通过 | 通过 |
| required／critical 目标数 | 144／111 | 145／112 |
| 宿主 MVID | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` |

`OrbContractRunner.SawatariController.cs` 使用实际宿主的布局选项创建、导航邻居、入场 Tween、焦点信号、GUI 输入、确认回调和 `ActiveScreenContext`，只替换图像／声音／投票外观，并在事件选择提交边界记录选中索引。测试首先重现 1.2.2 的“仅显示选项”旧路径，确认仍处于战斗界面且没有选项焦点；随后验证修复路径：

- `None`、`Normal`、`Fast`、`Instant` 全部速度，分别覆盖两选项的战后决定页和单选项的决斗结果页。
- 原生上下导航、两个选项的确认、重复释放不重复选择。
- 鼠标模式不抢焦点；重新切回手柄时正确聚焦事件。
- 隐藏选项恢复战斗焦点；旧入场动画完成后不抢焦点。
- 地图位于最上层时保持其优先级；关闭地图后恢复事件焦点。
- 无关事件保留原行为。

通过 `Run-Contracts.ps1` 并设置 `NINJASLAYER_CONTRACT_SAWATARI_CONTROLLER=1` 运行。测试挂载当前日常游戏资源包及已发布 1.2.2 模组资源包，执行的 DLL 均为本轮对应宿主新构建；本轮没有资源改动。新增 required 补丁计数同步到 `NinjaSlayer.RitsuLibContractTests`。补丁契约的故障注入错误属于预期测试结果，不等同于正常运行错误。

兼容配置检查、仓库一致性检查与 `git diff --check` 通过。未进行实体 PS5／Steam Input 验收或完整游戏录屏；这里验证的是 Godot 中实际焦点和输入分发行为。

本轮没有上传 Workshop、更新官网发布目录或向玩家发送回应；线上仍为 1.2.2，反馈状态未提前标记为已发布修复。
