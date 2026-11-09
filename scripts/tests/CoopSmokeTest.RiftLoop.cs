#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Net;
using ArpgGame.Run;

namespace ArpgGame.Tests;

/// <summary>
/// The Survival Rift's 2026-11 loop in a real party over ENet (docs/design/survival-roadmap.md):
/// the proposer's tier and Fear cards reach every machine (M4), a client's Skip and Banish go
/// through the host's round and leave the host's copy of the build equal to the client's (M1), a
/// chest opens for its owner only, without pausing anyone (M2), and a shrine touched by two
/// players on the same frame is used once, with its Greed stack on every screen (M3). The content
/// audit of 2026-11-09 found none of these covered in co-op.
/// </summary>
public partial class CoopTestHost
{
    /// <summary>The tier and Fear card the party's Rift is started on (see Scenario).</summary>
    private const int CoopRiftTier = 2;
    private const string CoopRiftFear = "thick";

    private async Task RiftLoopCoopChecks(RunController run, RiftHost host, LobbyMember alpha, LobbyMember beta)
    {
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
        RunPlayer betaRp = run.State.PlayerByPeer(beta.PeerId)!;
        string L(Dictionary<string, string> f) => string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}"));

        // --- M4: the proposer's tier and Fear cards, on every machine --------------------------
        RiftDifficulty d = run.State.Difficulty;
        string hostFears = string.Join(",", d.Fears.Select(x => x.Id));
        Dictionary<string, string> da = Fields(await Ask(alpha.PeerId, "difficulty") ?? "");
        Dictionary<string, string> db = Fields(await Ask(beta.PeerId, "difficulty") ?? "");
        Check("COOP-RIFT-TIER-AND-FEARS", d.Tier == CoopRiftTier && hostFears == CoopRiftFear
                && F(da, "tier") == CoopRiftTier && da.GetValueOrDefault("fears") == CoopRiftFear
                && F(db, "tier") == CoopRiftTier && db.GetValueOrDefault("fears") == CoopRiftFear,
            $"host: tier {d.Tier}, fears '{hostFears}'; alpha: {L(da)}; beta: {L(db)}");

        // --- M1: alpha skips its row; the host takes it from alpha's own skips -------------------
        int hostSkips = alphaRp.Build!.SkipsLeft;
        int alphaHistory = alphaRp.Build.History.Count;
        float missing = run.State.ExperienceNeeded(run.State.PartyLevel) - run.State.PartyExperience;
        run.State.AddExperience(betaRp, missing + 0.1f);
        bool skipRound = await WaitFor(() => host.RoundOpen, 3f);
        run.LevelUp.ChooseIndex(0);
        await Ask(beta.PeerId, "pick");
        Dictionary<string, string> sk = Fields(await Ask(alpha.PeerId, "skip") ?? "");
        bool skipClosed = await WaitFor(() => !host.RoundOpen && !GetTree().Paused, 5f);
        await Seconds(1.2f); // the resume grace and the client's PickApplied
        Dictionary<string, string> pa = Fields(await Ask(alpha.PeerId, "pick_control") ?? "");
        Check("COOP-SKIP-THROUGH-THE-HOST", skipRound && skipClosed && F(sk, "skipped") == 1f
                && alphaRp.Build.SkipsLeft == hostSkips - 1 && F(pa, "skips") == alphaRp.Build.SkipsLeft
                && alphaRp.Build.History.Count == alphaHistory + 1 && alphaRp.Build.History[^1] == "Skipped a level",
            $"round open={skipRound}, closed by picks={skipClosed}; alpha: {L(sk)} → {L(pa)}; the host's copy: skips {hostSkips} → {alphaRp.Build.SkipsLeft}, "
            + $"last entry '{alphaRp.Build.History.LastOrDefault()}'");
        await BuildsMatch("COOP-SKIP-SAME-BUILD", run, alpha);

        // --- M1: beta banishes a card; its new row reaches the host, which accepts the pick ------
        int refused = host.RefusedPicks;
        missing = run.State.ExperienceNeeded(run.State.PartyLevel) - run.State.PartyExperience;
        run.State.AddExperience(alphaRp, missing + 0.1f);
        bool banishRound = await WaitFor(() => host.RoundOpen, 3f);
        run.LevelUp.ChooseIndex(0);
        await Ask(alpha.PeerId, "pick");
        Dictionary<string, string> bn = Fields(await Ask(beta.PeerId, "banish") ?? "");
        bool banishClosed = await WaitFor(() => !host.RoundOpen && !GetTree().Paused, 5f);
        await Seconds(1.2f);
        Check("COOP-BANISH-ROW-REACHES-HOST", banishRound && banishClosed && F(bn, "banished") == 1f && F(bn, "picked") == 1f
                && bn.GetValueOrDefault("identity", "") != bn.GetValueOrDefault("new_identity", "") && host.RefusedPicks == refused,
            $"round open={banishRound}, closed by picks={banishClosed} (not the 10 s timeout); beta: {L(bn)}; the host refused {host.RefusedPicks - refused} picks");
        await BuildsMatch("COOP-BANISH-SAME-BUILD", run, beta);

        // --- M2: a chest for alpha opens for alpha only, and pauses nobody -------------------------
        int hostChestsAlpha = alphaRp.ChestsOpened;
        int hostChestsBeta = betaRp.ChestsOpened;
        Dictionary<string, string> ca0 = Fields(await Ask(alpha.PeerId, "chests") ?? "");
        Dictionary<string, string> cb0 = Fields(await Ask(beta.PeerId, "chests") ?? "");
        alphaHistory = alphaRp.Build.History.Count;
        run.State.DropChest(alphaRp.Player!.GlobalPosition, 3, alphaRp.Player);
        bool opened = await WaitFor(() => alphaRp.ChestsOpened == hostChestsAlpha + 1, 5f);
        bool hostRunning = !GetTree().Paused && run.State.ClockRunning;
        await Seconds(0.8f);
        Dictionary<string, string> ca = Fields(await Ask(alpha.PeerId, "chests") ?? "");
        Dictionary<string, string> cb = Fields(await Ask(beta.PeerId, "chests") ?? "");
        Check("COOP-CHEST-OPENS-FOR-ITS-OWNER", opened && alphaRp.Build.History.Count > alphaHistory
                && F(ca, "opened") == F(ca0, "opened") + 1f && betaRp.ChestsOpened == hostChestsBeta && F(cb, "opened") == F(cb0, "opened"),
            $"host: alpha's chests {hostChestsAlpha} → {alphaRp.ChestsOpened} (+{alphaRp.Build.History.Count - alphaHistory} entries), beta's {hostChestsBeta} → {betaRp.ChestsOpened}; "
            + $"alpha's screen {L(ca0)} → {L(ca)}; beta's {L(cb0)} → {L(cb)}");
        Check("COOP-CHEST-NO-PAUSE", opened && hostRunning && F(ca, "paused") == 0f && F(cb, "paused") == 0f,
            $"host running while it opened={hostRunning}; alpha paused={ca.GetValueOrDefault("paused")}, beta paused={cb.GetValueOrDefault("paused")}");
        await BuildsMatch("COOP-CHEST-SAME-BUILD", run, alpha);

        // --- M3: a Greed shrine under two players on the same frame is used once ------------------
        int greed = run.State.Difficulty.Greed;
        Vector2 hostWas = run.Player.GlobalPosition;
        Vector2 spot = alphaRp.Player.GlobalPosition;
        int uses = 0;
        System.Action<Shrine, Player?> counted = (_, _) => uses++;
        run.Director.ShrineUsed += counted;
        Shrine shrine = run.Director.PlaceShrine(ShrineKind.Greed, spot);
        run.Player.GlobalPosition = spot;
        await Frames(1);
        for (int i = 0; i < 10; i++)
        {
            run.Director.TickShrines(1f / 60f); // the director's step with spawning off
        }

        run.Director.ShrineUsed -= counted;
        run.Player.GlobalPosition = hostWas;
        await Seconds(0.8f);
        Dictionary<string, string> ga = Fields(await Ask(alpha.PeerId, "difficulty") ?? "");
        Dictionary<string, string> gb = Fields(await Ask(beta.PeerId, "difficulty") ?? "");
        Check("COOP-SHRINE-USED-ONCE", shrine.Used && uses == 1 && run.State.Difficulty.Greed == greed + 1,
            $"two players on it: used={shrine.Used}, used {uses} time(s), Greed {greed} → {run.State.Difficulty.Greed}");
        Check("COOP-GREED-ON-EVERY-SCREEN", F(ga, "greed") == greed + 1 && F(gb, "greed") == greed + 1 && F(ga, "used") >= 1f && F(gb, "used") >= 1f,
            $"alpha: {L(ga)}; beta: {L(gb)}");
    }
}

public partial class CoopTestClient
{
    private async Task<string> RiftLoopAnswer(RunController run, string question)
    {
        switch (question.Split(' ')[0])
        {
            case "difficulty":
            {
                RiftDifficulty d = run.State.Difficulty;
                int shrines = run.Director.Shrines.Count();
                int used = run.Director.Shrines.Count(s => s.Used);
                return $"tier={d.Tier} greed={d.Greed} fears={string.Join(",", d.Fears.Select(x => x.Id))} shrines={shrines} used={used}";
            }

            case "skip":
            {
                // The Skip button's path: in a party round it sends the skip key as this row's pick.
                await WaitFor(() => run.LevelUp.IsOpen, 3f);
                bool skipped = run.LevelUp.SkipRow();
                return $"skipped={(skipped ? 1 : 0)} round={run.LevelUp.PartyRound}";
            }

            case "pick_control":
                return $"skips={run.Build.SkipsLeft} banishes={run.Build.BanishesLeft} rerolls={run.Build.RerollsLeft}";
            case "banish":
            {
                // Banish the first card (it is replaced on this screen and the new row is reported),
                // then take the card that replaced it.
                await WaitFor(() => run.LevelUp.IsOpen, 3f);
                string identity = run.LevelUp.Offers.Count > 0 ? run.LevelUp.Offers[0].Identity ?? "" : "";
                bool banished = run.LevelUp.BanishCard(0);
                string newKey = run.LevelUp.Offers.Count > 0 ? run.LevelUp.Offers[0].Key : "";
                string newIdentity = run.LevelUp.Offers.Count > 0 ? run.LevelUp.Offers[0].Identity ?? "" : "";
                await WaitFor(() => run.LevelUp.Armed, 2f);
                run.LevelUp.ChooseIndex(0);
                bool picked = run.LevelUp.PartyChosenThisRound;
                return $"banished={(banished ? 1 : 0)} identity={identity} new_identity={newIdentity} key={newKey} picked={(picked ? 1 : 0)} banishes={run.Build.BanishesLeft}";
            }

            case "chests":
                return $"opened={run.State.ChestsOpened} paused={(GetTree().Paused ? 1 : 0)} clock={(run.State.ClockRunning ? 1 : 0)}";
        }

        return $"unknown question '{question}'";
    }
}
