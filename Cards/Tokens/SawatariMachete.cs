using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(TokenCardPool))]
public sealed class SawatariMachete : NinjaSlayerStandaloneCardTemplate
{
    private int _heldHand = -1;
    [SavedProperty]
    public int HeldHand { get => _heldHand; set { AssertMutable(); _heldHand = value; } }

    public SawatariMachete() : base(new NinjaSlayerCardSpec(nameof(SawatariMachete),
        2, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy, false)) { }
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(12, ValueProp.Move)];

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card != this) return Task.CompletedTask;
        if (Pile?.Type is not (PileType.Hand or PileType.Play)) HeldHand = -1;
        if (Pile?.Type == PileType.Hand && HeldHand >= 0 && HeldCards(Owner)
            .Any(other => other != this && other.HeldHand == HeldHand)) HeldHand = -1;
        foreach (var knife in PileType.Hand.GetPile(Owner).Cards.OfType<SawatariMachete>())
            if (knife.HeldHand < 0) knife.HeldHand = ChooseFreeHand(Owner);
        PlayerMacheteVisuals.Refresh(Owner);
        return Task.CompletedTask;
    }

    private static IEnumerable<SawatariMachete> HeldCards(Player player) =>
        player.PlayerCombatState!.AllCards.OfType<SawatariMachete>()
            .Where(card => card.Pile?.Type is PileType.Hand or PileType.Play && card.HeldHand >= 0);

    internal static int ChooseFreeHand(Player player)
    {
        int occupied = HeldCards(player).Aggregate(0, (mask, card) => mask | (1 << card.HeldHand));
        return occupied switch { 0 => player.RunState.Rng.Niche.NextInt(2), 1 => 1, 2 => 0, _ => -1 };
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var held = HeldCards(Owner).ToArray();
        SawatariMachete thrown = held.Length > 1 ? Owner.RunState.Rng.Niche.NextItem(held)! : held.SingleOrDefault() ?? this;
        int throwHand = thrown.HeldHand >= 0 ? thrown.HeldHand : Owner.RunState.Rng.Niche.NextInt(2);
        if (thrown != this) thrown.HeldHand = HeldHand;
        HeldHand = -1;
        var receiver = cardPlay.Target?.Monster as SawatariMonster;
        int returnHand = receiver is { ActThree: true, MacheteCount: < 2 } ? receiver.ChooseReturnHand() : -1;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .Targeting(cardPlay.Target!).WithNoAttackerAnim()
            .AfterAttackerAnim(() => PlayerMacheteVisuals.Throw(this, thrown, throwHand, cardPlay.Target!, returnHand))
            .WithHitFx(VfxCmd.slashPath, NinjaSlayerCombatVfx.MacheteHitSfx)
            .Execute(choiceContext);
        if (returnHand >= 0) receiver!.ReturnMachete(returnHand);
    }
    protected override void OnUpgrade() { }
}
