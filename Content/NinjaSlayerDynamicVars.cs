using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Cards.DynamicVars;

namespace NinjaSlayer.Content;

public sealed class KarateVar : DynamicVar
{
    public const string Key = "Karate";
    public KarateVar(decimal amount) : base(Key, amount) =>
        this.WithTooltip(_ => HoverTipFactory.FromPower<KaratePower>());
}

public sealed class ShurikenVar : DynamicVar
{
    public const string Key = "Shuriken";
    public ShurikenVar(decimal amount) : base(Key, amount) =>
        this.WithTooltip(_ => HoverTipFactory.FromOrb<ShurikenOrb>());
}

public sealed class ChadoVar : DynamicVar
{
    public const string Key = "Chado";
    public ChadoVar(decimal amount) : base(Key, amount) =>
        this.WithTooltip(_ => HoverTipFactory.FromCard<ChadoEnergyRedesignV1>());
}

public sealed class NarakuLifeVar : DynamicVar
{
    public const string Key = "NarakuLife";
    public NarakuLifeVar(decimal amount) : base(Key, amount) =>
        this.WithTooltip(_ => HoverTipFactory.FromPower<NarakuLifePower>());
}

public sealed class VigorAmountVar : DynamicVar
{
    public const string Key = "Vigor";
    public VigorAmountVar(decimal amount) : base(Key, amount) =>
        this.WithTooltip(_ => HoverTipFactory.FromPower<VigorPower>());
}

public sealed class CalculatedKarateVar : CalculatedVar
{
    public const string Key = "CalculatedKarate";
    public CalculatedKarateVar() : base(Key) =>
        this.WithTooltip(_ => HoverTipFactory.FromPower<KaratePower>());
}

public static class NinjaSlayerDynamicVarExtensions
{
    public static KarateVar Karate(this DynamicVarSet vars) => (KarateVar)vars[KarateVar.Key];

    public static ShurikenVar Shuriken(this DynamicVarSet vars) => (ShurikenVar)vars[ShurikenVar.Key];

    public static ChadoVar Chado(this DynamicVarSet vars) => (ChadoVar)vars[ChadoVar.Key];

    public static NarakuLifeVar NarakuLife(this DynamicVarSet vars) => (NarakuLifeVar)vars[NarakuLifeVar.Key];

    public static VigorAmountVar VigorAmount(this DynamicVarSet vars) =>
        (VigorAmountVar)vars[VigorAmountVar.Key];

    public static CalculatedKarateVar CalculatedKarate(this DynamicVarSet vars) =>
        (CalculatedKarateVar)vars[CalculatedKarateVar.Key];
}
