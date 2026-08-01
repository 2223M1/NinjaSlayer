using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class CardPlays
    {
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
        private static readonly ConditionalWeakTable<CardPlay, PlayerHolder> Players = new();

        public static Player GetPlayer(CardPlay cardPlay)
        {
            ArgumentNullException.ThrowIfNull(cardPlay);
            return Players.TryGetValue(cardPlay, out PlayerHolder? holder)
                ? holder.Player
                : cardPlay.Card.Owner;
        }

        public static void AssociatePlayer(CardPlay cardPlay, Player player)
        {
            ArgumentNullException.ThrowIfNull(cardPlay);
            ArgumentNullException.ThrowIfNull(player);
            Players.Remove(cardPlay);
            Players.Add(cardPlay, new PlayerHolder(player));
        }

        private sealed record PlayerHolder(Player Player);
#else
        public static Player GetPlayer(CardPlay cardPlay)
        {
            ArgumentNullException.ThrowIfNull(cardPlay);
            return cardPlay.Player;
        }

        public static void AssociatePlayer(CardPlay cardPlay, Player player)
        {
        }
#endif
    }

    internal static class AttackCommands
    {
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
        private static readonly ConditionalWeakTable<AttackCommand, CardPlayHolder> CardPlays = new();

        public static void AssociateCardPlay(AttackCommand command, CardPlay cardPlay)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(cardPlay);
            CardPlays.Remove(command);
            CardPlays.Add(command, new CardPlayHolder(cardPlay));
        }

        public static bool TryGetCardPlay(AttackCommand command, out CardPlay? cardPlay)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (CardPlays.TryGetValue(command, out CardPlayHolder? holder))
            {
                cardPlay = holder.CardPlay;
                return true;
            }

            cardPlay = null;
            return false;
        }

        private sealed record CardPlayHolder(CardPlay CardPlay);
#else
        public static void AssociateCardPlay(AttackCommand command, CardPlay cardPlay)
        {
        }

        public static bool TryGetCardPlay(AttackCommand command, out CardPlay? cardPlay)
        {
            ArgumentNullException.ThrowIfNull(command);
            cardPlay = command.CardPlay;
            return cardPlay != null;
        }
#endif
    }
}
