using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherForecastFrameKey : IEquatable<FinisherForecastFrameKey>
{
    private readonly Creature _owner;
    private readonly CardModel _card;
    private readonly CardPlay _cardPlay;
    private readonly AttackCommand? _command;
    private readonly Creature? _singleTarget;
    private readonly Creature[] _fixedTargets;
    private readonly EnemySnapshot[] _enemies;
    private readonly EffectSnapshot[] _effects;
    private readonly ValueProp _props;
    private readonly FinisherTargeting _targeting;
    private readonly decimal? _narakuHpLoss;
    private readonly int _hits;
    private readonly int _hashCode;

    public FinisherForecastFrameKey(
        Creature owner,
        FinisherAttackSpec spec,
        AttackCommand? command,
        IReadOnlyList<Creature> enemies,
        IReadOnlyList<decimal> damageByTarget,
        decimal? narakuHpLoss,
        int hits,
        Creature? singleTarget,
        IReadOnlyList<FinisherForecastEffect> effects)
    {
        FinisherForecastDescriptor descriptor = spec.Forecast;
        _owner = owner;
        _card = spec.Card;
        _cardPlay = spec.CardPlay;
        _command = command;
        _singleTarget = singleTarget;
        _fixedTargets = descriptor.FixedTargets?.ToArray() ?? [];
        _props = descriptor.Props;
        _targeting = descriptor.Targeting;
        _narakuHpLoss = narakuHpLoss;
        _hits = hits;
        _enemies = enemies.Select((enemy, index) => new EnemySnapshot(
            enemy,
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>(),
            enemy.GetPowerAmount<VulnerablePower>(),
            damageByTarget[index])).ToArray();
        _effects = effects.Select(effect => new EffectSnapshot(effect)).ToArray();

        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(_owner));
        hash.Add(RuntimeHelpers.GetHashCode(_card));
        hash.Add(RuntimeHelpers.GetHashCode(_cardPlay));
        hash.Add(ReferenceHashCode(_command));
        hash.Add(ReferenceHashCode(_singleTarget));
        hash.Add(_props);
        hash.Add(_targeting);
        hash.Add(_narakuHpLoss);
        hash.Add(_hits);
        AddReferenceSequenceHash(ref hash, _fixedTargets);
        foreach (EnemySnapshot enemy in _enemies)
        {
            enemy.AddHash(ref hash);
        }

        foreach (EffectSnapshot effect in _effects)
        {
            effect.AddHash(ref hash);
        }

        _hashCode = hash.ToHashCode();
    }

    public bool Equals(FinisherForecastFrameKey? other) =>
        other is not null
        && ReferenceEquals(_owner, other._owner)
        && ReferenceEquals(_card, other._card)
        && ReferenceEquals(_cardPlay, other._cardPlay)
        && ReferenceEquals(_command, other._command)
        && ReferenceEquals(_singleTarget, other._singleTarget)
        && _props == other._props
        && _targeting == other._targeting
        && _narakuHpLoss == other._narakuHpLoss
        && _hits == other._hits
        && ReferenceSequenceEqual(_fixedTargets, other._fixedTargets)
        && _enemies.AsSpan().SequenceEqual(other._enemies)
        && _effects.AsSpan().SequenceEqual(other._effects);

    public override bool Equals(object? obj) => Equals(obj as FinisherForecastFrameKey);

    public override int GetHashCode() => _hashCode;

    private static bool ReferenceSequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : class
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddReferenceSequenceHash<T>(ref HashCode hash, IReadOnlyList<T> values)
        where T : class
    {
        foreach (T value in values)
        {
            hash.Add(RuntimeHelpers.GetHashCode(value));
        }
    }

    private static int ReferenceHashCode(object? value) =>
        value is null ? 0 : RuntimeHelpers.GetHashCode(value);

    private readonly struct EnemySnapshot : IEquatable<EnemySnapshot>
    {
        private readonly Creature _enemy;
        private readonly int _hp;
        private readonly int _block;
        private readonly int _karate;
        private readonly int _vulnerable;
        private readonly decimal _damage;

        public EnemySnapshot(
            Creature enemy,
            int hp,
            int block,
            int karate,
            int vulnerable,
            decimal damage)
        {
            _enemy = enemy;
            _hp = hp;
            _block = block;
            _karate = karate;
            _vulnerable = vulnerable;
            _damage = damage;
        }

        public bool Equals(EnemySnapshot other) =>
            ReferenceEquals(_enemy, other._enemy)
            && _hp == other._hp
            && _block == other._block
            && _karate == other._karate
            && _vulnerable == other._vulnerable
            && _damage == other._damage;

        public override bool Equals(object? obj) => obj is EnemySnapshot other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            RuntimeHelpers.GetHashCode(_enemy),
            _hp,
            _block,
            _karate,
            _vulnerable,
            _damage);

        public void AddHash(ref HashCode hash)
        {
            hash.Add(RuntimeHelpers.GetHashCode(_enemy));
            hash.Add(_hp);
            hash.Add(_block);
            hash.Add(_karate);
            hash.Add(_vulnerable);
            hash.Add(_damage);
        }
    }

    private readonly struct EffectSnapshot : IEquatable<EffectSnapshot>
    {
        private readonly decimal _amount;
        private readonly ValueProp _props;
        private readonly Creature? _dealer;
        private readonly CardModel? _cardSource;
        private readonly CardPlay? _cardPlay;
        private readonly FinisherForecastEffectTargeting _targeting;

        public EffectSnapshot(FinisherForecastEffect effect)
        {
            _amount = effect.Amount;
            _props = effect.Props;
            _dealer = effect.Dealer;
            _cardSource = effect.CardSource;
            _cardPlay = effect.CardPlay;
            _targeting = effect.Targeting;
        }

        public bool Equals(EffectSnapshot other) =>
            _amount == other._amount
            && _props == other._props
            && ReferenceEquals(_dealer, other._dealer)
            && ReferenceEquals(_cardSource, other._cardSource)
            && ReferenceEquals(_cardPlay, other._cardPlay)
            && _targeting == other._targeting;

        public override bool Equals(object? obj) => obj is EffectSnapshot other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            _amount,
            _props,
            ReferenceHashCode(_dealer),
            ReferenceHashCode(_cardSource),
            ReferenceHashCode(_cardPlay),
            _targeting);

        public void AddHash(ref HashCode hash)
        {
            hash.Add(_amount);
            hash.Add(_props);
            hash.Add(ReferenceHashCode(_dealer));
            hash.Add(ReferenceHashCode(_cardSource));
            hash.Add(ReferenceHashCode(_cardPlay));
            hash.Add(_targeting);
        }
    }
}
