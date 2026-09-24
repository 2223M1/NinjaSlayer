# 远程处决与近战保持验证

## 基线与范围

本地分支 `codex/card-board-v115-integration`，源码起点为
`94c9807515ec6006e5d85d784d3e4af1782fda07`。候选 DLL 使用 0.3.2 本地版本标识，
SourceRevision 为上述基线并包含本轮未提交差异，不是干净发布包。
本轮最终本地提交包含实现、测试与本记录；没有推送或上传。

按最后确认重新纳入原版伤害充能球。忍者杀手及已支持友军仍要求清空剩余主要敌人，
敌方仍以对忍者杀手的实际致死伤害进入反向处决；未扩大其他真人角色的资格。
没有修改伤害数值、卡牌、库存规则、随机目标、存档或联机协议。
已有卡牌目录、本地化及卡图重绘要求是其他工作区修改，留在工作树，不纳入本次提交。

## 实现与维护边界

| 文件／类型 | 本次变化 |
| --- | --- |
| `FinisherEligibilityService` | 删除小刀／手里剑排除入口；`CreateCompanionSession` 合并更名为 `CreateActionSession`，继续使用原有预测、资格与登记接口 |
| `FinisherRangedAction` | 动作范围内保存来源、视觉引用、延迟释放与抵达任务；恢复嵌套调用上下文并释放临时投射物；精确识别卡牌和怪物行动 |
| `NinjaSlayerFinisherCinematic`、`NinjaSlayerFinisherPatches` | 远程伤害等待已知抵达，再执行原生命令；来源不同的附带卡牌伤害不计为当前远程主命中；退出战斗后取消未抵达的伤害 |
| `FinisherSessionRequest`、`FinisherSession` 及 `.Presentation` | 复用同一处决记录和死亡提交；远程不领取近战位移，命中时强调受击者；完成后恢复冻结资源；近战分支和时间常量保持 |
| `FinisherImpactVfxFreezeLease` | 扩展现有冻结租约，纳入明确归属的投射物；暂停 GPU／CPU 粒子，恢复原速率；原生绑定节点的 Tween 随节点暂停与恢复 |
| `NinjaSlayerDeathClassifier`、`DarkNinjaAttackExecution` | 敌方远程致死关联当前动作；死亡斩保留原来的出屏／返程和结算帧，不额外接近；暗打及居合仍为近战 |
| `FinisherRangedPatches`、`Entry` | 必需事务内注册 Lightning／Glass 被动、Lightning／Dark／Glass 激发、小刀视觉与等待；preview 另接原生投射物抵达回调 |
| `ShurikenCombat`、`NinjaSlayerCombatAnimations` | 库存逐发接入，显式传递延迟发射的视觉归属；原版小刀在忍者杀手上使用投掷姿势，保留快速出牌接线 |
| `YukanoCombatAnimations`、`SawatariWeaponVisuals.Flight` | 命中前等待原投射物，Doom 内保留显示；已被接住的大刀仍由接收方持有 |
| `YamotoKokiMonster`、`YamotoKokiOrigamiMissile`、`YukanoMonster`、`SawatariMonster` 及 `.Weapons` | 共用动作入口；紙鹤实际抵达和爆炸前摇不变；泽渡投刀结束后不再操作已退出战斗的武器节点 |
| `OrbContractRunner.RangedFinishers`、`.AimPose`、主 runner | 增加原生行动区分、延迟释放、充能球 RNG／回调次数及冻结恢复契约 |
| `RitsuLibContractTests.ContractRunner` | 更新双宿主必需／关键目标数量，继续验证真实安装与失败回滚 |
| `Docs/combat-action-timing.md` | 同步共用入口及远程规则；不修改卡牌规格 |

原生行为证据来自 0.107.1／0.111.0 的 `OrbModel` 三个伤害球实现、
`NShivThrowVfx.PlaySequence`、`AttackCommand.Execute` 及列出的怪物行动方法。
小刀保留原有 0.15 秒抵达及 2 秒清理等待，transpiler 只观察两个精确的
`Cmd.Wait(float, CancellationToken, bool)` 调用；没有复制原版粒子序列。
preview 的 `NVfxProjectileHandler.Create` 存在抵达回调，stable 没有该类型，
差异就地使用编译分支。没有全局时间倍率、兼容图、指纹平台、轮询、锁或 GC 控制。

新增私有访问只涉及小刀 async 状态机的 `MoveNext` 和捕获的原实例字段，用于保留
原生等待及清理时序；两个精确宿主均实际安装成功。已有原生命令、镜头及死亡保护
私有接线继续由原功能拥有。本次不新增第二套处决管理器。
动作范围的 `AsyncLocal` 服务于卡牌、球、宠物和怪物的嵌套异步调用，
`TaskCompletionSource` 对应实际释放／抵达事件，不创建后台伤害线程。

生产 C#（排除 Tests、tools、build 和生成目录）从 **500 文件／853 声明／61,010 行**
变为 **502 文件／860 声明／61,437 行**。声明按行首类型声明计数。
完整文件路径及源码文件校验值在[结构化证据](ranged-finishers-20260924-evidence.json)中。

## 执行环境

| 项目 | 实际输入 |
| --- | --- |
| stable | 0.107.1，MVID `97f10687-c306-4798-ab75-8b9f23f34dfb` |
| preview | 0.111.0，MVID `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` |
| 编译／产品契约 | 各宿主 RitsuLib SDK 0.5.12；Godot 4.5.1 Mono；独立 .NET 9 |
| 本机图形游戏 | stable 0.107.1、Workshop RitsuLib 0.6.2；Steam 关闭、独立配置、ENet 仅 loopback、阻止 Internet 出站 |
| 资源包 | 沿用已验证 PCK；本轮没有资源变化，不将复用包称为重新导出 |

仅本机执行，没有使用 ninja5080，没有捕获截图。图形回归由本地测试 Mod 驱动真实游戏，
不是人工观感验收；基线回合和玩家出牌使用原生联机 Action，后续演出夹具按相同顺序
调用实际卡牌／球／怪物入口，并对照所有客户端结果。

## 实际结果

| 检查 | 结果与证据 |
| --- | --- |
| 逻辑测试 | 356 通过、0 失败；`build/ranged-logic.log` |
| 仓库、兼容配置、构建边界、差异格式 | 全部通过；`validate-repository.mjs`、`sync-compatibility.mjs --check`、`test-build-boundaries.mjs`、`git diff --check` |
| 双宿主产品构建 | 0 警告、0 错误；`build/ranged-{channel}-build-accepted.log` |
| 最终候选 DLL 产品契约 | stable／preview 全套通过；`build/ranged-{channel}-contract-accepted.log` |
| RitsuLib 安装与回滚契约 | 双宿主通过；`build/ranged-{channel}-ritsu-accepted.log`；stable 120／87、preview 121／88 个必需／关键目标 |
| SmokeDriver | 双宿主构建通过；`build/ranged-{channel}-smoke-accepted.log` |
| 双进程原生 ENet 契约 | 双宿主通过；`build/ranged-enet-{channel}/` |
| 本机单人／双人 | 初轮完整回归通过；`qa-20260924-184354`、`qa-20260924-184715`；使用早期 DLL，校验值单列在结构化证据中 |
| 最终本机四人 | `qa-20260924-185658` 四客户端全部通过，战斗检查点、随机结果和结算结果一致；使用最终 stable DLL |

新契约覆盖十二种怪物在同一模型中的远程与其他行动区分、实际延迟释放后才加入的
抵达任务、原版小刀等待接线、原版三种伤害球的被动及两次激发、非忍者杀手隔离、
RNG 消耗、Glass 原生衰减、Dark 原生最低生命目标。冻结测试验证目标区域外明确归属
的投射物、GPU／CPU 原速率、绑定 Tween 暂停及释放后恢复。

四人图形回归包含友军竹击、双刀、居合、射箭、手里剑的普通／快速模式真实扣血帧，
以及随后各自的处决；检查 UI 根节点、命中位置和死亡提交。
另有 11 组近战／远程用例：普通打击、小刀、强手里剑、大刀回投、库存手里剑、
闪电被动／激发、黑暗连续两次激发、玻璃被动／激发、纸鹤。
黑暗第一发把生命从 12 降至 6 时没有处决，第二发才进入。
有实体投射物的用例观察到 Doom 冻结；结束后会话全部释放。

四组敌方实际致死攻击分别为原版劫掠者弩手 Fire、黑暗忍者死亡斩、泽渡射箭、泽渡投刀，
依次击杀四名玩家。四客户端均使用远程分支，攻击者没有被移动到近战位置。
保留原生结束回合投票和泽渡决斗转场回归。

### 发现并修复

第一次四人全队败北夹具 `qa-20260924-185246` 在最后一次泽渡投刀后，
四客户端均报 `SawatariWeaponVisuals.Refresh` 访问已释放 `Node2D`。
致死结算已结束战斗，后续投刀代码仍刷新武器。修复位于 `ThrowMove` 的实际攻击等待后，
检查战斗结束及原 combat 归属，再执行强化／发牌／刷新；已死亡的泽渡不刷新姿势。
相同四人夹具复测通过，失败记录保留。

契约迭代还发现了小刀等待方法漏掉可选 `bool` 参数、测试重载选取及两宿主 RNG
测试读取差异，均修正后重跑。没有根据失败结果放宽生产目标或静默跳过补丁。

## 候选校验值与限制

| 文件 | SHA-256 |
| --- | --- |
| stable DLL | `049a1e2e110dd63909c61fd850ff70d8cc9ad17fceee009e82463d85fa5cc51f` |
| preview DLL | `4fbe96ca6fee227c724057e33027a66c2ae73da28058d8ebbe3e6ff5e7537434` |
| 复用 PCK | `ca164abd1c25de73c8e59c785f3400edfa0d7b17c5dee66bff534cd1b022582c` |
| 实机 RitsuLib | `e3959f1746fcb7aa404cb9cd861443dc540e8488b50f7d156eacbe79925156b6` |

没有运行 preview 图形实机、公网／跨机器联机，也没有逐一实测其余十种原版远程怪物的
完整演出、所有暂停／断线／任意帧退出保存组合。它们的行动区分由双宿主实际模型契约
验证，不记为实机通过。近战依据代码分支保持、原有动作契约及本机命中回归确认，
没有做截图逐像素比较。

日志仍保留原有兼容渲染器粒子提示、RitsuLib 信号提示及退出资源诊断；
脚本主动造成败北的夹具有原版 `How did the game end??` 诊断。
这些不记为日志完全无警告，也不扩展为本轮无关修复。
