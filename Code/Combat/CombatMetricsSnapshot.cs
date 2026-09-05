namespace NinjaSlayer.Code.Combat;

internal sealed class CombatMetricsSnapshot<TPlayer>
    where TPlayer : class
{
    private readonly Dictionary<TPlayer, PlayerMetrics> _players = new(ReferenceEqualityComparer.Instance);
    private int _turnRound;
    private int _turnSide;

    public CombatMetricsSnapshot(int turnRound, int turnSide)
    {
        _turnRound = turnRound;
        _turnSide = turnSide;
    }

    public void EnsureTurn(int round, int side)
    {
        if (_turnRound == round && _turnSide == side)
        {
            return;
        }

        _turnRound = round;
        _turnSide = side;
        foreach (PlayerMetrics metrics in _players.Values)
        {
            metrics.ResetTurn();
        }
    }

    public void MarkCardDiscarded(TPlayer player) => GetOrCreatePlayerMetrics(player).DiscardedCard = true;
    public void MarkHpLost(TPlayer player) => GetOrCreatePlayerMetrics(player).LostHp = true;

    public void AddFinishedCard(TPlayer player, bool isAttack, bool isSkill, bool isMelee)
    {
        PlayerMetrics metrics = GetOrCreatePlayerMetrics(player);
        metrics.PreviousFinishedWasAttack = isAttack;
        metrics.PreviousFinishedWasSkill = isSkill;
        if (isMelee)
        {
            metrics.MeleeAttacks++;
        }
    }

    public bool DiscardedCard(TPlayer player) => GetOrCreatePlayerMetrics(player).DiscardedCard;
    public bool LostHp(TPlayer player) => GetOrCreatePlayerMetrics(player).LostHp;
    public bool PreviousFinishedWasAttack(TPlayer player) => GetOrCreatePlayerMetrics(player).PreviousFinishedWasAttack;
    public bool PreviousFinishedWasSkill(TPlayer player) => GetOrCreatePlayerMetrics(player).PreviousFinishedWasSkill;
    public int MeleeAttacks(TPlayer player) => GetOrCreatePlayerMetrics(player).MeleeAttacks;

    private PlayerMetrics GetOrCreatePlayerMetrics(TPlayer player)
    {
        if (!_players.TryGetValue(player, out PlayerMetrics? metrics))
        {
            metrics = new PlayerMetrics();
            _players.Add(player, metrics);
        }

        return metrics;
    }

    private sealed class PlayerMetrics
    {
        public bool DiscardedCard { get; set; }
        public bool LostHp { get; set; }
        public bool PreviousFinishedWasAttack { get; set; }
        public bool PreviousFinishedWasSkill { get; set; }
        public int MeleeAttacks { get; set; }

        public void ResetTurn()
        {
            DiscardedCard = false;
            LostHp = false;
            MeleeAttacks = 0;
        }
    }
}
