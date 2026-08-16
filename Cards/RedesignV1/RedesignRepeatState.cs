using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Cards.RedesignV1;

internal static class RedesignRepeatState
{
    private static readonly ConditionalWeakTable<CardModel, object> Cards = new();
    public static bool Has(CardModel card) => Cards.TryGetValue(card, out _);
    public static void Add(CardModel card) => Cards.GetValue(card, static _ => new object());
}
