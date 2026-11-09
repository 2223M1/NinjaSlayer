using Godot;
using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Events;
using NinjaSlayer.Relics;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyOctober29Cards()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            foreach (string previous in new[] { "none", "attack", "skill" })
            {
                using var combat = new OrbCombat();
                if (previous == "attack") await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
                if (previous == "skill") await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
                int hp = combat.Enemy.CurrentHp;
                var other = combat.AddEnemy();
                await CardCmd.AutoPlay(Choice, AddCard<LowUppercut>(combat, upgraded: upgraded), combat.Enemy);
                int debuff = previous == "attack" ? (upgraded ? 2 : 1) : 0;
                Require(combat.Enemy.CurrentHp == hp - (upgraded ? 10 : 8)
                    && combat.Enemy.GetPowerAmount<WeakPower>() == debuff
                    && combat.Enemy.GetPowerAmount<VulnerablePower>() == debuff
                    && !other.HasPower<WeakPower>() && !other.HasPower<VulnerablePower>(),
                    $"Low Uppercut must check the previous finished card and debuff only its target ({previous}, {upgraded}).");
            }
            using (var combat = new OrbCombat())
            {
                var owner = combat.Player.Creature;
                await CardCmd.AutoPlay(Choice, AddCard<DevourFlame>(combat, upgraded: upgraded), null);
                int gain = upgraded ? 3 : 2;
                var wound = combat.State.CreateCard<Wound>(combat.Player);
                await CardPileCmd.AddGeneratedCardToCombat(wound, PileType.Hand, combat.Player);
                await CardCmd.Discard(Choice, wound);
                await CardPileCmd.Add(wound, PileType.Hand);
                Require(!owner.HasPower<NarakuLifePower>(), "Generation, discard and pile movement must not trigger Devour Flame.");
                await CardCmd.Exhaust(Choice, wound);
                var endurance = AddCard<Endurance>(combat, upgraded: upgraded);
                Require(endurance.Keywords.Contains(CardKeyword.Ethereal), "Endurance must have Ethereal in both versions.");
                await CardCmd.Exhaust(Choice, endurance, causedByEthereal: true);
                Require(owner.GetPowerAmount<NarakuLifePower>() == gain * 2,
                    "Devour Flame must trigger once for ordinary and Ethereal exhaust, regardless of card type.");
                var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
                combat.State.AddPlayer(other);
                other.ResetCombatState();
                var otherCard = combat.State.CreateCard<Wound>(other);
                await CardPileCmd.Add(otherCard, PileType.Hand);
                await CardCmd.Exhaust(Choice, otherCard);
                Require(owner.GetPowerAmount<NarakuLifePower>() == gain * 2,
                    "Another player's exhaust must not trigger Devour Flame.");
                other.PlayerCombatState!.AfterCombatEnd();
            }
            using (var combat = new OrbCombat())
            {
                await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
                await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
                await CardCmd.AutoPlay(Choice, AddCard<SomersaultKick>(combat, upgraded: upgraded), combat.Enemy);
                Require(combat.Enemy.CurrentHp == 1000 - 2 * ((upgraded ? 9 : 7) + 10)
                    && !combat.Player.Creature.HasPower<VigorPower>(),
                    "Somersault Kick must apply Strength and Vigor to both 7/9 hits and consume Vigor once.");
            }
        }
        GD.Print("PASS October 29: Low Uppercut condition/target, owner-only ordinary/Ethereal exhaust, Somersault two-hit Vigor.");
        await VerifySawatariRecordedChoice();
    }

    private static async Task VerifySawatariRecordedChoice()
    {
        Type sessionType = typeof(TheMovingJungleEvent).Assembly.GetType("NinjaSlayer.Code.Combat.SawatariEventSession", true)!;
        foreach (string phase in new[] { "Intermission", "DuelResult" })
        {
            using var fixture = new SawatariFixture();
            var combat = fixture.Combat;
            var player = combat.Player;
            var eventModel = (TheMovingJungleEvent)ModelDb.Event<TheMovingJungleEvent>().ToMutable();
            AccessTools.Property(typeof(EventModel), "Owner").SetValue(eventModel, player);
            AccessTools.Property(typeof(EventModel), "Rng").SetValue(eventModel, new MegaCrit.Sts2.Core.Random.Rng(17));
            player.AddRelicInternal(ModelDb.Relic<NSTVPressPassRelic>().ToMutable());
            for (int i = 0; i < 3; i++) AddCard<DefendIronclad>(combat, PileType.Deck);
            int upgradedBefore = player.Deck.Cards.Count(card => card.IsUpgraded);
            var room = new NCombatRoom();
            var ui = new NCombatUi();
            room.AddChild(ui);
            ui.Hide();
            AccessTools.Property(typeof(NCombatRoom), "Ui").SetValue(room, ui);
            object session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, [combat.State, room, new[] { eventModel }, player], null)!;
            object sessions = AccessTools.Field(sessionType, "Sessions").GetValue(null)!;
            sessions.GetType().GetMethod("Add")!.Invoke(sessions, [combat.State, session]);
            bool testing = MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn;
            var audio = new Harmony("NinjaSlayer.October29.HeadlessAudio");
            audio.Patch(AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]),
                prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(SkipOctober29Audio)));
            try
            {
                MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
                object gate = AccessTools.Field(sessionType, "_phases").GetValue(session)!;
                PropertyInfo current = AccessTools.Property(gate.GetType(), "Current");
                current.SetValue(gate, Enum.Parse(current.PropertyType, phase));
                var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                AccessTools.Field(sessionType, "_intermissionChoice").SetValue(session, pending);
                AccessTools.Field(sessionType, "_ownsCombatPause").SetValue(session, true);
                CombatManager.Instance.Pause();
                Require(CombatManager.Instance.IsPaused, "The event fixture must begin with native combat paused.");
                var combatRoom = (CombatRoom)fixture.Run.CurrentRoom!;
                if (phase == "DuelResult") combatRoom.AddExtraReward(player,
                    new RelicReward(ModelDb.Relic<BioBambooSplintRelic>().ToMutable(), player));
                Require((bool)AccessTools.Method(sessionType, "ShouldStopCombat").Invoke(null, [combat.State])!,
                    "The event fixture must initially hold combat open for the choice.");
                EventOption option = (EventOption)AccessTools.Method(typeof(ReporterPassEventOptionPatch), "CreateRecordOption")
                    .Invoke(null, [eventModel])!;
                await option.Chosen();
                Require(eventModel.IsFinished && player.Deck.Cards.Count(card => card.IsUpgraded) == upgradedBefore + 3,
                    "Recording Sawatari must finish the event and upgrade three cards.");
                Require(!CombatManager.Instance.IsPaused && ui.Visible
                    && !(bool)AccessTools.Method(sessionType, "ShouldStopCombat").Invoke(null, [combat.State])!,
                    "Recording Sawatari must restore the combat UI and release the native win-condition gate.");
                Require(phase != "Intermission" || pending.Task.IsCompletedSuccessfully,
                    "Recording the intermission must release the pending player-turn continuation.");
                Require(combatRoom.ExtraRewards.Values.All(rewards => rewards.Count == 0),
                    "Recording must replace the duel's extra rewards, including bamboo.");
            }
            finally
            {
                sessions.GetType().GetMethod("Remove")!.Invoke(sessions, [combat.State]);
                if (CombatManager.Instance.IsPaused) CombatManager.Instance.Unpause();
                MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = testing;
                audio.UnpatchAll(audio.Id);
                room.Free();
            }
        }
        GD.Print("PASS Sawatari reporter choice: three upgrades, both decision gates released, pending turn resumed, pause cleared, duel rewards replaced.");
    }

    private static bool SkipOctober29Audio() => false;
}
