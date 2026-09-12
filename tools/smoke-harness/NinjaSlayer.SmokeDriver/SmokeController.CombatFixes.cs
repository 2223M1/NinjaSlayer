using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyCombatFixPresentation(ICombatState combat, Player player, Creature target)
    {
        var choice = new BlockingPlayerChoiceContext();
        Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        FastModeType speed = SaveManager.Instance.PrefsSave.FastMode;
        CardModel[] originalHand = PileType.Hand.GetPile(player).Cards.ToArray();
        foreach (var card in originalHand) await CardPileCmd.Add(card, PileType.Discard);
        try
        {
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            await PowerCmd.Apply<KaratePower>(choice, player.Creature, 4, player.Creature, null);
            var alabama = combat.CreateCard<AlabamaDropRedesignV1>(player);
            await CardPileCmd.Add(alabama, PileType.Hand);
            await PlayWithStationaryCombatUi(alabama, target);
            Require(target.CurrentHp == 976 && player.Creature.GetPowerAmount<KaratePower>() == 3,
                "Rendered Alabama must deal 20 main plus four Karate and consume one stack.");
            await PowerCmd.Remove<KaratePower>(player.Creature);
            foreach (Type type in new[] { typeof(KarateStraightRedesignV1), typeof(SatsubatsuRedesignV1),
                typeof(OneDrinkOneStrikeRedesignV1), typeof(RoundhouseKickRedesignV1),
                typeof(SweepKickRedesignV1), typeof(DragonFlyingKickRedesignV1) })
            {
                CardModel card = combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)), player);
                await CardPileCmd.Add(card, PileType.Hand);
                await PlayWithStationaryCombatUi(card, target);
                foreach (var flame in PileType.Hand.GetPile(player).Cards.OfType<BlackFlameRedesignV1>().ToArray())
                    await CardPileCmd.Add(flame, PileType.Discard);
            }
            await CapturePresentation("combat-fixes-returned");
            _checkpoints.Write("combat-fixes.presentation-completed");
        }
        finally
        {
            NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck;
            SaveManager.Instance.PrefsSave.FastMode = speed;
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
            foreach (var card in originalHand) await CardPileCmd.Add(card, PileType.Hand);
            await CreatureCmd.SetCurrentHp(target, 1000);
        }
    }
}
