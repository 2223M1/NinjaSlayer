using Godot;
using System.Globalization;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib;
using STS2RitsuLib.Combat.SecondaryResources;

namespace NinjaSlayer.Content;

public static class NinjaSlayerTeaEnergy
{
    private const string LocalId = "tea_energy";

    public static SecondaryResourceDefinition Definition { get; private set; } = null!;
    public static string Id => Definition.Id;

    public static IDisposable Register(string modId)
    {
        ModSecondaryResourceRegistry registry = RitsuLibFramework.GetSecondaryResourceRegistry(modId);
        string iconPath = NinjaSlayerAssetPaths.Image("chado_energy_counter_base.png");
        Definition = registry.Register(LocalId, new SecondaryResourceDefinition(
            defaultAmount: RedesignV1Rules.StartingTeaEnergy,
            baseMaxAmount: RedesignV1Rules.StartingTeaEnergy,
            minAmount: 0,
            hardMaxAmount: RedesignV1Rules.AncientTeaEnergy,
            turnStartPolicy: SecondaryResourceTurnStartPolicy.None,
            persistencePolicy: SecondaryResourcePersistencePolicy.Run,
            smallIconPath: iconPath,
            largeIconPath: iconPath));

        registry.AlwaysShowInCombatUiForCharacter<NinjaSlayerRedesignCharacter>(LocalId);
        registry.RegisterCombatUi(
            "tea_energy_counter",
            parent =>
            {
                NSecondaryResourceCounter counter = NSecondaryResourceCounter.Create(
                    Definition,
                    new SecondaryResourceCounterStyle
                    {
                        FontSize = 28,
                        FormatAmount = static (amount, max) => max is { } value
                            ? $"{amount}/{value}"
                            : amount.ToString(CultureInfo.InvariantCulture),
                        IconStyle = SecondaryResourceIconStyle.Default with
                        {
                            Size = new Vector2(76, 76),
                            HoverTip = SecondaryResourceHoverTipStyle.Default
                        }
                    });
                Control energyCounter = parent.GetNode<Control>("%EnergyCounterContainer");
                counter.Position = energyCounter.Position + new Vector2(112, -112);
                return counter;
            },
            static context => context.Node.Bind(context.Player));

        registry.RegisterCardUi(
            "tea_energy_card_cost",
            parent =>
            {
                NSecondaryResourceCardCostUi ui = NSecondaryResourceCardCostUi.Create(
                    Id,
                    new SecondaryResourceCardCostUiStyle
                    {
                        IconSize = new Vector2(48, 48),
                        FontSize = 24
                    });
                ui.Position = parent.GetNode<TextureRect>("%EnergyIcon").Position + new Vector2(0, 76);
                return ui;
            },
            static context => context.Node.Refresh(context));

        return RitsuLibFramework.SubscribeLifecycle<RestSiteHealedEvent>(OnRestSiteHealed);
    }

    private static void OnRestSiteHealed(RestSiteHealedEvent evt)
    {
        if (evt.Player.Character is not NinjaSlayerRedesignCharacter || evt.IsMimicked)
        {
            return;
        }

        _ = TaskHelper.RunSafely(
            SecondaryResourceCmd.Gain(
                evt.Player,
                Id,
                RedesignV1Rules.RestTeaGain,
                source: evt.Player.Character));
    }
}
