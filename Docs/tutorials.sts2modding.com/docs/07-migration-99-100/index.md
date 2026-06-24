<!-- Source: https://tutorials.sts2modding.com/docs/07-migration-99-100/ -->
<!-- Synced: 2026-06-17 14:40:26 +08:00 -->

# 正式版 至 测试版

[2026年03月27日]()[647 字]()[大概 3 分钟]()[Reme]()

## 0.105测试版 至 0.106测试版

## 变量变动

-
新增枚举`HpLossHookPhase`，用于`ModifyHpLost`。

-
`ModifyDamageHookType`新增`ModifyDamageHookType.Cap`，用于`ModifyDamageCap`接口。

## 函数变动

### AbstractModel

涉及一些函数改名和参数新增。

| 0.105 | 0.106

| `BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, ICombatState combatState)` | `BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)`

| `AfterSideTurnStart(CombatSide side, ICombatState combatState)` | `AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)`

| `BeforeTurnEndVeryEarly(PlayerChoiceContext choiceContext, CombatSide side)` | `BeforeSideTurnEndVeryEarly(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)`

| `BeforeTurnEndEarly(PlayerChoiceContext choiceContext, CombatSide side)` | `BeforeSideTurnEndEarly(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)`

| `BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)` | `BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)`

| `AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)` | `AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)`

| `AfterTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side)` | `AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)`

| `AfterCardChangedPiles(..., AbstractModel? source)` | `AfterCardChangedPiles(..., AbstractModel? clonedBy)`

| `AfterCardChangedPilesLate(..., AbstractModel? source)` | `AfterCardChangedPilesLate(..., AbstractModel? clonedBy)`

此外`ModifyHpLost`一些函数进行了合并，新增枚举`HpLossHookPhase`。

之前的`ModifyHpLostBeforeOsty`相当于传入参数`HpLossHookPhase.BeforeOsty`，afterosty以此类推。

| 0.105 | 0.106

| `ModifyHpLostBeforeOsty(..., out IEnumerable<AbstractModel> modifiers)` | `ModifyHpLost(..., HpLossHookPhase phases, out IEnumerable<AbstractModel> modifiers)`

| `ModifyHpLostAfterOsty(..., out IEnumerable<AbstractModel> modifiers)` | `ModifyHpLost(..., HpLossHookPhase phases, out IEnumerable<AbstractModel> modifiers)`

### CardPileCmd

| 0.105 | 0.106

| `Task AddCurseToDeck<T>(Player owner)` | `Task<CardModel?> AddCurseToDeck<T>(Player owner)`

| `Task AddCursesToDeck(IEnumerable<CardModel> curses, Player owner)` | `Task<IEnumerable<CardPileAddResult>> AddCursesToDeck(...)`

| `Add(..., AbstractModel? source = null, ...)` | `Add(..., AbstractModel? clonedBy = null, ...)`

### CardSelectCmd

新增函数`FromCombatPile`。

### OrbCmd

`IncreaseBaseOrbCount`改名为`AddSlots`。

## 0.103 至 0.105测试版

### manifest json变动

-
添加了`min_game_version`字段，必填。

-
依赖mod写法变动，查看`环境配置`或者两个基础库的第0章。

### 变量变动

-
`bool ShowsInfiniteHp`改成了`HpDisplay`枚举。

-
`bool IsInstanced`改成了`PowerInstanceType`枚举。

### 函数变动

-
一些函数开始传入`PlayerChoiceContext`参数，与下面的进行配合。

-
一些效果执行函数，例如`PowerCmd.Apply`等，需要一个`PlayerChoiceContext`参数。如果你的函数传入参数有对应类型添加即可。如果你找不到这个类型的参数，传入`new ThrowingPlayerChoiceContext()`。

-
`CardPileCmd.AddGeneratedCardToCombat`等，之前传入`addedByPlayer`的`bool`类型的参数的位置，改成了`Player? creator`。所以如果之前是`false`的现在填`null`，是`true`的话填`cardPlay.card.Owner`或者`Owner`，根据语境。

-
`OnTurnEndInHand`从`public virtual`改为`protected virtual`。

-
`GetResultPileType`改名为`GetResultPileTypeForCardPlay`。新增`GetResultPileTypeForOnTurnEndInHandEffect`。

旧版 `AbstractModel` 里有：

- `BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)`
- `BeforePlayPhaseStartLate(PlayerChoiceContext choiceContext, Player player)`

0.104 版这两个点被移除了，换成了：

- `AfterAutoPrePlayPhaseEnteredEarly(PlayerChoiceContext choiceContext, Player player)`
- `AfterAutoPrePlayPhaseEntered(PlayerChoiceContext choiceContext, Player player)`
- `AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext choiceContext, Player player)`
- `AfterAutoPostPlayPhaseEntered(PlayerChoiceContext choiceContext, Player player)`

### 接口变动

- 一些参数的类型从`CombatState`改成了`ICombatState`。

## 0.99 至 0.103

主要是能量表盘问题。

结构从：

```plaintext
TestEnergyCounter (Control)
├── BurstBack (CPUParticles2D) %
├── Layers (Control) %
│   ├── Layer1 (TextureRect，或任意)
│   └── RotationLayers (Control) %
├── BurstFront (CPUParticles2D) %
└── Label (Label)
```

改成了：

```plaintext
TestEnergyCounter (Control)
├── EnergyVfxBack (NParticlesContainer) %
├── Layers (Control) %
│   ├── Layer1 (TextureRect，或任意)
│   └── RotationLayers (Control) %
├── EnergyVfxFront (NParticlesContainer) %
└── Label (Label)
```

所以如果你在正式版添加人物，需要添加`BurstBack (CPUParticles2D) %`和`BurstFront (CPUParticles2D) %`这两个节点。
