using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class AlabamaDropRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(0),
        new ExtraDamageVar(6),
        new CalculatedDamageVar(ValueProp.Move | ValueProp.Unpowered)
            .WithMultiplier(static (card, _) => card.Owner.Creature.GetPowerAmount<KaratePower>()),
        new DynamicVar("Dazed", 3)
    ];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromCard<Dazed>()];

    public AlabamaDropRedesignV1()
        : base(nameof(AlabamaDropRedesignV1), "AlabamaDrop", 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        bool resolved = false;
        async Task ResolveImpact()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            int karate = Owner.Creature.GetPowerAmount<KaratePower>();
            await CreatureCmd.Damage(
                choiceContext,
                cardPlay.Target!,
                karate * DynamicVars.ExtraDamage.BaseValue,
                ValueProp.Move | ValueProp.Unpowered,
                this
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , cardPlay
#endif
            );
        }

        await AlabamaDropAnimation.Play(Owner.Creature, cardPlay.Target!, ResolveImpact);
        await ResolveImpact();
        await PowerCmd.Remove<KaratePower>(Owner.Creature);
        for (int index = 0; index < DynamicVars["Dazed"].IntValue; index++)
        {
            await NinjaSlayerCardCmd.AddGeneratedCard<Dazed>(Owner, PileType.Draw);
        }
    }

    protected override void OnUpgrade() => DynamicVars.ExtraDamage.UpgradeValueBy(2);
}
