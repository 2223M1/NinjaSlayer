"""Direct monster arithmetic for the review; no player deck or HP-loss model.

Run with Python 3. Prints reproducible turn ledgers as JSON. No files are written.
"""
from dataclasses import asdict, dataclass
import json

@dataclass
class Enemy:
    knife_base: int = 14
    bamboo_base: int = 2
    strength_gain: int = 3
    weapons: int = 2
    strength: int = 0
    forced_throw: bool = False

    def intent(self):
        if self.weapons == 0:
            return "bamboo", self.bamboo_base + self.strength, 4
        if self.weapons == 1 or self.forced_throw:
            return "throw", self.knife_base + self.strength, 1
        return "dual", self.knife_base + self.strength, 2

    def return_one(self):
        self.weapons = min(2, self.weapons + 1)

    def act(self):
        move, per_hit, hits = self.intent()
        generated = 0
        if move == "throw":
            self.weapons -= 1
            self.forced_throw = False
            self.strength += self.strength_gain
            generated = 1
        elif move == "dual":
            self.forced_throw = True
        return move, per_hit, hits, generated

POLICIES = ("never", "immediate_one", "empty_one", "empty_two", "once_t4")

def choose(policy, turn, enemy, hand_knives):
    if policy == "never":
        return 0
    if policy == "immediate_one":
        return int(hand_knives > 0)
    if policy == "empty_one":
        return int(enemy.weapons == 0 and hand_knives > 0)
    if policy == "empty_two":
        return min(2, hand_knives) if enemy.weapons == 0 else 0
    if policy == "once_t4":
        return int(turn == 4 and hand_knives > 0)
    raise ValueError(policy)

def timeline(policy, turns=12, knife_base=14, strength_gain=3, bamboo_base=2):
    enemy = Enemy(knife_base=knife_base, strength_gain=strength_gain, bamboo_base=bamboo_base)
    knives = 0
    rows = []
    for turn in range(1, turns + 1):
        before = asdict(enemy)
        knives_before = knives
        played = choose(policy, turn, enemy, knives)
        for _ in range(played):
            enemy.return_one()
            knives -= 1
        after_returns = asdict(enemy)
        move, per_hit, hits, generated = enemy.act()
        knives += generated
        rows.append(dict(turn=turn, before=before, knives_before=knives_before,
                         played=played, energy=played * 2, knife_damage=played * 12,
                         after_returns=after_returns, move=move, per_hit=per_hit,
                         hits=hits, incoming=per_hit * hits, after=asdict(enemy),
                         generated=generated, knives_after=knives, knives_exhausted=sum(r['played'] for r in rows)+played))
    return rows

def totals(rows):
    return dict(turns=len(rows), incoming=sum(r['incoming'] for r in rows),
                energy=sum(r['energy'] for r in rows), knife_damage=sum(r['knife_damage'] for r in rows))

def mecha(turns=12, a10=True):
    strength = 0
    rows = []
    for t in range(1, turns + 1):
        added_block = burns = 0
        old_strength = strength
        if t == 1:
            move, damage = 'charge', 30 if a10 else 25
        elif (t - 2) % 3 == 0:
            move, damage, burns = 'flame', (12 if a10 else 8) + strength, 4
        elif (t - 2) % 3 == 1:
            move, damage, added_block = 'windup', 0, 15
            strength += 5
        else:
            move, damage = 'heavy', (40 if a10 else 35) + strength
        rows.append(dict(turn=t, move=move, incoming=damage, strength_before=old_strength,
                         strength_after=strength, block_gained=added_block, burns_generated=burns))
    return rows

def verify():
    expected={
        'never':[28,14,17,32,32,32,32,32],
        'immediate_one':[28,14,34,17,40,20,46,23],
        'empty_one':[28,14,17,20,23,26,29,32],
        'empty_two':[28,14,17,40,20,23,52,26],
        'once_t4':[28,14,17,20,44,44,44,44]}
    for policy, values in expected.items():
        assert [r['incoming'] for r in timeline(policy,8)] == values
    assert [r['incoming'] for r in mecha(8)] == [30,12,0,45,17,0,50,22]
    pending=Enemy(weapons=1,strength=9,forced_throw=True)
    pending.return_one()
    assert pending.intent()==('throw',23,1)
    pending.return_one()
    assert pending.weapons==2 and pending.forced_throw
    for base in (12,14):
        for policy in POLICIES:
            for row in timeline(policy,knife_base=base):
                assert 0<=row['after']['weapons']<=2
                assert row['knives_after']>=0
                assert row['before']['weapons']+row['knives_before']==2
                assert row['after']['weapons']+row['knives_after']==2
    assert totals(timeline('never'))['incoming']==347
    assert totals(timeline('empty_one'))['incoming']==347

if __name__=='__main__':
    verify()
    result={'validation':'passed','scope':'enemy attack arithmetic only; no player deck, mitigation, kill turn, or empirical HP loss',
            'assumptions':'enemies and player survive the full horizon; knife costs and damage unmodified; no copied knives; no card cycling needed because generated knives fit in hand'}
    result['timelines']={str(base):{p:timeline(p,knife_base=base) for p in POLICIES} for base in (12,14)}
    result['horizon_totals']={str(base):{p:{str(t):totals(timeline(p,t,knife_base=base)) for t in (4,6,8,10,12)} for p in POLICIES} for base in (12,14)}
    result['mecha']={level:mecha(a10=(level=='A10')) for level in ('A0','A10')}
    result['strength_sensitivity']={str(g):{p:totals(timeline(p,8,strength_gain=g)) for p in ('never','empty_one')} for g in (1,2,3)}
    result['bamboo_sensitivity']={str(b):{p:totals(timeline(p,8,bamboo_base=b)) for p in ('never','empty_one')} for b in (2,3,4)}
    print(json.dumps(result,ensure_ascii=False,indent=2))
