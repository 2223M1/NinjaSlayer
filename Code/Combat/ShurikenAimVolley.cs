using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
#if NINJASLAYER_CHANNEL_STABLE
using AimPower = NinjaSlayer.Powers.BladeSweepPower;
#endif

namespace NinjaSlayer.Code.Combat;

// A Scry discard batch and its nested stock shots spend one Aim snapshot together.
internal sealed class ShurikenAimVolley : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<Creature, ShurikenAimVolley> Active = new();
    private readonly Creature _owner;
    private readonly PlayerChoiceContext _choiceContext;
    private readonly AimPower? _power;
    private readonly int _bonus;
    private int _leases = 1;
    private bool _fired;

    private ShurikenAimVolley(PlayerChoiceContext choiceContext, Creature owner)
    {
        _owner = owner;
        _choiceContext = choiceContext;
        _power = owner.GetPower<AimPower>();
        _bonus = Math.Max(0, _power?.Amount ?? 0);
    }

    internal static ShurikenAimVolley Begin(PlayerChoiceContext choiceContext, Creature owner)
    {
        if (Active.TryGetValue(owner, out var volley))
        {
            volley._leases++;
            return volley;
        }
        volley = new ShurikenAimVolley(choiceContext, owner);
        Active.Add(owner, volley);
        return volley;
    }

    internal static int DamageBonus(Creature owner) => Active.TryGetValue(owner, out var volley)
        ? volley._bonus : Math.Max(0, owner.GetPowerAmount<AimPower>());

    internal void MarkFired() => _fired = true;

    public async ValueTask DisposeAsync()
    {
        if (--_leases > 0) return;
        Active.Remove(_owner);
        if (_fired && _bonus > 0 && _owner.GetPower<AimPower>() is { } power
            && ReferenceEquals(power, _power))
            await PowerCmd.ModifyAmount(_choiceContext, power, -Math.Min(_bonus, power.Amount), null, null);
    }
}
