using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Encounters;
using NinjaSlayer.Events;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class DarkNinjaMonster : ModMonsterTemplate
{
    private static readonly Vector2 StandingShadowPosition = new(-8f, -20.625f);
    private static readonly Vector2 StandingShadowScale = new(0.44f, 0.25f);
    private static readonly Vector2 CombatShadowPosition = new(5f, -20.625f);
    private static readonly Vector2 CombatShadowScale = new(0.6f, 0.28f);

    internal const string StandingTexturePath =
        "res://NinjaSlayer/images/monsters/dark_ninja_standing.png";
    internal const string CombatTexturePath =
        "res://NinjaSlayer/images/monsters/dark_ninja.png";
    internal const string BladeGlowTexturePath =
        "res://NinjaSlayer/images/monsters/dark_ninja_blade_glow.png";

    public const string CounterStanceMoveId = "DARK_COUNTER_STANCE";
    public const string DeathSlashMoveId = "DEATH_SLASH";
    public const string DarkRobeMoveId = "DARK_ROBE";
    public const string DarkStrikeMoveId = "DARK_STRIKE";
    public const string KillingIntentMoveId = "DARK_KILLING_INTENT";

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 200, 180);

    public override int MaxInitialHp => MinInitialHp;

    public override string DeathSfx => NinjaSlayerAudio.DarkNinjaDeathEvent;

    private bool _hasPlayedBegin;
    private bool _hasPlayedDeathKiri;
    private bool _hasPlayedDarkRobe;
    private bool _hasPlayedEvasionInsult;
    private bool _hasEnteredCombatStance;

    [SavedProperty]
    public bool HasEnteredCombatStance
    {
        get => _hasEnteredCombatStance;
        private set
        {
            AssertMutable();
            _hasEnteredCombatStance = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedBegin
    {
        get => _hasPlayedBegin;
        private set
        {
            AssertMutable();
            _hasPlayedBegin = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedDeathKiri
    {
        get => _hasPlayedDeathKiri;
        private set
        {
            AssertMutable();
            _hasPlayedDeathKiri = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedDarkRobe
    {
        get => _hasPlayedDarkRobe;
        private set
        {
            AssertMutable();
            _hasPlayedDarkRobe = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedEvasionInsult
    {
        get => _hasPlayedEvasionInsult;
        private set
        {
            AssertMutable();
            _hasPlayedEvasionInsult = value;
        }
    }

    internal static int StrengthAmount =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);

    internal static int DeathSlashDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 30, 25);

    internal static int DarkRobeBlock =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 20, 15);

    internal static int DarkStrikeDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 16, 14);

    protected override string VisualsPath =>
        "res://NinjaSlayer/scenes/creature_visuals/dark_ninja.tscn";

    protected override MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(VisualsPath);

    public override IEnumerable<string> AssetPaths =>
        base.AssetPaths
            .Concat([StandingTexturePath, CombatTexturePath])
            .Concat(DarkNinjaBladeChargePresentation.AssetPaths)
            .Concat(DarkNinjaSpecialAttackPresentation.AssetPaths)
            .Concat(DarkNinjaBattleFirePresentation.AssetPaths);

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        ApplyPoseTexture(HasEnteredCombatStance ? CombatTexturePath : StandingTexturePath);
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            Creature,
            99,
            Creature,
            null);

        if (!HasPlayedBegin)
        {
            HasPlayedBegin = true;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaBeginEvent);
        }

        if (Creature.CombatState?.Encounter is DarkNinjaEncounter
            && NCombatRoom.Instance is { } room)
        {
            DarkNinjaBattleFirePresentation.Ensure(
                room,
                revealImmediately: HasEnteredCombatStance);
        }
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature != Creature || wasRemovalPrevented)
        {
            return Task.CompletedTask;
        }

        NRunMusicController.Instance?.UpdateMusicParameter(
            NinjaSlayerAudio.DarkNinjaProgressParameter,
            NinjaSlayerAudio.DarkNinjaEndProgress);
        DarkNinjaMusicSession.EndBattle();
        return Task.CompletedTask;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState stance = new(CounterStanceMoveId, CounterStanceMove, new BuffIntent());
        MoveState slash = new(
            DeathSlashMoveId,
            DeathSlashMove,
            new SingleAttackIntent(() => DeathSlashDamage),
            new DebuffIntent());
        MoveState robe = new(DarkRobeMoveId, DarkRobeMove, new DefendIntent(), new BuffIntent());
        MoveState strike = new(
            DarkStrikeMoveId,
            DarkStrikeMove,
            new SingleAttackIntent(() => DarkStrikeDamage),
            new HealIntent(),
            new DebuffIntent());
        MoveState intent = new(KillingIntentMoveId, KillingIntentMove, new BuffIntent());

        stance.FollowUpState = slash;
        slash.FollowUpState = robe;
        robe.FollowUpState = strike;
        strike.FollowUpState = intent;
        intent.FollowUpState = slash;
        return new MonsterMoveStateMachine([stance, slash, robe, strike, intent], stance);
    }

    private async Task CounterStanceMove(IReadOnlyList<Creature> _)
    {
        EnterCombatStance();
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaBeppinAwakensEvent);
        var choiceContext = new ThrowingPlayerChoiceContext();
        Task bladeCharge = DarkNinjaBladeChargePresentation.Play(Creature);
        await PowerCmd.Apply<StrengthPower>(
            choiceContext,
            Creature,
            StrengthAmount,
            Creature,
            null);
        Task fireReveal = NCombatRoom.Instance is { } room
            ? DarkNinjaBattleFirePresentation.RevealFromRightToLeft(room)
            : Task.CompletedTask;
        await PowerCmd.Apply<IaiPower>(
            choiceContext,
            Creature,
            DarkNinjaCombatMath.CounterInterval,
            Creature,
            null);
        await Task.WhenAll(bladeCharge, fireReveal);
    }

    private async Task DeathSlashMove(IReadOnlyList<Creature> targets)
    {
        var attack = await DarkNinjaAttackExecution.PlayDeathSlash(this, targets, DeathSlashDamage);
        if (!HasPlayedDeathKiri && attack.Results.SelectMany(result => result).Any())
        {
            HasPlayedDeathKiri = true;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaDeathKiriEvent);
        }

        await PowerCmd.Apply<FrailPower>(
            new ThrowingPlayerChoiceContext(),
            targets,
            3,
            Creature,
            null);
    }

    private async Task DarkRobeMove(IReadOnlyList<Creature> _)
    {
        if (!HasPlayedDarkRobe)
        {
            HasPlayedDarkRobe = true;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaDarkRobeEvent);
        }

        await CreatureCmd.GainBlock(Creature, DarkRobeBlock, ValueProp.Move, null);
        await PowerCmd.Apply<VigorPower>(
            new ThrowingPlayerChoiceContext(),
            Creature,
            8,
            Creature,
            null);
    }

    private async Task DarkStrikeMove(IReadOnlyList<Creature> targets)
    {
        IReadOnlyList<Creature> connectedTargets =
            await DarkNinjaAttackExecution.PlayDarkStrike(this, targets, DarkStrikeDamage);

        if (connectedTargets.Count > 0)
        {
            await PowerCmd.Apply<WeakPower>(
                new ThrowingPlayerChoiceContext(),
                connectedTargets,
                2,
                Creature,
                null);
        }
    }

    private async Task KillingIntentMove(IReadOnlyList<Creature> _)
    {
        Task bladeCharge = DarkNinjaBladeChargePresentation.Play(Creature);
        await PowerCmd.Apply<StrengthPower>(
            new ThrowingPlayerChoiceContext(),
            Creature,
            StrengthAmount,
            Creature,
            null);
        await bladeCharge;
    }

    private void EnterCombatStance()
    {
        if (!HasEnteredCombatStance)
        {
            HasEnteredCombatStance = true;
        }

        ApplyPoseTexture(CombatTexturePath);
    }

    private void ApplyPoseTexture(string texturePath)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(Creature)?.Visuals;
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(visuals);
        if (body == null)
        {
            return;
        }

        try
        {
            body.Texture = PreloadManager.Cache.GetTexture2D(texturePath);
            bool isCombatPose = texturePath == CombatTexturePath;
            Vector2 shadowPosition = isCombatPose ? CombatShadowPosition : StandingShadowPosition;
            Vector2 shadowScale = isCombatPose ? CombatShadowScale : StandingShadowScale;
            var shadowController = visuals?.GetNodeOrNull<NinjaSlayerShadowController>(
                NinjaSlayerVisualRig.ShadowControllerNodeName);
            if (shadowController != null)
            {
                shadowController.SetAuthoredPresentation(shadowPosition, shadowScale);
            }
            else if (NinjaSlayerVisualRig.GetShadow(visuals) is { } shadow)
            {
                shadow.Position = shadowPosition;
                shadow.Scale = shadowScale;
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Dark Ninja pose was unavailable: {exception.Message}");
        }
    }

    internal void PlayEvasionInsultOnce()
    {
        if (HasPlayedEvasionInsult)
        {
            return;
        }

        HasPlayedEvasionInsult = true;
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaInsultEvent);
    }
}
