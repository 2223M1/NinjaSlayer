# 《忍者杀手 × 杀戮尖塔》剧情与事件机制大纲

> 文档性质：剧情与事件设计规格。本文不改动代码、本地化表或美术资源。
>
> 时间点：第三部中后期。藤木户已认识南希·李、矢本·小季、泽渡·弗利斯特、由佳乃、银之匙和黑暗忍者。

## 1. 证据边界与核心命题

本文使用三种标记。**[原文事实]** 表示可由后附语料位置直接支持；**[设计推论]** 表示将原作事实接入《杀戮尖塔》规则的解释；**[项目设定]** 表示本模组为补足跨作品因果而新增的内容，不宣称属于任一原作正史。

故事的核心不是“藤木户忘了自己是谁”，而是“藤木户每次都能确认自己是谁，却无法记得自己为何已经做过同一件事”。他始终记得冬子与枥木、龙·玄道宋的教诲、忍者杀手的使命，也认识所有原有同伴与仇敌。被回滚夺走的只有从塔底醒来到本轮结束之间形成的情景记忆。玩家因此不会看到失去人格、重新结识伙伴的藤木户，而会看到一个判断力完整的人，不断面对可信却没有亲历感的证据。

一句话梗概：南希追踪异常 IRC 信号时打开了金阁·寺的后门，忍者杀手被卷入与黄金立方体重叠的尖塔；他一次次登顶、以忍者礼法处决建筑师，又一次次在塔底醒来，直到众人不再试图保存整段人生，只把一句必须相信的事实钉进他的灵魂边缘：**塔中有人记得忍者杀手，并在等待他。**

主线是线性的，路线是局部可变的。四次成功登顶分别对应建筑师对话的 `VisitIndex 0–3`。普通失败局是夹在四章之间、没有被完整记录的小循环；它们说明玩家实际进行的每一局都发生过，却不要求每局都生成泽渡、小季或奈落事件。固定事件、遗留物、南希校验码和塔顶对话承担主线连续性，随机事件只增加证据、代价与人物侧写。

所有普通事件与尖塔原版规则对齐：**同一事件、同一分支、同一当局状态下，标题、正文、选项和对白每次完全相同，不读取主线 `VisitIndex` 换词。** 四章大纲描述的是同一段事件文本在不同背景下产生的不同意义，不是四套事件本地化。玩家选择造成的结果页差异，以及“小季仍同行／已离队”这种当局状态分支仍可保留。涅奥和南希属于先古之民，使用原版先古对话槽位；塔顶建筑师使用 `VisitIndex 0–3` 的专属对话。二者均不属于普通事件。

## 2. 世界观：黄金立方体为何悬在尖塔之上

### 2.1 原作支点

[原文事实] 黄金立方体长久悬于 IRC 言灵空间上方，连熟练骇客也无法轻易接近；藤木户从该空间返回现实后，曾迅速失去异常旅程中的姓名与经历，只留下无力感。[第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:29] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:170]

[原文事实] 古代忍者曾把黄金立方体理解为储存忍者灵魂之处，并称其为金阁·寺；第三部进一步说明，有人发现了金阁·寺的“后门”，但相关装置会导致记忆与灵魂缺损。[第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:346] [第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:351] [第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:352] [第三部｜【第一年02】Guilty of Being Ninja by DUB-AL00｜p3-c085-s03.md:8] [第三部｜【第一年02】Guilty of Being Ninja by DUB-AL00｜p3-c085-s03.md:9]

[设计推论] 因此，黄金立方体无需被改写成尖塔制造的假象。更合适的关系是：**尖塔不是金阁·寺，尖塔的回滚核心偶然接通了金阁·寺的后门，两套系统发生了世界重叠。** 玩家在塔内仰视到的立方体是真正的金阁·寺，但它像隔着两层错误协议，既在言灵空间上方，又投影在尖塔天穹。塔中砖石、怪物与伤口均是真实的；它们在回滚时被重新编排，并非一场无后果的梦。

### 2.2 建筑师、涅奥与回滚核心

[项目设定] 建筑师原是一名神代古忍者。他拒绝等待末法默示之日，企图把自己的忍魂、建筑知识与支配意志写入金阁·寺。写入并不完整；其忍魂长期处于不能转生、不能消散的存储状态。尖塔的 Preon 构造系统从后门读取到这份人格，把它误认作适合管理塔身的蓝图，并为其制造了肉体。于是，“建筑师”既是塔的管理者，也是古忍者残魂得到的错误现世化。

[项目设定] 涅奥对尖塔的反抗与 Blight 对塔身的侵蚀，令建筑师的控制权裂成互相牵制的两部分：上层的建筑师可以调度房间、敌人与回滚；塔底的涅奥则占据回滚边界，可以观察被退回的人并送出有限赐福。二者都位于重置命令之外，所以能记住全部循环。建筑师把阻挠布置在整座塔中，却在忍者杀手抵达塔顶后被直接处决；核心随即以“修复管理者”为名恢复整座塔。涅奥无法停止命令，只能在下一轮把他重新推向上方。

黄金立方体不是最终 Boss，也不应被打碎；它负责连通忍者灵魂、言灵空间与回滚，并让京都城、银钥匙、南希和由佳乃能够介入。被反复处决的是滥用后门的建筑师。

## 3. 循环与记忆规则

### 3.1 回滚发生条件

一次循环从忍者杀手在涅奥面前醒来开始。普通死亡、放弃或未抵达塔顶，触发局部回滚；这些失败只留下模糊的错误计数，南希偶尔能看到断裂日志，却无法恢复具体经过。抵达塔顶并处决建筑师，则触发覆盖全塔的完整回滚，并令主线 `VisitIndex` 前进。完整回滚会重建地图、敌群、商店与大部分塔内物件，也会清除被卷入者本轮形成的情景记忆。

核心只把建筑师遭受致命破坏视为完整修复条件；忍者杀手死亡仅触发记录更少的局部回滚。建筑师记得每次处决，并在下一轮改造沿途房间与守卫；忍者杀手只能靠招式反射、外部记录和同伴追赶。塔顶不另设长篇战斗文本：两句专属对话结束后立即进入既有处决演出。

### 3.2 谁能留下什么

| 对象 | 回滚后保留 | 回滚后失去 | 叙事表现 |
|---|---|---|---|
| 藤木户 | 入塔前人生、人物关系、使命、基础空手道 | 本轮路线、会面、战斗与约定 | 不会问“南希是谁”，会问“这份记录何时由我确认” |
| 奈落 | 杀意、痛感、空手道反射 | 完整场景、因果和可靠时间顺序 | 只让熟悉的冲动与夺体欲再次浮现，不负责说明循环 |
| 涅奥 | 全部循环 | 无 | 通过原版先古对话槽位给出碎裂、简短的引导 |
| 建筑师 | 全部循环与塔内遥测 | 每次重建肉体的连续触感 | 由傲慢转为戒备，再转为恐惧与急躁 |
| 南希 | 塔外离线终端中的日志、校验码 | 逻辑投影在回滚瞬间的短暂感受 | 每轮都可重新读取报告，不假装亲历未保存部分 |
| 银之匙、由佳乃 | 言灵空间侧流中的观察结果 | 未被侧流覆盖的塔内细节 | 从第三轮起稳定打开同一条后门 |
| 黑暗忍者 | 京都城锚点回收的意识与战斗记录 | 临时肉体的伤痛连续性 | 能准确预判忍者杀手，死亡后化为 0 和 1 回收 |
| 小季、泽渡等实体卷入者 | 入塔前经历、性格、习惯 | 本轮爬塔记忆 | 会重复相似判断；必须阅读记录或查看遗留物 |

[原文事实] 银之匙曾以实体银钥匙打开精神世界的门，藤木户返回现实后仍把钥匙握在手中，说明极少数实体可以跨过边界而不被还原成纯粹信息。[第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:209] [第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:372] [第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:376]

[项目设定] 本模组把这条性质用作物理连续性，但不把银钥匙写成万能记忆盘。钥匙能证明“同一件物存在过”，不能把回忆直接灌回藤木户。小季的折纸标记能证明路线，南希的校验码能证明数据未被篡改，由佳乃能防止精神边界被奈落或建筑师污染；四者合在一起仍只能保存陈述，不能重现亲历感。这一限制是第三轮失败、第四轮改变策略的关键。

## 4. 四次里程碑爬塔

### 第一章：黄金立方体（`VisitIndex 0`）

南希从离线终端追到一股混有言灵空间、陌生建筑与忍者杀手生体签名的 IRC 回声。忍者杀手穿过图纸上不存在的鸟居，重力随即翻转；酸雨与霓虹消失，他在涅奥面前醒来。涅奥只指出上方的建筑师，他遂沿唯一出口开始调查。

完整内容路径依次经过泽渡的临时停火、小季的短程同行、南希在 `Glory` 的装备投送，以及第三层黑暗忍者精英战。黑暗忍者败北后化为 0 和 1，京都城同时回收这具肉体的战斗记录。塔顶的建筑师命令忍者杀手返回塔底；两句对话后，忍者杀手将其处决，黄金立方体旋转，全塔重建。回到塔底时，他只记得自己为追踪南希信号进入异地，不记得本轮爬塔。

### 第二章：只有自己不记得（`VisitIndex 1`）

涅奥仍以碎裂短句将忍者杀手推向上方；南希的先古界面则多出旧时间戳与他的认证短语。小季和泽渡同样失忆：前者靠重新收到的折纸简报行动，后者在相似地形中再次布下近似伏击。普通事件文本都不因这些背景换词。

黑暗忍者则已把暗黑长袍放在忍者杀手惯用的切入线上，并预先用居合封锁规避路线。忍者杀手由这项准备确认对方保有前轮战斗情报。第二次处决建筑师后，南希从京都城回收临时肉体时留下的通讯痕迹着手，转查银之匙、由佳乃与金阁·寺记录。

### 第三章：金阁寺后门（`VisitIndex 2`）

南希将残句与日志交叉比对后，银之匙和由佳乃在第一层固定打开一扇异常鸟居，实体银钥匙由此进入本轮。小季的折纸记录、南希写入装备的数据、银钥匙的物理连续性和由佳乃维持的精神边界组成第一版保存方案；小季在队与离队各有一套固定现场演出，但都能揭开黑暗忍者的伏击。

黑暗忍者争夺银钥匙，企图让京都城取得更稳定的现世入口；战败后，钥匙仍不进入奖励池。第三次介错建筑师后，物件和记录成功留到塔底，藤木户却只确认“先前的自己做过这些”，无法立刻恢复对证据的确信。数据保存成功，行动所需的亲历感仍然失败。

### 第四章：等待者（`VisitIndex 3`）

众人放弃保存完整经历，只固定一句最小命题：**“塔中有人记得忍者杀手，并在等待他。”** 小季留下可由身体识别的攻防节奏，南希保存同一句文本，银钥匙维持物理连续性，由佳乃保护精神边界。泽渡仍仅以可选的游击路线帮助本轮，不加入保存方案；奈落也不承担保存或复述命题的职责。

黑暗忍者最后企图夺取塔的重建能力，却在败势中判断继续占据会危及京都城，因而撤回临时肉体；这不是和解。第四次登顶时，建筑师已公开恼怒。藤木户记不起前三战，却凭银钥匙、身体节奏和最小命题继续完成介错。

忍者杀手再度在涅奥面前醒来。路线、同伴与处决细节仍然消失；但他触到银钥匙后保留了那句命题，于是主动走向阶梯。循环尚未解除，爬塔的主导权已经改变。

## 5. 相遇形式与机制规格

玩家可见文本分为三种界面。**先古对话**只有角色气泡和按钮，不使用旁白；**普通事件页**允许短旁白、对白、选项和结果页，并在相同当局状态下跨循环逐字一致；**塔顶专属对话**只供建筑师按 `VisitIndex` 切换。下列普通事件初始页以 70–110 个可见字符为目标，最多 120；结果页以 35–80 个可见字符为目标，最多 90。富文本标记不计入长度，标点计入。

### 5.1 涅奥：塔底的先古之民

涅奥每轮固定出现并提供赐福。她使用原版先古对话槽位，而非普通事件正文；语气保持 `[sine]`、停顿和“醒来／向上／建筑师”等短词，不解释循环，也不配套任何玩家可见旁白。

**初访：**

1. **涅奥：** `[sine]...两道...声音... 一具...身体.... 你是...何物...？[/sine]`
2. **忍者杀手：** `DOMO，涅奥=SAN。忍者杀手DESU。汝是忍者吗。`
3. **涅奥：** `[sine]...不是.... 向上.... 汝会...找到...应杀之人....[/sine]`

**再次醒来：**

- **涅奥：** `[sine]...复仇者... 又一次.... 醒来... 向上去吧....[/sine]`

**登顶后的再访：**

1. **涅奥：** `[sine]...你...抵达过.... 却又...回到...这里....[/sine]`
2. **忍者杀手：** `敌人未死。那便再上去。`
3. **涅奥：** `[sine]...那么.... 再杀...一次....[/sine]`

### 5.2 泽渡·弗利斯特：移动的丛林

事件仅进入第一层普通事件池。泽渡侦察异常补给通道时被卷入尖塔；蛙人、海德拉仅通过无线电和远处动静存在。植物、毒液与重组房间会触发越南幻觉，但不削弱其地形判断、补给责任和撤退规划。

[原文事实] 泽渡能在敌方设施中长期潜伏、掌握复杂地形和前进路线，也曾与忍者杀手面对共同机械敌人时立即达成短暂停火并互相掩护。[第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:56] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:66] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:69] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:73]

[原文事实] 生化锭对忍者杀手只是无价值的绿色羊羹，却关系到幸存者道场成员的生存；泽渡会为部下缺乏生化锭而痛苦。[第一部｜【新埼玉炎上17】ONE MINUTE BEFORE THE TANUKI 狸猫前一分钟｜p1-c017-s01.md:90] [第一部｜【新埼玉炎上17】ONE MINUTE BEFORE THE TANUKI 狸猫前一分钟｜p1-c017-s01.md:91]

**初始页（玩家可见）：**

> 竹枪从补给箱后刺出！忍者杀手偏头，绊索收紧。泽渡伏在毒藤后。“DOMO，忍者杀手=SAN。泽渡·弗利斯特DESU。”“DOMO，泽渡·弗利斯特=SAN。忍者杀手DESU。”门外爪足齐响。“西贡……决斗延后。先杀它们。”

| 选项 | 即时结果 | 剧情解释 |
|---|---|---|
| **共同设伏** | 获得限时事件遗物“游击战准备”；随后 3 场战斗所有敌人开战时获得 1 层虚弱和 1 层易伤 | 两人以尖刺门、绊索和毒性植被建立杀伤区；房间移动后被强行分开 |
| **现在决胜** | 失去 8 点生命；随机升级 2 张攻击牌 | 短暂空手道交锋逼忍者杀手修正攻击；泽渡用烟幕与预设退路脱离 |
| **离开伏击区** | 无奖励结束 | 忍者杀手退回通道；双方保留决斗关系 |

**共同设伏结果页：**

> 泽渡扳下起爆器。尖刺与毒藤吞没冲入门内的敌群！二人随即从左右杀入。最后一头怪物倒下时，房间开始移位。停火到此为止。

**现在决胜结果页：**

> “咿呀—！”竹枪与赤黑手刀碰撞！泽渡借力翻入烟幕。忍者杀手没有追击；方才的短促交锋已令两式空手道得到修正。

**离开伏击区结果页：**

> 忍者杀手割断绊索，退回门外。泽渡没有追击，只把竹枪重新对准入口。二人的决斗并未结束。

生化锭只作为泽渡必须保护的补给出现，不可选择食用或治疗忍者杀手。合作不是宽恕。后续循环重遇时，泽渡不记得合作，却会因近似地形和敌情再次做出近似部署。

### 5.3 银之匙与由佳乃：错误的鸟居

从第三次里程碑起，本事件固定出现在第一层，不进入随机事件池。二人位于言灵空间侧流，不以实体队友常驻。事件必定取得剧情用银钥匙，再选择一种牌组辅助：**银之匙开错路**移除一张牌；**由佳乃校正呼吸**升级一张牌。两项不改变钥匙或结局。

银之匙会承认不安，但关键动作必须坚定；由佳乃平静、具体，需要制止错误行动时直接喝止。二人不使用谜语式比喻，也不重演相识过程。

**初始页（玩家可见）：**

> 0与1爬满歪斜鸟居。忍者杀手正要触碰，手腕忽被由佳乃扣住。“忍者杀手=SAN，别碰。门里的气息正在变。”退路轰然坠落！银之匙望着锁孔，脸色发白。“我不敢保证另一头有什么。可是锁还能开。”他将银钥匙插到底。

**银之匙结果页：**

> 银之匙把钥匙逆向拧到底。门后不是阶梯，而是一段被切离尖塔的道路。缠绕忍者杀手的多余残像在那里消失了。

**由佳乃结果页：**

> 由佳乃没有让忍者杀手立刻进门。她用木杖敲正他的肩与肘。“再来一次。”鸟居开启时，那一式空手道已无多余动作。

### 5.4 矢本·小季：五场护送

事件只允许在第二层固定宝箱之后出现，删除第三层前半的候补生成。标题与选项改为任务语言。**并肩前进**取得现有同伴遗物，小季使用折纸飞弹与居合斩参与恰好五场战斗；**分头行动**保留获得 50 金币效果，解释为小季交付备用万札后赶往南希中继点。

第五场结算后，小季携带折纸路线与战斗记录离队，演出不得造成死亡误解。遭遇黑暗忍者时，按同伴遗物是否仍有效切换在队／离队文本。

[原文事实] 小季曾直接感受到天空黄金立方体与 0／1 异象，因而让她的折纸忍术对重叠边界产生反应，并非凭空赋予的侦测能力。[第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s10.md:70]

**初始页（玩家可见）：**

> 纸鹤从宝箱后射出！忍者杀手夹住它，它自行展开，落回小季掌中。“DOMO，忍者杀手=SAN。矢本·小季DESU。”“DOMO，矢本·小季=SAN。忍者杀手DESU。”她验过折痕。“南希=SAN的记号。咱也要上去。同行吧。”

**并肩前进结果页：**

> 小季把纸鹤折成飞弹，站到忍者杀手身侧。“咱会跟上。”前方铁门升起，两人同时踏入阴影。

**分头行动结果页：**

> 小季递出一叠备用万札。“中继点那边不能没人。忍者杀手=SAN，之后再会。”纸鹤簇拥她飞入另一条通道。

黑暗忍者事件按小季当局状态选用以下固定短段，两套文本本身均不随循环变化：

- **仍在队伍：** 暗黑长袍从瓦砾后滑出。小季的纸飞弹钉住衣角。“忍者杀手=SAN，那东西刚才不在那里。”“嗯。别碰长袍。”她已拔刀守住左侧。
- **已经离队：** 门槛下压着一只向左的纸鹤。忍者杀手刚将它拾起，纸翼便被居合刀风切断！他随即伏身。刀锋掠过头顶，斩入石壁。

### 5.5 南希·李：`Glory` 的先古之民

南希继承 `ModAncientEventTemplate` 并注册于第三层 `Glory`。塔内形象是逻辑化身，本体仍在塔外维护离线终端。她始终是忍者杀手的老战友，不重新采访、不询问身世，也不把奈落当作新奇研究对象。

通信带宽每次只能传送一件完整装备。现有三组奖池保持不变，每组随机展示一项，玩家从三项中选择一件：IRC终端机／Mother UNIX访问密钥，NSTV特派记者证／南希的电子墨镜，吸附型脉冲地雷／坐禅Drink枪带。无论选择哪件，静默界面都记录同一份外部数据；角色不朗读日志或奖励机制。

南希只使用 `firstVisitEver` 和两组 `ANY` 先古对话，不配置玩家可见旁白：

**首次：**

1. **南希：** `你迟到了三分四十秒，忍者杀手=SAN。`
2. **忍者杀手：** `路上有忍者。`
3. **南希：** `我猜也是。能送进去的东西只有一件。选吧。`

**随机再访一：**

1. **南希：** `线路还能维持九十秒。你有六十秒。`
2. **忍者杀手：** `余下三十秒呢。`
3. **南希：** `我的问题。去拿装备。`

**随机再访二：**

1. **南希：** `右侧只有一个生命反应。`
2. **忍者杀手：** `忍者。`
3. **南希：** `当然。所以我把装备放在这里。`

### 5.6 奈落：神经元中的第二道声音

奈落事件保留第三层现有条件、页面结构和所有数值：依次支付 5／6／7 点不可格挡、非强化生命伤害，之后可“接受奈落”或“令其闭嘴”。页面只加深奈落对身体主导权的侵入，不提供循环解说，也不把服从奈落设为正史答案。

[原文事实] 奈落会在忍者杀手虚弱时嘲弄其失态并企图夺取身体；忍者杀手拒绝交身后，仍可利用二者共同存亡的关系逼奈落提供帮助。[第一部｜【新埼玉炎上04】MACHINE OF VENGEANCE 复仇机器｜p1-c005-s01.md:115] [第一部｜【新埼玉炎上04】MACHINE OF VENGEANCE 复仇机器｜p1-c005-s01.md:118] [第一部｜【新埼玉炎上-支线】BLACK STRIPES 黑色条纹｜p1-c029-s01.md:24] [第一部｜【新埼玉炎上-支线】BLACK STRIPES 黑色条纹｜p1-c029-s01.md:29]

以下是按现有页面拆分的原创基准。它们依据关系与语气创作，不复现原文句子，也不把某个词当作必现条件。

**`INITIAL`：**

> 赤黑色的火在神经元深处睁眼。(((喀喀喀……爬到这种地方也会疲惫么，藤木户。让老夫来。))) 忍者杀手以茶道呼吸压住右手。那只手仍在自行寻找刀柄。

**`CALL_1`：**

> 痛楚沿脊椎烧上后脑。奈落窥向高处，发出低笑。(((上面有值得一杀的家伙。汝这副迟钝身体，何时才能爬到那里？))) “闭嘴。”忍者杀手站直。

**`CALL_2`：**

> 视野染成赤黑。右臂猛然抬起，不听使唤！(((交给老夫，省得汝继续出丑。))) 忍者杀手以左手扣住右腕。骨头作响。手刀停在咽喉之前。

**`ACCEPT`：**

> 奈落的笑声与心跳重叠。(((很好。只需放松。杀意会替汝走完余下的路。))) 忍者杀手的五指缓缓松开。现在，他必须决定由谁站起来。

**`ACCEPTED`：**

> 忍者杀手不再压制赤黑之火。第二道呼吸从他口中吐出。石室里只剩重叠的笑声。

**`SILENCED`：**

> “奈落，闭嘴。”茶道呼吸贯通四肢。自行抬起的右手落回身侧，神经元中的笑声沉入黑暗。

### 5.7 黑暗忍者：京都城的回收体

新增第三层不可回避的精英战斗事件。招式取第三部形象：猛虎突击式连击，暗黑长袍防御或替身，居合建立威胁，“死·斩”作为预告重击。标题、入场描述、Aisatsu、胜利页和选项在四轮中完全一致；不同轮次的争夺目标只存在于幕后剧情。

[原文事实] 黑暗忍者能用蜕下长袍完成替身，以暗黑长袍承受特殊攻击，并施展死·斩。[第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:28] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:32] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:34]

[原文事实] 第三部的黑暗忍者可借京都城线路消费临时肉体在现世出现，并在完成行动后化为 0 和 1 消失；这为跨循环意识锚提供原作侧依据。[第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:41] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:42]

**战前页（玩家可见）：**

> 暗黑长袍立在石室。衣内无人！杀气从背后袭来。“DOMO，忍者杀手=SAN。黑暗忍者DESU。”“DOMO，黑暗忍者=SAN。忍者杀手DESU。”手里剑贯穿长袍。GOURANGA! 真身已伏在忍者杀手的落脚处！死·斩迫近！

**胜利页（玩家可见）：**

> 黑暗忍者的临时肉体从伤口处化为 0 与 1。刀锋、长袍和冷笑依次消失，尽数被石墙深处的京都城线路收走。

战后恢复事件页面并正常发放精英奖励。当前肉体不说 `Sayonara`，不判定永久死亡，不掉落别嫔。无论小季是否出现，事件均可完整结算。

### 5.8 建筑师：塔顶专属对话

建筑师按 `VisitIndex 0–3` 使用四组文本。每组严格只有建筑师一句、忍者杀手一句；第二句结束后立即触发既有处决演出。没有玩家可见入场旁白、战斗旁白、胜利收束或书面介错感叹。

**访问 0：**

1. **建筑师：** `DOMO，忍者杀手=SAN。建筑师DESU。回到塔底。`
2. **忍者杀手：** `DOMO，建筑师=SAN。忍者杀手DESU。拒绝。`

**访问 1：**

1. **建筑师：** `又是你。这里不该留下这种错误。`
2. **忍者杀手：** `错的是汝。`

**访问 2：**

1. **建筑师：** `你甚至不知道自己来过。`
2. **忍者杀手：** `无关紧要。汝仍在这里。`

**访问 3：**

1. **建筑师：** `我还要忍受你多少次？`
2. **忍者杀手：** `直到汝不再归来。`

处决令建筑师肉体死亡并触发完整回滚，但不代表永久消灭；四轮一律不说 `Sayonara`。第四轮只保留最小命题，不立刻摧毁金阁·寺，也不治愈全部记忆损伤。

## 6. 叙事落地规则

1. **先分界面，再写文字。** 涅奥和南希只有先古对话；建筑师只有塔顶两句式对话；旁白只进入普通事件的描述与结果页。
2. **普通事件文案静态。** 普通事件及既定状态分支跨循环逐字一致，不读取 `VisitIndex`。先古对话槽位和建筑师专属访问对话不受此条限制。
3. **长度按页面计算。** 初始页目标 70–110 个可见字符，结果页目标 35–80；任何单页不得为容纳完整剧情而膨胀成小说段落。
4. **叙述者不是监控镜头。** 旁白紧随现场，却可短暂进入人物思考、突然指出危险或补充一项与眼前转折直接相关的事实；不强制限制为角色已经确认的观察。
5. **事件不写成报告。** 不固定“状况、判断、动作、反制、结果”的流水线，不为展示聪明而罗列距离和角度。事件可以停在危险逼近、人物提议或玩家选择之前。
6. **称呼属于关系。** 忍者行动场景的旁白使用“忍者杀手”；伙伴和敌手称“忍者杀手=SAN”。奈落可因寄生关系直呼“藤木户”，但不把该称呼当作口癖。
7. **对白只回应眼前。** 南希报时并处理线路，泽渡下令，小季直率回应，银之匙承认不安，由佳乃直接制止，奈落争夺控制；无人朗读世界观、玩法或奖励。
8. **角色关系不重置。** 忍者杀手认识所有原有同伴与仇敌；选择只改局部结果，不让他抢劫小季、食用生化锭、交易奈落或因停火宽恕泽渡。
9. **随机缺席不阻断主线。** 小季缺席时仅送达无奖励折纸；南希未生成时由外部日志补足必要数据；泽渡与奈落缺席只减少人物侧写。
10. **死亡语义与原创边界严格。** 被京都城回收者和会被回滚重建者不说 `Sayonara`。所有玩家文本均为模组原创；引文只校准事实、关系和语气，不拼接原句。

## 7. 后续实装清单（本轮不执行）

- 新增第一层泽渡普通事件、三分支结算及限时遗物“游击战准备”。
- 将小季事件收紧到第二层固定宝箱之后，移除第三层候补，重写事件与遗物名称、选项和告别文本，保留五战同伴与 50 金币数值。
- 新增第三轮起第一层固定的银之匙／由佳乃双人事件、实体银钥匙剧情状态和移牌／升级二选一。
- 将南希保持为 `Glory` 先古之民，以 `firstVisitEver` 和两组 `ANY` 承载纯对白；保持三池各随机一项、最终三选一的奖励算法。
- 重写奈落全部现有页面，以夺体冲突替换循环残句，不改变 5／6／7 生命代价及接受／闭嘴分支。
- 新增第三层黑暗忍者怪物、精英战斗事件、单套固定战前文本、京都城回收演出、小季在队／离队条件对白。
- 将建筑师 `VisitIndex 0–3` 对话收为每组两句，并在第二句后触发既有处决演出；补充四次回滚与最小命题结局。
- 新增跨局剧情进度、失败局弱日志、主线固定事件保护，以及“剧情物不占普通遗物奖励”的存档规则。
- 完成中英文本地化后，分别检查字符长度、礼法拼写、死亡语义、事件楼层和所有缺席分支；代码与资源测试另立实装任务。

### 7.1 当前文本与运行时偏差

以下问题仅记录供评估，本轮不修改对应文件：

| 位置 | 当前问题 | 后续目标 |
|---|---|---|
| `NinjaSlayer/localization/zhs/ancients.json` 南希首次对话 | 南希称“藤木户=SAN” | 改称“忍者杀手=SAN” |
| 同文件南希 `pages.DONE.description` | 先古相遇结束页含第三人称旁白 | 移除旁白，改由先古对话或界面状态承担 |
| 同文件 `TANX` 的两个 `char` 槽位 | 角色槽位写入第三人称碰撞叙述 | 改为忍者杀手实际说出的短 Kiai |
| 同文件建筑师四组对话 | 每组仍为三句，且访问 2 使用藤木户本名 | 按本纲改为每组两句 |
| `NinjaSlayer/localization/zhs/events.json` 奈落 | 主要复述妻儿噩梦，未表现奈落趁虚夺取主导权的关系 | 按现有页面结构重写短段 |
| 同文件小季 | 标题、选项与结果仍是卖萌、抱走和打劫语气 | 改为同行与分头执行任务 |
| `Code/Patches/ArchitectExecutionPatch.cs` | 当前补丁会跳过忍者杀手的全部建筑师对白 | 后续允许两句对话播放完毕再开始处决 |

## 8. English Writing Guide (740–780 words)

### Scope and reference order

This guide governs original English localization, not copied serialized prose. Use [@NJSLYR](https://x.com/NJSLYR) and the official [8th-anniversary examples](https://diehardtales.com/n/n276ec768fd4b?hl=en) for recurring usage, Aisatsu, combat cries, KAISHAKU, and explosive-death formatting. The [fan-translation guidelines](https://diehardtales.com/n/n96e186db18ff?hl=en) set the publication boundary.

Neow and Nancy use Ancient speech bubbles without narration. Ordinary events use a short description, dialogue, choices, and results. The Architect has four visit-indexed summit exchanges: one Architect line, one Ninja Slayer line, then the execution animation. Never place narration in an Ancient bubble or summit exchange.

Ordinary-event copy is invariant across loops and never reads `VisitIndex`. Current-run state may select a fixed branch, such as Yamoto being present or departed. Ancient and summit indices remain native dialogue systems.

Player-facing prose is an immediate encounter, not a briefing or miniature chapter. Fit one disturbance and one reaction into a base-game-sized page; stop when danger advances, someone proposes action, or the player must choose. Put counts and rewards in options. Never expose `VisitIndex`, the rollback core, memory anchor, checksum, or Kinkaku backdoor.

### Fixed names and terms

Use these forms: **Ninja Slayer**, **Kenji Fujikido**, **Naraku Ninja** or **Naraku**, **Nancy Lee**, **Yamoto Koki**, **Forest Sawatari**, **Silver Key**, **Yukano**, **Dark Ninja**, **Neow**, and **the Architect**. World terms are **Neo-Saitama**, **Kotodama Space**, **the Golden Cube**, **Kinkaku Temple**, **Kyoto Castle**, and **Survivor Dojo**. Also use **IRC**, **UNIX**, **Preon**, **shuriken**, **karate**, **jutsu**, **Chado breathing**, **ninja soul**, and **origami missile**.

Use **Ninja Slayer** in action narration and **Ninja Slayer-san** when Nancy, Yamoto, Sawatari, Yukano, Silver Key, or Dark Ninja addresses him. Reserve **Kenji Fujikido** for human-identity analysis, deliberate disclosure, or Naraku's invasive address. Allies do not use it casually.

Use **-san** after the addressed name in Aisatsu and relationship-sensitive dialogue. Keep **Domo** and **desu** untranslated in the formal formula, but do not attach honorifics to every casual sentence. A full Aisatsu remains `“Domo, [Opponent]-san. [Ninja Name] desu.”`

### Aisatsu and dialogue

Ninja Slayer identifies, judges, and acts without recapping the plot. Nancy uses exact times and dry operational humor. Yamoto is earnest and practical, never cute for its own sake. Sawatari combines field commands, supply anxiety, and intermittent false Vietnam memories; **“Saigon!”** is occasional, not his personality. Silver Key admits fear before committing. Yukano is calm and plain, then severe when discipline requires it. Dark Ninja is formal, cold, and contemptuous.

Naraku is an ancient, malicious second will. When Ninja Slayer weakens, he mocks him, treats surrender as obvious, and notices strong prey. This relationship is a voice anchor, not a phrase checklist. Deeper pages increase bodily intrusion; branch choices let Ninja Slayer resist, suppress, or allow him. Naraku never recites loop lore.

Neow follows the base game's voice: `[sine]` markup, broken clauses, long pauses, and simple words such as *awaken*, *go*, and *Architect*. The Architect uses terse dismissal, correction, surprise, and mounting irritation. Nancy, Neow, and the Architect receive no narrator prose on their dialogue surfaces.

### Combat narration

Use Ninja Slayer's mobile external narrator. Stay near the action, but allow a sudden warning, brief thought, or one relevant fact. Do not force every page through situation, reading, action, counteraction, and consequence; that produces a report. Angles, distances, and geometry belong only where they create danger, humor, or reversal, not as proof of authenticity.

Use **“YEEART!”** for committed karate kiai, **“GWAARGH!”** for a serious hit, and **“AIEEE!”** chiefly for fear or panic. Sudden narrator calls are exceptional. **GOURANGA!** may mark Dark Ninja's decoy reversal; do not add **NAMU-SAN!**, **SATSUBATSU!**, or impact noises merely for texture. The Architect's execution is shown by animation, not a written narrator call.

### Death, rollback, and memory

A permanent ninja death may end with `“Sayonara!”` followed by **exploded and scattered**. Never use that closure for Dark Ninja's tower body: it becomes zeroes and ones and is recalled by Kyoto Castle. The Architect also receives no `Sayonara` during Visits 0–3 because rollback reconstructs him. If **KAISHAKU** is used elsewhere as a narrator call, capitalize it, but do not add it to the two-line summit exchange.

Ninja Slayer never forgets who Nancy, Yamoto, Yukano, Silver Key, Sawatari, or Dark Ninja are; he forgets tower episodes only. Distinguish memory from evidence: Nancy **reads** an offline log, Yamoto **recognizes** her notation, and Ninja Slayer **verifies** a record but does not **remember** writing it. Naraku may reproduce murderous pressure or a karate reflex, never a convenient recap.

The final invariant sentence is: **“Someone in the Spire remembers Ninja Slayer and is waiting for him.”** Keep its wording stable wherever it is treated as the anchored proposition. Surrounding dialogue may call the place “the tower,” but the stored English proposition uses this exact sentence.

## 9. 原作与英文格式索引

- 黄金立方体、言灵空间与返回后的记忆缺损：[第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:29] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:161] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:170]
- 忍魂存储与金阁·寺命名：[第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:346] [第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:351] [第二部｜Curse of Ancient 汉字｜p2-c019-s02.md:352]
- 金阁·寺后门及装置造成的记忆／灵魂缺损：[第三部｜【第一年02】Guilty of Being Ninja by DUB-AL00｜p3-c085-s03.md:8] [第三部｜【第一年02】Guilty of Being Ninja by DUB-AL00｜p3-c085-s03.md:9]
- 实体银钥匙跨越精神边界：[第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:209] [第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:372] [第二部｜Diffusion·Accumulation·Reborn·Destruction｜p2-c025-s02.md:376]
- 小季与黄金立方体／0、1 异象：[第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s10.md:70]
- 黑暗忍者、暗黑长袍、死·斩与京都城回收：[第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:28] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:34] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:41] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:42]
- 泽渡的地形利用与临时共斗：[第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:56] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:66] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:73]
- 泽渡、生化锭与幸存者道场责任：[第一部｜【新埼玉炎上17】ONE MINUTE BEFORE THE TANUKI 狸猫前一分钟｜p1-c017-s01.md:90] [第三部｜[第3部050]Nichome War by alex.ma｜p3-c052-s01.md:43] [第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s09.md:119] [第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s15.md:111]
- 奈落在虚弱时嘲弄、争夺身体及共同存亡下的交涉：[第一部｜【新埼玉炎上04】MACHINE OF VENGEANCE 复仇机器｜p1-c005-s01.md:115] [第一部｜【新埼玉炎上04】MACHINE OF VENGEANCE 复仇机器｜p1-c005-s01.md:119] [第一部｜【新埼玉炎上-支线】BLACK STRIPES 黑色条纹｜p1-c029-s01.md:24] [第一部｜【新埼玉炎上-支线】BLACK STRIPES 黑色条纹｜p1-c029-s01.md:29]
- 人物称呼：南希称“忍者杀手=SAN”[第三部｜(Untitled)｜p3-c084-s01.md:125] [第三部｜[第3部050]Nichome War by alex.ma｜p3-c052-s04.md:33]；银之匙与二丁目人物称“忍者杀手=SAN”[第三部｜[第3部049]Farewell My Shadow 后篇 by alex.ma｜p3-c051-s02.md:43] [第三部｜[第3部050]Nichome War by alex.ma｜p3-c052-s06.md:123]；奈落可直呼藤木户[第一部｜【新埼玉炎上02】BACK IN BLACK 归于黑暗｜p1-c003-s02.md:99] [第三部｜[第3部046]Death of Achilles by alex.ma｜p3-c047-s02.md:9]。
- 临时共斗与替身逆转的动作节奏：[第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:64] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:66] [第一部｜Like A Blood Arrow Straight 宛如直飞血矢｜p1-c041-s02.md:70] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:27] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:28] [第三部｜[第3部023]【京都城潜入01】Zaibatsu Young Team by DUB-AL00｜p3-c023-s03.md:34]
- 异常空间、立方体异变与返回现实后的观察顺序：[第三部｜[第3部058]【鹫之翼02】Nichome Ohigan Lockout by 84701｜p3-c060-s01.md:28] [第三部｜[第3部058]【鹫之翼02】Nichome Ohigan Lockout by 84701｜p3-c060-s01.md:32] [第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s10.md:68] [第三部｜[第3部064]Ninja Slayer Never Dies by Zhizh｜p3-c069-s10.md:70] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:160] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:164] [第二部｜Glance·of·Mother-Curse｜p2-c002-s01.md:169]
- 人物语气抽查：小季的简短应答与“咱”[第三部｜[第3部037]【十月十日前02】Nichome War... Beginning by alex.ma｜p3-c037-s01.md:121]；银之匙承认不安后承担任务[第三部｜[第3部058]【鹫之翼02】Nichome Ohigan Lockout by 84701｜p3-c060-s01.md:13]；由佳乃的平静劝导与直接喝止[第三部｜[第3部041]【京都城潜入04】Under the Black Sun by DUB-AL00｜p3-c041-s01.md:66] [第三部｜[第3部041]【京都城潜入04】Under the Black Sun by DUB-AL00｜p3-c041-s01.md:90]；黑暗忍者的冷静判断[第一部｜【新埼玉炎上25】CONSPIRACY UPON THE BROKEN BLADE 断刀之谋｜p1-c025-s01.md:91]。
- 涅奥与建筑师语气：本地《杀戮尖塔 2》0.110.0 原版 `localization/zhs/ancients.json` 中的 `NEOW`、`THE_ARCHITECT` 文本。
- 英文节奏与格式参考：[@NJSLYR](https://x.com/NJSLYR)、[官方周年短例](https://diehardtales.com/n/n276ec768fd4b?hl=en)、[官方同人翻译规范](https://diehardtales.com/n/n96e186db18ff?hl=en)。
