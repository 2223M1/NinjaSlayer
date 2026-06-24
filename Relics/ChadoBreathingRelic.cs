using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
[RegisterCharacterStarterRelic(typeof(NinjaSlayerCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(DeepChadoBreathingRelic))]
public class ChadoBreathingRelic : ModRelicTemplate
{
    private int _lastBreathedTurnNumber;

    protected virtual int HealAmount => 1;
    public override RelicRarity Rarity => RelicRarity.Starter;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: $"res://NinjaSlayer/images/relics/{GetType().Name}.png",
        IconOutlinePath: $"res://NinjaSlayer/images/relics/{GetType().Name}_outline.png",
        BigIconPath: $"res://NinjaSlayer/images/relics/{GetType().Name}_large.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new HealVar(HealAmount)
    ];

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner || Owner.PlayerCombatState?.TurnNumber <= 1)
        {
            return;
        }

        await TryBreathe(choiceContext, LostHpLastPlayerTurn);
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (Owner.PlayerCombatState?.TurnNumber <= 1 || room.CombatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        await TryBreathe(new HookPlayerChoiceContext(this, Owner.NetId, room.CombatState, GameActionType.Combat), LostHpThisTurn);
    }

    public async Task TryBreathe(PlayerChoiceContext choiceContext, Func<Creature, bool> lostHp)
    {
        if (_lastBreathedTurnNumber == Owner.PlayerCombatState?.TurnNumber || lostHp(Owner.Creature) || Owner.Creature.IsDead)
        {
            return;
        }

        _lastBreathedTurnNumber = Owner.PlayerCombatState?.TurnNumber ?? 0;
        Flash();
        await CreatureCmd.Heal(Owner.Creature, HealAmount);
        if (Owner.Creature.HasPower<NarakuPower>())
        {
            await NinjaSlayerActions.ExitNaraku(Owner.Creature);
        }
    }

    private static bool LostHpThisTurn(Creature creature)
    {
        return CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Any(e =>
            e.HappenedThisTurn(creature.CombatState) &&
            e.Receiver == creature &&
            e.Result.UnblockedDamage > 0);
    }

    private static bool LostHpLastPlayerTurn(Creature creature)
    {
        if (creature.Player == null)
        {
            return false;
        }

        return CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Any(e =>
            e.HappenedLastPlayerTurn(creature.Player) &&
            e.Receiver == creature &&
            e.Result.UnblockedDamage > 0);
    }
}
