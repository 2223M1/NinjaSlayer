# 九模组兼容验证 — 2026-09-26

## 候选与结论

本地基线 `7ef64c976636f6d4963c893f01be80328be97972`，分支 `codex/cfc-singleplayer-seeds`。工作区为 `C:/Users/theon/.codex/worktrees/cfc-singleplayer-seeds/NinjaSlayer`，包含本轮兼容修改及此前单人不歪种的未提交差异。程序集版本仍为 0.3.2；日志 `candidateSha` 是来源基线，不代表改动已经提交。没有 Git 提交、推送或 Workshop 上传。

已完成本地实现和下面列出的验证，**不能据此认定九个模组在全部功能及多人模式下完全兼容**。逐项边界见 [兼容矩阵](../compatibility-nine-mods.md)，文件与日志指纹见 [证据清单](compatibility-nine-mods-20260926-evidence.json)。

用户切换 Steam 后，本机完整游戏为 preview 0.111.0，实际游戏 DLL 与该引用一致。没有切换或覆盖用户正常游戏。测试由本机独立副本运行，Steam 关闭，存档／配置隔离并对副本配置出站阻断；未使用 ninja5080。

| 输入 | stable | preview |
|---|---|---|
| 宿主 API | 0.107.1 | 0.111.0 |
| 宿主 MVID | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` |
| 最终产品 DLL SHA-256 | `EA7B4731F251A860090C32437ADFF99A54E34A9BC57C0A784C6CD1B654A953DF` | `96568083FA43DF156BDAAE58061F1FBE8D8A8857EE6A14C661C84BEB93B3CF0D` |
| Ritsu 编译／契约 SDK | 0.5.12 | 0.5.12 |
| 实机 Ritsu | 早期 stable 运行 0.6.2 | 最终 preview 运行 0.6.2 |

可选桥接 SHA-256 为 `D3DC44BF495AFF253ACFF160CFE17902A79ED07C20F9BA6A1531A7F74C871F1D`。完整本地包的清单指纹为 `1cf4a4cb5be435d8c3b174f40b71322a10e841eb132186d53d0632463e8c6ae1`。完整九模组实机包含 BaseLib 3.4.7；Better Menu 清单只报告 `v0.0.0`，以实际两个运行库的指纹固定版本，不推断其营销版本。

## 实际故障与修复证据

1. **Loadout 使处决被整体停用**：`live-preview-all-13/game-godot.log` 有处决资格拒绝记录，安装 Loadout、未开启无敌也受影响。限定允许已核实的无敌前缀后，`live-preview-all-14`、`live-preview-all-20` 通过实际战斗。独立双宿主 `*-loadout-04.log` 调用真实前缀：无敌关闭时生命 10、奈落 3、来伤 5 得到生命 8／奈落 0／吸收收据 3；无敌开启时两者不减少且没有虚假收据。
2. **外部体型被演出覆盖**：`live-preview-focused-16/checkpoints.jsonl` 记录阿拉巴马落中最大生命改变后，预期 `Visuals.Scale=2.1111112`，实际被恢复到 1。删去不属于该演出的缩放恢复，`focused-17`、`18`、`19` 实际调用 SizeUpdater 后均保持 2.1111112。早期对根节点的检查不足以覆盖此问题，最终以 Visuals 实际结果为准。
3. **建筑师问候期间不可投药水／药水动作覆盖死亡**：恢复原生对话和继续入口，并等待真实飞行任务。`live-preview-focused-19` 中继续前没有处决控制器，真实 `ShouldOfferThrow` 允许投药水，`ThrowAndConsume` 飞行期间点继续，药水移除、飞行结束后处决，胜利计数只增加一次。
4. **海克斯接管扣血时漏记奈落／误禁处决**：`stable-hextech-08.log` 和 `preview-hextech-08.log` 使用实际海克斯 DLL 的濒死前缀，覆盖完全奈落吸收、溢出、玩家／敌方保命以及别嫔一次触发。敌人最大生命 1000、当前 3，伤害 5 后保持 1、债务 2；再伤害 60 才确认死亡。未知跳过原函数的前缀仍触发处决保护。
5. **东尼组件接入**：原有 NinjaSlayer 不在其外部角色组件表；新桥接在模型冻结前注册。初轮英文组件文案重复注册导致启动失败，以外部注册表判定复用后，最终九模组启动和组件契约通过。

## 实机与契约分开记录

证据路径以下均相对 `build/compatibility-nine/`。

| 检查 | 结果与证据 | 覆盖范围 |
|---|---|---|
| 双宿主产品、打包 | 通过，`package-stable-07.log`、`package-preview-07.log`、`bundle-final.log` | 真实候选 DLL 与通用本地包；不代表实机 |
| 双宿主 SmokeDriver | 通过，stable 最终构建与 preview 实际启动构建 | 驱动本身编译 |
| 全量候选产品契约 | 通过，`stable-all-08.log`、`preview-all-08.log` | 93 张内容、充能球、近远处决、暗打、事件及原生模型站位等 |
| 必需补丁、回滚契约 | 通过，`stable-ritsu-08.log`、`preview-ritsu-08.log` | Ritsu 事务和宿主目标；含主动注入的失败与回滚 |
| Loadout／海克斯真实前缀 | 通过，`*-loadout-04.log`、`*-hextech-08.log` | 加载真实第三方 DLL 的产品契约，非 UI 操作 |
| 东尼产品契约 | 通过，`preview-anthony-33.log` | 84 行基础／升级构造及序列化，80 奖励槽，同种子、设置、部分实际组件、改数值、回手与存档恢复 |
| 本机 preview 九模组战斗与重进 | 通过，`live-preview-all-21/attestation.json` | 真实图形游戏，第一场战斗、奈落／手里剑、暗打、普通／多段／反向处决及保存重进；输出 PNG 为 0 |
| 本机 preview 特定交互 | 通过，`live-preview-focused-19/attestation.json` | 光标缓存、外部缩放、小季不增加 Minty 来伤、原生删卡价格、Better Menu 读取及重开、建筑师投药水 |
| 双进程原生 ENet | 通过，`network-stable-01`、`network-preview-01` | 原生动作／选择同步、手里剑、牌与事件归属、双方结束回合；headless 产品测试 |
| 逻辑测试 | 360／360 通过、0 跳过，`logic-final.log` | 不以纯逻辑测试代替宿主或实机 |
| 仓库与构建边界 | 通过，`repository-final.log`、`sync-final.log`、`build-boundaries-final.log` | 仓库一致性、双宿主派生配置与编译边界 |

`focused-19` 与最终产品主 DLL 相同，但使用最后一轮牌池修复之前的桥接；该组不验证东尼生成牌池。最终桥接由 `preview-anthony-33`、`live-preview-all-20` 和无截图的 `live-preview-all-21` 覆盖。截图入口修正后，stable SmokeDriver 也重新构建通过，见 `driver-stable-final-02.log`。

Better Menu 实机先保存 `PotionDropCalls=17`，真实 `QuickLoadAsync` 后仍为 17；真实同种子及随机重开后均重置为 0。期间正常完成海克斯选择。CookieCursor 刷新已有图标后内容校验值不变。RemovalCostViewer 检查 0／1／2 次移除时与 `MerchantCardRemovalEntry` 一致。小季加入前后来伤均为 12，真人数仍为 1。

东尼实际执行覆盖直拳伤害／空手道、升级备镖库存／格挡、无星之夜固定 Power 和强手里剑快照、升级撒菱独立到期、空预见、升级小口饮茶、外部改伤害／费用／保留后的分解，以及打击·打击前三次回手／第四次弃牌／消耗优先。不是全部 84 张来源牌效果的实机覆盖。

## 测试工具及诊断

- 早期 `live-preview-focused-15` 检查了错误的缩放节点；17 的自动重开等待在海克斯正常选择界面；18 的 AutoSlayer 模式跳过等待，不能证明药水飞行。分别修正测试定位、完成真实按钮选择、临时关闭该场景的跳等待后，19 通过。没有把这些驱动错误当作产品故障。
- 完整九模组的早期 stable 运行不能通过。建筑师插件调用 stable 没有的 `Player.get_CanUseOrRemovePotions`（`live-eight-03/game-godot.log`），其可选补丁事务回滚。东尼 0.3.102 使用 preview API；未在 NinjaSlayer 内伪造 stable 接口。
- headless 产品契约有退出时 CanvasItem／ObjectDB 释放诊断；Ritsu 契约的资源替身和故意失败用例会产生日志错误；海克斯单独加载的契约有未关联真实 ModManager 的类型注册诊断。不把这些日志称为“零错误”，也不把它们当作完整联机结果。
- **截图要求的偏差**：早期驱动虽然接收 `-NoScreenshots`，只阻止了失败截图，常规测试仍生成了 PNG。发现后将所有静态截图入口统一检查该选项，跳过会自行捕获画面的原生 F2 上传测试，禁止与视频录制阶段组合。此前图片未用于交付证据。最终无截图重跑结果及文件数量写入证据清单。

## 未执行与发布边界

- 用户切换到 preview 后，没有再运行最终 stable 图形游戏；stable 最终覆盖是构建、产品契约及原生双进程 ENet，不冒充最终 stable 实机。
- 没有完成每个模组单独组合的全功能矩阵，也没有完整九模组双人／四人真实游戏客户端联机。四玩家模型加三宠物的布局契约不能替代四客户端实测。
- 没有完成东尼多人重连、全部 84 个来源效果的实际结算、全部自定义次级数值的动态预览、所有茶道依赖组合或 Loadout 联机编辑同步。
- 光标远端与全参数、全部商店折扣、Minty 全提示、海克斯全部保命／复活／抽牌组合仍待专项实测。
- 本地包显式加入了桥接 DLL。自动发布 workflow 尚未接入桥接构建参数；正式发布前必须补齐。未更新线上依赖或上传第三方文件。

正常卡牌效果未改，不调整卡牌规格或目录。本轮普通模式的单人不歪种、已有本地内容均保留。

## 维护与复核

相对于 HEAD，当前生产 C# 从 507 个文件／62,819 行／872 个类型声明变为 523 个文件／64,612 行／1,005 个类型声明。此统计包含此前未提交的 CFC 修改及东尼 90 个原生序列化槽位；类型数为词法统计，不是 Roslyn 符号计数。逐文件指纹在证据清单中，测试与工具代码不计入上述数字。

删除项主要是无所有权的缩放快照、建筑师进房即处决和屏蔽原生对话的路径，以及失败后直接胜利的兜底。保留的动态补丁分别属于 CookieCursor 自定义图标和建筑师投掷接口，弱引用分别跟随伤害结果或战斗房间；没有另建全局兼容管理器。东尼三处私有访问及发布接线限制已列在兼容说明中，需随依赖版本重新核对。

复核入口为 `Tests/NinjaSlayer.OrbContractTests/Run-Contracts.ps1`、Ritsu 契约项目、`tools/smoke-harness/Invoke-NinjaSlayerSmoke.ps1 -Mode ModCompatibility -NoScreenshots` 及 `-Mode FirstCombatRestart -NoScreenshots`。本轮精确参数保存在本地 `build/compatibility-nine/check.ps1`、`check-anthony.ps1`、`package-current.ps1` 和 `live-preview-*.ps1`；宿主契约串行运行，避免共用输出被另一宿主覆盖。stable headless 契约使用 stable DLL，但共享当前安装的 preview 宿主资源包，不能据此证明 stable 的完整资源表现。
