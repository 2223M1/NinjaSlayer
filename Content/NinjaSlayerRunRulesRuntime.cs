using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Content;

public static class NinjaSlayerRunRulesRuntime
{
    private static readonly HashSet<ulong> PendingPlayerIds = [];
    private static bool _replaceSingleplayerCharacter;

    public static IDisposable Subscribe() =>
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataLobbyStagingEvent>(OnLobbyStaging);

    public static bool TryReplaceCharacter(ref CharacterModel character, ulong netId)
    {
        bool shouldReplace = PendingPlayerIds.Remove(netId);
        if (_replaceSingleplayerCharacter && character is NinjaSlayerCharacter)
        {
            _replaceSingleplayerCharacter = false;
            shouldReplace = true;
        }

        if (!shouldReplace)
        {
            return false;
        }

        if (character is NinjaSlayerCharacter)
        {
            character = ModelDb.Character<NinjaSlayerRedesignCharacter>();
            return true;
        }

        return false;
    }

    private static void OnLobbyStaging(RunSavedDataLobbyStagingEvent evt)
    {
        if (evt.Reason != RunSavedDataLobbyStagingReason.Committing
            || evt.IsMultiplayer && !evt.IsHost)
        {
            return;
        }

        NinjaSlayerRulesVersion version = NinjaSlayerSettings.UseRedesignV1
            ? NinjaSlayerRulesVersion.RedesignV1
            : NinjaSlayerRulesVersion.Legacy;
        NinjaSlayerRunData.Rules.Lobby.Set(evt.Lobby, new NinjaSlayerRunRules { RulesVersion = version });

        PendingPlayerIds.Clear();
        _replaceSingleplayerCharacter = false;
        if (version == NinjaSlayerRulesVersion.Legacy)
        {
            return;
        }

        for (int index = 0; index < evt.Lobby.Players.Count; index++)
        {
            var player = evt.Lobby.Players[index];
            if (player.character is not NinjaSlayerCharacter)
            {
                continue;
            }

            if (!evt.IsMultiplayer)
            {
                _replaceSingleplayerCharacter = true;
            }
            else
            {
                PendingPlayerIds.Add(player.id);
                player.character = ModelDb.Character<NinjaSlayerRedesignCharacter>();
                evt.Lobby.Players[index] = player;
            }
        }
    }
}
