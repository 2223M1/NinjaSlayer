# 忍者杀手 · 重画名单与美术需求

## 本轮采用范围

根据用户交回的“忍者杀手_整改编辑板_已审查.html”整理，审查板指纹为 `9a57b111e560052d8be59b7cf0271c93ecb9933d2115eb27f46721917b74454f`。执行时工作树提交为 `94c9807515ec6006e5d85d784d3e4af1782fda07`。本文沿用已审查板中的图像观察、建议与证据标记，不新增原作设定结论。

- 全部93张在用卡牌采用审查板的中英文名称分配；Chop打击、直拳、崩拳、掌打及原技能上的海军战锤保留，燃血改为强攻。
- 仅下表9张卡牌采用新版效果文案及已有选择提示。其余84张的非名称字段逐字保留，取舍和月面宙反虽标为搁置，也仍采用名称分配。
- 16件遗物与38个Power类对应的现有玩家文本采用提案。后台辅助类不新增本地化键；钩绳临时降力量继续继承原版文字。
- 事件奖励说明仅同步别嫔残刃、生化竹片的名称引用，中英共6处。不采用其他事件、关键词或手里剑详情的改稿。
- 卡牌目录同步中英文名称与相关引用，原有机制说明保持不变。
- 本轮不迁移类名、内部ID或本地化键，不改机制、奖励、图片和资源映射；不提交Git。本文是后续重画的交接单，不代表图片已制作或已经替换。

| 采用文案的卡牌 | 当前类名 |
|---|---|
| 黑炎 | `BlackFlameRedesignV1` |
| 砍刀 | `SawatariMachete` |
| 嘶哈 | `MetabolicAccelerationRedesignV1` |
| 半月圆规踢 | `SweepKickRedesignV1` |
| 巴投 | `ObserveBattlefield` |
| 聚气 | `GatherKi` |
| 强攻 | `Slaughter` |
| 重拾 | `Excavate` |
| 一心同体 | `OneBodyOneSoul` |

本次检查未发现卡牌、Power、遗物源码或在用图片偏离审查基线；新增的泽渡事件会话流程不在本文的修改范围。

## 工作量与顺序

| 分类 | 建议重画 | 可调配现有素材 | 可保留 |
|---|---:|---:|---:|
| 卡牌 | 25 | 2 | 66 |
| Power | 26 | 0 | 12 |
| 遗物 | 7 | 0 | 9 |
| 合计 | 58 | 2 | 87 |

这里统计的是模型条目，不是独立图片文件。多个Power目前共用同图，后续拆分时应分别交付，不能直接覆盖共享源图。

建议先处理主体明显错误的卡图和遗物，再拆分难以辨认的Power同图。BS1260踢与海军战锤的精确动作仍待核实；玛卡古调图只是近似动作候选，不能据此认定原作招式姿势。卡牌文案未获采用，不影响本表按已采用名称提出重画需求。

## 通用美术要求

- 延续项目现有的粗暗轮廓、手绘厚涂与高对比色块。卡图靠人物动作和受力关系表达效果，不把抽牌、能量、数值画成漂浮的界面。
- 卡图优先沿用1000×760横幅比例，动作落点放在中央可裁切区，缩至卡面后仍能辨认。一心同体当前为606×852竖图，重画时先检查卡面裁切，不能直接拉伸旧图。
- Power使用透明背景、大轮廓和单一辨识重点，按当前256×256资源尺寸交付，同时检查64与32像素。允许拳、箭头、环线、牌形边角等UI抽象符号，不画需要放大才认得的全身人物或招式文字。
- 遗物以居中单物件为主，透明背景。先制作256×256主稿，再按条目列出的现有normal、large、outline规格输出；三版轮廓必须对应。当前部分normal与outline为85×85，不统一误写成128×128。
- 不以颜色作为唯一差别。同图拆分后要能仅凭剪影区分“未来收益”“群攻”“轮转”“备刃”等效果。不要给忍者凭空添加实体战锤、气象魔法、医疗光束或新的装备能力。
- 交付素材先按当前类名分目录；本文的现用路径仅供找图。后续接入时再调整别名与资源映射，不能因为显示名称改变就自动改ID或覆盖同名文件。

## 两项素材调配

| 接收卡牌 | 建议复用的现图 | 原持有者后续处理 |
|---|---|---|
| 空手入白刃 | [cards/ObserveBattlefield.png](../NinjaSlayer/images/cards/ObserveBattlefield.png) | 巴投另画后倒蹬腹的抛投；先保存旧夹刃图，不能被新巴投图覆盖。 |
| 玛卡古 | [cards/TonyRetention.png](../NinjaSlayer/images/cards/TonyRetention.png) | 审势另画防守中的观察；单手倒立图仅作近似动作候选，原作腿路仍待核实。 |

## 逐项清单

### 卡牌 · 27项

#### C01 茶道 · 建议重画

- 英文名：Chado。
- 当前类名：`ChadoEnergyRedesignV1`；[Cards/RedesignV1/ChadoEnergyRedesignV1.cs](../Cards/RedesignV1/ChadoEnergyRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_CHADO_ENERGY_REDESIGN_V1`。
- 现图：[cards/ChadoCard.png](../NinjaSlayer/images/cards/ChadoCard.png)，1000×760。
- 当前画面：茶杯与延伸的小径。
- 不适配处：咖啡杯及无限小径容易落入字面茶饮。
- 重画／调配需求：以无把漆器茶碗为前景，忍者腹部有克制呼吸纹；碗占三分之一，避免咖啡杯、文字与法阵。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第一部｜DARK NINJA RETURNS 黑暗忍者归来｜p1-c042-s01.md:71]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C02 投石器摔 · 建议重画

- 英文名：Catapult Throw。
- 当前类名：`WhiskTeaFlashRedesignV1`；[Cards/RedesignV1/WhiskTeaFlashRedesignV1.cs](../Cards/RedesignV1/WhiskTeaFlashRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_WHISK_TEA_FLASH_REDESIGN_V1`。
- 现图：[cards/WhiskSlash.png](../NinjaSlayer/images/cards/WhiskSlash.png)，1000×760。
- 当前画面：大拳砸向弯曲手臂。
- 不适配处：新名是投石器摔，现图完全是拳臂。
- 重画／调配需求：双人侧视，忍者抓领向后倒，敌人被抛过头顶；让抛物线和抓领关系清楚，不画阿拉巴马式垂直倒栽。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上36】STRANGER STRANGER THAN FICTION 比虚构还要离奇｜p1-c040-s03.md:78]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C03 倒挂金钩踢 · 建议重画

- 英文名：Somersault Kick。
- 当前类名：`OneDrinkOneStrikeRedesignV1`；[Cards/RedesignV1/OneDrinkOneStrikeRedesignV1.cs](../Cards/RedesignV1/OneDrinkOneStrikeRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_ONE_DRINK_ONE_STRIKE_REDESIGN_V1`。
- 现图：[cards/PursuitStrike.png](../NinjaSlayer/images/cards/PursuitStrike.png)，1000×760。
- 当前画面：单手支地旋转横踢。
- 不适配处：单手支地横扫不等于后空翻踢下颚。
- 重画／调配需求：侧面后空翻，脚尖自下而上命中下颚；人物形成反弓弧，别画横扫、手撑地或只有脚部特写。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上02】BACK IN BLACK 归于黑暗｜p1-c003-s01.md:78]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C04 BS1260踢 · 建议重画

- 英文名：BS1260 Kick。
- 当前类名：`SatsubatsuRedesignV1`；[Cards/RedesignV1/SatsubatsuRedesignV1.cs](../Cards/RedesignV1/SatsubatsuRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_SATSUBATSU_REDESIGN_V1`。
- 现图：[cards/RedBlackFlame.png](../NinjaSlayer/images/cards/RedBlackFlame.png)，1000×760。
- 当前画面：空中伸腿踢击。
- 不适配处：只有普通飞踢，没有1260旋转辨识。
- 重画／调配需求：保留红黑忍者空中踢击，但精确动作待原作核实后再画；不要自行按数字堆三圈光环。此项暂缓定稿。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### C05 察敌 · 建议重画

- 英文名：Read the Enemy。
- 当前类名：`LuckyStrikeRedesignV1`；[Cards/RedesignV1/LuckyStrikeRedesignV1.cs](../Cards/RedesignV1/LuckyStrikeRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_LUCKY_STRIKE_REDESIGN_V1`。
- 现图：[cards/LuckyStrikeRedesignV1.png](../NinjaSlayer/images/cards/LuckyStrikeRedesignV1.png)，1000×760。
- 当前画面：手持碎片靠近面甲。
- 不适配处：拿碎片靠近脸更像鉴定物品。
- 重画／调配需求：眼部近景看向进入边缘的细小飞刃，用目光与来袭方向联系预见；不要发光第三只眼或水晶占卜。
- 文案采用状态：仅采用名称，卡牌文案保留现状。

#### C06 左·上勾拳 · 建议重画

- 英文名：Left Uppercut。
- 当前类名：`RightHeavyPunchRedesignV1`；[Cards/RedesignV1/RightHeavyPunchRedesignV1.cs](../Cards/RedesignV1/RightHeavyPunchRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_RIGHT_HEAVY_PUNCH_REDESIGN_V1`。
- 现图：[cards/BangBangFist.png](../NinjaSlayer/images/cards/BangBangFist.png)，1000×760。
- 当前画面：屈肘拳向来拳上挑。
- 不适配处：拳路更像架住来拳，左侧归属不够清晰。
- 重画／调配需求：左拳从腰际斜上击中对手下颚，另一手收肋；确保透视中左右明确，不用文字L/R。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### C07 右·上勾拳 · 建议重画

- 英文名：Right Uppercut。
- 当前类名：`RightHeavyPunchAfterSkillRedesignV1`；[Cards/RedesignV1/RightHeavyPunchAfterSkillRedesignV1.cs](../Cards/RedesignV1/RightHeavyPunchAfterSkillRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_RIGHT_HEAVY_PUNCH_AFTER_SKILL_REDESIGN_V1`。
- 现图：[cards/LuckyStrike.png](../NinjaSlayer/images/cards/LuckyStrike.png)，1000×760。
- 当前画面：低头架臂并举拳。
- 不适配处：当前是举拳防守，缺上勾打击落点。
- 重画／调配需求：右拳斜上打出，左臂护面，从目标肩后看拳与下颚；与左拳成互补构图，不直接水平镜像。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### C08 嘶哈 · 建议重画

- 英文名：Hiss and Huff。
- 当前类名：`MetabolicAccelerationRedesignV1`；[Cards/RedesignV1/MetabolicAccelerationRedesignV1.cs](../Cards/RedesignV1/MetabolicAccelerationRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_METABOLIC_ACCELERATION_REDESIGN_V1`。
- 现图：[cards/MetabolicAccelerationRedesignV1.png](../NinjaSlayer/images/cards/MetabolicAccelerationRedesignV1.png)，1000×760。
- 当前画面：只露面甲的侧脸。
- 不适配处：单纯面甲侧脸没有恢复或呼吸动作。
- 重画／调配需求：肩颈放松、腹部起伏、手指从僵直转松，茶碗只作边缘提示；不画治疗光束或喝药瓶。
- 文案采用状态：采用新版卡牌文案。

#### C09 巴投 · 建议重画

- 英文名：Tomoe Throw。
- 当前类名：`ObserveBattlefield`；[Cards/RedesignV1/ObserveBattlefield.cs](../Cards/RedesignV1/ObserveBattlefield.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_OBSERVE_BATTLEFIELD`。
- 现图：[cards/ObserveBattlefield.png](../NinjaSlayer/images/cards/ObserveBattlefield.png)，1000×760。
- 当前画面：双掌夹住刀刃。
- 不适配处：夹刀图属于空手入白刃，不能表达巴投。
- 重画／调配需求：侧视忍者后倒、一脚抵对手腹部，借蹬腿将其翻过头顶；清楚画出抓握、支点和抛出方向，不锁双臂倒栽。现夹刀图可调配给空手入白刃。
- 文案采用状态：采用新版卡牌文案。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第二部｜Cry Havoc Bend the End｜p2-c014-s03.md:104]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C10 聚气 · 建议重画

- 英文名：Gather Ki。
- 当前类名：`GatherKi`；[Cards/RedesignV1/GatherKi.cs](../Cards/RedesignV1/GatherKi.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_GATHER_KI`。
- 现图：[cards/GatherKi.png](../NinjaSlayer/images/cards/GatherKi.png)，1000×760。
- 当前画面：双手间漂浮金色书本。
- 不适配处：金色漂浮书把呼吸写成魔法。
- 重画／调配需求：一手置腹前一手握拳，低重心呼气，胸腹到拳的小幅气流；避免秘籍、法阵、悬浮文字。
- 文案采用状态：采用新版卡牌文案。

#### C11 月面宙反 · 建议重画

- 英文名：Moonsault。
- 当前类名：`OyeahThrowSword`；[Cards/RedesignV1/OyeahThrowSword.cs](../Cards/RedesignV1/OyeahThrowSword.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_OYEAH_THROW_SWORD`。
- 现图：[cards/OyeahThrowSword.png](../NinjaSlayer/images/cards/OyeahThrowSword.png)，1000×760。
- 当前画面：忍者趴地躲避飞镖。
- 不适配处：趴地并非月面宙反。
- 重画／调配需求：忍者在空中向后翻转，从翻转轨迹投出手里剑；远近镖建立纵深，避免地面匍匐。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第二部｜Three Dirty Ninja-Bond｜p2-c017-s03.md:227]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C12 审势 · 建议重画

- 英文名：Assess。
- 当前类名：`TonyRetention`；[Cards/RedesignV1/TonyRetention.cs](../Cards/RedesignV1/TonyRetention.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_TONY_RETENTION`。
- 现图：[cards/TonyRetention.png](../NinjaSlayer/images/cards/TonyRetention.png)，1000×760。
- 当前画面：单手倒立、双腿分开的人影。
- 不适配处：这张旧玛卡古已改为格挡加预见，翻身图可留给回避牌，审势应突出防守中的观察。
- 重画／调配需求：近景忍者以前臂挡住来击，目光越过护腕观察敌人下一步；眼神、接触点与后方威胁形成三角。避免翻身、把玩纸牌或留手藏镖的旧设计。
- 文案采用状态：仅采用名称，卡牌文案保留现状。

#### C13 海军战锤 · 建议重画

- 英文名：Navy Hammer。
- 当前类名：`WasshoiRedesignV1`；[Cards/RedesignV1/WasshoiRedesignV1.cs](../Cards/RedesignV1/WasshoiRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_WASSHOI_REDESIGN_V1`。
- 现图：[cards/NinjaGreeting.png](../NinjaSlayer/images/cards/NinjaGreeting.png)，1000×760。
- 当前画面：双手相握、下方木板。
- 不适配处：画面近似破板练习；仅凭旧图无法确认这是小说招式的准确姿势。
- 重画／调配需求：以同一忍者与敌人贴身接触的一次沉重打击为主体，接触点居中，前后短残迹辅助表现接续攻击。此为效果适配构图，精确招式姿势核实前暂缓动作定稿。不要实体锤子、海军制服、扑克牌或分身。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配：按用户决定保留原归属；自动出牌与额外打出没有直接原作依据，精确日文与手脚姿势待核实。。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上-支线】DAY OF THE LOBSTER TRILOGY 龙虾之日三部曲｜p1-c046-s01.md:102]；[第三部｜Under the Black Sun｜p3-c041-s02.md:129]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C14 龙卷拳 · 建议重画

- 英文名：Tornado Fist。
- 当前类名：`TornadoFistRedesignV1`；[Cards/RedesignV1/TornadoFistRedesignV1.cs](../Cards/RedesignV1/TornadoFistRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_TORNADO_FIST_REDESIGN_V1`。
- 现图：[cards/TornadoFistRedesignV1.png](../NinjaSlayer/images/cards/TornadoFistRedesignV1.png)，1000×760。
- 当前画面：手臂挥拳并绕出弧线。
- 不适配处：原作龙卷拳是旋转双腿踢，不是拳头转圈。
- 重画／调配需求：空中斜向前旋身，双腿如镰刀扫向头颈，躯干在旋转轴上；去掉水系漩涡和拳头中心。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上19】MENACE OF DARK NINJA 黑暗忍者的威胁｜p1-c019-s01.md:68]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C15 强攻 · 建议重画

- 英文名：Press the Attack。
- 当前类名：`Slaughter`；[Cards/RedesignV1/Slaughter.cs](../Cards/RedesignV1/Slaughter.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_SLAUGHTER`。
- 现图：[cards/Slaughter.png](../NinjaSlayer/images/cards/Slaughter.png)，1000×760。
- 当前画面：拳砸向地面并激起碎裂。
- 不适配处：砸地容易被读成范围攻击；当前为对单个敌人的打击，标题也不再涉及血焰。
- 重画／调配需求：红黑忍者向画面侧前方单个敌人贴身挥拳，接触点与前压身姿占据中心，后方短残迹表达动作衔接。不要砸地冲击波、血液燃烧、实体纸牌或锤子。
- 文案采用状态：采用新版卡牌文案。

#### C16 撒菱 · 建议重画

- 英文名：Caltrops。
- 当前类名：`PlaceholderBlueDefense01`；[Cards/RedesignV1/PlaceholderBlueDefense01.cs](../Cards/RedesignV1/PlaceholderBlueDefense01.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_PLACEHOLDER_BLUE_DEFENSE01`。
- 现图：[cards/PlaceholderBlueDefense01.png](../NinjaSlayer/images/cards/PlaceholderBlueDefense01.png)，1000×760。
- 当前画面：平放的四角手里剑。
- 不适配处：现图是平面四角镖，落地后不具撒菱外形。
- 重画／调配需求：近景几枚立体四尖撒菱，一尖朝上，远景追击者鞋底将踏入；不画投掷中的平面飞镖。
- 文案采用状态：仅采用名称，卡牌文案保留现状。

#### C17 空手入白刃 · 可调配现有素材

- 英文名：Barehanded Catch。
- 当前类名：`CounteroffensiveGuardRedesignV1`；[Cards/RedesignV1/CounteroffensiveGuardRedesignV1.cs](../Cards/RedesignV1/CounteroffensiveGuardRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_COUNTEROFFENSIVE_GUARD_REDESIGN_V1`。
- 现图：[cards/CounteroffensiveGuardRedesignV1.png](../NinjaSlayer/images/cards/CounteroffensiveGuardRedesignV1.png)，1000×760。
- 当前画面：忍者低伏躲过巨拳。
- 不适配处：现图只是避开拳头，缺夹刃；巴投现图恰是双掌夹刃。
- 重画／调配需求：建议调配 ObserveBattlefield 的原图；保留双掌左右夹住刀身的接触点和刀锋方向，不新增光盾。
- 候选源图：[cards/ObserveBattlefield.png](../NinjaSlayer/images/cards/ObserveBattlefield.png)。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第二部｜Bigger cageslonger chains｜p2-c029-s02.md:287]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C18 风林火山 · 建议重画

- 英文名：Furin Kazan。
- 当前类名：`ChadoFurinKazanRedesignV1`；[Cards/RedesignV1/ChadoFurinKazanRedesignV1.cs](../Cards/RedesignV1/ChadoFurinKazanRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_CHADO_FURIN_KAZAN_REDESIGN_V1`。
- 现图：[cards/SenchaStorm.png](../NinjaSlayer/images/cards/SenchaStorm.png)，1000×760。
- 当前画面：四元素悬于人物周围。
- 不适配处：四元素悬浮易被误认为元素法术。
- 重画／调配需求：忍者借柱、低墙与高差遮断敌方视线，前景掩体中景人影远景入口；不要浮空的风树火山图腾。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### C19 岚之拳 · 建议重画

- 英文名：Storm Fist。
- 当前类名：`StormFistRedesignV1`；[Cards/RedesignV1/StormFistRedesignV1.cs](../Cards/RedesignV1/StormFistRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_STORM_FIST_REDESIGN_V1`。
- 现图：[cards/TornadoFist.png](../NinjaSlayer/images/cards/TornadoFist.png)，1000×760。
- 当前画面：拳下有水面漩涡。
- 不适配处：现图只有拳下水涡，没有四连拳或茶道爆发。
- 重画／调配需求：前景一拳命中，其他三道短拳路收敛于同一点，背后呼吸蒸汽；避免凭空水龙卷和多长手臂。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第二部｜Shadow-Con｜p2-c030-s03.md:406]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C20 重拾 · 建议重画

- 英文名：Recover。
- 当前类名：`Excavate`；[Cards/RedesignV1/Excavate.cs](../Cards/RedesignV1/Excavate.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_EXCAVATE`。
- 现图：[cards/PlaceholderGoldDefense01.png](../NinjaSlayer/images/cards/PlaceholderGoldDefense01.png)，1000×760。
- 当前画面：两掌平举防御。
- 不适配处：平掌防御与消耗堆回收不相关。
- 重画／调配需求：忍者从碎石灰烬旁拾回完好的忍具，手与物件清晰，背景克制；不画挖宝或复活尸体。
- 文案采用状态：采用新版卡牌文案。

#### C21 阿拉巴马落 · 建议重画

- 英文名：Alabama Drop。
- 当前类名：`AlabamaDropRedesignV1`；[Cards/RedesignV1/AlabamaDropRedesignV1.cs](../Cards/RedesignV1/AlabamaDropRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_ALABAMA_DROP_REDESIGN_V1`。
- 现图：[cards/AlabamaDrop.png](../NinjaSlayer/images/cards/AlabamaDrop.png)，1000×760。
- 当前画面：抱住对手横向倒落。
- 不适配处：横向抱摔不是锁臂垂直落下的阿拉巴马落。
- 重画／调配需求：竖向构图，施术者在后锁住对手双臂，两人头朝下垂直坠落；头颈接近地面，避免巴投式蹬腹或横抱。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上19】MENACE OF DARK NINJA 黑暗忍者的威胁｜p1-c019-s01.md:38]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C22 地狱龙卷 · 建议重画

- 英文名：Hell Tornado。
- 当前类名：`HellTornadoRedesignV1`；[Cards/RedesignV1/HellTornadoRedesignV1.cs](../Cards/RedesignV1/HellTornadoRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_HELL_TORNADO_REDESIGN_V1`。
- 现图：[cards/HellTornado.png](../NinjaSlayer/images/cards/HellTornado.png)，1000×760。
- 当前画面：魔法龙卷风卷着飞镖。
- 不适配处：真实龙卷风会被理解为气象术。
- 重画／调配需求：以旋转忍者为轴，四周发射多枚镖形成环向轨迹，保留人体与手部动作；不画独立风柱。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上07】SURPRISED DOJO 道场突袭｜p1-c008-s01.md:55]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C23 伺机备刃 · 建议重画

- 英文名：Watchful Blades。
- 当前类名：`RecycledBladesRedesignV1`；[Cards/RedesignV1/RecycledBladesRedesignV1.cs](../Cards/RedesignV1/RecycledBladesRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_RECYCLED_BLADES_REDESIGN_V1`。
- 现图：[cards/ShurikenThrow.png](../NinjaSlayer/images/cards/ShurikenThrow.png)，1000×760。
- 当前画面：手里剑与蓄势痕迹。
- 不适配处：旧图的资源循环感强，新名强调预见后的备刃。
- 重画／调配需求：忍者眼神盯住敌人空隙，近景指间刚扣起两枚镖；不画回收垃圾、机械发射器。
- 文案采用状态：仅采用名称，卡牌文案保留现状。

#### C24 龙·飞踢 · 建议重画

- 英文名：Dragon Flying Kick。
- 当前类名：`DragonFlyingKickRedesignV1`；[Cards/RedesignV1/DragonFlyingKickRedesignV1.cs](../Cards/RedesignV1/DragonFlyingKickRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_DRAGON_FLYING_KICK_REDESIGN_V1`。
- 现图：[cards/NinjaSlayerFootwork.png](../NinjaSlayer/images/cards/NinjaSlayerFootwork.png)，1000×760。
- 当前画面：忍者向前打出的拳臂特写。
- 不适配处：当前近景是直线出拳，不是飞踢。
- 重画／调配需求：红黑忍者跃起前踢，前景鞋底与伸直腿建立透视，后腿收起；保留胸腹和髋部连接，不能把手臂画成腿。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 沿用审查板的原作索引：[第二部｜飞踢·Versus·Amnesia｜p2-c031-s03.md:332]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### C25 澄明 · 建议重画

- 英文名：Clarity。
- 当前类名：`FurinKazanChadoRedesignV1`；[Cards/RedesignV1/FurinKazanChadoRedesignV1.cs](../Cards/RedesignV1/FurinKazanChadoRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_FURIN_KAZAN_CHADO_REDESIGN_V1`。
- 现图：[cards/TeaSamadhi.png](../NinjaSlayer/images/cards/TeaSamadhi.png)，1000×760。
- 当前画面：忍者拿着破碎面具。
- 不适配处：破面具在暗示识破身份，与人工制品无关。
- 重画／调配需求：平稳呼吸的眼部近景，来袭的细线在护腕外断开；表现免疫干扰，不画摘面具、读心或符文。
- 文案采用状态：仅采用名称，卡牌文案保留现状。

#### C26 玛卡古 · 可调配现有素材

- 英文名：Macaco。
- 当前类名：`HardItOutRedesignV1`；[Cards/RedesignV1/HardItOutRedesignV1.cs](../Cards/RedesignV1/HardItOutRedesignV1.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_HARD_IT_OUT_REDESIGN_V1`。
- 现图：[cards/KarateWall.png](../NinjaSlayer/images/cards/KarateWall.png)，1000×760。
- 当前画面：双脚快速踏地闪身。
- 不适配处：新名玛卡古需要翻身动作，原TonyRetention图接近单手翻。
- 重画／调配需求：可调入 TonyRetention 单手倒立图，暂作为动作候选；正式重画前核实原作腿路，不能把近似卡波耶拉当作原作证据。
- 候选源图：[cards/TonyRetention.png](../NinjaSlayer/images/cards/TonyRetention.png)。
- 文案采用状态：仅采用名称，卡牌文案保留现状。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### C27 一心同体 · 建议重画

- 英文名：One Mind, One Body。
- 当前类名：`OneBodyOneSoul`；[Cards/Ancients/OneBodyOneSoul.cs](../Cards/Ancients/OneBodyOneSoul.cs)。
- 当前内部ID：`NINJA_SLAYER_CARD_ONE_BODY_ONE_SOUL`。
- 现图：[cards/OneBodyOneSoul.png](../NinjaSlayer/images/cards/OneBodyOneSoul.png)，606×852。
- 当前画面：同一人物交叉双臂。
- 不适配处：仍能表示同一肉体，但旧版沿呼吸汇合的重画说明已不对应空手道触发奈落生命。
- 重画／调配需求：同一忍者的拳臂完成近身打击，赤黑火线沿同一手臂收拢到胸腹，形成护住躯干的轮廓；保持单人身体完整。不要第二个肉体、治疗十字或免伤护罩。
- 文案采用状态：采用新版卡牌文案。
- 原作／适配边界：牵强适配／待审。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上19】MENACE OF DARK NINJA 黑暗忍者的威胁｜p1-c019-s01.md:61]。此索引用于查回场景，不代表游戏数值在原作中存在。

### Power · 26项

#### P01 飞刃轮转 · 建议重画

- 英文名：Blade Cycle。
- 当前类名：`BladeCyclePower`；[Powers/BladeCyclePower.cs](../Powers/BladeCyclePower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_BLADE_CYCLE_POWER`。
- 现图：[powers/HellTornadoPower.png](../NinjaSlayer/images/powers/HellTornadoPower.png)，256×256。
- 当前画面：旋转手里剑共用图。
- 不适配处：轮转、群攻和延迟齐射难以区分。
- 重画／调配需求：循环箭头绕单枚手里剑，环线粗而闭合。
- 文案采用状态：采用新版名称与文案。

#### P02 飞刃席卷 · 建议重画

- 英文名：Blade Sweep。
- 当前类名：`BladeSweepPower`；[Powers/BladeSweepPower.cs](../Powers/BladeSweepPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_BLADE_SWEEP_POWER`。
- 现图：[powers/HellTornadoPower.png](../NinjaSlayer/images/powers/HellTornadoPower.png)，256×256。
- 当前画面：旋转手里剑共用图。
- 不适配处：轮转、群攻和延迟齐射难以区分。
- 重画／调配需求：三枚镖排成扇面，轨迹向左右展开。
- 文案采用状态：采用新版名称与文案。

#### P03 烈焰 · 建议重画

- 英文名：Inferno。
- 当前类名：`BurnBurnBurnPower`；[Powers/BurnBurnBurnPower.cs](../Powers/BurnBurnBurnPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_BURN_BURN_BURN_POWER`。
- 现图：[powers/NarakuPower.png](../NinjaSlayer/images/powers/NarakuPower.png)，256×256。
- 当前画面：共用黑炎鬼面。
- 不适配处：纯增幅与一心同体不应同为失控鬼面。
- 重画／调配需求：纯赤黑火团，火焰向外增厚，表示黑炎增幅；不要重复一心同体或奈落形态的鬼脸。
- 文案采用状态：采用新版名称与文案。

#### P04 保留茶道 · 建议重画

- 英文名：Retain Chado。
- 当前类名：`ChadoRetainPower`；[Powers/ChadoRetainPower.cs](../Powers/ChadoRetainPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_CHADO_RETAIN_POWER`。
- 现图：[powers/ChadoRetainPower.png](../NinjaSlayer/images/powers/ChadoRetainPower.png)，256×256。
- 当前画面：倾斜茶杯水流。
- 不适配处：多个呼吸或保留效果图形相同。
- 重画／调配需求：完整茶碗加环扣，表示留下。
- 文案采用状态：采用新版名称与文案。

#### P05 Chop打击 · 建议重画

- 英文名：Chop Strike。
- 当前类名：`ChopStrikeNextTurnPower`；[Powers/ChopStrikeNextTurnPower.cs](../Powers/ChopStrikeNextTurnPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_CHOP_STRIKE_NEXT_TURN_POWER`。
- 现图：[powers/KaratePower.png](../NinjaSlayer/images/powers/KaratePower.png)，256×256。
- 当前画面：红色握拳与黄色冲击闪光。
- 不适配处：当前图标与通用空手道相同，且手刀来源被画成拳头。
- 重画／调配需求：一只横向手刀与向右的短箭头，掌指合为粗大轮廓，表示下回合收益；不要锤、双拳或细小钟面。
- 文案采用状态：采用新版名称与文案。

#### P06 动静相生 · 建议重画

- 英文名：Motion and Stillness。
- 当前类名：`CombatAdjustmentPower`；[Powers/CombatAdjustmentPower.cs](../Powers/CombatAdjustmentPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_COMBAT_ADJUSTMENT_POWER`。
- 现图：[powers/TeaDrinkingSwordPower.png](../NinjaSlayer/images/powers/TeaDrinkingSwordPower.png)，256×256。
- 当前画面：红色刀刃与气流。
- 不适配处：来源效果均非持刀招式，且多个不同行为共用。
- 重画／调配需求：一掌一拳组成交替弧线。
- 文案采用状态：采用新版名称与文案。

#### P07 忍耐 · 建议重画

- 英文名：Endurance。
- 当前类名：`EndurancePower`；[Powers/EndurancePower.cs](../Powers/EndurancePower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_ENDURANCE_POWER`。
- 现图：[powers/KaratePower.png](../NinjaSlayer/images/powers/KaratePower.png)，256×256。
- 当前画面：共用红色拳头闪光。
- 不适配处：临时力量、未来收益和持续修行的时点不明。
- 重画／调配需求：抱拳收在护腕后，去掉爆炸光。
- 文案采用状态：采用新版名称与文案。

#### P08 地狱龙卷 · 建议重画

- 英文名：Hell Tornado。
- 当前类名：`HellTornadoRedesignPower`；[Powers/HellTornadoRedesignPower.cs](../Powers/HellTornadoRedesignPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_HELL_TORNADO_REDESIGN_POWER`。
- 现图：[powers/HellTornadoPower.png](../NinjaSlayer/images/powers/HellTornadoPower.png)，256×256。
- 当前画面：旋转手里剑共用图。
- 不适配处：轮转、群攻和延迟齐射难以区分。
- 重画／调配需求：竖向旋转弧环绕手里剑，底部小向上轮廓。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上07】SURPRISED DOJO 道场突袭｜p1-c008-s01.md:55]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### P09 钩绳降力量（沿用原版名称） · 建议重画

- 英文名：Vanilla inherited text。
- 当前类名：`HookRopeStrengthDownPower`；[Powers/HookRopeStrengthDownPower.cs](../Powers/HookRopeStrengthDownPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_HOOK_ROPE_STRENGTH_DOWN_POWER`。
- 现图：[powers/RiffleStrengthDownPower.png](../NinjaSlayer/images/powers/RiffleStrengthDownPower.png)，256×256。
- 当前画面：弯曲红剑。
- 不适配处：借原版降力量可读，但不显绳索束缚。
- 重画／调配需求：粗钩索缠住刀柄或护腕，仍保留红色减益调性。
- 文案采用状态：采用新版名称与文案。

#### P10 空手入白刃 · 建议重画

- 英文名：Barehanded Catch。
- 当前类名：`IBlockPower`；[Powers/IBlockPower.cs](../Powers/IBlockPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_I_BLOCK_POWER`。
- 现图：[powers/IBlockPower.png](../NinjaSlayer/images/powers/IBlockPower.png)，256×256。
- 当前画面：蓝盾与闪光。
- 不适配处：能表示防守，但不对应新的夹刃标题。
- 重画／调配需求：两掌夹住一截刀刃，刀与掌三块轮廓占满图标，避免完整人物。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第二部｜Bigger cageslonger chains｜p2-c029-s02.md:287]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### P11 拔刀反击 · 建议重画

- 英文名：Blade Counter。
- 当前类名：`IaiPower`；[Powers/IaiPower.cs](../Powers/IaiPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_IAI_POWER`。
- 现图：[powers/IaiPower.png](../NinjaSlayer/images/powers/IaiPower.png)，256×256。
- 当前画面：紫色全身铠甲剑士。
- 不适配处：人物不像黑暗忍者，缩小后剑势消失。
- 重画／调配需求：黑色刀鞘口与短促拔刀斩弧，宽刀刃形成一个大剪影；不用全身武士或文字。
- 文案采用状态：采用新版名称与文案。
- 原作／适配边界：黑暗忍者机制名为拟名；不修改招式代码。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### P12 以静制动 · 建议重画

- 英文名：Poise。
- 当前类名：`KarateTeaPower`；[Powers/KarateTeaPower.cs](../Powers/KarateTeaPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_KARATE_TEA_POWER`。
- 现图：[powers/TeaDrinkingSwordPower.png](../NinjaSlayer/images/powers/TeaDrinkingSwordPower.png)，256×256。
- 当前画面：红色刀刃与气流。
- 不适配处：来源效果均非持刀招式，且多个不同行为共用。
- 重画／调配需求：茶碗后方露出握拳剪影。
- 文案采用状态：采用新版名称与文案。

#### P13 修行 · 建议重画

- 英文名：Training。
- 当前类名：`KarateTrainingPower`；[Powers/KarateTrainingPower.cs](../Powers/KarateTrainingPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_KARATE_TRAINING_POWER`。
- 现图：[powers/KaratePower.png](../NinjaSlayer/images/powers/KaratePower.png)，256×256。
- 当前画面：共用红色拳头闪光。
- 不适配处：临时力量、未来收益和持续修行的时点不明。
- 重画／调配需求：拳与练习木桩，环形短轨迹。
- 文案采用状态：采用新版名称与文案。

#### P14 攻势不息 · 建议重画

- 英文名：Relentless。
- 当前类名：`LingeringMeleePower`；[Powers/LingeringMeleePower.cs](../Powers/LingeringMeleePower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_LINGERING_MELEE_POWER`。
- 现图：[powers/TeaDrinkingSwordPower.png](../NinjaSlayer/images/powers/TeaDrinkingSwordPower.png)，256×256。
- 当前画面：红色刀刃与气流。
- 不适配处：来源效果均非持刀招式，且多个不同行为共用。
- 重画／调配需求：两道相继向前的拳迹。
- 文案采用状态：采用新版名称与文案。

#### P15 一心同体 · 建议重画

- 英文名：One Mind, One Body。
- 当前类名：`OneBodyOneSoulPower`；[Powers/OneBodyOneSoulPower.cs](../Powers/OneBodyOneSoulPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_ONE_BODY_ONE_SOUL_POWER`。
- 现图：[powers/NarakuPower.png](../NinjaSlayer/images/powers/NarakuPower.png)，256×256。
- 当前画面：黑炎包围的鬼面。
- 不适配处：新版强调空手道结算补入奈落生命，鬼面与奈落形态难以区分。
- 重画／调配需求：一只拳与贴着护腕收拢的赤黑火焰，二者组成单一紧凑轮廓；不用第二张鬼脸、呼吸茶碗或医疗符号。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上19】MENACE OF DARK NINJA 黑暗忍者的威胁｜p1-c019-s01.md:61]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### P16 下回合茶道 · 建议重画

- 英文名：Chado Next Turn。
- 当前类名：`PourTeaNextTurnPower`；[Powers/PourTeaNextTurnPower.cs](../Powers/PourTeaNextTurnPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_POUR_TEA_NEXT_TURN_POWER`。
- 现图：[powers/PourTeaPower.png](../NinjaSlayer/images/powers/PourTeaPower.png)，256×256。
- 当前画面：倾斜茶杯水流。
- 不适配处：多个呼吸或保留效果图形相同。
- 重画／调配需求：茶碗与向右的短箭头。
- 文案采用状态：采用新版名称与文案。

#### P17 伺机备刃 · 建议重画

- 英文名：Watchful Blades。
- 当前类名：`RecycledBladesPower`；[Powers/RecycledBladesPower.cs](../Powers/RecycledBladesPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_RECYCLED_BLADES_POWER`。
- 现图：[powers/ExhaustForShurikenPower.png](../NinjaSlayer/images/powers/ExhaustForShurikenPower.png)，256×256。
- 当前画面：弹簧式机械装置。
- 不适配处：预见后准备忍具不依赖机械炮台，与海军战锤和备战共用现图也难以区分。
- 重画／调配需求：一只注视前方的眼与下方握住的一枚镖，眼和镖均用完整大轮廓；不要机械发射器、实体纸牌或表示额外打出的拳影。
- 文案采用状态：采用新版名称与文案。

#### P18 噬火 · 建议重画

- 英文名：Devour Flame。
- 当前类名：`ReturnReturnReturnPower`；[Powers/ReturnReturnReturnPower.cs](../Powers/ReturnReturnReturnPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_RETURN_RETURN_RETURN_POWER`。
- 现图：[powers/NarakuLifePower.png](../NinjaSlayer/images/powers/NarakuLifePower.png)，256×256。
- 当前画面：紫焰心形。
- 不适配处：噬火给力量，不是治疗。
- 重画／调配需求：黑炎被一只拳攥住，拳形大于火焰；不画心形或医疗十字。
- 文案采用状态：采用新版名称与文案。

#### P19 备战 · 建议重画

- 英文名：Battle Ready。
- 当前类名：`ShurikenDrawPower`；[Powers/ShurikenDrawPower.cs](../Powers/ShurikenDrawPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_SHURIKEN_DRAW_POWER`。
- 现图：[powers/ExhaustForShurikenPower.png](../NinjaSlayer/images/powers/ExhaustForShurikenPower.png)，256×256。
- 当前画面：弹簧式机械装置。
- 不适配处：现图沿用ExhaustForShurikenPower，容易被理解为机械发射器，且与预见后备刃混淆。
- 重画／调配需求：主体为伸向忍具袋的手与两枚露出的镖，大轮廓只保留手、袋口和镖尖。不要纸牌、文字、弹簧发射器。
- 文案采用状态：采用新版名称与文案。

#### P20 啜饮 · 建议重画

- 英文名：Sip。
- 当前类名：`SipTeaPower`；[Powers/SipTeaPower.cs](../Powers/SipTeaPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_SIP_TEA_POWER`。
- 现图：[powers/PourTeaPower.png](../NinjaSlayer/images/powers/PourTeaPower.png)，256×256。
- 当前画面：倾斜茶杯水流。
- 不适配处：多个呼吸或保留效果图形相同。
- 重画／调配需求：茶碗上三段短蒸汽。
- 文案采用状态：采用新版名称与文案。

#### P21 韧性 · 建议重画

- 英文名：Resilience。
- 当前类名：`StatusDrawPower`；[Powers/StatusDrawPower.cs](../Powers/StatusDrawPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_STATUS_DRAW_POWER`。
- 现图：[powers/DamageFocusPower.png](../NinjaSlayer/images/powers/DamageFocusPower.png)，256×256。
- 当前画面：共用红眼。
- 不适配处：抽状态补牌与命中加力均缺特征。
- 重画／调配需求：破损牌形边角后露出完整牌形轮廓，表示抽到状态牌后的补牌；作为Power图标使用抽象牌形，不画完整人物或共用红眼。
- 文案采用状态：采用新版名称与文案。

#### P22 蓄势 · 建议重画

- 英文名：Gather Momentum。
- 当前类名：`VitalityTeaPower`；[Powers/VitalityTeaPower.cs](../Powers/VitalityTeaPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_VITALITY_TEA_POWER`。
- 现图：[powers/TeaDrinkingSwordPower.png](../NinjaSlayer/images/powers/TeaDrinkingSwordPower.png)，256×256。
- 当前画面：红色刀刃与气流。
- 不适配处：来源效果均非持刀招式，且多个不同行为共用。
- 重画／调配需求：茶碗蒸汽凝向一只紧握拳头。
- 文案采用状态：采用新版名称与文案。

#### P23 海军战锤 · 建议重画

- 英文名：Navy Hammer。
- 当前类名：`WasshoiDuplicationPower`；[Powers/WasshoiDuplicationPower.cs](../Powers/WasshoiDuplicationPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_WASSHOI_DUPLICATION_POWER`。
- 现图：[powers/EveryThirdAttackPower.png](../NinjaSlayer/images/powers/EveryThirdAttackPower.png)，256×256。
- 当前画面：弹簧式机械装置。
- 不适配处：额外打出攻击牌不是机械发射；旧追打命名已撤回。
- 重画／调配需求：用一只拳的实轮廓与两道短残迹表达攻击接续，轮廓保持紧凑；这只是效果图标，不冒充招式姿势。不要机械发射器或实体锤子。
- 文案采用状态：采用新版名称与文案。
- 原作／适配边界：牵强适配：按用户决定保留原归属；自动出牌与额外打出没有直接原作依据，精确日文与手脚姿势待核实。。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上-支线】DAY OF THE LOBSTER TRILOGY 龙虾之日三部曲｜p1-c046-s01.md:102]；[第三部｜Under the Black Sun｜p3-c041-s02.md:129]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### P24 乘胜 · 建议重画

- 英文名：Onslaught。
- 当前类名：`WasssssshoiPower`；[Powers/WasssssshoiPower.cs](../Powers/WasssssshoiPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_WASSSSSSHOI_POWER`。
- 现图：[powers/DamageFocusPower.png](../NinjaSlayer/images/powers/DamageFocusPower.png)，256×256。
- 当前画面：共用红眼。
- 不适配处：抽状态补牌与命中加力均缺特征。
- 重画／调配需求：一枚手里剑的短轨迹通向握拳轮廓，表示命中后的临时力量；不用与韧性相同的红眼。
- 文案采用状态：采用新版名称与文案。

#### P25 乘胜·力量 · 建议重画

- 英文名：Onslaught Strength。
- 当前类名：`WasssssshoiStrengthPower`；[Powers/WasssssshoiStrengthPower.cs](../Powers/WasssssshoiStrengthPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_WASSSSSSHOI_STRENGTH_POWER`。
- 现图：[powers/KaratePower.png](../NinjaSlayer/images/powers/KaratePower.png)，256×256。
- 当前画面：共用红色拳头闪光。
- 不适配处：临时力量、未来收益和持续修行的时点不明。
- 重画／调配需求：拳上升的短箭头，外圈断开表示临时。
- 文案采用状态：采用新版名称与文案。

#### P26 残心 · 建议重画

- 英文名：Zanshin。
- 当前类名：`ZanshinPower`；[Powers/ZanshinPower.cs](../Powers/ZanshinPower.cs)。
- 当前内部ID：`NINJA_SLAYER_POWER_ZANSHIN_POWER`。
- 现图：[powers/KillingIntentPower.png](../NinjaSlayer/images/powers/KillingIntentPower.png)，256×256。
- 当前画面：与杀气共用红黑爆裂符号。
- 不适配处：残心应为警戒收势而不是杀意爆发。
- 重画／调配需求：侧视收拳与一只冷静眼睛，外围留白，去掉放射爆裂。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上02】BACK IN BLACK 归于黑暗｜p1-c003-s01.md:57]。此索引用于查回场景，不代表游戏数值在原作中存在。

### 遗物 · 7项

#### R01 别嫔残刃 · 建议重画

- 英文名：Beppin Shard。
- 当前类名：`BeppinFragmentRelic`；[Relics/BeppinFragmentRelic.cs](../Relics/BeppinFragmentRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_BEPPIN_FRAGMENT_RELIC`。
- 现图：[relics/BeppinFragmentRelic.png](../NinjaSlayer/images/relics/BeppinFragmentRelic.png)，85×85。
- 当前画面：细长金属刀片。
- 不适配处：残片太细，易误认为完整小刀。
- 重画／调配需求：不规则断口残刃置于深灰容器边，残刃占主体，不画完整别嫔。
- 三规格：normal 85×85（[relics/BeppinFragmentRelic.png](../NinjaSlayer/images/relics/BeppinFragmentRelic.png)）；_large 256×256（[relics/BeppinFragmentRelic_large.png](../NinjaSlayer/images/relics/BeppinFragmentRelic_large.png)）；_outline 85×85（[relics/BeppinFragmentRelic_outline.png](../NinjaSlayer/images/relics/BeppinFragmentRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜DARK NINJA RETURNS 黑暗忍者归来｜p1-c042-s01.md:22]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### R02 生化竹片 · 建议重画

- 英文名：Bio-Bamboo Splint。
- 当前类名：`BioBambooRelic`；[Relics/BioBambooRelic.cs](../Relics/BioBambooRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_BIO_BAMBOO_RELIC`。
- 现图：[relics/BioBambooRelic.png](../NinjaSlayer/images/relics/BioBambooRelic.png)，85×85。
- 当前画面：细长绿色竹管。
- 不适配处：图太细，改名后应体现竹片。
- 重画／调配需求：两片厚竹以粗绳交叠捆扎，竹节与断面清楚；不要发光盾法阵。
- 三规格：normal 85×85（[relics/BioBambooRelic.png](../NinjaSlayer/images/relics/BioBambooRelic.png)）；_large 256×256（[relics/BioBambooRelic_large.png](../NinjaSlayer/images/relics/BioBambooRelic_large.png)）；_outline 85×85（[relics/BioBambooRelic_outline.png](../NinjaSlayer/images/relics/BioBambooRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。
- 原作／适配边界：拟制物件，覆甲关系为设计推论。名称已按用户意见采用，这条保留意见仅约束后续画面，不撤销名称。

#### R03 吸附型脉冲地雷 · 建议重画

- 英文名：Adhesive EMP Mine。
- 当前类名：`ElectricBoobyTrapRelic`；[Relics/ElectricBoobyTrapRelic.cs](../Relics/ElectricBoobyTrapRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_ELECTRIC_BOOBY_TRAP_RELIC`。
- 现图：[relics/ElectricBoobyTrapRelic.png](../NinjaSlayer/images/relics/ElectricBoobyTrapRelic.png)，128×128。
- 当前画面：黄色环形电线炸弹。
- 不适配处：缺吸附盘结构，看似手雷。
- 重画／调配需求：扁圆吸附盘、粗金属接点和一圈脉冲灯；不画拉环、计时数字或散乱细线。
- 三规格：normal 128×128（[relics/ElectricBoobyTrapRelic.png](../NinjaSlayer/images/relics/ElectricBoobyTrapRelic.png)）；_large 256×256（[relics/ElectricBoobyTrapRelic_large.png](../NinjaSlayer/images/relics/ElectricBoobyTrapRelic_large.png)）；_outline 128×128（[relics/ElectricBoobyTrapRelic_outline.png](../NinjaSlayer/images/relics/ElectricBoobyTrapRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第三部｜[第3部049]Farewell My Shadow 后篇 by alex.ma｜p3-c051-s01.md:30]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### R04 坐禅Drink枪带 · 建议重画

- 英文名：Zazen Drink Bandolier。
- 当前类名：`NancyZazenDrinkRelic`；[Relics/NancyZazenDrinkRelic.cs](../Relics/NancyZazenDrinkRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_NANCY_ZAZEN_DRINK_RELIC`。
- 现图：[relics/NancyZazenDrinkRelic.png](../NinjaSlayer/images/relics/NancyZazenDrinkRelic.png)，128×128。
- 当前画面：茶碗与勺子。
- 不适配处：完全不对应五瓶饮料枪带。
- 重画／调配需求：五瓶小饮料固定在弧形宽腰带，瓶盖为明亮辨识点；无枪、茶壶或杯子。
- 三规格：normal 128×128（[relics/NancyZazenDrinkRelic.png](../NinjaSlayer/images/relics/NancyZazenDrinkRelic.png)）；_large 256×256（[relics/NancyZazenDrinkRelic_large.png](../NinjaSlayer/images/relics/NancyZazenDrinkRelic_large.png)）；_outline 128×128（[relics/NancyZazenDrinkRelic_outline.png](../NinjaSlayer/images/relics/NancyZazenDrinkRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上17】ONE MINUTE BEFORE THE TANUKI 狸猫前一分钟｜p1-c017-s02.md:95]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### R05 奈落解放 · 建议重画

- 英文名：Naraku Unleashed。
- 当前类名：`NarakuWithinRelic`；[Relics/NarakuWithinRelic.cs](../Relics/NarakuWithinRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_NARAKU_WITHIN_RELIC`。
- 现图：[relics/NarakuWithinRelic.png](../NinjaSlayer/images/relics/NarakuWithinRelic.png)，128×128。
- 当前画面：破裂黑紫胸甲。
- 不适配处：奈落解放不是换穿实体铠甲。
- 重画／调配需求：赤黑火焰挣开一圈束缚，中心留凶恶轮廓；不画可穿胸甲。
- 三规格：normal 128×128（[relics/NarakuWithinRelic.png](../NinjaSlayer/images/relics/NarakuWithinRelic.png)）；_large 256×256（[relics/NarakuWithinRelic_large.png](../NinjaSlayer/images/relics/NarakuWithinRelic_large.png)）；_outline 128×128（[relics/NarakuWithinRelic_outline.png](../NinjaSlayer/images/relics/NarakuWithinRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。

#### R06 道具社忍具袋 · 建议重画

- 英文名：Dogusha Tool Pouch。
- 当前类名：`PortableIrcTerminalRelic`；[Relics/PortableIrcTerminalRelic.cs](../Relics/PortableIrcTerminalRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_PORTABLE_IRC_TERMINAL_RELIC`。
- 现图：[relics/PortableIrcTerminalRelic.png](../NinjaSlayer/images/relics/PortableIrcTerminalRelic.png)，128×128。
- 当前画面：红色便携电脑。
- 不适配处：代码遗留电脑图与忍具袋冲突。
- 重画／调配需求：束口深红布袋露两枚镖，粗绳扣带和缝线；不要终端屏幕。
- 三规格：normal 128×128（[relics/PortableIrcTerminalRelic.png](../NinjaSlayer/images/relics/PortableIrcTerminalRelic.png)）；_large 256×256（[relics/PortableIrcTerminalRelic_large.png](../NinjaSlayer/images/relics/PortableIrcTerminalRelic_large.png)）；_outline 128×128（[relics/PortableIrcTerminalRelic_outline.png](../NinjaSlayer/images/relics/PortableIrcTerminalRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。
- 沿用审查板的原作索引：[第一部｜【新埼玉炎上28】OGRE THE COLD STEEL 冷钢恶鬼｜p1-c028-s01.md:27]。此索引用于查回场景，不代表游戏数值在原作中存在。

#### R07 鸟居之约 · 建议重画

- 英文名：Torii Pact。
- 当前类名：`YukanoCompanionRelic`；[Relics/YukanoCompanionRelic.cs](../Relics/YukanoCompanionRelic.cs)。
- 当前内部ID：`NINJA_SLAYER_RELIC_YUKANO_COMPANION_RELIC`。
- 现图：[relics/YukanoCompanionRelic.png](../NinjaSlayer/images/relics/YukanoCompanionRelic.png)，128×128。
- 当前画面：红色四角手里剑。
- 不适配处：鸟居之约缺鸟居或由佳乃的标识。
- 重画／调配需求：小型朱红鸟居与一圈结绳，粗横梁和两脚辨识；不画镖、人物肖像。
- 三规格：normal 128×128（[relics/YukanoCompanionRelic.png](../NinjaSlayer/images/relics/YukanoCompanionRelic.png)）；_large 256×256（[relics/YukanoCompanionRelic_large.png](../NinjaSlayer/images/relics/YukanoCompanionRelic_large.png)）；_outline 128×128（[relics/YukanoCompanionRelic_outline.png](../NinjaSlayer/images/relics/YukanoCompanionRelic_outline.png)）。
- 文案采用状态：采用新版名称与文案。

## 共用资源的影响范围

以下分组按图片SHA256合并，包含同内容但文件名不同的资源。列出所有当前使用者，含本次建议保留的条目。后续替换必须逐模型接入，不能一次覆盖整组。

| 现图路径（相同内容合并） | 当前使用者 | 本次处理 |
|---|---|---|
| [powers/NarakuLifePower.png](../NinjaSlayer/images/powers/NarakuLifePower.png) | 浴火（`BlackFlameRecoveryPower`）<br>奈落生命（`NarakuLifePower`）<br>噬火（`ReturnReturnReturnPower`） | 浴火：可保留<br>奈落生命：可保留<br>噬火：建议重画 |
| [powers/HellTornadoPower.png](../NinjaSlayer/images/powers/HellTornadoPower.png) | 飞刃轮转（`BladeCyclePower`）<br>飞刃席卷（`BladeSweepPower`）<br>地狱龙卷（`HellTornadoRedesignPower`） | 飞刃轮转：建议重画<br>飞刃席卷：建议重画<br>地狱龙卷：建议重画 |
| [powers/NarakuPower.png](../NinjaSlayer/images/powers/NarakuPower.png) | 烈焰（`BurnBurnBurnPower`）<br>奈落形态（`NarakuFormRedesignPower`）<br>一心同体（`OneBodyOneSoulPower`） | 烈焰：建议重画<br>奈落形态：可保留<br>一心同体：建议重画 |
| [powers/KaratePower.png](../NinjaSlayer/images/powers/KaratePower.png) | Chop打击（`ChopStrikeNextTurnPower`）<br>忍耐（`EndurancePower`）<br>空手道（`KaratePower`）<br>修行（`KarateTrainingPower`）<br>乘胜·力量（`WasssssshoiStrengthPower`） | Chop打击：建议重画<br>忍耐：建议重画<br>空手道：可保留<br>修行：建议重画<br>乘胜·力量：建议重画 |
| [powers/TeaDrinkingSwordPower.png](../NinjaSlayer/images/powers/TeaDrinkingSwordPower.png) | 动静相生（`CombatAdjustmentPower`）<br>以静制动（`KarateTeaPower`）<br>攻势不息（`LingeringMeleePower`）<br>蓄势（`VitalityTeaPower`） | 动静相生：建议重画<br>以静制动：建议重画<br>攻势不息：建议重画<br>蓄势：建议重画 |
| [powers/KillingIntentPower.png](../NinjaSlayer/images/powers/KillingIntentPower.png) | 杀气（`KillingIntentRedesignPower`）<br>残心（`ZanshinPower`） | 杀气：可保留<br>残心：建议重画 |
| [powers/PourTeaPower.png](../NinjaSlayer/images/powers/PourTeaPower.png) | 下回合茶道（`PourTeaNextTurnPower`）<br>啜饮（`SipTeaPower`） | 下回合茶道：建议重画<br>啜饮：建议重画 |
| [powers/ExhaustForShurikenPower.png](../NinjaSlayer/images/powers/ExhaustForShurikenPower.png)<br>[powers/EveryThirdAttackPower.png](../NinjaSlayer/images/powers/EveryThirdAttackPower.png) | 伺机备刃（`RecycledBladesPower`）<br>备战（`ShurikenDrawPower`）<br>海军战锤（`WasshoiDuplicationPower`） | 伺机备刃：建议重画<br>备战：建议重画<br>海军战锤：建议重画 |
| [powers/DamageFocusPower.png](../NinjaSlayer/images/powers/DamageFocusPower.png) | 韧性（`StatusDrawPower`）<br>乘胜（`WasssssshoiPower`） | 韧性：建议重画<br>乘胜：建议重画 |

## 名称分配的关键变动

本表帮助美术人员按新名称找回旧文件。显示名称变化不会改变上文的类名、内部ID或文件路径。英文独立修订以本地化为准。

| 旧中文名 | 采用中文名 | 当前类名 |
|---|---|---|
| 死线 | 占线 | `BusyLine` |
| 大刀 | 砍刀 | `SawatariMachete` |
| 低空上勾拳 | 投石器摔 | `WhiskTeaFlashRedesignV1` |
| 忍者感应 | 察敌 | `LuckyStrikeRedesignV1` |
| 未命名 | 备战 | `ShurikenDraw` |
| 燃烧殆尽 | 引火 | `RedBlackFlameAttackRedesignV1` |
| 深渊之力 | 奈落之力 | `AbyssStrengthRedesignV1` |
| 洞察 | 应变 | `TechniqueSearchRedesignV1` |
| 玛卡古 | 审势 | `TonyRetention` |
| 燃血 | 强攻 | `Slaughter` |
| 发掘 | 重拾 | `Excavate` |
| 弃刃成锋 | 伺机备刃 | `RecycledBladesRedesignV1` |
| 制定计划 | 谋定 | `ComposeHaikuRedesignV1` |
| 真名看破 | 澄明 | `FurinKazanChadoRedesignV1` |
| 疾闪 | 玛卡古 | `HardItOutRedesignV1` |

## 后续交付检查

- [ ] 58项重画与2项调配逐项标记完成；暂缓动作定稿的条目先补证据。
- [ ] 卡图在实际卡面裁切后可读，Power在32像素可读，遗物三规格外形一致。
- [ ] 共用图片已拆分接入，保留项目没有被连带覆盖。
- [ ] 巴投、空手入白刃、审势、玛卡古的旧图和新图没有交叉覆盖。
- [ ] 所有素材按当前实际ID关联；命名调整没有引发代码或存档字段迁移。
- [ ] 9张采用文案的卡牌与其余84张的文案边界保持不变，原作拟名与未核实动作没有被写成事实。
