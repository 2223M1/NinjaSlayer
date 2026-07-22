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

    public void AddGeneratedChado(TPlayer player) => Get(player).GeneratedChado++;
    public void MarkChadoDiscarded(TPlayer player) => Get(player).ChadoDiscarded = true;
    public void MarkChadoExhausted(TPlayer player) => Get(player).ChadoExhausted = true;
    public void MarkHpLost(TPlayer player) => Get(player).LostHp = true;

    public void AddFinishedCard(TPlayer player, bool isAttack, bool isMelee)
    {
        PlayerMetrics metrics = Get(player);
        metrics.PreviousFinishedWasAttack = isAttack;
        if (isMelee)
        {
            metrics.MeleeAttacks++;
        }
    }

    public int GeneratedChado(TPlayer player) => Get(player).GeneratedChado;
    public bool ChadoDiscarded(TPlayer player) => Get(player).ChadoDiscarded;
    public bool ChadoExhausted(TPlayer player) => Get(player).ChadoExhausted;
    public bool LostHp(TPlayer player) => Get(player).LostHp;
    public bool PreviousFinishedWasAttack(TPlayer player) => Get(player).PreviousFinishedWasAttack;
    public int MeleeAttacks(TPlayer player) => Get(player).MeleeAttacks;

    private PlayerMetrics Get(TPlayer player)
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
        public int GeneratedChado { get; set; }
        public bool ChadoDiscarded { get; set; }
        public bool ChadoExhausted { get; set; }
        public bool LostHp { get; set; }
        public bool PreviousFinishedWasAttack { get; set; }
        public int MeleeAttacks { get; set; }

        public void ResetTurn()
        {
            ChadoDiscarded = false;
            ChadoExhausted = false;
            LostHp = false;
            MeleeAttacks = 0;
        }
    }
}
