using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Orbs;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyMaintenanceRefactor()
    {
        foreach (bool throwing in new[] { false, true })
        foreach (int handSize in new[] { 0, 1, 2 })
        {
            using var combat = new OrbCombat();
            CardModel source = throwing ? AddCard<ThrowKunaiRedesignV1>(combat) : AddCard<ReadyStanceRedesignV1>(combat);
            CardModel? sly = handSize > 0 ? AddCard<ShurikenCreation>(combat) : null;
            CardModel? other = handSize > 1 ? AddCard<DefendIronclad>(combat) : null;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                Require(sly != null && options.Contains(sly), "Hand discard did not offer the Sly card.");
                return [sly!];
            }));
            await AddStock(combat.Player, 3);
            await CardCmd.AutoPlay(Choice, source, throwing ? combat.Enemy : null);
            Require(combat.Stock == (handSize == 0 ? 3 : 4),
                "Actual hand-discard cards must fire once, then play Sly once; empty hands must finish normally.");
            Require(sly == null || sly.Pile?.Type == PileType.Discard,
                "The selected Sly card did not complete its native autoplay.");
            Require(other == null || other.Pile?.Type == PileType.Hand,
                "Hand discard moved an unselected card.");
        }

        foreach (bool upgraded in new[] { false, true })
        foreach (int teaCount in new[] { 0, 1, 2 })
        {
            using var combat = new OrbCombat();
            var source = AddCard<MetabolicAccelerationRedesignV1>(combat, upgraded: upgraded);
            combat.Player.Creature.SetCurrentHpInternal(30);
            var teas = Enumerable.Range(0, teaCount).Select(_ => AddCard<ChadoEnergyRedesignV1>(combat)).ToArray();
            var drawTea = AddCard<ChadoEnergyRedesignV1>(combat, PileType.Draw);
            var discardTea = AddCard<ChadoEnergyRedesignV1>(combat, PileType.Discard);
            var other = AddCard<DefendIronclad>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                Require(options.All(card => teas.Contains(card)), "Healing offered a non-hand tea or another card.");
                return [teas.Last()];
            }));
            await CardCmd.AutoPlay(Choice, source, null);
            Require(combat.Player.Creature.CurrentHp == 30 + (teaCount > 0 ? upgraded ? 8 : 5 : 0),
                "Healing must require one successfully selected hand tea, including autoplay without tea.");
            Require(teas.Count(card => card.Pile?.Type == PileType.Exhaust) == Math.Min(teaCount, 1)
                && drawTea.Pile?.Type == PileType.Draw && discardTea.Pile?.Type == PileType.Discard
                && other.Pile?.Type == PileType.Hand, "Healing exhausted the wrong cards.");
        }
        GD.Print("PASS native hand-discard cards, Sly, empty hands and hand-only tea healing");

        VerifyCompanionModelIdentity();
        VerifyHoverTipLeaseRestoration();
        VerifyCompanionIntentGenerations();
    }

    private static void VerifyCompanionModelIdentity()
    {
        Type targeting = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Patches.FriendlyCompanionTargeting", true)!;
        var supportsCard = AccessTools.Method(targeting, "Supports", [typeof(CardModel)]).CreateDelegate<Func<CardModel, bool>>();
        var supportsPotion = AccessTools.Method(targeting, "Supports", [typeof(PotionModel)]).CreateDelegate<Func<PotionModel, bool>>();
        Type[] cards =
        [
            typeof(MegaCrit.Sts2.Core.Models.Cards.Coordinate), typeof(DemonicShield), typeof(Intercept), typeof(Lift), typeof(Mimic),
#if NINJASLAYER_CHANNEL_PREVIEW
            typeof(Blaze), typeof(Concoct), typeof(Fade),
#endif
        ];
        Type[] potions = [typeof(BlockPotion), typeof(DexterityPotion), typeof(FlexPotion), typeof(FyshOil),
            typeof(HeartOfIron), typeof(LiquidBronze), typeof(LuckyTonic), typeof(MazalethsGift),
            typeof(RegenPotion), typeof(ShipInABottle), typeof(SpeedPotion), typeof(StrengthPotion)];
        foreach (Type type in cards)
            Require(supportsCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type))), $"Native ally card {type.Name} lost companion targeting.");
        foreach (Type type in potions)
            Require(supportsPotion(ModelDb.GetById<PotionModel>(ModelDb.GetId(type))), $"Native potion {type.Name} lost companion targeting.");
        // This foreign type deliberately shares a simple name; do not register it over vanilla's ModelId.
        var sameNameCard = (CardModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Coordinate));
        Require(!supportsCard(sameNameCard) && !supportsCard(ModelDb.Card<StrikeIronclad>()),
            "Companion targeting admitted an unrelated or same-name card.");
        GD.Print("PASS native companion card/potion allowlist and same-name mod exclusion");
    }

    private static void VerifyHoverTipLeaseRestoration()
    {
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerHoverTipSuppression", true)!;
        var acquire = AccessTools.Method(type, "Acquire").CreateDelegate<Func<IDisposable>>();
        bool original = NHoverTipSet.shouldBlockHoverTips;
        try
        {
            foreach (bool alreadyBlocked in new[] { false, true })
            {
                NHoverTipSet.shouldBlockHoverTips = alreadyBlocked;
                using IDisposable first = acquire();
                using IDisposable second = acquire();
                first.Dispose();
                first.Dispose();
                Require(NHoverTipSet.shouldBlockHoverTips, "Closing one cinematic restored tooltips while another owned suppression.");
                second.Dispose();
                Require(NHoverTipSet.shouldBlockHoverTips == alreadyBlocked, "Final cinematic cleanup lost the prior tooltip state.");
            }
        }
        finally { NHoverTipSet.shouldBlockHoverTips = original; }
        GD.Print("PASS overlapping cinematic tooltip suppression and repeated cleanup");
    }

    private sealed class Coordinate() : CardModel(0, CardType.Skill, CardRarity.Common, TargetType.AnyAlly)
    {
        public override TargetType TargetType => TargetType.AnyAlly;
    }

    private static void VerifyCompanionIntentGenerations()
    {
        using var combat = new OrbCombat();
        var creature = new Creature(ModelDb.Monster<YamotoKokiMonster>().ToMutable(), CombatSide.Player, null)
        { CombatState = combat.State };
        var node = new NCreature();
        try
        {
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(node, creature);
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Combat.YamotoKokiIntentLifecycle", true)!;
        object Call(string method, params object[] args) => AccessTools.Method(type, method).Invoke(null, args)!;
        Task pending = Task.CompletedTask;
        object first = Call("BeginCombat", creature);
        Require(YamotoKokiIntentGenerationPatch.Prefix(node, ref pending), "Active companion intents were blocked.");
        Call("Invalidate", creature);
        Require(!(bool)Call("IsCurrent", first) && !YamotoKokiIntentGenerationPatch.Prefix(node, ref pending),
            "A completed combat still allowed a late companion intent update.");
        object second = Call("BeginCombat", creature);
        Require(!(bool)Call("IsCurrent", first) && (bool)Call("IsCurrent", second)
            && YamotoKokiIntentGenerationPatch.Prefix(node, ref pending),
            "A new combat revived an old intent callback or failed to enable its own intents.");
        Call("RehideIfInactive", first);
        Require((bool)Call("IsCurrent", second), "Late old-combat cleanup invalidated the new combat.");
        Call("Invalidate", creature);
        GD.Print("PASS companion intent retirement, delayed callbacks and combat restart");
        }
        finally { node.Free(); }
    }
}
