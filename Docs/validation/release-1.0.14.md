# NinjaSlayer 1.0.14

## 奈落血条

反馈中的 `The active Naraku health bar is not attached to a creature node.` 确实存在于1.0.13的 `NarakuLifeHealthBarLayoutPatch`。已用工坊下载的原始1.0.13 DLL（`94b066993ab33bf34b43c6f4ff673a2f852c16080a33872f4470de54d1300c03`）重现：正常非角色节点下的共享血条、持有奈落生命时，补丁抛出完全相同的异常。用户本条未提供实际日志文件，因此不把未检查的具体玩家回合栈当作已逐行核验；源码及生产DLL复现已经证明问题真实。

正式0.107.1和预览0.111.0的 `NMultiplayerPlayerState` 都复用 `NHealthBar`，将其绑定 `Player.Creature`，在生命、格挡和能力变化时调用宽度及前景刷新。它不属于 `NCreatureStateDisplay -> NCreature` 战斗节点结构。原补丁错误地把共享组件的合法UI使用当成损坏的战斗节点。

修复继续为共享血条刷新／移除嵌入奈落血段，仅在直接父节点是 `NCreatureStateDisplay` 时应用角色Hitbox／格挡锚点修正。真正损坏的战斗结构仍抛错；不吞异常、不默认血量、不改伤害吸收或存档字段，不增加全局兼容层。

## 沢渡名称与可见入场

完整显示名按用户要求统一为“沢渡 · 佛雷斯特”，单独姓氏为“沢渡”；中文事件、怪物、砍刀文案与目录同步，英文、日文和模型ID保持。原文证据及显示写法分类见原著索引；EPUB和私有正文不入Git。

事件在原生房间加载阶段等待入场，原生 `RunManager.FadeIn` 要到 `EnterRoom` 返回后才执行，造成演出发生在黑幕下。删除事件对模组特定过场的入场委托，改为事件实例拥有一次性的待显露战斗启动；精确必需Patch在原生 `FadeIn(bool)` Task完成后消费它，再播放入场并开始嵌入战斗。场景创建后先隐藏忍者杀手，只在真正入场时显露，保留同一战斗／决斗事件机制和原有六种入场演出。

## 验证

## 多段处决

1.0.13生产DLL已重现：风暴拳实际4段，显式 `ExecuteWithFinisher` 却通过只接受原版程序集的 `VanillaHitPreviewCompatibility` 构造预测，回落为1段／NotGuaranteed。不是活力伤害遗漏；两版原生 `VigorPower.ModifyDamageAdditive` 已将活力加入每段，`AttackCommand.Execute` 在整个命令前后各调用一次BeforeAttack／AfterAttack。旧生产者导致整组总伤害预测不足，直到末段直接伤害入口才取得处决，且错误 `ResolvedHits=1` 会令 `BeginComboRecovery` 的终段判断提前成立。

删除显式路径的卡牌预览推导，复用攻击命令适配边界的真实伤害／段数／属性／目标；保留原有显式参数签名和旋风拳覆盖。不补登记猜测表、不改变实际伤害、原版活力消耗、随机目标、动画、模型ID或存档。风暴拳／龙回旋踢／掌底突刺／对空连打基础和升级的力量+活力、格挡、差1点不杀、预测无副作用契约覆盖该根因；渲染Release114增加首击前处决、前半1.35倍镜头、后续每段的普通距离回退／前冲、UI根节点稳定和最终释放验收。实机及最终干净提交结果以发布证据为准。

原版行为参照为0.107.1及0.111.0的 `Core/Commands/Builders/AttackCommand.cs:Execute` 和 `Core/Models/Powers/VigorPower.cs:BeforeAttack/ModifyDamageAdditive/AfterAttack`。无新Patch目标、反射字段或兼容分支；共用既有命令私有字段边界，删除错误单调用生产者。卡牌数值及文案未改变，目录不因该处决修复改变。

1.0.13原始DLL失败基线、非角色共享血条的嵌入段／原生锚点不改／能力移除、损坏战斗节点仍报错契约通过。379项逻辑测试、仓库／兼容／构建边界、双宿主DLL及Patch事务契约通过。新增的必需目标是RunManager.FadeIn，正式版该成员不可由nameof引用，采用两版源码已确认的精确字符串目标和bool签名，不增加运行时猜测。

预览0.111.0完整客户端 `Release114` 隔离实机用四个原生Player模型及真实 `NMultiplayerPlayerState`，覆盖生命、格挡、奈落叠加／移除／再次给予、回合结束至第二回合及继续出牌；普通和快速均通过。事件实机在入场开始时检查原生过场已结束、SimpleTransition透明且人物尚隐藏，随后验证恰好一次可见入场及正常进入战斗，普通和快速均通过。这是单个渲染客户端的原生联机UI布局模拟，不是独立客户端网络传输验证。

按用户确认，只以一个真实预览客户端作为发布门槛。正式完整安装实机、macOS/Linux和独立多人客户端未运行。最终合并提交、CI、准确上传包的实机检查、12文件下载SHA256和工坊元数据以同目录发布证据为准；更新既有3776911445，固定说明保持“我们修复了一些问题，增添了一些内容，调整了一些东西。”，不创建GitHub Release或部署Worker。

生产C#由521文件／901类型声明／64354物理行变为522／902／64389。修改NarakuLifeHealthBarLayoutPatch的合法呈现上下文边界；TheMovingJungleEvent拥有和消费延后启动，SawatariEventSession负责入场前隐藏，新增SawatariRoomRevealPatch并由Entry集中注册。删除原先单调用的错误时序，不增反射、GC、同步、回退、历史路径或能力图；既有Session与原生FadeIn直接复用。
