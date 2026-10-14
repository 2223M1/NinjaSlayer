# 1.0.27 卡牌调整

产品提交为 `3ef752b01ddd8a0581b3b8b43975a90695ee5eb6`（[PR #214](https://github.com/2223M1/NinjaSlayer/pull/214)），该提交的 [CI](https://github.com/2223M1/NinjaSlayer/actions/runs/38509248003) 通过。

- 嘲讽获得原生保留；保持0费、13→16格挡及给予敌人4层空手道。
- 交叉防御移除保留，保持1费、6→9格挡。将本回合打出的下一张牌放回手牌，包括消耗牌和能力牌；未使用的效果在本回合结束时清除。
- 杀气·直气为本回合可叠层能力，每次打出增加1层。完全格挡后，每层独立执行一次原有直气群体反击，原生力量、虚弱、易伤、活力和空手道分别结算。混用基础与升级版沿用较高伤害值47/56。敌方回合末无论是否满足反击条件都清除层数。
- 同步16语言卡牌与能力描述、动态伤害/次数显示及AutoAnthony公共组件接口。

交叉防御直接使用原生出牌结果钩子：stable 0.107.1 的 ModifyCardPlayResultPileTypeAndPosition 与 preview 0.111.0 的 ModifyCardPlayResultLocation。参考两版 Core/Models/Powers/ReboundPower.cs 的一次消费/回合清除，以及 preview FeralPower.cs 和两版 CardModel.Play 的原生返手结算顺序；仅在该能力内区分两版签名，不增加全局补丁。

从干净产品提交重新导出资源并构建两版程序集。两版卡牌行为和双进程原生ENet检查通过，覆盖返手、消耗/能力牌、原生保留保存/克隆、回合清除、叠层反击、未完全格挡、多目标与伤害修正。AutoAnthony公共组件检查通过。包内16语言使用原生格式化验证，卡牌目录包含1672个模型。未执行完整图形客户端重放、移动端/macOS/Linux实机或完整第三方模组组合测试。

已更新既有[工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3776911445)，重新下载12个文件，长度和SHA-256全部等于冻结包。保留16语言介绍、标签、图片、依赖和公开状态；更新说明回读为“我们修复了一些问题，增添了一些内容，调整了一些东西。”。未创建GitHub Release。

官网由同一已验证包导出，指纹为 `e9cb7c6beedcfdc443e72b77a12a038ce28fea2067666ff006ca56e7b1225698`。本次不更改反馈状态。发布文件和验证日志散列见[精简证据](release-1.0.27-evidence.json)。

回验后已核对清理目标绝对路径及运行进程。自动审批以“blocked by policy”拒绝删除本次重复构建、上传暂存和目录缓存；未执行删除，未尝试其他删除方式。本地产物与精简日志均保留。
