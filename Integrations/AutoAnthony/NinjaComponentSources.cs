using ChaosCardGenerator;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using SourceTag = ChaosCardGenerator.CardTag;

namespace NinjaSlayer.AutoAnthony;

internal static class NinjaComponentSources
{
    internal const string ProfileId = "ninjaslayer";
    internal const string Opcode = "ninjaslayer";
    internal static readonly Dictionary<string, Type> PowerTypes = new(StringComparer.Ordinal);

    internal sealed record Source(Type Card, string Chinese, string English, Func<CardModel, ComponentAtom[]> Components);

    // Each row records actual source occurrences, including repeated common components.
    internal static readonly Source[] Sources =
    [
        new(typeof(StrikeNinjaSlayerRedesignV1), "打击", "Strike", c => [Damage(c)]),
        new(typeof(DefendNinjaSlayerRedesignV1), "防御", "Defend", c => [Block(c)]),
        new(typeof(KarateStraightRedesignV1), "直拳", "Straight Punch", c => [Damage(c), Karate(c)]),
        new(typeof(Prejudge), "预判", "Anticipate", c => [ScryBlock(c)]),
        new(typeof(ReadyStanceRedesignV1), "柔术", "Jujutsu", c => [Karate(c), Discard(1)]),
        new(typeof(BladeReserveRedesignV1), "备刃", "Blade Prep", c => [Stock(c), Draw(c)]),
        new(typeof(PreparedShurikenRedesignV1), "备镖", "Ready Shuriken", c => [Block(c), Stock(c)]),
        new(typeof(ChadoStillnessRedesignV1), "守静", "Stillness", c => [Breath(c), Custom("chado_retain", "在接下来的[[amount]]回合内，保留你的茶道。", "Retain your Chado for the next [[amount]] turns.", V(c,"Turns"))]),
        new(typeof(Slaughter), "强攻", "Press the Attack", c => [Damage(c), Custom("draw_discard_nonstatus", "抽[[amount]]张牌，丢弃其中的所有非状态牌。", "Draw [[amount]] cards. Discard all non-Status cards drawn this way.", V(c,"Cards"))]),
        new(typeof(TechniqueSearchRedesignV1), "应变", "Adapt", c => [Custom("draw_sly", "抽[[amount]]张牌。这些牌获得奇巧。", "Draw [[amount]] cards. They gain Sly.", V(c,"Cards"))]),
        new(typeof(SpiralRoundhouseJumpRedesignV1), "螺旋回转跳", "Spiral Jump", c => [AreaDamage(c), Stock(c)]),
        new(typeof(SatsubatsuRedesignV1), "BS1260踢", "BS1260 Kick", c => [Damage(c), BlackFlame(1, false)]),
        new(typeof(GuidingFlameRedesignV1), "炎甲", "Flame Guard", c => [Block(c), BlackFlame(1, true)]),
        new(typeof(BackBridgeRedesignV1), "后拱桥", "Back Bridge", c => [Block(c), Karate(c)]),
        new(typeof(AbyssStrengthRedesignV1), "奈落之力", "Naraku's Might", c => [Karate(c), BlackFlame(1, false)]),
        new(typeof(ThrowKunaiRedesignV1), "投掷苦无", "Kunai Throw", c => [Damage(c), Scry(c,"Cards"), Discard(1)]),
        new(typeof(ShurikenCreation), "手里剑生成", "Shuriken Creation", c => [Scry(c,"Scry"), Stock(c)]),
        new(typeof(ShurikenGenerationRedesignV1), "飞刃屏障", "Blade Barrier", c => [Block(c), Custom("block_after_stock", "若本回合获得过手里剑，获得[[amount]]点格挡。", "If you gained Shuriken this turn, gain [[amount]] Block.", V(c,"Block"))]),
        new(typeof(TonyRetention), "审势", "Assess", c => [Block(c), Scry(c,"Scry")]),
        new(typeof(RedBlackFlameAttackRedesignV1), "引火", "Kindle", c => [Draw(c), BlackFlame(V(c,"BlackFlames"), true)]),
        new(typeof(AbandonThoughtRedesignV1), "舍念", "Let Go", c => [Custom("exhaust_top", "消耗抽牌堆顶部的牌。", "Exhaust the top card of your draw pile.", 1, fixedRule: true), Breath(c)]),
        new(typeof(CommonChopRedesignV1), "Chop", "Chop", c => [Damage(c), Karate(c), Custom("topdeck_self", "将此牌放到抽牌堆顶部。", "Put this card on top of your draw pile.", 1, fixedRule: true)]),
        new(typeof(LeftHeavyPunchRedesignV1), "Chop打击", "Chop Strike", c => [Damage(c), Power<ChopStrikeNextTurnPower>(V(c,"Karate"), "下个回合获得[[amount]]层空手道。", "Gain [[amount]] Karate next turn.")]),
        new(typeof(PourTeaRedesignV1), "调息", "Regulate Breath", c => [Block(c), Power<PourTeaNextTurnPower>(V(c,"Breath"), "下个回合，茶道呼吸[[amount]]。", "Next turn, Chado Breathing [[amount]].")]),
        new(typeof(CombatAdjustmentRedesignV1), "动静相生", "Motion and Stillness", c => [Damage(c), Power<CombatAdjustmentPower>(V(c,"Breath"), "本回合每打出一张技能牌，茶道呼吸[[amount]]。", "Whenever you play a Skill this turn, Chado Breathing [[amount]].")]),
        new(typeof(ChopDefenseRedesignV1), "交叉防御", "Cross Guard", c => [Block(c), Power<ReboundPower>(1, "你打出的下一张牌回到抽牌堆顶部。", "The next card you play goes on top of your draw pile.", fixedRule: true)]),
        new(typeof(HiddenEdgeRedesignV1), "藏锋", "Hidden Edge", c => [Builtin("D:GainFocus", "获得[[amount]]点集中。", "Gain [[amount]] Focus.", "amount", V(c,"FocusPower")), Builtin("D:GainDexterity", "获得[[amount]]点敏捷。", "Gain [[amount]] Dexterity.", "amount", V(c,"DexterityPower"))]),
        new(typeof(FurinKazanChadoRedesignV1), "澄明", "Clarity", c => [Power<ArtifactPower>(V(c,"ArtifactPower"), "获得[[amount]]层人工制品。", "Gain [[amount]] Artifact.")]),
        new(typeof(PalmThrustRedesignV1), "掌打", "Palm Thrust", c => [RandomDamage(c)]),
        new(typeof(HardItOutRedesignV1), "玛卡古", "Macaco", c => [Power<EvasionPower>(V(c,"EvasionPower"), "获得[[amount]]层回避。", "Gain [[amount]] Evasion."), BlackFlame(2, false)]),
        new(typeof(BladeSweepRedesignV1), "飞刃席卷", "Blade Sweep", c => [Stock(c), Power<BladeSweepPower>(1, "本回合手里剑攻击所有敌人。", "Shuriken hit all enemies this turn.", fixedRule: true)]),
        new(typeof(CounteroffensiveGuardRedesignV1), "空手入白刃", "Barehanded Catch", c => [Block(c), Power<IBlockPower>(1, "在下个回合开始时，获得等同于剩余格挡的活力。", "At the start of your next turn, gain Vigor equal to your remaining Block.", fixedRule: true)]),
        new(typeof(KillingIntentRedesignV1), "杀气", "Killing Intent", c => [Block(c), Power<KillingIntentRedesignPower>(1, "在回合结束时，若你格挡了所有攻击伤害，下回合获得1张直气。", "If you block all attack damage, add a Straight Ki to your hand next turn.", fixedRule: true)]),
        new(typeof(OnlyKarateRedesignV1), "倍劲", "Redouble", c => [Custom("double_karate", "翻倍你的空手道层数。", "Double your Karate.", 1, fixedRule: true)]),
        new(typeof(TurtleShellRedesignV1), "坚壁", "Bulwark", c => [Custom("karate_to_plating", "失去所有空手道，获得等量覆甲。", "Lose all Karate. Gain that much Plating.", 1, fixedRule: true)]),
        new(typeof(Excavate), "重拾", "Recover", c => [Custom("exhaust_to_top", "选择消耗堆中的[[amount]]张牌，将其放到抽牌堆顶部。", "Choose [[amount]] cards in your Exhaust Pile. Put them on top of your draw pile.", 1)]),
        new(typeof(DecidedOutcomeRedesignV1), "取舍", "Discern", c => [Custom("scry_exhaust_breath", "预见[[amount]]，消耗选中的牌，每消耗1张牌，茶道呼吸1。", "Scry [[amount]], exhausting selected cards. Chado Breathing 1 for each card exhausted this way.", V(c,"Cards"))]),
        new(typeof(ShurikenDraw), "备战", "Battle Ready", c => [Power<ShurikenDrawPower>(V(c,"Cards"), "每当你获得手里剑时，抽[[amount]]张牌。", "Whenever you gain Shuriken, draw [[amount]] cards.", persistent: true)]),
        new(typeof(AdversityCarapaceRedesignV1), "蓄势", "Gather Momentum", c => [Power<VitalityTeaPower>(V(c,"VigorPower"), "每当茶道被消耗时，获得[[amount]]点活力。", "Whenever Chado is Exhausted, gain [[amount]] Vigor.", persistent: true)]),
        new(typeof(KarateTrainingRedesignV1), "修行", "Training", c => [Power<KarateTrainingPower>(V(c,"Karate"), "在你的回合开始时，获得[[amount]]层空手道，丢弃1张牌。", "At the start of your turn, gain [[amount]] Karate and discard 1 card.", persistent: true)]),
        new(typeof(FlyingBladeDanceRedesignV1), "从容", "Composure", c => [Power<ScryBlockPower>(V(c,"Block"), "每当有一张牌被丢弃时，获得[[amount]]点格挡。", "Whenever you discard a card, gain [[amount]] Block.", persistent: true)]),
        new(typeof(ReturnReturnReturnRedesignV1), "噬火", "Devour Flame", c => [Power<ReturnReturnReturnPower>(V(c,"StrengthPower"), "每当黑炎被消耗时，获得[[amount]]点力量。", "Whenever Black Flame is Exhausted, gain [[amount]] Strength.", persistent: true)]),
        new(typeof(BurnBurnBurnRedesignV1), "烈焰", "Inferno", c => [Power<BurnBurnBurnPower>(V(c,"EnemyHpLoss"), "黑炎使敌人额外失去[[amount]]点生命。", "Black Flame causes enemies to lose [[amount]] additional HP.", persistent: true)]),
        new(typeof(GiantShurikenRedesignV1), "无星之夜", "Starless Night", c => [Power<StarlessNightRedesignPower>(1, "每当你获得手里剑时，将1张同等伤害的强·手里剑加入你的手牌。", "Whenever you gain Shuriken, add a Strong Shuriken with the same damage to your Hand.", persistent: true, fixedRule: true)]),
        new(typeof(StatusDraw), "韧性", "Resilience", c => [Power<StatusDrawPower>(V(c,"Cards"), "每当你抽到状态牌，抽[[amount]]张牌。", "Whenever you draw a Status, draw [[amount]] cards.", persistent: true)]),
        new(typeof(Zanshin), "残心", "Zanshin", c => [Power<ZanshinPower>(V(c,"Cards"), "如果你在一回合内打出了至少3张攻击牌，在下个回合开始时额外抽[[amount]]张牌。", "If you play at least 3 Attacks in a turn, draw [[amount]] additional cards at the start of your next turn.", persistent: true)]),
        new(typeof(NarakuFormRedesignV1), "奈落形态", "Naraku Form", c => [Power<NarakuFormRedesignPower>(1, "你打出的所有攻击牌都会触发一次黑炎。", "Whenever you play an Attack, trigger Black Flame.", persistent: true, fixedRule: true)]),
        new(typeof(TeaTeaRedesignV1), "入定", "Meditation", c => [Power<TeaTeaPower>(1, "茶道获得保留。\n在你的回合开始时，茶道呼吸1。", "Your Chado cards gain Retain.\nAt the start of your turn, Chado Breathing 1.", persistent: true, fixedRule: true)]),
        new(typeof(KarateTeaRedesignV1), "以静制动", "Poise", c => [Power<KarateTeaPower>(V(c,"Karate"), "每当生成1张茶道时，获得[[amount]]层空手道。", "Whenever you generate a Chado, gain [[amount]] Karate.", persistent: true)]),
        new(typeof(ComposeHaikuRedesignV1), "谋定", "Forethought", c => [Power<ScryPlanningPower>(1, "你在预见中未弃掉的牌获得奇巧。", "Cards you do not discard while Scrying gain Sly.", persistent: true, fixedRule: true)]),
        new(typeof(LingeringMeleeRedesignV1), "攻势不息", "Relentless", c => [Power<LingeringMeleePower>(V(c,"Cards"), "在你的回合结束时，打出抽牌堆顶部的[[amount]]张牌。", "At the end of your turn, play the top [[amount]] cards of your Draw Pile.", persistent: true)]),
        new(typeof(RecycledBladesRedesignV1), "伺机备刃", "Watchful Blades", c => [Power<RecycledBladesPower>(1, "每当你预见时，获得1层手里剑。", "Whenever you Scry, gain 1 Shuriken.", persistent: true, fixedRule: true)]),
        new(typeof(Wasssssshoi), "乘胜", "Onslaught", c => [Power<WasssssshoiPower>(V(c,"StrengthPower"), "每当你的手里剑对敌人造成伤害时，本回合获得[[amount]]点力量。", "Whenever your Shuriken damages an enemy, gain [[amount]] Strength this turn.", persistent: true)]),
        new(typeof(Endurance), "忍耐", "Endurance", c => [Karate(c), Power<EndurancePower>(V(c,"LaterKarate"), "若本回合未打出攻击牌，在回合结束时获得[[amount]]层空手道。", "At the end of this turn, if you played no Attacks, gain [[amount]] Karate.")]),
        new(typeof(LuckyStrikeRedesignV1), "察敌", "Read the Enemy", c => [Scry(c,"Cards"), Builtin("N:Draw", "抽[[draw]]张牌。", "Draw [[draw]] cards.", "draw", V(c,"Draw"))]),
        new(typeof(DragonFlyingKickRedesignV1), "龙·飞踢", "Dragon Flying Kick", c => [Damage(c), Custom("fill_hand", "抽牌，直到手牌达到上限。", "Draw until your hand is full.", 1, fixedRule: true), Breath(c)]),
        new(typeof(OyeahThrowSword), "月面宙反", "Moonsault", c => [Block(c), Custom("fire_stock", "激发并清空全部手里剑。", "Fire and consume all Shuriken.", 1, fixedRule: true)]),
        new(typeof(ShurikenStorm), "手里剑风暴", "Shuriken Storm", c => [Stock(c), Custom("discard_stock", "丢弃所有手牌，每丢弃1张牌获得1层手里剑。", "Discard your hand. Gain 1 Shuriken for each card discarded.", 1, fixedRule: true)]),
        new(typeof(PlaceholderBlueDefense01), "撒菱", "Caltrops", c => [Block(c), Custom("timed_thorns", "获得持续3次敌方行动的[[amount]]点荆棘。", "Gain [[amount]] Thorns for the next 3 enemy turns.", V(c,"ThornsPower"))]),
        new(typeof(WhiskTeaFlashRedesignV1), "投石器摔", "Catapult Throw", c => [Damage(c), Custom("draw_if_tea", "如果手中有茶道，抽[[amount]]张牌。", "If you have Chado in hand, draw [[amount]] cards.", V(c,"Cards"))]),
        new(typeof(OneDrinkOneStrikeRedesignV1), "倒挂金钩踢", "Somersault Kick", c => [Damage(c), Custom("breath_after_discard", "如果本回合丢弃过牌，茶道呼吸[[amount]]。", "If you discarded a card this turn, Chado Breathing [[amount]].", V(c,"Breath"))]),
        new(typeof(RightHeavyPunchRedesignV1), "左·上勾拳", "Left Uppercut", c => [Damage(c), Custom("vulnerable_after_attack", "如果上一张打出的牌是攻击牌，给予[[amount]]层易伤。", "If the previous card played was an Attack, apply [[amount]] Vulnerable.", V(c,"VulnerablePower"), targeted: true)]),
        new(typeof(RightHeavyPunchAfterSkillRedesignV1), "右·上勾拳", "Right Uppercut", c => [Damage(c), Custom("weak_after_skill", "如果上一张打出的牌是技能牌，给予[[amount]]层虚弱。", "If the previous card played was a Skill, apply [[amount]] Weak.", V(c,"WeakPower"), targeted: true)]),
        new(typeof(IronBodyRedesignV1), "嘲讽", "Taunt", c => [Block(c), Custom("enemy_karate", "随机一名敌人获得[[amount]]层空手道。", "A random enemy gains [[amount]] Karate.", V(c,"Karate"), downside: true)]),
        new(typeof(HookRopeRedesignV1), "钩绳", "Grappling Hook", c => [Custom("hook_strength", "使敌人在本回合失去等同于你空手道的力量。", "The enemy loses Strength equal to your Karate this turn.", 1, fixedRule: true, targeted: true), Builtin("T:W", "给予[[amount]]层虚弱。", "Apply [[amount]] Weak.", "amount", V(c,"WeakPower"), true)]),
        new(typeof(BlackFlameRecovery), "浴火", "Rekindle", c => [Custom("transform_flame", "将1张手牌变为黑炎。", "Transform a card in your hand into Black Flame.", 1, fixedRule: true), Power<BlackFlameRecoveryPower>(V(c,"NarakuLife"), "本回合每打出1张攻击牌，获得[[amount]]点奈落生命。", "Whenever you play an Attack this turn, gain [[amount]] Naraku HP.")]),
        new(typeof(BladeCycleRedesignV1), "飞刃轮转", "Blade Cycle", c => [Custom("blade_cycle", "弃牌不再消耗手里剑。每次洗牌失去[[amount]]层手里剑。", "Discarding no longer consumes Shuriken. Lose [[amount]] Shuriken whenever you shuffle.", V(c,"StockLoss"), fixedRule: true, persistent: true)]),
        new(typeof(HellTornadoRedesignV1), "地狱龙卷", "Hell Tornado", c => [Power<SoarPower>(1, "获得1层飞行。", "Gain 1 Soar.", fixedRule: true), Custom("double_stock", "翻倍手里剑库存。", "Double your Shuriken stock.", 1, fixedRule: true), Power<HellTornadoRedesignPower>(1, "下回合开始时激发并清空手里剑，失去飞行。", "At the start of your next turn, fire all Shuriken and lose Soar.", fixedRule: true)]),
        new(typeof(GreatUkeRedesignV1), "大·受身", "Great Ukemi", c => [Custom("play_statuses", "打出抽牌堆、手牌与弃牌堆中的所有状态牌并消耗它们。", "Play and Exhaust all Status cards in your draw pile, hand and discard pile.", 1, fixedRule: true), Power<BufferPower>(1, "获得1层缓冲。", "Gain 1 Buffer.", fixedRule: true)]),
        new(typeof(ChadoFurinKazanRedesignV1), "风林火山", "Furin Kazan", c => [Custom("copy_hand_top", "选择[[amount]]张手牌，将复制品放到抽牌堆顶部。", "Choose [[amount]] cards in your hand. Put copies on top of your draw pile.", V(c,"Cards"))]),
        new(typeof(SipTea), "啜饮", "Sip", c => [Custom("sip_tea", "茶道呼吸[[amount]]。在接下来的[[turns]]个己方回合开始时，茶道呼吸1。", "Chado Breathing [[amount]]. At the start of your next [[turns]] turns, Chado Breathing 1.", c.IsUpgraded ? 1 : 0, extraValues: [new("turns",3)])]),
        new(typeof(MetabolicAccelerationRedesignV1), "嘶哈", "Hiss and Huff", c => [Custom("tea_heal", "消耗1张茶道，回复[[amount]]点生命。", "Exhaust a Chado to heal [[amount]] HP.", V(c,"Heal"))]),
        new(typeof(GatherKi), "聚气", "Gather Ki", c => [Custom("tea_karate", "消耗1张茶道，获得其能量数值[[amount]]倍的空手道。", "Exhaust a Chado. Gain Karate equal to [[amount]] times its Energy.", 2)]),
        new(typeof(ObserveBattlefield), "巴投", "Tomoe Throw", c => [Custom("tea_block_weak", "消耗1张茶道，获得[[amount]]点格挡并给予[[weak]]层虚弱。", "Exhaust a Chado to gain [[amount]] Block and apply [[weak]] Weak.", V(c,"Block"), extraValues: [new("weak", V(c,"WeakPower"))], targeted: true)]),
        new(typeof(WasshoiRedesignV1), "海军战锤", "Navy Hammer", c => [Custom("play_top_exhaust", "打出抽牌堆顶部的牌。若为攻击牌，打出[[amount]]次。消耗该牌。", "Play the top card of your draw pile, [[amount]] times if it is an Attack. Exhaust it.", V(c,"Repeat"))]),
        new(typeof(AlabamaDropRedesignV1), "阿拉巴马落", "Alabama Drop", c => [Custom("karate_damage", "造成你空手道层数[[amount]]倍的伤害。", "Deal damage equal to [[amount]] times your Karate.", V(c,"ExtraDamage"), targeted: true), Custom("dazed_draw", "将[[amount]]张晕眩加入抽牌堆。", "Add [[amount]] Dazed to your draw pile.", V(c,"Dazed"), downside: true)]),
        new(typeof(AntiAirBangBangFist), "对空砰砰拳", "Anti-Air Bang-Bang Fist", c => [Custom("buff_hits", "随机造成[[amount]]点伤害，攻击次数为1加你的正面增益种类数。", "Deal [[amount]] damage to random enemies 1 time plus once for each type of positive buff on you.", V(c,"Damage"))]),
        new(typeof(StormFistRedesignV1), "岚之拳", "Storm Fist", c => [Custom("tea_exhaust_damage", "造成[[amount]]点伤害[[hits]]次。消耗堆中每张茶道使每段伤害增加[[bonus]]。", "Deal [[amount]] damage [[hits]] times. Each Chado in Exhaust adds [[bonus]] damage per hit.", V(c,"CalculationBase"), extraValues: [new("hits", V(c,"Repeat"), Upgradable: false), new("bonus",V(c,"ExtraDamage"))], targeted: true)]),
        new(typeof(SweepKickRedesignV1), "半月圆规踢", "Half-Moon Compass Kick", c => [Custom("tea_area_damage", "对所有敌人造成[[amount]]点伤害。可消耗任意茶道，每张追加2次攻击。", "Deal [[amount]] damage to ALL enemies. You may Exhaust any number of Chado; attack 2 more times for each.", V(c,"Damage"))]),
        new(typeof(TornadoFistRedesignV1), "龙卷拳", "Tornado Fist", c => [Custom("area_x", "对所有敌人造成[[amount]]点伤害X次。若X至少为4，每段给予1层易伤。", "Deal [[amount]] damage to ALL enemies X times. If X is at least 4, each hit applies 1 Vulnerable.", V(c,"Damage"), energyX: true)]),
        new(typeof(TeaStormRedesignV1), "长息", "Deep Breath", c => [Custom("breath_next_x", "下回合茶道呼吸X乘[[amount]]加[[extra]]。", "Next turn, Chado Breathing X times [[amount]] plus [[extra]].", V(c,"BreathPerX"), extraValues: [new("extra",V(c,"ExtraX"))], energyX: true)]),
        new(typeof(ChopStrikeRedesignV1), "打击·打击", "Strike Strike", c => [Damage(c), Scry(c,"Cards"), Custom("return_first", "在本回合前三次打出时，将此牌返回手牌。", "Return this card to your hand the first 3 times you play it each turn.", 3, fixedRule: true)]),
        new(typeof(ChopRedesignV1), "地狱·Chop", "Hell Chop", c => [Damage(c), Karate(c), Custom("return_attacks", "每打出[[amount]]张其他攻击牌，将此牌放入手牌。", "Whenever you play [[amount]] other Attacks in a turn, put this card in your hand.", V(c,"Cards"), fixedRule: true)]),
        new(typeof(RoundhouseKickRedesignV1), "龙回旋踢", "Dragon Roundhouse Kick", c => [Custom("area_multi", "对所有敌人造成[[amount]]点伤害[[hits]]次。", "Deal [[amount]] damage to ALL enemies [[hits]] times.", V(c,"Damage"), extraValues: [new("hits",V(c,"Repeat"))]), Custom("play_on_top", "回合结束时若此牌在抽牌堆顶部，自动打出。", "At the end of your turn, if this card is on top of your draw pile, play it.", 1, fixedRule: true)]),
    ];

    internal static IroncladCardRecipe Recipe(Source source, CardModel card)
    {
        ComponentAtom[] atoms = source.Components(card);
        var tags = card.Keywords.Select(k => Enum.TryParse<SourceTag>(k.ToString(), out var tag) ? (SourceTag?)tag : null)
            .Where(tag => tag.HasValue).Select(tag => tag!.Value).ToList();
        foreach (var tag in card.Tags)
            if (Enum.TryParse<SourceTag>(tag.ToString(), out var parsed) && !tags.Contains(parsed)) tags.Add(parsed);
        return new IroncladCardRecipe(source.Card.Name, source.Chinese,
            card.EnergyCost.CostsX ? -1 : card.EnergyCost.GetWithModifiers(CostModifiers.Local),
            Enum.Parse<GeneratedCardType>(card.Type.ToString()),
            card.TargetType == TargetType.AnyEnemy ? TargetMode.SingleEnemy : TargetMode.Other,
            Enum.Parse<GeneratedRarity>(card.Rarity.ToString()), tags, atoms,
            Enumerable.Repeat(-1, atoms.Length).ToArray(), EnglishTitle: source.English);
    }

    private static ComponentAtom Damage(CardModel card) => Builtin("T:D", "造成[[damage]]点伤害。", "Deal [[damage]] damage.", "damage", card.DynamicVars.Damage.IntValue, true);
    private static ComponentAtom Block(CardModel card) => Builtin("N_BLOCK", "获得[[block]]点格挡。", "Gain [[block]] Block.", "block", card.DynamicVars.Block.IntValue);
    private static ComponentAtom Draw(CardModel card) => Builtin("N:Draw", "抽[[draw]]张牌。", "Draw [[draw]] cards.", "draw", card.DynamicVars.Cards.IntValue);
    private static ComponentAtom Discard(int count) => Builtin("N:Discard", "丢弃[[count]]张牌。", "Discard [[count]] cards.", "count", count);
    private static ComponentAtom Karate(CardModel card) => Custom("karate", "获得[[amount]]层空手道。", "Gain [[amount]] Karate.", card.DynamicVars["Karate"].IntValue);
    private static ComponentAtom Stock(CardModel card) => Custom("shuriken", "获得[[amount]]层手里剑。", "Gain [[amount]] Shuriken.", card.DynamicVars["Stock"].IntValue);

    private static int V(CardModel card, string variable) => card.DynamicVars[variable].IntValue;
    private static ComponentAtom Breath(CardModel card) => Custom("breath", "茶道呼吸[[amount]]。", "Chado Breathing [[amount]].", V(card, "Breath"));
    private static ComponentAtom Scry(CardModel card, string variable) => Custom("scry", "预见[[amount]]。", "Scry [[amount]].", V(card, variable));
    private static ComponentAtom BlackFlame(int amount, bool draw) => Custom(draw ? "blackflame_draw" : "blackflame_hand",
        draw ? "将[[amount]]张黑炎加入抽牌堆。" : "将[[amount]]张黑炎加入手牌。",
        draw ? "Add [[amount]] Black Flames to your draw pile." : "Add [[amount]] Black Flames to your hand.", amount);

    private static ComponentAtom ScryBlock(CardModel card) => Custom("scry_block", "预见[[amount]]。每张以此法丢弃的牌使你获得[[block]]点格挡。",
        "Scry [[amount]]. Gain [[block]] Block for each card discarded this way.", V(card, "Scry"),
        extraValues: [new("block", V(card, "Block"))]);

    private static ComponentAtom Power<T>(int amount, string chinese, string english, bool persistent = false, bool fixedRule = false) where T : PowerModel
    {
        string variant = typeof(T).Name.ToLowerInvariant();
        PowerTypes[variant] = typeof(T);
        return Custom(variant, chinese, english, amount, fixedRule, persistent);
    }

    private static ComponentAtom AreaDamage(CardModel card) => Builtin("N:AllD", "对所有敌人造成[[damage]]点伤害。", "Deal [[damage]] damage to ALL enemies.", "damage", V(card,"Damage"));
    private static ComponentAtom RandomDamage(CardModel card)
    {
        var text = new OperationLocalizedText("对随机敌人造成[[damage]]点伤害[[hits]]次。", "Deal [[damage]] damage to a random enemy [[hits]] times.");
        var op = new GeneratorOperation("N:RandomD", OperationScope.NonTargeted,
            $"对随机敌人造成{V(card,"Damage")}点伤害{V(card,"Repeat")}次。", new Dictionary<string,int>());
        return Atom(op.Template, text, OperationRuntimeSpecCompiler.GetOrCompile(op), false);
    }

    internal static ComponentAtom Custom(string variant, string chinese, string english, int amount, bool fixedRule = false,
        bool persistent = false, RuntimeValueSlot[]? extraValues = null, bool targeted = false, bool downside = false, bool energyX = false)
    {
        string[] flags = persistent
            ? ["has_numeric_literal", "scalable_reward_wording", "api_beneficial", "api_power_foundation"]
            : ["has_numeric_literal", "scalable_reward_wording", "api_beneficial"];
        if (downside) flags = ["has_numeric_literal", "cost_wording", "api_negative"];
        if (energyX) flags = [..flags, "uses_energy_x"];
        if (variant is "karate_damage" or "buff_hits" or "tea_exhaust_damage" or "tea_area_damage" or "area_multi" or "area_x")
            flags = [..flags, "api_enemy_damage"];
        if (variant is "return_first" or "return_attacks" or "play_on_top" or "topdeck_self")
            flags = [..flags, "api_self_card_movement"];
        var spec = new OperationRuntimeSpec(1, Opcode, variant, targeted ? "target" : "self", "none", "none", "any", flags,
            [new("amount", amount, Upgradable: !fixedRule, Explicit: chinese.Contains("[[amount]]", StringComparison.Ordinal)), ..extraValues ?? []]);
        return Atom("NS:" + variant, new(chinese, english), spec, targeted) with
        {
            Category = downside ? ComponentCategory.Negative : persistent ? ComponentCategory.Growth : variant switch
            {
                "karate_damage" or "buff_hits" or "tea_exhaust_damage" or "tea_area_damage" or "area_multi" or "area_x" => ComponentCategory.Damage,
                "block_after_stock" or "tea_block_weak" or "timed_thorns" => ComponentCategory.Defense,
                "vulnerable_after_attack" or "weak_after_skill" or "hook_strength" => ComponentCategory.Status,
                "shuriken" or "fire_stock" or "double_stock" or "discard_stock" => ComponentCategory.Orb,
                "return_first" or "return_attacks" or "play_on_top" or "topdeck_self" or "fill_hand" or "draw_if_tea"
                    or "copy_hand_top" or "transform_flame" or "play_statuses" or "play_top_exhaust" or "exhaust_top"
                    or "exhaust_to_top" or "scry" or "scry_block" or "scry_exhaust" or "scry_exhaust_breath"
                    or "draw_sly" or "draw_discard_nonstatus" => ComponentCategory.CardManipulation,
                _ => ComponentCategory.Resource
            }
        };
    }

    private static ComponentAtom Builtin(string template, string chinese, string english, string slot, int amount, bool targeted = false)
    {
        var text = new OperationLocalizedText(chinese, english);
        string projection = chinese.Replace("[[" + slot + "]]", amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var operation = new GeneratorOperation(template, targeted ? OperationScope.SingleEnemyOnly : OperationScope.NonTargeted,
            projection, new Dictionary<string, int>(), RequiresSingleTarget: targeted);
        var spec = OperationRuntimeSpecCompiler.GetOrCompile(operation);
        return Atom(template, text, spec, targeted);
    }

    private static ComponentAtom Atom(string template, OperationLocalizedText text, OperationRuntimeSpec spec, bool targeted) =>
        new(template, targeted ? OperationScope.SingleEnemyOnly : OperationScope.NonTargeted,
            text.RenderChinese(spec), targeted, CardReferenceRequirement.None)
        {
            SemanticId = ProfileId + ":" + template + ":" + string.Join('_', spec.Values.Select(v => v.BaseValue)),
            RuntimeSpec = spec,
            LocalizedText = text,
        };
}
