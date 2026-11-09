namespace HuntStatsOverlayTools;

public sealed class GameLogContext
{
    public const string SoulSurvivor = "soul-survivor";
    public const string ShootingRange = "shooting-range";
    public string SelectedMode { get; private set; } = "unknown";
    public string Mode { get; private set; } = "unknown";
    private bool loading;
    public bool Excluded => Mode == ShootingRange;
    public GameLogContext Copy() => (GameLogContext)MemberwiseClone();

    public void Observe(string line)
    {
        foreach (var (target, mode) in LobbyModes)
            if (line.Contains($"SelectDeepLinkingTarget({target},") ||
                line.Contains($"GotoDeepLinkingTarget(Lobby, {target})"))
                SelectedMode = mode;
        if (line.Contains("Starting UI state machine 'quickplay/StateMachine'"))
            SelectedMode = SoulSurvivor;
        if (line.Contains("Starting UI state machine 'shootingrange/StateMachine'"))
            SelectedMode = ShootingRange;
        if (line.Contains("Starting UI state machine 'loading/StateMachine'") || line.Contains("CGameLobby::OnGameStarting()"))
        {
            if (!loading)
                Mode = SelectedMode;
            loading = true;
        }
        if (line.Contains("[LocalMission|ShootingRange] Successfully generated local mission data!") ||
            line.Contains("Loading level levels/shooting ") || line.Contains("PrepareLevel 'levels/shooting'"))
            Mode = ShootingRange;
        else if (line.Contains("PrepareLevel 'levels/") && line.Contains("(client)") && Excluded)
            Mode = SelectedMode == ShootingRange ? "unknown" : SelectedMode;
        if (line.Contains("transition from state(eLS_Loading) to (eLS_Game)") ||
            line.Contains("entering state eLS_Game(7) from eLS_Loading") ||
            line.Contains("Stopping UI state machine 'loading/StateMachine'"))
            loading = false;
        if (line.Contains("PrepareLevel 'menu' (server)"))
        {
            Mode = "unknown";
            loading = false;
        }
    }

    private static readonly (string Target, string Mode)[] LobbyModes =
    [
        ("QuickplayLobby", SoulSurvivor),
        ("ShootingRangeLobby", ShootingRange),
        ("BountyHuntLobby", "bounty-hunt"),
        ("BountyClashLobby", "bounty-clash"),
        ("TrainingLobby", "unknown")
    ];
}
