using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Content;

public sealed class KarateVar(decimal amount) : DynamicVar(Key, amount)
{
    public const string Key = "Karate";
}

public sealed class ShurikenVar(decimal amount) : DynamicVar(Key, amount)
{
    public const string Key = "Shuriken";
}

public sealed class ChadoVar(decimal amount) : DynamicVar(Key, amount)
{
    public const string Key = "Chado";
}

public sealed class NarakuLifeVar(decimal amount) : DynamicVar(Key, amount)
{
    public const string Key = "NarakuLife";
}

public sealed class VigorAmountVar(decimal amount) : DynamicVar(Key, amount)
{
    public const string Key = "Vigor";
}

public sealed class CalculatedKarateVar() : CalculatedVar(Key)
{
    public const string Key = "CalculatedKarate";
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
