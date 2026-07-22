using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherDamageLedger(IEnumerable<Creature> victims)
{
    private readonly Dictionary<DamageResult, int> _damageDisplayOverrides =
        new(ReferenceEqualityComparer.Instance);

    public HashSet<Creature> Victims { get; } = victims.ToHashSet();
    public HashSet<Creature> DeferredDeaths { get; } = [];

    public bool TryProtect(Creature target, bool committing, ref decimal amount, out int displayDamage)
    {
        displayDamage = 0;
        if (committing
            || !Victims.Contains(target)
            || amount < target.CurrentHp
            || target.CurrentHp <= 0)
        {
            return false;
        }

        displayDamage = (int)Math.Clamp(amount, 0m, 999999999m);
        DeferredDeaths.Add(target);
        if (target.CurrentHp == 1)
        {
            if (target.MaxHp > 1)
            {
                target.SetCurrentHpInternal(2);
                amount = 1m;
            }
            else
            {
                amount = 0m;
            }
        }
        else
        {
            amount = target.CurrentHp - 1;
        }

        return true;
    }

    public void RegisterProtectedDamageResult(DamageResult result, int displayDamage)
    {
        if (displayDamage <= 0 || !Victims.Contains(result.Receiver))
        {
            return;
        }

        if (result.UnblockedDamage + result.OverkillDamage > 0)
        {
            _damageDisplayOverrides[result] = displayDamage;
            return;
        }

        NDamageNumVfx? damageVfx = NDamageNumVfx.Create(result.Receiver, displayDamage);
        Node? vfxContainer = result.Receiver.GetVfxContainer();
        if (damageVfx != null && vfxContainer != null)
        {
            vfxContainer.AddChild(damageVfx);
        }
    }

    public bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage)
    {
        if (_damageDisplayOverrides.Remove(result, out displayDamage))
        {
            return true;
        }

        displayDamage = 0;
        return false;
    }

    public bool GuaranteedClearMatchedRuntime() =>
        Victims.All(victim => victim.IsDead || DeferredDeaths.Contains(victim));

    public List<Creature> LivingDeferredDeaths() =>
        DeferredDeaths.Where(creature => creature.IsAlive).ToList();

    public void Clear() => _damageDisplayOverrides.Clear();
}
