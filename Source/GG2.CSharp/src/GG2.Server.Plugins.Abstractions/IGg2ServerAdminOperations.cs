using GG2.Core;

namespace GG2.Server.Plugins;

public interface IGg2ServerAdminOperations
{
    void BroadcastSystemMessage(string text);

    bool TryDisconnect(byte slot, string reason);

    bool TryMoveToSpectator(byte slot);

    bool TrySetTeam(byte slot, PlayerTeam team);

    bool TrySetClass(byte slot, PlayerClass playerClass);

    bool TryChangeMap(string levelName, int mapAreaIndex = 1, bool preservePlayerStats = false);
}
