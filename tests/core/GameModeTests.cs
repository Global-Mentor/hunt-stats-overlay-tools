using System.Diagnostics;
using Xunit;

namespace HuntStatsOverlayTools.Tests;

public sealed class GameModeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HuntStatsOverlayToolsModes-" + Guid.NewGuid().ToString("N"));
    public GameModeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private const string Loading = "Starting UI state machine 'loading/StateMachine'";
    private const string Loaded = "transition from state(eLS_Loading) to (eLS_Game)";
    private const string Ended = "Client_OnDisconnect(cause=17, reasonType='GameSessionEnded')";
    private const string Menu = "PrepareLevel 'menu' (server)";
    private const string Range = "[LocalMission|ShootingRange] Successfully generated local mission data!";

    private static Statistics SoulMatch()
    {
        var stats = new Statistics();
        stats.ProcessLine("SelectDeepLinkingTarget(QuickplayLobby, Quickplay)");
        stats.ProcessLine(Loading);
        stats.ProcessLine("PrepareLevel 'levels/creek' (client)");
        stats.ProcessLine(Loaded);
        return stats;
    }

    [Fact]
    public void SoulSurvivorNormalEndCountsOnceAndPersistsEvidence()
    {
        var stats = SoulMatch();
        stats.ProcessLine(Ended);
        stats.ProcessLine(Ended);
        stats.ProcessLine(Menu);
        Assert.Equal(1, stats.Games);
        Assert.Equal(1, stats.Survived);
        Assert.Equal(0, stats.Lost);
        Assert.Equal("survived", Assert.Single(stats.Recent).Outcome);
        var store = new StatisticsStore(root);
        store.Save(stats);
        var saved = store.Load();
        Assert.Equal(GameLogContext.SoulSurvivor, Assert.Single(saved.Recent).GameMode);
        Assert.Equal(1, saved.Survived);
    }

    [Theory]
    [InlineData("death-screen")]
    [InlineData("combat-death")]
    [InlineData("partial")]
    [InlineData("loading")]
    [InlineData("disconnect")]
    [InlineData("user-quit")]
    public void SoulSurvivorDoesNotTurnSpectatingOrIncompleteEvidenceIntoSurvival(string scenario)
    {
        var stats = SoulMatch();
        if (scenario == "death-screen")
        {
            stats.ProcessLine("Starting UI state machine 'death/StateMachine'");
            stats.ProcessLine("Stopping UI state machine 'death/StateMachine'");
        }
        if (scenario == "combat-death") stats.Combat("death", stats.Current!.Id);
        if (scenario == "partial") stats.Current!.Full = false;
        if (scenario == "loading") stats.Current!.Loaded = false;
        stats.ProcessLine(scenario == "disconnect" ? "Client_OnDisconnect(cause=1, reasonType='Timeout')"
            : scenario == "user-quit" ? "Client_OnDisconnect(cause=0, reasonType='UserRequested')" : Ended);
        Assert.Equal(0, stats.Survived);
        Assert.Equal(1, stats.Lost);
        Assert.Equal("lost", Assert.Single(stats.Recent).Outcome);
        if (scenario == "death-screen") Assert.True(stats.Recent[0].DeathObserved);
    }

    [Fact]
    public void BountyMatchAfterSoulSurvivorKeepsItsOwnOutcomeRules()
    {
        var stats = SoulMatch();
        stats.ProcessLine(Ended);
        stats.ProcessLine(Menu);
        stats.ProcessLine("GotoDeepLinkingTarget(Lobby, BountyHuntLobby) - Cause:set - Stack[1]");
        stats.ProcessLine(Loading);
        stats.ProcessLine(Loaded);
        stats.ProcessLine("Starting UI state machine 'death/StateMachine'");
        stats.ProcessLine("Stopping UI state machine 'death/StateMachine'");
        stats.ProcessLine("Client_OnDisconnect(cause=0, reasonType='MissionRequested')");
        Assert.Equal(2, stats.Survived);
        Assert.Equal("bounty-hunt", stats.Recent[1].GameMode);
        stats.ProcessLine(Loading);
        stats.ProcessLine(Loaded);
        stats.ProcessLine(Ended);
        Assert.Equal(2, stats.Survived);
        Assert.Equal(1, stats.Lost);
    }

    [Theory]
    [InlineData("[LocalMission|ShootingRange] Successfully generated local mission data!")]
    [InlineData("PrepareLevel 'levels/shooting' (server)")]
    [InlineData("PrepareLevel 'levels/shooting' (client)")]
    [InlineData("============================ Loading level levels/shooting ============================")]
    public void ShootingRangeCannotChangeAnyStatisticOrHistory(string marker)
    {
        var stats = SoulMatch();
        stats.ProcessLine(Ended);
        stats.ProcessLine(Menu);
        stats.ProcessLine(Loading);
        stats.ProcessLine(marker);
        stats.ProcessLine(Loaded);
        stats.ProcessLine("Starting UI state machine 'death/StateMachine'");
        stats.Combat("kill", null);
        stats.Combat("death", null);
        stats.Trade(null);
        stats.ProcessLine(Ended);
        stats.ProcessLine(Menu);
        Assert.Equal(1, stats.Games);
        Assert.Equal(1, stats.Survived);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(0, stats.Kills + stats.Deaths + stats.Trades);
        Assert.Equal(1, stats.Maps.Values.Sum());
        Assert.Single(stats.Recent);
        stats.ProcessLine("SelectDeepLinkingTarget(BountyClashLobby, BanishMode)");
        stats.ProcessLine(Loading);
        stats.ProcessLine("PrepareLevel 'levels/civilwar' (client)");
        stats.ProcessLine(Loaded);
        stats.Combat("kill", stats.Current!.Id);
        Assert.Equal(2, stats.Games);
        Assert.Equal(1, stats.Kills);
        Assert.Equal(1, stats.Maps["civilwar"]);
    }

    [Fact]
    public void VisitingRangeMenuOrLoadingTutorialUiDoesNotExcludeAMatch()
    {
        var stats = new Statistics();
        stats.ProcessLine("Starting UI state machine 'shootingrange/StateMachine'");
        Assert.False(stats.Context.Excluded);
        stats.ProcessLine("SelectDeepLinkingTarget(QuickplayLobby, Quickplay)");
        stats.ProcessLine(Loading);
        stats.ProcessLine("Starting UI state machine 'scripted_tutorial/TutorialFlow'");
        stats.ProcessLine(Loaded);
        stats.ProcessLine(Ended);
        Assert.Equal(1, stats.Survived);
    }

    [Fact]
    public void EnteringRangePreservesThePreviousUnfinishedMatch()
    {
        var stats = SoulMatch();
        stats.Combat("kill", stats.Current!.Id);
        stats.ProcessLine("SelectDeepLinkingTarget(ShootingRangeLobby, ShootingRange)");
        stats.ProcessLine(Loading);
        stats.ProcessLine(Range);
        stats.ProcessLine(Loaded);
        stats.ProcessLine(Menu);
        Assert.Equal(1, stats.Games);
        Assert.Equal(1, stats.Kills);
        Assert.Equal(1, stats.Lost);
        Assert.Equal("next_match", Assert.Single(stats.Recent).Reason);
        Assert.Null(stats.Current);
    }

    [Fact]
    public void ReadingContextStopsAtTheOriginalCursor()
    {
        var path = Path.Combine(root, "Game.log");
        File.WriteAllText(path, Loading + "\n" + Range + "\n");
        var original = new LogTail(path, true);
        File.AppendAllText(path, Menu + "\n");
        var history = new LogTail(path, false);
        var context = new GameLogContext();
        foreach (var line in history.Read(endPosition: original.Position)) context.Observe(line);
        Assert.True(context.Excluded);
        Assert.Equal(original.Position, history.Position);
        Assert.Equal(new[] { Menu }, original.Read());
    }

    [Fact]
    public async Task RunningCollectorExcludesRangeAcrossStartupResetAndLogReplacement()
    {
        var gameFolder = Path.Combine(root, "game");
        var userFolder = Path.Combine(gameFolder, "USER");
        var sdkFolder = Path.Combine(root, "sdk");
        Directory.CreateDirectory(userFolder);
        Directory.CreateDirectory(sdkFolder);
        var gameLog = Path.Combine(userFolder, "Game.log");
        File.WriteAllText(gameLog, Loading + "\n" + Range + "\n" + Loaded + "\n");
        using var process = Process.GetCurrentProcess();
        var sdkLog = Path.Combine(sdkFolder, $"HuntGame_{process.Id}.log");
        File.WriteAllText(sdkLog, "");
        new AppConfig { GameFolder = gameFolder, NvidiaLogs = sdkFolder, AlertsEnabled = false }.Save(root);
        var engine = new TrackerEngine(root, process.ProcessName);
        var running = engine.RunAsync();
        async Task SettledRangeEvent(string id)
        {
            File.AppendAllText(sdkLog, $"Posted Request HLSetVideo[{id}]\n");
            await Task.Delay(850, TestContext.Current.CancellationToken);
            var stats = await engine.CurrentHistory();
            Assert.Equal(0, stats.Kills + stats.Deaths + stats.Trades + stats.Games + stats.Survived + stats.Lost);
            Assert.Empty(stats.Recent);
        }
        try
        {
            await WaitFor(async () => (await engine.CurrentHistory()).Context.Excluded);
            await SettledRangeEvent("startup");
            await engine.NewSession();
            await SettledRangeEvent("reset");
            File.WriteAllText(gameLog, Range + "\n");
            await SettledRangeEvent("replacement");
            File.AppendAllText(gameLog, Menu + "\nSelectDeepLinkingTarget(QuickplayLobby, Quickplay)\n" + Loading + "\nPrepareLevel 'levels/creek' (client)\n" + Loaded + "\n");
            await WaitFor(async () => (await engine.CurrentHistory()).Games == 1);
            File.AppendAllText(sdkLog, "Posted Request HLSetVideo[realmatch]\n");
            await WaitFor(async () => (await engine.CurrentHistory()).Kills == 1);
            File.AppendAllText(gameLog, Ended + "\n");
            await WaitFor(async () => (await engine.CurrentHistory()).Survived == 1);
            File.AppendAllText(gameLog, Menu + "\n" + Loading + "\n" + Range + "\n" + Loaded + "\n");
            await Task.Delay(150, TestContext.Current.CancellationToken);
            File.AppendAllText(sdkLog, "Posted Request HLSetVideo[rangeagain]\n");
            await Task.Delay(850, TestContext.Current.CancellationToken);
            var final = await engine.CurrentHistory();
            Assert.Equal(1, final.Games);
            Assert.Equal(1, final.Kills);
            Assert.Equal(1, final.Survived);
            Assert.Single(final.Recent);
        }
        finally
        {
            engine.Stop();
            await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitFor(Func<Task<bool>> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(8), "Collector did not reach expected state.");
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }
    }
}
