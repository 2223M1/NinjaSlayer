using global::AutoAnthony;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.AutoAnthony;

[RegisterCard(typeof(NinjaAnthonyCardPool))]
public abstract class NinjaChaosCard : ExternalChaosCardModel
{
    protected override string ComponentProfileId => NinjaComponentSources.ProfileId;
    public override CardPoolModel Pool => ModelDb.CardPool<NinjaSlayerCardPool>();
}

public sealed class NinjaAnthonyCardPool : NinjaSlayerCardPoolTemplate;
