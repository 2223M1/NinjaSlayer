# 华彩燃火多人反馈核验

核验基线为已下载工坊1.0.15生产DLL，预览SHA256 `1b1fd010a99d2df271b02a48f71234a5b8dfa2c6ea781f119835f55579940175`，源码 `6355a1a09a6bd69a33eb74a15dbea791865f372f`。仓库当前另含未发布的16语支持PR #187。本反馈未附实际玩家日志、牌堆状态或其他模组列表。

## 结论与边界

尚未复现“华彩燃火使队友无法结算动画”。不据此断言玩家反馈不存在，也不把测试夹具错误当作产品缺陷。没有修改燃火、华彩、伤害、动画或行动队列的生产代码，没有全局免疫／超时回退／兼容框架。1.0.16纳入全语言支持与新增的可重复回归，不宣称包含未经证实的燃火修复。

0.107.1／0.111.0的原版 `Core/Models/Enchantments/Glam.cs` 同为首次增加1次重放、AfterCardPlayed标记本战斗已使用；中文原版 `enchantments.json:GLAM.title` 为华彩。`Core/Models/CardModel.cs:OnPlayWrapper` 每次重放均执行Card.OnPlay及原生Hook，原版附魔自身不操纵队友动画。燃火沿用原生Draw，再按生成者Owner生成1张黑炎至弃牌堆；预览沿用 `Core/Commands/CardCmd.cs:PreviewCardPileAdd` 的本地归属检查。

生产调用链已检查：Kindle、NinjaSlayerCardCmd、RapidCardResolutionScopePatch／RapidMultiCardPlayPatch、RapidCardPresentationContext、NinjaSlayerDrawAnimationBatch、卡牌结算状态袋及原版CardPileCmd／NCard／TweenHelper。状态按实际Card／CardPlay／Creature及异步作用域归属，没有证据支持新增修补层。代码质量删除账本为空；生产C#未变，不新增反射、动态Patch、同步、历史迁移、GC或性能设施。

## 已执行

- 未修改的1.0.15完整预览客户端、隔离后台桌面：普通／快速，本地／远端忍者杀手，基础／升级，无华彩对照／有华彩，足量牌堆／第二次出牌需要空堆洗牌，共32组。核对原生PlayCardAction完成、重放次数、实际抽牌与两张黑炎归属、队友继续打击、身体及UI基线恢复、结束回合至下一回合。
- 同一32组增加真实原版Ironclad玩家，燃火后队友原版Inflame及忍者杀手Resilience能力牌均正常完成。该客户端包含原生多人状态栏和远端卡牌显示，但不是两个独立渲染客户端。
- 正式0.107.1与预览0.111.0各两个独立ENet进程：双方分别打出华彩燃火基础／升级，核对重复抽牌、黑炎归属和对方后续原生行动，完整多人契约通过。这是独立网络逻辑检查，不冒充完整客户端动画验证。
- 初次网络夹具在既有StrikeStrike的空堆预见时缺少离线选择器；补充选择器后，又发现新增卡牌准备屏障／完成计数取值存在测试竞态。均修正于测试代码，未给生产队列加捕获或回退。生成卡牌ID与等待计数错误未作为反馈复现证据。

原始精简日志在本机 `build/validation-1.0.16/`。上述测试不覆盖所有第三方组合、独立渲染网络客户端或玩家未提供的具体运行存档。日常游戏安装／存档未修改。
