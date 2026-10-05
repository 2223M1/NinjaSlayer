using AutoAnthony;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using NinjaSlayer.Content;

namespace NinjaSlayer.AutoAnthony;

// This optional assembly calls the same resource commands as the ordinary cards.
internal sealed class NinjaComponentRuntime : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        var card = context.Card;
        var owner = card.Owner;
        var choice = context.ChoiceContext;
        var primary = context.RuntimeSpec.Values[0];
        int amount = primary.Upgradable ? context.Amount : primary.BaseValue + primary.Offset;
        int Value(string id)
        {
            var slot = context.RuntimeSpec.Values.Single(v => v.Id == id);
            return slot.BaseValue + slot.Offset;
        }
        if (NinjaComponentSources.PowerTypes.TryGetValue(context.RuntimeSpec.Variant, out Type? power))
        {
            await PowerCmd.Apply(choice, (PowerModel)ModelDb.GetById<PowerModel>(ModelDb.GetId(power)).ToMutable(),
                owner.Creature, amount, owner.Creature, card);
            return true;
        }
        switch (context.RuntimeSpec.Variant)
        {
            case "sip_tea":
                if (amount > 0) await ChadoBreathCmd.Apply(choice, owner, amount);
                await PowerCmd.Apply<SipPower>(choice, owner.Creature, Value("turns"), owner.Creature, card);
                break;
            // These are intrinsic card rules; NinjaComponentCardPatches uses native card hooks.
            case "return_first":
            case "return_attacks":
            case "play_on_top":
                break;
            case "fill_hand":
                context.ReplaceDrawnCards(await CardPileCmd.Draw(choice,
                    Math.Max(0, CardPile.MaxCardsInHand - PileType.Hand.GetPile(owner).Cards.Count), owner));
                break;
            case "draw_if_tea":
                if (PileType.Hand.GetPile(owner).Cards.OfType<Chado>().Any())
                    context.ReplaceDrawnCards(await CardPileCmd.Draw(choice, amount, owner));
                break;
            case "breath_after_discard":
                if (NinjaSlayerCombatMetrics.DiscardedCardThisTurn(owner))
                    await ChadoBreathCmd.Apply(choice, owner, amount);
                break;
            case "vulnerable_after_attack":
                if (context.Target is { IsAlive: true } vulnerable && NinjaSlayerCombatMetrics.PreviousFinishedCardWasAttack(owner))
                    await PowerCmd.Apply<VulnerablePower>(choice, vulnerable, amount, owner.Creature, card);
                break;
            case "weak_after_skill":
                if (context.Target is { IsAlive: true } weak && NinjaSlayerCombatMetrics.PreviousFinishedCardWasSkill(owner))
                    await PowerCmd.Apply<WeakPower>(choice, weak, amount, owner.Creature, card);
                break;
            case "hook_strength":
                if (context.Target is { IsAlive: true } hooked && amount > 0)
                    await PowerCmd.Apply<GrapplingHookStrengthDownPower>(choice, hooked, amount, owner.Creature, card);
                break;
            case "enemy_karate":
                if (owner.RunState.Rng.CombatTargets.NextItem(card.CombatState!.HittableEnemies) is { } enemy)
                    await PowerCmd.Apply<KaratePower>(choice, enemy, amount, owner.Creature, card);
                break;
            case "fire_stock":
                if (ShurikenOrb.Find(owner) is { } volley) await volley.FireConsumedVolley(choice, amount, card);
                break;
            case "double_stock":
                if (ShurikenOrb.Find(owner) is { } stock) await ShurikenOrb.AddStock(choice, owner, stock.StackCount);
                break;
            case "discard_stock":
                var hand = PileType.Hand.GetPile(owner).Cards.ToArray();
                await CardCmd.Discard(choice, hand);
                await ShurikenOrb.AddStock(choice, owner, hand.Length);
                break;
            case "blade_cycle":
                if (owner.Creature.GetPower<BladeCyclePower>() is not { } cycle)
                    await PowerCmd.Apply<BladeCyclePower>(choice, owner.Creature, amount, owner.Creature, card);
                else if (amount < cycle.Amount)
                    await PowerCmd.ModifyAmount(choice, cycle, amount - cycle.Amount, owner.Creature, card);
                break;
            case "thorns":
                await PowerCmd.Apply<ThornsPower>(choice, owner.Creature, amount, owner.Creature, card);
                break;
            case "copy_hand_top":
                var copies = (await CardSelectCmd.FromHand(choice, owner,
                    new CardSelectorPrefs(new MegaCrit.Sts2.Core.Localization.LocString("cards", ModelDb.Card<FurinKazan>().Id.Entry + ".selectionScreenPrompt"), amount), c => c != card, card)).ToArray();
                foreach (var copy in copies.Reverse())
                    await CardPileCmd.AddGeneratedCardToCombat(copy.CreateClone(), PileType.Draw, owner, CardPilePosition.Top);
                break;
            case "transform_flame":
                var toTransform = await CardSelectCmd.FromHand(choice, owner,
                    new CardSelectorPrefs(new MegaCrit.Sts2.Core.Localization.LocString("cards", ModelDb.Card<Rekindle>().Id.Entry + ".selectionScreenPrompt"), 1), c => c != card && c.IsTransformable, card);
                foreach (var transformed in toTransform) await CardCmd.TransformTo<BlackFlame>(transformed);
                break;
            case "play_statuses":
                var statuses = new[] { PileType.Draw, PileType.Hand, PileType.Discard }.SelectMany(p => p.GetPile(owner).Cards)
                    .Where(c => c.Type == CardType.Status).ToArray();
                foreach (var status in statuses)
                {
                    if (!status.Keywords.Contains(CardKeyword.Unplayable)) await CardCmd.AutoPlay(choice, status, null);
                    if (status.Pile is { Type: not PileType.Exhaust }) await CardCmd.Exhaust(choice, status);
                }
                break;
            case "play_top_exhaust":
                if (PileType.Draw.GetPile(owner).Cards.FirstOrDefault() is { } topCard)
                {
                    if (topCard.Type == CardType.Attack && amount > 1)
                    {
                        var duplication = (NavyHammerPower)ModelDb.Power<NavyHammerPower>().ToMutable();
                        duplication.Arm(topCard);
                        await PowerCmd.Apply(choice, duplication, owner.Creature, amount - 1, owner.Creature, card);
                    }
                    topCard.ExhaustOnNextPlay = true;
                    await CardCmd.AutoPlay(choice, topCard, null);
                    if (topCard.Pile is { Type: not PileType.Exhaust }) await CardCmd.Exhaust(choice, topCard);
                }
                break;
            case "tea_heal":
            case "tea_karate":
            case "tea_block_weak":
                var tea = (await CardSelectCmd.FromHand(choice, owner,
                    new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
                    c => c is Chado, card)).FirstOrDefault();
                if (tea is null) break;
                decimal energy = tea.DynamicVars.Energy.BaseValue;
                await CardCmd.Exhaust(choice, tea);
                if (tea.Pile?.Type != PileType.Exhaust) break;
                if (context.RuntimeSpec.Variant == "tea_heal") await CreatureCmd.Heal(owner.Creature, amount);
                else if (context.RuntimeSpec.Variant == "tea_karate")
                    await PowerCmd.Apply<KaratePower>(choice, owner.Creature, energy * amount, owner.Creature, card);
                else
                {
                    await CreatureCmd.GainBlock(owner.Creature, amount, ValueProp.Move, context.CardPlay);
                    if (context.Target is { IsAlive: true } thrown)
                        await PowerCmd.Apply<WeakPower>(choice, thrown, Value("weak"), owner.Creature, card);
                }
                break;
            case "dazed_draw":
                for (int i = 0; i < amount; i++) await NinjaSlayerCardCmd.AddGeneratedCard<Dazed>(owner, PileType.Draw);
                break;
            case "karate_damage":
                var karateAttack = DamageCmd.Attack(amount * owner.Creature.GetPowerAmount<KaratePower>())
                    .FromCard(card, context.CardPlay).Targeting(context.Target!);
                await karateAttack.Execute(choice);
                context.RecordDamageDealt((int)karateAttack.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "buff_hits":
                int hits = 1 + owner.Creature.Powers.Where(p => p.Amount > 0 && p.TypeForCurrentAmount == PowerType.Buff
                    && p is StrengthPower or DexterityPower or VigorPower or KaratePower or FocusPower
                        or ArtifactPower or BufferPower or IntangiblePower or ThornsPower or PlatingPower or RegenPower or EvasionPower)
                    .Select(p => p.Id).Distinct().Count();
                var random = DamageCmd.Attack(amount).FromCard(card, context.CardPlay).WithHitCount(hits).TargetingRandomOpponents(card.CombatState!);
                await random.Execute(choice);
                context.RecordDamageDealt((int)random.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "tea_exhaust_damage":
                var teaAttack = DamageCmd.Attack(amount + Value("bonus") * PileType.Exhaust.GetPile(owner).Cards.OfType<Chado>().Count())
                    .FromCard(card, context.CardPlay).WithHitCount(Value("hits")).Targeting(context.Target!);
                await teaAttack.Execute(choice);
                context.RecordDamageDealt((int)teaAttack.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "tea_area_damage":
                var kickTea = (await CardSelectCmd.FromHand(choice, owner,
                    new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
                    c => c is Chado, card)).FirstOrDefault();
                if (kickTea == null) break;
                await CardCmd.Exhaust(choice, kickTea);
                if (kickTea.Pile?.Type != PileType.Exhaust) break;
                var kick = DamageCmd.Attack(amount).FromCard(card, context.CardPlay).TargetingAllOpponents(card.CombatState!);
                await kick.Execute(choice);
                context.RecordDamageDealt((int)kick.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                NinjaComponentCardPatches.DoubleDamageThisTurn(card);
                break;
            case "target_multi":
                var targeted = DamageCmd.Attack(amount).FromCard(card, context.CardPlay).WithHitCount(Value("hits"))
                    .Targeting(context.Target!);
                await targeted.Execute(choice);
                context.RecordDamageDealt((int)targeted.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "area_multi":
                var multi = DamageCmd.Attack(amount).FromCard(card, context.CardPlay).WithHitCount(Value("hits"))
                    .TargetingAllOpponents(card.CombatState!);
                await multi.Execute(choice);
                context.RecordDamageDealt((int)multi.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "area_x":
                int repeats = card.ResolveEnergyXValue();
                if (repeats >= 4) repeats *= 2;
                var area = DamageCmd.Attack(amount).FromCard(card, context.CardPlay).WithHitCount(repeats)
                    .TargetingAllOpponents(card.CombatState!);
                await area.Execute(choice);
                context.RecordDamageDealt((int)area.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage));
                break;
            case "breath_next_x":
                await PowerCmd.Apply<ChadoNextTurnPower>(choice, owner.Creature, card.ResolveEnergyXValue() * amount + Value("extra"), owner.Creature, card);
                break;
            case "scry_block":
                var scry = await ScryCmd.Execute(choice, owner, amount);
                int block = Value("block");
                for (int i = 0; i < scry.Discarded; i++)
                    await CreatureCmd.GainBlock(owner.Creature, block, ValueProp.Move, context.CardPlay);
                break;
            case "scry_exhaust_breath":
                var exhausted = await ScryCmd.Execute(choice, owner, amount, exhaustDiscarded: true);
                await ChadoBreathCmd.Apply(choice, owner, exhausted.ExhaustedCards);
                break;
            case "block_after_stock":
                if (ShurikenOrb.HasGainedThisTurn(owner))
                    await CreatureCmd.GainBlock(owner.Creature, amount, ValueProp.Move, context.CardPlay);
                break;
            case "double_karate":
                await PowerCmd.Apply<KaratePower>(choice, owner.Creature, owner.Creature.GetPowerAmount<KaratePower>(), owner.Creature, card);
                break;
            case "karate_to_plating":
                if (owner.Creature.GetPower<KaratePower>() is { } karate)
                {
                    int plating = karate.Amount;
                    await PowerCmd.Remove(karate);
                    await PowerCmd.Apply<PlatingPower>(choice, owner.Creature, plating, owner.Creature, card);
                }
                break;
            case "topdeck_self":
                if (!card.Keywords.Contains(CardKeyword.Exhaust) && !card.ExhaustOnNextPlay)
                    await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Top);
                break;
            case "exhaust_top":
                if (PileType.Draw.GetPile(owner).Cards.FirstOrDefault() is { } top)
                    await CardCmd.Exhaust(choice, top);
                break;
            case "exhaust_to_top":
                var selected = await CardSelectCmd.FromSimpleGrid(choice, PileType.Exhaust.GetPile(owner).Cards,
                    owner, new CardSelectorPrefs(new MegaCrit.Sts2.Core.Localization.LocString("cards", ModelDb.Card<Recover>().Id.Entry + ".selectionScreenPrompt"), amount));
                await CardPileCmd.Add(selected, PileType.Draw, CardPilePosition.Top);
                break;
            case "karate":
                await PowerCmd.Apply<KaratePower>(choice, owner.Creature, amount, owner.Creature, card);
                break;
            case "breath":
                await ChadoBreathCmd.Apply(choice, owner, amount);
                break;
            case "shuriken":
                await ShurikenOrb.AddStock(choice, owner, amount);
                break;
            case "naraku":
                await PowerCmd.Apply<NarakuLifePower>(choice, owner.Creature, amount, owner.Creature, card);
                break;
            case "scry":
                await ScryCmd.Execute(choice, owner, amount);
                break;
            case "scry_exhaust":
                await ScryCmd.Execute(choice, owner, amount, exhaustDiscarded: true);
                break;
            case "chado_retain":
                await PowerCmd.Apply<RetainChadoPower>(choice, owner.Creature, amount, owner.Creature, card);
                break;
            case "generate_strike_strike":
                var generatedStrike = owner.Creature.CombatState!.CreateCard<StrikeStrike>(owner);
                if (card.IsUpgraded) CardCmd.Upgrade(generatedStrike);
                await CardPileCmd.AddGeneratedCardToCombat(generatedStrike, PileType.Hand, owner);
                break;
            case "draw_discard_nonattack":
            case "draw_discard_nonstatus":
                var directDraw = await CardPileCmd.Draw(choice, amount, owner);
                context.ReplaceDrawnCards(directDraw);
                await CardCmd.Discard(choice, directDraw.Where(c => c.Type != (context.RuntimeSpec.Variant == "draw_discard_nonattack" ? CardType.Attack : CardType.Status) && c.Pile?.Type == PileType.Hand).ToArray());
                break;
            case "blackflame_hand":
            case "blackflame_draw":
            case "blackflame_discard":
                for (int i = 0; i < amount; i++)
                    await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(owner,
                        context.RuntimeSpec.Variant == "blackflame_hand" ? PileType.Hand
                            : context.RuntimeSpec.Variant == "blackflame_discard" ? PileType.Discard : PileType.Draw,
                        CardPilePosition.Random);
                break;
            default:
                throw new InvalidOperationException("Unregistered NinjaSlayer component: " + context.RuntimeSpec.Variant);
        }
        return true;
    }
}
