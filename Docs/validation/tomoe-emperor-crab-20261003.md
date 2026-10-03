# 巴投与皇帝蟹卡住修复

## 复现与原因

基线为已发布 1.0.6 的运行包 `b4455b891116aba989fe3b0b6c9d20fff2c66240`；修复工作树从后续目录提交 `cccbe06b02b8838dbaeedb322598a063bfc17f43` 开始。本轮为本地修复，没有发布新版本或修改日常游戏安装。

在 preview 0.111.0 的隔离游戏中创建两位原生 Player，进入原生第二幕 `KaiserCrabBoss`，通过 `PlayCardAction` 对 `Crusher` 使用巴投。1.0.6 在消耗茶道后、动画初始化时抛出 `NullReferenceException`，原生卡牌行动失败。堆栈为：

`FreeControlMotionBlur.RecordHistory → GrappledTargetPose.Apply → TomoeThrowAnimation.SyncTarget / _Ready → TomoeThrow.OnPlay → PlayCardAction`。

两个受支持宿主的 `scenes/creature_visuals/crusher.tscn` 和 `rocket.tscn` 都使用隐藏、无 Texture 的 `Sprite2D` 作为可选中部件的占位身体。真正的蟹身由 `NKaiserCrabBossBackground` 绘制。原逻辑给这个合法占位节点创建残影，随后无条件调用 `body.Texture.GetSize()`，导致空引用。该原因也影响单人，不是联机协议专有错误。

## 修复

只调整 `GrappledTargetPose` 的残影创建入口：无贴图的 Sprite2D 不创建残影。没有引入怪物名单、吞异常、超时跳过结算或全局去重。巴投的角色动作、落地时点、耗茶、格挡、虚弱、镜像和收招保持；普通 Sprite2D／Spine 身体继续使用原残影。

无卡牌效果、数值、关键词或描述变化，因此卡牌目录不变。无模型 ID、存档或网络协议改动。

## 验证方式

新增 SmokeDriver `TomoeThrow` 模式，通过通用包根加载器启动，并使用原生出牌队列。检查一／二／四位 Player 的队伍布局、皇帝蟹左右钳和可见的原生 Spine 怪物 `ThievingHopper`，各用基础／升级巴投各一次；同时覆盖 Normal／Fast。

每次核对茶道进入消耗堆、巴投离开打出区进入弃牌堆、格挡 6／7、所选目标虚弱 2／3 只结算一次、角色和目标定位恢复、动画与残影节点清理，以及下一张防御正常打出。每宿主共 18 次巴投及 18 次后续出牌。

stable 0.107.1 和 preview 0.111.0 上述实机检查全部通过，共 36 次巴投和 36 次后续出牌。两宿主产品、SmokeDriver、完整 DLL 契约以及 375 项逻辑测试通过；仓库一致性、兼容清单和构建边界检查通过。实际 RitsuLib 为 0.6.5，编译兼容包为 0.5.12。

这些是本机渲染游戏中的真实 Player 模型及原生队列验证，网络服务使用 `NetSingleplayerGameService`；没有把它表述为多个独立客户端的真实联机验证。独立客户端同步、玩家原始跑局重载、macOS／Linux 实机未执行。

首次修复候选尝试直接启动内部开发 DLL，因跳过通用加载器的 Box2D.NET 依赖注册而没有进入测试。保留该启动失败日志；随后改用完整通用包根加载器重测，未改动发布加载器或依赖逻辑。

具体宿主、RitsuLib、DLL／源码补丁校验值、基线异常和最终检查点见相邻 `tomoe-emperor-crab-20261003-evidence.json`。构建元数据中的源码 SHA 是工作树基线；JSON 另记生产修改的补丁和文件散列，不能把该候选误认为未修改的基线 DLL。

不使用 ninja5080，不新增截图或录屏。保留精简日志与证据。本轮重复生成目录共 686,013,180 字节；清理请求被自动审批以“blocked by policy”拒绝，没有执行删除或更换方法重试，临时目录仍保留。
