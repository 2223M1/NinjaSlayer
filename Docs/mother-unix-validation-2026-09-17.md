# Mother UNIX密钥预见时序验证（2026-09-17）

## 修改与候选

基线为 `main@c0e5f7dc491077ad8aaec17f4ad1bd73217c3a98`，加本轮未提交修改。
没有提交、推送、上传 Workshop 或修改已安装的游戏模组；保留无关的 `Docs/analysis/`。

遗物从 `AfterPlayerTurnStart` 改为原生 `BeforeHandDraw`，保留持有者判断、闪烁和 awaited `ScryCmd.Execute`。
同步中英文遗物与关键词说明及卡牌目录。预见仍使用原生批量弃牌；消耗路径不触发奇巧。
没有新增生产补丁、状态或存档字段。

核对两个匹配版本的原版 `src/Core/Combat/CombatManager.cs` 中 `SetupPlayerTurn`：
重置能量 → 等待 `Hook.BeforeHandDraw` → 计算正常抽牌数及首回合固有排序 → 抽牌 → `Hook.AfterPlayerTurnStart`。
源码来自仓库相邻的 `Slay the Spire 2/Slay the Spire 2 v0.107.1/src` 和 `v0.111.0/src`。
沿用该原生时序，没有额外调整首回合固有排序或其他遗物顺序。

候选与日志目录：`build/mother-unix-20260917/`。
`candidate-evidence.json` 记录生产源码、文本、测试文件及 DLL/PCK 的 SHA256。

| 宿主 | MVID | 候选 DLL SHA256 |
| --- | --- | --- |
| stable 0.107.1 | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `4C4E6D1D956CED42881FB56728644669C177545B767C4B3980450AAF7D91E56D` |
| preview 0.111.0 | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` | `AA74C67D402617CE9CA967B66C3310ED8D035FDD27B38A4F05ECFBF97C77C5FC` |

产品 DLL 位于该目录的 `stable/Release/NinjaSlayer.dll`、`preview/Release/NinjaSlayer.dll`。
各自的 SmokeDriver 通过 `NinjaSlayerAssemblyPath` 引用对应候选。编译使用固定 RitsuLib 0.5.12。
资源包 `NinjaSlayer.pck` SHA256：`A051E7A145311615632FF20CB3301C8D4C7A31A2BFBE5DE33EE9D2D9C8C4AA63`。

## 产品契约与构建

- 双宿主产品与 SmokeDriver 构建通过；完整 Orb 产品契约在两端通过。
- 新增测试直接执行原版 `SetupPlayerTurn`，验证首回合／第二回合、不弃牌／弃两张／奇巧嵌套预见／不足3张／空抽牌堆。选牌时正常手牌仍为空，结束后抽到预见处理后的前5张，不在抽牌后重复预见。
- 同一战斗加入第二名玩家，通过原生抽牌前 Hook 确认只有持有者触发预见。此项是双玩家模型契约，不是双客户端 ENet 实机。
- 既有原生弃牌批次、嵌套预见、消耗不触发奇巧及中英文基础／升级牌与悬停格式契约通过。
- `node tools/validate-repository.mjs`、`node tools/sync-compatibility.mjs --check`、`git diff --check` 通过。
- 产品构建仍报告既有 `SawatariEventSession.cs:380/382` 的两处可空性警告；SmokeDriver 构建零警告、零错误。本轮没有改动相关泽渡代码。

## stable 可视实机

最终通过记录在 `build/mother-unix-20260917/live/run-B-04/`。
Windows stable 0.107.1、RitsuLib 0.6.2、Vulkan，加载 NinjaSlayer、RitsuLib、SmokeDriver。
使用隔离本地存档、关闭 Steam，并对隔离游戏进程设置出站阻断。
`firewall-evidence.txt` 核对本次临时可执行文件的完整路径、启用状态及出站阻断；退出后移除本次专属规则。

- 通过原版结束回合流程，连续检查不弃牌、弃两张牌、弃掉手里剑生成触发奇巧并再次预见。实际选牌界面打开期间没有正常抽牌；两层选择完成后才抽5张，并获得2层手里剑。
- 使用原生 `SuspendSelectorForTest` 暂停 AutoSlay 自动选牌，以实际选牌界面及确认按钮完成选择，没有测试替代选择器绕过界面。
- 12个检查点通过，包含正常退出。第三次通过运行的 `mother-unix-tooltip.png` 与 `nested-sly-selection.png` 已人工查看，确认中文新说明可见、嵌套选择时手牌仍未抽取。最终第四次运行同样保存了全部截图及录像。
- 前两次运行分别暴露测试准备误处理永久牌组和 AutoSlay 自动选择绕过界面的问题，仅修正测试接线。第三次玩法全部通过，但复用的旧防火墙规则指向旧目录；修正本地启动脚本规则后，以相同候选完成第四次通过运行。

未执行 preview 可视实机、首回合可视选牌或双客户端选牌同步；首回合与持有者隔离由上述双宿主产品契约覆盖。
未重跑不受本次改动影响的纯逻辑测试。实机退出仍存在既有 Godot 节点／资源清理警告，不将日志描述为零错误。
