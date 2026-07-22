using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed class FinisherProtectionToken
{
    private readonly FinisherProtectionProtocol _protocol;

    internal FinisherProtectionToken(
        FinisherDamageLedger ledger,
        long sessionId,
        long combatEpoch,
        long protectionSequence,
        ICombatState combatState,
        Creature target,
        decimal requestedDamage,
        int hpBefore,
        bool temporaryHpBumpApplied,
        int displayDamage)
    {
        _protocol = new FinisherProtectionProtocol(
            sessionId,
            combatEpoch,
            protectionSequence,
            hpBefore,
            temporaryHpBumpApplied);
        Ledger = ledger;
        CombatState = combatState;
        Target = target;
        RequestedDamage = requestedDamage;
        HpBefore = hpBefore;
        TemporaryHpBumpApplied = temporaryHpBumpApplied;
        DisplayDamage = displayDamage;
    }

    internal FinisherDamageLedger Ledger { get; }
    internal ICombatState CombatState { get; }
    internal Creature Target { get; }
    internal decimal RequestedDamage { get; }
    internal int HpBefore { get; }
    internal bool TemporaryHpBumpApplied { get; }
    internal int DisplayDamage { get; }
    internal DamageResult? Result { get; private set; }

    public long SessionId => _protocol.SessionId;
    public long CombatEpoch => _protocol.CombatEpoch;
    public long ProtectionSequence => _protocol.ProtectionSequence;
    public bool IsConfirmed => _protocol.IsConfirmed;
    public bool IsReleased => _protocol.IsReleased;

    internal bool TryConfirm(DamageResult result)
    {
        if (!_protocol.TryConfirm())
        {
            return false;
        }

        Result = result;
        return true;
    }

    internal bool TryReleaseAndShouldRollback(bool contextIsCurrent) =>
        _protocol.TryReleaseAndShouldRollback(
            Ledger.SessionId,
            Ledger.CombatEpoch,
            Target.CurrentHp,
            contextIsCurrent);
}

internal sealed class FinisherDamageLedger
{
    private readonly object _sync = new();
    private readonly Dictionary<DamageResult, int> _damageDisplayOverrides =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Creature, FinisherProtectionToken> _activeProtections =
        new(ReferenceEqualityComparer.Instance);
    private readonly ICombatState _combatState;
    private readonly Func<bool> _isCurrentContext;
    private readonly Action<DamageResult, int> _presentProtectedDamage;
    private long _nextProtectionSequence;

    public FinisherDamageLedger(
        IEnumerable<Creature> victims,
        long sessionId,
        long combatEpoch,
        ICombatState combatState,
        Func<bool> isCurrentContext,
        Action<DamageResult, int>? presentProtectedDamage = null)
    {
        Victims = victims.ToHashSet();
        SessionId = sessionId;
        CombatEpoch = combatEpoch;
        _combatState = combatState;
        _isCurrentContext = isCurrentContext;
        _presentProtectedDamage = presentProtectedDamage ?? RegisterProtectedDamageResult;
    }

    public long SessionId { get; }
    public long CombatEpoch { get; }
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

        lock (_sync)
        {
            if (_activeProtections.ContainsKey(target))
            {
                return false;
            }

            int hpBefore = target.CurrentHp;
            decimal requestedDamage = amount;
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
                SessionId,
                CombatEpoch,
                ++_nextProtectionSequence,
                _combatState,
                target,
                requestedDamage,
                hpBefore,
                temporaryHpBumpApplied,
                displayDamage);
            _activeProtections.Add(target, token);
            return true;
        }
    }

    public void Confirm(FinisherProtectionToken token, DamageResult result, bool originalRan)
    {
        bool confirmed = false;
        lock (_sync)
        {
            if (!originalRan
                || !OwnsActiveToken(token)
                || !ReferenceEquals(result.Receiver, token.Target)
                || !_isCurrentContext()
                || !ReferenceEquals(token.Target.CombatState, _combatState))
            {
                return;
            }

            confirmed = token.TryConfirm(result);
            if (confirmed)
            {
                _activeProtections.Remove(token.Target);
                DeferredDeaths.Add(token.Target);
            }
        }

        if (confirmed)
        {
            _presentProtectedDamage(result, token.DisplayDamage);
        }
    }

    public void FinalizeProtection(FinisherProtectionToken token)
    {
        bool shouldRollbackBump = false;
        lock (_sync)
        {
            if (token.IsConfirmed || !OwnsActiveToken(token))
            {
                return;
            }

            _activeProtections.Remove(token.Target);
            shouldRollbackBump = token.TryReleaseAndShouldRollback(
                TokenStillBelongsToCurrentCombat(token));
        }

        if (shouldRollbackBump)
        {
            token.Target.SetCurrentHpInternal(1);
        }
    }

    public void ReleasePendingProtections(bool mayRestoreCurrentCombat)
    {
        List<FinisherProtectionToken> pending;
        lock (_sync)
        {
            pending = _activeProtections.Values.ToList();
            _activeProtections.Clear();
        }

        List<Exception>? failures = null;
        foreach (FinisherProtectionToken token in pending)
        {
            if (!token.TryReleaseAndShouldRollback(
                    mayRestoreCurrentCombat && TokenStillBelongsToCurrentCombat(token)))
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
        lock (_sync)
        {
            if (_damageDisplayOverrides.Remove(result, out displayDamage))
            {
                return true;
            }
        }

        displayDamage = 0;
        return false;
    }

    public bool GuaranteedClearMatchedRuntime()
    {
        lock (_sync)
        {
            return Victims.All(victim => victim.IsDead || DeferredDeaths.Contains(victim));
        }
    }

    public List<Creature> LivingDeferredDeaths()
    {
        lock (_sync)
        {
            return DeferredDeaths.Where(creature => creature.IsAlive).ToList();
        }
    }

    public void Clear(bool mayRestoreCurrentCombat)
    {
        Exception? releaseFailure = null;
        try
        {
            ReleasePendingProtections(mayRestoreCurrentCombat);
        }
        catch (Exception ex)
        {
            releaseFailure = ex;
        }

        lock (_sync)
        {
            _damageDisplayOverrides.Clear();
        }

        if (releaseFailure != null)
        {
            throw releaseFailure;
        }
    }

    private bool OwnsActiveToken(FinisherProtectionToken token) =>
        token.SessionId == SessionId
        && token.CombatEpoch == CombatEpoch
        && ReferenceEquals(token.Ledger, this)
        && _activeProtections.TryGetValue(token.Target, out FinisherProtectionToken? current)
        && ReferenceEquals(current, token)
        && current.ProtectionSequence == token.ProtectionSequence;

    private bool TokenStillBelongsToCurrentCombat(FinisherProtectionToken token) =>
        token.SessionId == SessionId
        && token.CombatEpoch == CombatEpoch
        && ReferenceEquals(token.CombatState, _combatState)
        && ReferenceEquals(token.Target.CombatState, _combatState)
        && _isCurrentContext();

    private void RegisterProtectedDamageResult(DamageResult result, int displayDamage)
    {
        if (displayDamage <= 0 || !Victims.Contains(result.Receiver))
        {
            return;
        }

        if (result.UnblockedDamage + result.OverkillDamage > 0)
        {
            lock (_sync)
            {
                _damageDisplayOverrides[result] = displayDamage;
            }
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
