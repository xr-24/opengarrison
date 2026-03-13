#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GG2.Client;

public partial class Game1
{
    private void DrawGameplayHudLayers(MouseState mouse, Vector2 cameraPosition)
    {
        DrawKillFeedHud();
        DrawChatHud();
        DrawScorePanelHud();
        DrawAutoBalanceNotice();
        DrawRespawnHud();
        DrawDeathCamHud();
        DrawWinBannerHud();
        if (!_networkClient.IsSpectator)
        {
            DrawLocalHealthHud();
            DrawAmmoHud();
            DrawSniperHud(mouse);
            DrawMedicHud();
            DrawMedicAssistHud();
            DrawHealerRadarHud(cameraPosition, mouse);
            DrawEngineerHud();
        }

        DrawBuildMenuHud();
        DrawNoticeHud();
        DrawScoreboardHud();
        if (!_networkClient.IsSpectator)
        {
            DrawBubbleMenuHud();
        }
    }

    private void DrawGameplayModalOverlays(MouseState mouse)
    {
        if (_passwordPromptOpen)
        {
            DrawPasswordPrompt();
        }

        if (_teamSelectOpen || _teamSelectAlpha > 0.02f)
        {
            DrawTeamSelectHud();
        }

        if (_classSelectOpen || _classSelectAlpha > 0.02f)
        {
            DrawClassSelectHud();
        }
        else if (!_teamSelectOpen
            && _teamSelectAlpha <= 0.02f
            && !_networkClient.IsSpectator
            && !_consoleOpen
            && !_inGameMenuOpen
            && !_optionsMenuOpen
            && !_controlsMenuOpen)
        {
            DrawCrosshair(mouse);
        }

        if (_consoleOpen)
        {
            DrawConsoleOverlay();
        }

        if (_inGameMenuOpen)
        {
            DrawInGameMenu();
        }
        else if (_optionsMenuOpen)
        {
            DrawOptionsMenu();
        }
        else if (_controlsMenuOpen)
        {
            DrawControlsMenu();
        }
    }
}
