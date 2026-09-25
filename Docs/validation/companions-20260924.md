# 友军意图、站位、行动与处决验证

## 范围与基线

- 本地分支 `codex/card-board-v115-integration`，起点 `acb3fc5e1770beeea1973ec94d0271897b0ede42`。
- 保留此前卡牌、半奈落归档和事件演出改动；本次不修改卡牌效果或卡牌目录。
- 只在本机验证；不使用 ninja5080，不截图，不推送 GitHub 或 Workshop。
- 构建包元数据为本地候选 `0.3.2`，SourceRevision 标记上述基线并包含本次未提交差异；不宣称它是干净发布提交。最终本地提交包含本记录。

## 实现

三位友军仍是原生宠物。没有新增 Player、无限生命／回避 Power、网络协议或存档字段。真实人数、奖励缩放和结束回合投票保持原生语义。

攻击意图使用原生图标、动画和粒子，只局部应用黄色 shader。非攻击意图及敌对沢渡恢复原材质。小季保留说明文字，使用原生动画键；不再以本地化前缀作为动画名。意图代次和退出清理统一由现有逻辑更名后的 `CompanionIntentLifecycle` 负责。

删除自建网格、宽度替代和复制的宠物布局；仅调整原生 `PositionPlayersAndPets` 的输入顺序和两处排列分组。每个视角为本地玩家、按加入顺序的全体友军、其余真人。纸鹤不占完整位置，奥斯提继续走原生布局。

只解除收招、返回和粒子余效的等待。伤害、目标随机数、召唤、治疗和死亡仍由等待中的玩法回调完成。纸鹤保留飞行及爆炸前摇；没有把伤害提前到出手时。三人统一使用 `CreateCompanionSession` 和既有处决 ledger／死亡收尾，小季近身居合、沢渡竹击／双刀及尤佳乃箭／手里剑保留各自动画命中时点。

## 删除与保留

- 删除 `YamotoKokiGridLayoutMath`、其两项重复网格数学测试及项目引用。
- 删除 `YamotoKokiIntentVisuals` 额外图标路径及小季两张紫色意图图片、导入文件。
- 删除纸鹤 `MissileOperation`、启动整批后等待整批及显式等待死亡动画尾段的流程。
- 删除两个友军遗物回合回调中记录异常后继续执行的路径。
- `YamotoKokiIntentLifecycle` 更名为 `CompanionIntentLifecycle` 并纳入友军沢渡；不建立第二份状态所有者。
- 小季专属处决创建入口合并为三人共用入口，三个调用者使用同一处决场景；动作分支仅保留表现差异。
- 保留局部 `_intentFadeTween` 读取，用于终止已实际发生的旧意图淡出与新意图显示竞争；保留原生布局精确目标 transpiler，并在两处调用不匹配时拒绝安装。
- 伤害 API 的 stable／preview 差异仍为原位置编译分支；没有新增全局兼容、方法指纹、同步锁、轮询或 GC 控制。

## 执行环境

行为参照为两个活动宿主源码导出中的 `Core/Nodes/Rooms/NCombatRoom.cs::PositionPlayersAndPets`、`Core/Nodes/Combat/NIntent.cs::UpdateVisuals`、`NCreature::PerformIntent`、`Core/Models/MonsterModel.cs::GetIntents/SetUpForCombat` 和 `Core/Commands/CreatureCmd.cs::TriggerAnim`。仅抽取布局分组、动画键和等待时序；没有复制原版管理层。两个宿主的这些接入点均由最终候选契约实际安装验证。

| 项目 | 版本／标识 |
| --- | --- |
| stable | 0.107.1；宿主 MVID `97f10687-c306-4798-ab75-8b9f23f34dfb` |
| preview | 0.111.0；宿主 MVID `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` |
| 编译及契约 RitsuLib | 0.5.12 对应宿主包 |
| stable 图形实机 RitsuLib | 0.6.2，已安装 Workshop 构建 |
| 产品契约 | Godot 4.5.1 Mono，独立 .NET 9 运行时 |
| 图形实机 | 本地原版游戏进程，独立测试配置，Steam 关闭，ENet 仅 loopback；测试程序阻止 Internet 出站 |

## 验证记录

| 检查 | 结果／证据 |
| --- | --- |
| 逻辑测试 | 356 通过、0 失败；删除的 2 项是旧自建网格测试，由原生布局对照替代。`build/companion-logic-final.log` |
| 仓库／兼容／构建边界 | `validate-repository.mjs`、`sync-compatibility.mjs --check`、`test-build-boundaries.mjs`、`git diff --check` 通过 |
| 双宿主产品打包 | stable／preview 均成功；`build/companion-{channel}-package-final.log` |
| 最终候选 DLL 产品契约 | stable／preview 全套通过；`build/{channel}-contract-companion-accepted.log` |
| RitsuLib 注册与回滚契约 | 双宿主通过；`build/verification-v115/ritsu-{channel}-companion-accepted.log`。必需目标总数仍为 113，其中 80 个标为关键目标；总事务仍不可部分安装 |
| 原生双进程 ENet 契约 | 双宿主通过；`build/verification-v115/enet-{channel}-companion-accepted/` |
| SmokeDriver | 双宿主构建通过；`build/verification-v115/{channel}-smoke-companion-accepted.log` |
| 本机 stable 图形宿主 | 原生单人、双人、四人模式各通过两次完整回合及所有玩家的原生出牌 Action；不同持有者的小季、尤佳乃和事件沢渡同场；各客户端状态检查点一致 |
| 命中与处决实机 | 单／双／四人中分别执行竹击、双刀、居合、射箭、手里剑；普通及快速模式在实际扣血回调检查人物命中姿态／投射物抵达和 UI 根位置；五种攻击随后均触发处决、提交死亡并释放会话 |

图形宿主由本地测试 Mod 自动驱动，不是人工操作或截图验收。[实机结构化结果](companions-20260924-evidence.json)记录每个客户端的站位、伤害检查点、命中时间和处决结果。最终记录包含 `qa-20260924-171459`（单人）、`qa-20260924-171806`（双人）及最终 `qa-20260924-172859`（四人）。双／四人比较只比较确定性战斗结果，不要求各窗口渲染时间戳完全相同。

前两组实测使用 stable DLL `89690d2bdad1165c76ebbfe7ad704e1ec9e62a2466d32caa5f4c52a10a8b1a01`；随后只追加沢渡决斗转场恢复友军意图的修复。最终候选在 `qa-20260924-172859` 重跑完整四人流程，全部四客户端通过并保持战斗检查点一致；全员原生结束回合后才出现选项，经原生联机选项进入决斗，场上只剩新敌方沢渡，旧友军实体已移除，敌方意图恢复原材质，小季和尤佳乃意图重新显示。

产品契约另外覆盖四个本地视角、居中／普通排列与两档缩放，和同等尺寸的原生布局逐点比较；覆盖奥斯提、纸鹤不占位、前后排色调、原生意图动画键、攻击／召唤／防御／治疗节点复用及敌方材质恢复。三位友军分别验证旧回调失效、重新开始和清理代次。竹击契约检查每段命中位置、普通／快速／瞬时模式、与受击重叠及最后命中后返回任务不再阻塞。

修改文件清单随结构化结果中的 `changedFiles` 保留；其中意图生命周期是更名及扩展，非并存的两个实现。

本次未改变卡牌内容，卡牌规格、注册数量和目录无需更新。

### 候选校验值

| 文件 | SHA-256 |
| --- | --- |
| stable `NinjaSlayer.dll` | `9f2c53837e42bc1df9b3447ce95827ed2b88545e8d22ab566423a137ee6e66bf` |
| preview `NinjaSlayer.dll` | `db37d787f71aee531376cd47be6013fa26915867a327dc1bb0ddfe0eec4e65d1` |
| 共享 `NinjaSlayer.pck` | `ca164abd1c25de73c8e59c785f3400edfa0d7b17c5dee66bff534cd1b022582c` |
| 实机 `STS2-RitsuLib.dll` | `e3959f1746fcb7aa404cb9cd861443dc540e8488b50f7d156eacbe79925156b6` |

PCK 包含黄色意图 shader，不包含已删除的两张紫色图标。产品 C#（排除 Tests、tools、build 和生成目录）从 501 文件／857 个声明／61,250 物理行，变为 500 文件／853 个声明／61,010 物理行；声明按行首 class／struct／interface／enum／record 统计。

失败迭代保留在 `build/`，没有删除失败证据。已发现并修正沢渡 canonical 预加载读取未绑定 Creature、重复 SetUpForCombat、小季说明前缀误作动画键，以及沢渡决斗转场未恢复其他友军意图。测试接线另修正了原生结束回合 Action、单人夹具启动、奥斯提夹具节点与必需 Patch 数量断言。投射物命中探针按发射时目标位置判定，不把受击位移后的目标坐标当成弹道原始终点。

## 实测范围限制

- 不包含 preview 图形实机、跨机器或公网联机。
- 多人实机没有逐一穷举暂停、反伤／分裂每一帧、断线恢复与所有存档位置；相应原有契约通过不等于这些组合的人工实测。
- 没有新增保存字段；本轮不把普通契约的保存测试当作四人事件在任意帧退出重载的验收。
- 兼容渲染器粒子能力提示、RitsuLib 设置弹窗断开不存在的 `size_changed` 信号，以及退出时原生资源诊断单列保留；不宣称日志完全无警告。
