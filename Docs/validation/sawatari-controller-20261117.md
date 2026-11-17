# 沢渡事件返回战斗的手柄修复，2026-11-17

用户反馈手柄仍会失灵，但无法确定发生阶段或提供本次版本。官网公开数据中可检索的仍是 1.2.2 的旧法语反馈，因此本记录不将本次故障归为已确认的特定设备问题。

## 可复现问题

1.2.3 修复了选项出现时的事件焦点，却遗漏了返回战斗的通知。选项入场后，`ActiveScreenContext.Update()` 会调用原版 `NCombatRoom.OnActiveScreenUpdated()`，将嵌入战斗的 `FocusBehaviorRecursive` 设为 Disabled，并调用 `NCombatUi.Disable()`。

选择决斗后，原有 `SawatariEventUi.Hide()` 只调用 `HideEventVisuals()`，后续 `ExitDecisionState()` 只恢复 UI 可见性。战斗界面能够显示，但控制器导航和控件仍可能保留禁用状态。另外，隐藏选项与决斗开场演出结束之间有异步等待，仅在选项消失时申请焦点不足以保证之后显示的手牌得到焦点。

上轮测试将战斗和事件节点留在场景树外，没有订阅两种房间的原版界面通知，手牌焦点节点也没有挂在嵌入战斗下面，因而漏掉了这条真实禁用路径。本轮补齐节点层级和原版监听后，修改前的生产 DLL 在“返回战斗恢复导航、控件和焦点”断言上失败。

## 修复

- 隐藏沢渡事件选项后通知 `ActiveScreenContext.Update()`，恢复原版的战斗导航和控件状态。
- 决斗开场演出结束、战斗 UI 重新显示时再次通知，让原版控制器管理器在可见的手牌区恢复焦点。
- 继续使用原版的顶层界面选择和输入映射，没有为特定手柄硬编码键位，也没有增加定时轮询或新补丁目标。

事件概率、奖励、卡牌、本地化、资源和存档没有变化，卡牌目录无需更新。本轮未上传 Workshop，未修改官网反馈状态或再次发送 1.2.8 公告。

## 宿主依据

已核对 `../Slay the Spire 2/Slay the Spire 2 v0.107.1/src/` 和 `v0.113.1/src/` 下的以下原版成员。RitsuLib 事件／补丁教程不定义这些界面节点之间的导航契约。

- `Core/Nodes/Rooms/NCombatRoom.cs` 的 `OnActiveScreenUpdated()`：切换递归导航并启用／禁用战斗 UI。
- `Core/Nodes/Rooms/NEventRoom.cs` 的 `OnActiveScreenUpdated()`：切换事件房间递归导航。
- `Core/Nodes/Screens/ScreenContext/ScreenContextUtils.cs` 的 `UpdateControllerNavEnabled()`：正式版显式启用当前房间；预览版将当前房间及其祖先恢复为 Inherited。
- `Core/Nodes/Events/NCombatEventLayout.cs` 的 `HideEventVisuals()`：切换 `HasCombatStarted`，没有发布界面更新通知。
- `Core/Nodes/CommonUi/NControllerManager.cs` 的 `OnScreenContextChanged()`：通过延后回调选择当前界面默认焦点。
- `Core/Nodes/GodotExtensions/NodeUtil.cs` 的 `TryGrabFocus()`：隐藏控件只延后一次尝试，不会等待整段决斗演出结束。

生产修复在两个宿主上相同，没有新增兼容分支。

## 验证

冻结生产源码为 `7da547f387c90fd71075d38e50bdc788baf6d076`，工作区干净后重新构建并运行。编译基线 RitsuLib 0.6.13，测试加载本机当前 Workshop RitsuLib 0.6.14。哈希与日志摘要见 [证据文件](sawatari-controller-20261117-evidence.json)。

| 检查 | stable 0.107.1 | preview 0.113.1 |
| --- | --- | --- |
| Release 构建 | 通过 | 通过 |
| 原版节点层级、界面通知及手柄输入专项 | 通过 | 通过 |
| 生产补丁安装、故障注入、回滚契约 | 通过 | 通过 |
| 手柄专项最终 Godot 错误／警告 | 0／0 | 0／0 |

专项覆盖全部四种动画速度、单／双选项、上下导航和确认、重复释放、鼠标／手柄切换、隐藏手牌等待后恢复战斗、旧入场回调、地图覆盖及关闭后返回、无关事件隔离。它保留原版选项入场、GUI 输入与确认回调、房间界面监听及递归导航；替换图像／声音／投票外观，在事件提交边界记录选项，在战斗 UI 的 Enable／Disable 边界记录调用，没有初始化所有战斗控件。

两个构建都有既有的 `ArchitectExecutionCinematic._eventModel` 未使用字段警告，未修改无关代码。补丁契约包含预期故障注入日志；手柄专项最终日志无错误或警告。兼容配置、仓库一致性和 `git diff --check` 通过。

未进行实体 PS5、Xbox、国产手柄及 Steam Input 驱动测试，未运行完整图形游戏或联机验证。完整游戏烟测启动器要求提升权限，本会话不是管理员，未绕过其隔离要求。这里证明的是三类手柄共用的游戏内导航切换修复，不能据此承诺所有设备和映射都不会出问题。
