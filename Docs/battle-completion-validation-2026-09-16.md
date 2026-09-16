# 战斗收尾、存档等待与瀑布巨人自爆

本轮基线为 `8c3cfa43ec4e9b99a81f2533772d2f4ed0b266fc` 加当前本地修改，
不是一个已提交候选 SHA。保留此前泽渡数值、收尾、随机投接刀和居合修复。
没有提交、推送或上传 Workshop。

## 玩家报告的证据

`C:/Users/theon/Downloads/godot (4).log` 的第 35,166 行记录本地写入
276,287 字节的 current_run.save；第 35,193 行明确等待进行中的保存才能返回主菜单。
相同大小的 Steam remote store 写入到第 42,845 行才返回，紧接着记录
TURRET_OPERATOR_WEAK 胜利及两个敌人的击杀统计。

stable 0.107.1 的原版 `CombatManager.EndCombatInternal` 先 MarkPreFinished，
然后等待 SaveManager.SaveRun，最后才发出 CombatWon。
RunSaveManager 经 CloudSaveStore 先完成本地写入，再等待 SteamRemoteSaveStore 的
FileWriteAsync 回调。读档读取本地已完成战斗的状态，因此直接领奖有明确解释。
日志不能确定云端回调迟滞的根因，也不能推算玩家实际卡住时长。

隔离实机使用正常保存时，持有两张黑炎的攻击击杀了最后两名敌人并显示奖励。
测试另在真实本地写入完成后暂缓保存 Task 返回，使用回合末黑炎击杀相同敌人，
确认本地 PreFinishedRoom 已写入、CombatWon 尚未发出、黑炎仍停留在中间结算画面。
解除等待后，胜利事件仅发出一次，奖励正常出现。
该实验模拟相同的等待边界，不模拟真实 Steam 网络、玩家的完整模组组合或迟滞原因。
本轮未修改生产保存流程、Steam 设置或添加超时绕过。

日志中的旁白生命周期 MissingMethodException 和 StartFadeOut 错误仍属独立问题。
大量 CueFrameSequencePlayer 已释放纹理错误出现在卸载缓存后；没有证据据此认定
黑炎特效为其产生者。未为这些未确认的关联添加兼容补丁。

## 已修正的独立问题

同一日志第 17,800 行附近的 Waterfall Giant 终结异常发生在更早的战斗，
不是上述炮塔战截图的直接错误。原版 SteamEruptionPower.AfterDeath 会将巨人
恢复为 999,999,999 生命并切换自爆准备阶段。旧 Finisher 在 Kill 返回后断言必须死亡，
因而报错并在应急路径重复调用 Kill。

仅加载 NinjaSlayer、RitsuLib 和 SmokeDriver 的 stable 实机已复现旧异常，
证据为 `build/action-compat-validation/run-B-battle-completion-before/`。
修正后以原生死亡命令成功返回为本次提交完成，保留原生复活／转阶段结果。
未提交的致死记录仍保留检查；已提交的记录不再阻止复活后实体行动。

按用户追加确认，自爆沿用现有 Boss 爆炸／解体演出，伤害在爆炸 cue 后通过原版
AttackCommand 执行。仅替换 `CreatureCmd.TriggerAnim` 的 WaterfallGiant/Erupt
动画等待。击倒和准备回合没有爆炸演出或额外伤害，最终 Kill 复用已开始的演出移除节点。

变更归属：FinisherSession 删除错误的“Kill 后必须死亡”断言并排除已提交记录；
BossDeathPresentationController 暴露当前爆炸 cue；BossDeathPresentationPatch
复用巨人的演出；WaterfallGiantExplosionPatch 在原生动画边界接线；
NinjaSlayerPatchGroups 将它加入既有可选演出事务。
SmokeController.BattleCompletion 和 ActionPreview 入口提供实机行为验证。
没有更改卡牌规则，因此卡牌目录未调整。

## 验证结果与产物

证据根目录：`build/battle-completion-20260916/`。
最终实机：`build/action-compat-validation/run-B-battle-completion-burst/`，13 个检查点，退出码 0。
其中包含巨人单次死亡提交、原生准备回合、自爆画面与 20 点实际伤害、最终死亡、
黑炎攻击胜利、回合末胜利保存等待和恢复后的奖励。
截图为 `giant-burst-damage.png`、`black-flame-save-pending.png` 和两张奖励截图，
完整演出录屏为该目录的 `preview.mp4`。

- 逻辑测试 354 项通过；仓库一致性、兼容配置和 diff 空白检查通过。
- stable 0.107.1 与 preview 0.111.0 产品及 SmokeDriver 构建：零错误、零警告。
- 两宿主完整 Orb 产品契约通过。
- 两宿主 RitsuLib 产品契约通过，包括必需 Patch 回滚、可选演出事务及目标签名。
  必需目标仍为 106 个／关键 69 个，新增演出目标属于可选 Boss 事务。
- stable Windows Vulkan 实机使用 RitsuLib 0.6.2；构建／接口契约使用固定 SDK 0.5.12。
- 本轮没有运行 preview 可视实机、玩家全部第三方模组组合、真实 Steam 故障注入，
  也没有新增巨人自爆的双玩家或致死玩家实机场景；不能把原生命令复用称为这些场景均已验收。
- 实机退出仍有既有 Godot 资源清理警告，不声称日志完全无警告。

宿主 MVID：stable `97f10687-c306-4798-ab75-8b9f23f34dfb`；
preview `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`。

候选 DLL 相对于仓库的路径及 SHA256：

| 通道 | DLL | SHA256 |
|---|---|---|
| stable | build/battle-completion-20260916/stable/Release/NinjaSlayer.dll | 7877E791F1622C6EA1000A503EB057CE7C6A5A5FC9C3286E368083C7F43A74BF |
| preview | build/battle-completion-20260916/preview/Release/NinjaSlayer.dll | 1C5D564E5BCBE558A36743E2FD2A9ABD5BE4EC35405DA7090DAFD8136E535902 |

资源未变化，复用 `build/sawatari-balance-20260916/NinjaSlayer.pck`，SHA256
`93AF6DF3BC0270FA9A5AEE76F084C8EDB8AC1A9CC6F31FB0BCDD97D859EBB114`。
DLL、SmokeDriver、PCK 的绝对路径及校验值均在本轮 `candidate-evidence.json`。
