#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Net;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tests;

public partial class CoopTestHost
{
    /// <summary>
    /// docs/design/run.md -> "Co-op": the Colossus offer is a party round. Host leaves, beta stays, alpha
    /// never answers: the party stays (anyone staying keeps everyone), alpha's silence counts as leave
    /// after 15 s, a bad message is refused. Then, with the host leaving and beta silent, the whole
    /// party goes home as a victory.
    /// </summary>
    private async Task CashOutStayChecks(RunController run, RiftHost host, LobbyMember alpha, LobbyMember beta)
    {
        host.OpenCashOutRoundForTest();
        bool open = await WaitFor(() => host.CashOutRoundOpen && GetTree().Paused && run.CashOut.IsOpen, 3f);
        Dictionary<string, string> onAlpha = Fields(await Ask(alpha.PeerId, "cashout") ?? "");
        Dictionary<string, string> onBeta = Fields(await Ask(beta.PeerId, "cashout") ?? "");
        Check("CASHOUT-COOP-OPENS", open && run.CashOut.Mode == CashOutPanel.PanelMode.Party && F(onAlpha, "open") == 1f && F(onBeta, "open") == 1f
                && F(onAlpha, "paused") == 1f && F(onBeta, "paused") == 1f && onAlpha.GetValueOrDefault("mode") == "Party",
            $"host panel open={run.CashOut.IsOpen} paused={GetTree().Paused}; alpha {string.Join(" ", onAlpha.Select(kv => $"{kv.Key}={kv.Value}"))}; beta open={onBeta.GetValueOrDefault("open")}");

        // The host leaves; beta stays; the counter shows it everywhere; a bad message is refused.
        await WaitFor(() => run.CashOut.Armed, 2f);
        bool hostEarly = run.CashOut.TryChoose(true);
        string? betaPick = await Ask(beta.PeerId, "cashout_pick stay");
        int refused0 = host.RefusedPicks;
        await Ask(alpha.PeerId, "cashout_bad");
        await Seconds(0.4f);
        Dictionary<string, string> alphaCount = Fields(await Ask(alpha.PeerId, "cashout") ?? "");
        Check("CASHOUT-COOP-COUNTER-AND-VALIDATION", hostEarly && host.RoundInfo.Picked == 2 && host.CashOutRoundOpen && alphaCount.GetValueOrDefault("count") == "2/3"
                && host.RefusedPicks >= refused0 + 2 && F(alphaCount, "chosen") == 0f,
            $"host chose leave={hostEarly}, beta {betaPick}; chosen {host.RoundInfo.Picked}/3, alpha sees '{alphaCount.GetValueOrDefault("count")}', refused {host.RefusedPicks - refused0} bad messages (last: {host.LastRefusal})");

        // Alpha never answers: the countdown runs the whole 15 s, then counts as leave.
        bool closed = await WaitFor(() => !host.RoundOpen, 20f);
        RunPlayer hostRp = run.State.Local;
        Check("CASHOUT-COOP-TIMEOUT-IS-LEAVE", closed && host.LastRoundSeconds is >= 14.5f and <= 17f && host.LastCashOutPicks.Contains($"{RiftHost.NameOf(run.State.PlayerByPeer(alpha.PeerId)!)}=leave"),
            $"round closed={closed} after {host.LastRoundSeconds:0.00} s (timeout {PartyRules.CashOutTimeout} s); answers: {host.LastCashOutPicks}");
        bool graced = run.State.Players.All(rp => rp.Player?.Health is { InvulnerableTime: > 0f });
        Check("CASHOUT-COOP-ANYONE-STAYING-KEEPS-THE-PARTY", host.LastCashOutLeft == false && host.LastCashOutStayer == RiftHost.NameOf(run.State.PlayerByPeer(beta.PeerId)!)
                && !run.State.Ended && !GetTree().Paused && !run.CashOut.IsOpen && graced && hostRp.Player is not null,
            $"left={host.LastCashOutLeft}, stayer '{host.LastCashOutStayer}', run ended={run.State.Ended}, paused={GetTree().Paused}, grace={graced}");
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            Dictionary<string, string> f = Fields(await Ask(m.PeerId, "cashout") ?? "");
            string toast = await Ask(m.PeerId, "toast") ?? "";
            Check($"CASHOUT-COOP-RESUMES-{m.Name.ToUpperInvariant()}", F(f, "open") == 0f && F(f, "paused") == 0f && toast.Contains("The party stays") && toast.Contains(host.LastCashOutStayer),
                $"open={f.GetValueOrDefault("open")} paused={f.GetValueOrDefault("paused")} toast '{toast}'");
        }
    }

    private async Task CashOutLeaveChecks(RunController run, RiftHost host, LobbyMember beta)
    {
        RiftHost.CashOutTimeoutOverride = 3f;
        host.OpenCashOutRoundForTest();
        bool open = await WaitFor(() => host.CashOutRoundOpen && run.CashOut.IsOpen, 3f);
        await WaitFor(() => run.CashOut.Armed, 2f);
        run.CashOut.TryChoose(true);
        bool ended = await WaitFor(() => run.State.Ended, 8f);
        RiftHost.CashOutTimeoutOverride = null;
        Dictionary<string, string> f = Fields(await Ask(beta.PeerId, "cashout") ?? "");
        bool betaEnded = await WaitFor(() => false, 0.1f) || F(f, "ended") == 1f;
        if (!betaEnded)
        {
            f = Fields(await Ask(beta.PeerId, "cashout") ?? "");
        }

        Check("CASHOUT-COOP-EVERYONE-LEAVES", open && ended && run.State.Victory && !run.State.Fell && host.LastCashOutLeft == true && F(f, "ended") == 1f && F(f, "victory") == 1f,
            $"host ended={ended} victory={run.State.Victory}, answers {host.LastCashOutPicks}; beta ended={f.GetValueOrDefault("ended")} victory={f.GetValueOrDefault("victory")}");
    }
}

public partial class CoopTestClient
{
    private string CashOutLine(RunController run)
    {
        CashOutPanel c = run.CashOut;
        string count = c.PartyRound != 0 ? c.PartyCountText.Replace(" chosen", "").Replace(" ", "") : "-";
        return $"open={(c.PartyRound != 0 && c.IsOpen ? 1 : 0)} round={c.PartyRound} mode={c.Mode} paused={(GetTree().Paused ? 1 : 0)} chosen={(c.PartyChosenThisRound ? 1 : 0)} "
            + $"count={count} secs={c.PartySecondsShown} ended={(run.State.Ended ? 1 : 0)} victory={(run.State.Victory ? 1 : 0)}";
    }

    private async Task<string> CashOutAnswer(RunController run, NetSession session, string question)
    {
        RiftClient client = run.NetClient!;
        switch (question.Split(' ')[0])
        {
            case "cashout":
                return CashOutLine(run);
            case "cashout_pick":
            {
                bool leave = question.EndsWith("leave");
                await WaitFor(() => run.CashOut.Armed, 2f);
                bool counted = run.CashOut.TryChoose(leave);
                return $"picked counted={counted} chosen={(run.CashOut.PartyChosenThisRound ? 1 : 0)}";
            }

            case "cashout_bad":
            {
                int round = run.CashOut.PartyRound;
                client.SendPick(round + 100, "leave"); // wrong round
                client.SendPick(round, "lava");         // not an answer
                return $"sent 2 bad cash-out messages for round {round}";
            }

            case "toast":
                return run.Hud.ToastText;
        }

        return $"unknown question '{question}'";
    }
}
