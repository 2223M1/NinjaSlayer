# 事件遗物验证记录（2026-09-16）

## 候选与最终规则

基线 `8c3cfa43ec4e9b99a81f2533772d2f4ed0b266fc` 加当前未提交工作树；不是干净提交的发布包。
保留既有泽渡、居合、瀑布巨人和保存归因调查修改。本轮没有提交、推送或上传 Workshop。
证据目录为 `build/event-relics-20260916/`，完整路径与 SHA256 见 `candidate-evidence.json`，
生产源码及相关资源文件哈希见 `source-files.json`。

- 生化竹子：每打出两张攻击牌获得 1 覆甲，余数跨回合、跨战斗并随原生遗物序列化保存。
- 别嫔碎片：每战第一次实际损失生命获得 7 空手道；奈落生命吸收也计入。
- 原版百年积木同样识别奈落吸收，保留其自身抽牌、每战一次和重置流程。
- 两件遗物均为事件稀有度，不进入随机遗物池。图标来自现有竹子和刀刃素材，按用户要求再次截短。
- **最终要求为手动领取**。泽渡决斗使用原生 ExtraRewards 提供一件固定生化竹子，取消常规战利品和原先两件随机遗物；普通奖励分支不给竹子。
- 黑暗忍者的碎片作为原生固定额外奖励，与失窃牌返还同屏出现。保留已有常规战利品，取消两件随机遗物。遗物及每张失窃牌均可领取或放弃，Resume 不补发遗物。
- 固定奖励、牌主和父事件使用原生战斗房间序列化，没有新增存档格式或奖励系统。

## 双宿主构建与产品契约

| 宿主 | MVID | 候选 DLL SHA256 |
| --- | --- | --- |
| stable 0.107.1 | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `C1989CBEC5D3ADC2AC189BD937BF25BCC73F838F73145F51BCC5C09E01DFE7A6` |
| preview 0.111.0 | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` | `E3A2B0BCAB5306CE1C52C4E90E23BB1BF3439526B436885B69C07A6767C0E658` |

DLL 分别位于证据目录的 `stable/Release/NinjaSlayer.dll` 和 `preview/Release/NinjaSlayer.dll`。
各自的 SmokeDriver 通过 `NinjaSlayerAssemblyPath` 显式引用对应候选 DLL；四次构建均零警告、零错误。
最终资源包 `NinjaSlayer.pck` 的 SHA256 为
`99E7C6B892DAC696DC70651182860DD736B8ECB262CE5917575D2766F676A300`。

完整 Orb 产品契约及 RitsuLib 集成契约在两个宿主上通过。新增断言包括：

- 原生命令自动打出、非攻击不计数、多段只计一张、连续触发、回合／战斗／原生序列化保留余数。
- 真实扣血、自伤、奈落完全吸收、恰好耗尽、溢出、全格挡、缓冲、闪避、零伤害、预览及每战重置。
- 同时持有碎片和百年积木时各触发一次；下一次伤害不重复；完全吸收后的共享 DamageResult 仍为零真实扣血。
- 两玩家原生战斗房间奖励保存／反序列化，固定遗物和归属不变；一人领取、一人放弃分别生效；泽渡不再生成随机遗物或普通奖励。

双进程原生 ENet 契约在 stable、preview 均通过，证据在 `enet-stable-manual/`、`enet-preview-manual/`。
通过原生联网出牌队列核对两玩家竹子计数、覆甲及碎片的奈落触发归属，最终双方状态完全一致。
同时保留手里剑、奈落、大刀投接与随机手部选择的原有联网断言。

逻辑测试 354 项通过；仓库校验、兼容配置检查及 `git diff --check` 通过。
编译／契约使用固定 RitsuLib 0.5.12，实机使用 0.6.2，两者不混称。

## stable 实机

最终记录在 `build/action-compat-validation/run-B-event-relics-manual-01/`。
Windows stable 0.107.1、RitsuLib 0.6.2，真实 Vulkan 游戏进程，隔离本地存档并关闭 Steam。
22 个检查点通过，进程正常退出。加载 NinjaSlayer、RitsuLib、SmokeDriver。

- 两个截短图标、原生计数、覆甲／空手道悬停说明及奈落吸收触发已检查。
- 一层泽渡决斗手动领取竹子；三层决斗跳过竹子；普通战利品分支不发竹子。
- 黑暗忍者正常击杀领取碎片，并只取回一张失窃牌；荆棘反杀取回全部四张牌但跳过碎片。结束事件没有自动补发或第二个遗物选择屏。
- 回归非致死居合、确认致死不居合、受击等待期间死亡取消居合，以及两幕地精佣兵分裂、雾菇／利齿之眼退场、助战与反伤击杀。

截图：`BioBambooRelic-tooltip.png`、`BeppinFragmentRelic-tooltip.png`、
`act1-fogmog-duel-native-reward.png`、`act3-gremlin-duel-support-reward.png`、
`event-normal-take-one-skip-three-cards.png`、`event-thorns-all-four-cards.png`。

测试夹具改用原生 EnterRoomDebug 初始化回放。原版 event 控制台命令直接 EnterRoom，
连续场景中会缺少回放初始状态。召唤导致原生布局重排，不再用绝对位置不变判断是否提前进入选项；
改为检查己方归属、暂停状态及选项可见性。没有为这些测试接线问题添加生产补丁。

未执行 preview 可视实机、双客户端事件投票界面或完整进程退出后的胜利存档重开；
存档证据是原生遗物／房间／奖励序列化契约，ENet 证据不代替事件 UI 测试。
本轮没有重做完整 AutoSlay 或保存卡住的归因实验。实机退出仍有既有 Godot 清理警告，
不将日志描述为完全无警告。
