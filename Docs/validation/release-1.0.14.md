# NinjaSlayer 1.0.14

## 奈落血条

反馈中的 `The active Naraku health bar is not attached to a creature node.` 确实存在于1.0.13的 `NarakuLifeHealthBarLayoutPatch`。已用工坊下载的原始1.0.13 DLL（`94b066993ab33bf34b43c6f4ff673a2f852c16080a33872f4470de54d1300c03`）重现：正常非角色节点下的共享血条、持有奈落生命时，补丁抛出完全相同的异常。用户本条未提供实际日志文件，因此不把未检查的具体玩家回合栈当作已逐行核验；源码及生产DLL复现已经证明问题真实。

正式0.107.1和预览0.111.0的 `NMultiplayerPlayerState` 都复用 `NHealthBar`，将其绑定 `Player.Creature`，在生命、格挡和能力变化时调用宽度及前景刷新。它不属于 `NCreatureStateDisplay -> NCreature` 战斗节点结构。原补丁错误地把共享组件的合法UI使用当成损坏的战斗节点。

修复继续为共享血条刷新／移除嵌入奈落血段，仅在直接父节点是 `NCreatureStateDisplay` 时应用角色Hitbox／格挡锚点修正。真正损坏的战斗结构仍抛错；不吞异常、不默认血量、不改伤害吸收或存档字段，不增加全局兼容层。

## 沢渡名称与可见入场

完整显示名按用户要求统一为“沢渡 · 佛雷斯特”，单独姓氏为“沢渡”；中文事件、怪物、砍刀文案与目录同步，英文、日文和模型ID保持。原文证据及显示写法分类见原著索引；EPUB和私有正文不入Git。

事件在原生房间加载阶段等待入场，原生 `RunManager.FadeIn` 要到 `EnterRoom` 返回后才执行，造成演出发生在黑幕下。删除事件对模组特定过场的入场委托，改为事件实例拥有一次性的待显露战斗启动；精确必需Patch在原生 `FadeIn(bool)` Task完成后消费它，再播放入场并开始嵌入战斗。场景创建后先隐藏忍者杀手，只在真正入场时显露，保留同一战斗／决斗事件机制和原有六种入场演出。

## 多段处决

1.0.13生产DLL已重现：岚之拳实际4段，显式 `ExecuteWithFinisher` 却通过只接受原版程序集的 `VanillaHitPreviewCompatibility` 构造预测，回落为1段／NotGuaranteed。不是活力伤害遗漏；两版原生 `VigorPower.ModifyDamageAdditive` 已将活力加入每段，`AttackCommand.Execute` 在整个命令前后各调用一次BeforeAttack／AfterAttack。旧生产者导致整组总伤害预测不足，直到末段直接伤害入口才取得处决，且错误 `ResolvedHits=1` 会令 `BeginComboRecovery` 的终段判断提前成立。

删除显式路径的卡牌预览推导，复用攻击命令适配边界的真实伤害／段数／属性／目标；保留原有显式参数签名和旋风拳覆盖。不补登记猜测表、不改变实际伤害、原版活力消耗、随机目标、动画、模型ID或存档。风暴拳／龙回旋踢／掌底突刺／对空连打基础和升级的力量+活力、格挡、差1点不杀、预测无副作用契约覆盖该根因；渲染Release114增加首击前处决、前半1.35倍镜头、后续每段的普通距离回退／前冲、UI根节点稳定和最终释放验收。实机及最终干净提交结果以发布证据为准。

原版行为参照为0.107.1及0.111.0的 `Core/Commands/Builders/AttackCommand.cs:Execute` 和 `Core/Models/Powers/VigorPower.cs:BeforeAttack/ModifyDamageAdditive/AfterAttack`。无新Patch目标、反射字段或兼容分支；共用既有命令私有字段边界，删除错误单调用生产者。卡牌数值及文案未改变，目录不因该处决修复改变。

## 验证

1.0.13原始DLL失败基线、非角色共享血条的嵌入段／原生锚点不改／能力移除、损坏战斗节点仍报错契约通过。379项逻辑测试、仓库／兼容／构建边界、双宿主DLL及Patch事务契约通过。新增的必需目标是RunManager.FadeIn，正式版该成员不可由nameof引用，采用两版源码已确认的精确字符串目标和bool签名，不增加运行时猜测。

预览0.111.0完整客户端 `Release114` 隔离实机用四个原生Player模型及真实 `NMultiplayerPlayerState`，覆盖生命、格挡、奈落叠加／移除／再次给予、回合结束至第二回合及继续出牌；普通和快速均通过。事件实机在入场开始时检查原生过场已结束、SimpleTransition透明且人物尚隐藏，随后验证恰好一次可见入场及正常进入战斗，普通和快速均通过。这是单个渲染客户端的原生联机UI布局模拟，不是独立客户端网络传输验证。

按用户确认，只以一个真实预览客户端作为发布门槛。正式完整安装实机、macOS/Linux和独立多人客户端未运行。最终合并提交、CI、准确上传包的实机检查、12文件下载SHA256和工坊元数据以同目录发布证据为准；更新既有3776911445，固定说明保持“我们修复了一些问题，增添了一些内容，调整了一些东西。”，不创建GitHub Release或部署Worker。

生产C#由521文件／901类型声明／64354物理行变为522／902／64349。修改NarakuLifeHealthBarLayoutPatch的合法呈现上下文边界；TheMovingJungleEvent拥有和消费延后启动，SawatariEventSession负责入场前隐藏，新增SawatariRoomRevealPatch并由Entry集中注册。处决复用既有FinisherAttackCommandAdapter，删除FinisherAttackSpec.FromCard的错误重复推导。删除原先单调用的错误时序，不增反射、GC、同步、回退、历史路径或能力图；既有Session与原生FadeIn直接复用。

## 最终发布回验（2026-10-05）

游戏源码经PR #181、#182合并，最终提交为 `f4ad7482fe5f164b5781da222fd9e6460e4bacd5`；main CI `37324815259` 成功。双宿主最终DLL及必需Patch事务／故障回滚、预览实际东尼0.3.136契约通过。准确冻结包再次运行双宿主完整DLL契约，正式宿主使用预览资源包只作为逻辑／文案契约，不作为正式完整客户端实机证据。

准确通用包在单个完整预览0.111.0隔离客户端执行Release114、Release113。Release114的16组（四张卡×基础／升级×普通／快速）共46段，均首击前取得完整段数处决、前半1.35倍镜头，岚之拳／龙回旋踢每段恢复120px，掌打／对空砰砰拳恢复90px，再回到命中点；人物及UI基线最终释放。首次新检查把掌打错当作120px慢攻击，实测90px后修正了测试，不修改其原有距离。奈落四人模型状态栏、回合推进及沢渡可见入场均通过；Release113保留已发布编辑及友军蜂群回归。

首次发布脚本重导出PCK时发现与前置契约包不一致，立即在上传前终止，远端仍为1.0.13。其余11个包文件相同；现有资源包逐项比较1,614项，仅 `.godot/uid_cache.bin` 不同，UID变化仅指向构建临时AssemblyInfo等文件，1,613项游戏资源未变。准确冻结包补验通过后直接上传，不再导出。冻结清单SHA256为 `31b091481e5afc202444db572d3c674f8f357f739f3f5b0daf1d4223e0df8371`，PCK为 `931f051b1833624bdede5052bb54b9574203f0cd31211aceef3620c9111b5e3d`。

既有工坊3776911445已更新1.0.14，重新下载的12文件逐文件SHA256及大小全部一致。公开状态、三语标题／介绍、主图／画廊、标签和RitsuLib依赖保持；最新说明逐字为既定句子。回验后才推广官网运行目录，未部署Worker或创建GitHub Release。完整文件及实机证明见 `release-1.0.14-evidence.json`。

交付原格式 `D:/daily/download/忍者杀手_卡牌编辑板_v1.19_1.0.14.html`，284条目、20条备注、布局、归档及原图保留，包含应变抽3→4及下一牌置顶、五张现役修订和沢渡姓氏同步。编辑／备注／新增删除槽位／保存重开／旧格式兼容交互检查通过。原始HTML与反馈ZIP保留，ZIP不入GitHub。
