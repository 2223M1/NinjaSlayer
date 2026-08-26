using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed class FinisherProtectionToken
{
    internal FinisherProtectionToken(
        FinisherDamageLedger ledger,
        Creature target,
        bool temporaryHpBumpApplied,
        int displayDamage)
    {
        Ledger = ledger;
        Target = target;
        TemporaryHpBumpApplied = temporaryHpBumpApplied;
        DisplayDamage = displayDamage;
    }

    internal FinisherDamageLedger Ledger { get; }
    internal Creature Target { get; }
    internal bool TemporaryHpBumpApplied { get; }
    internal int DisplayDamage { get; }
}

internal sealed class FinisherDamageLedger
{
    private readonly Dictionary<DamageResult, int> _damageDisplayOverrides =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Creature, FinisherProtectionToken> _activeProtections =
        new(ReferenceEqualityComparer.Instance);
    private readonly ICombatState _combatState;
    private readonly Func<bool> _isCurrentContext;
    private readonly Action<DamageResult, int> _presentProtectedDamage;

    public FinisherDamageLedger(
        IEnumerable<Creature> victims,
        ICombatState combatState,
        Func<bool> isCurrentContext,
        Action<DamageResult, int>? presentProtectedDamage = null)
    {
        Victims = victims.ToHashSet();
        _combatState = combatState;
        _isCurrentContext = isCurrentContext;
        _presentProtectedDamage = presentProtectedDamage ?? RegisterProtectedDamageResult;
    }

    public HashSet<Creature> Victims { get; }
    public HashSet<Creature> DeferredDeaths { get; } = [];

    public bool TryProtect(
        Creature target,
        bool committing,
        ref decimal amount,
        out FinisherProtectionToken? token)
    {
        token = null;
        if (committing
            || !_isCurrentContext()
            || !ReferenceEquals(target.CombatState, _combatState)
            || !Victims.Contains(target)
            || amount < target.CurrentHp
            || target.CurrentHp <= 0)
        {
            return false;
        }

        if (_activeProtections.ContainsKey(target))
        {
            return false;
        }

        int hpBefore = target.CurrentHp;
        int displayDamage = (int)Math.Clamp(amount, 0m, 999999999m);
        bool temporaryHpBumpApplied = hpBefore == 1 && target.MaxHp > 1;
        if (temporaryHpBumpApplied)
        {
            target.SetCurrentHpInternal(2);
            amount = 1m;
        }
        else if (hpBefore == 1)
        {
            amount = 0m;
        }
        else
        {
            amount = hpBefore - 1;
        }

        token = new FinisherProtectionToken(
            this,
            target,
            temporaryHpBumpApplied,
            displayDamage);
        _activeProtections.Add(target, token);
        return true;
    }

    public bool Confirm(FinisherProtectionToken token, DamageResult result, bool originalRan)
    {
        if (!originalRan
            || !OwnsActiveToken(token)
            || !ReferenceEquals(result.Receiver, token.Target)
            || !_isCurrentContext()
            || !ReferenceEquals(token.Target.CombatState, _combatState))
        {
            return false;
        }

        _activeProtections.Remove(token.Target);
        DeferredDeaths.Add(token.Target);
        return true;
    }

    public void PresentProtectedDamage(FinisherProtectionToken token, DamageResult result)
    {
        if (!ReferenceEquals(token.Ledger, this))
        {
            throw new InvalidOperationException("A finisher protection token was presented by a different damage ledger.");
        }

        _presentProtectedDamage(result, token.DisplayDamage);
    }

    public void FinalizeProtection(FinisherProtectionToken token)
    {
        if (!OwnsActiveToken(token))
        {
            return;
        }

        _activeProtections.Remove(token.Target);
        if (ShouldRollbackTemporaryBump(token, _isCurrentContext()))
        {
            token.Target.SetCurrentHpInternal(1);
        }
    }

    public void ReleasePendingProtections(bool mayRestoreCurrentCombat)
    {
        List<FinisherProtectionToken> pending = _activeProtections.Values.ToList();
        _activeProtections.Clear();

        List<Exception>? failures = null;
        foreach (FinisherProtectionToken token in pending)
        {
            if (!ShouldRollbackTemporaryBump(token, mayRestoreCurrentCombat && _isCurrentContext()))
            {
                continue;
            }

            try
            {
                token.Target.SetCurrentHpInternal(1);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("One or more pending finisher HP bumps could not be restored.", failures);
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

    public void Clear(bool mayRestoreCurrentCombat)
    {
        try
        {
            ReleasePendingProtections(mayRestoreCurrentCombat);
        }
        finally
        {
            _damageDisplayOverrides.Clear();
        }
    }

    private bool OwnsActiveToken(FinisherProtectionToken token) =>
        ReferenceEquals(token.Ledger, this)
        && _activeProtections.TryGetValue(token.Target, out FinisherProtectionToken? current)
        && ReferenceEquals(current, token);

    private bool ShouldRollbackTemporaryBump(
        FinisherProtectionToken token,
        bool contextIsCurrent) =>
        contextIsCurrent
        && token.TemporaryHpBumpApplied
        && token.Target.CurrentHp == 2
        && ReferenceEquals(token.Target.CombatState, _combatState);

    private void RegisterProtectedDamageResult(DamageResult result, int displayDamage)
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
}
