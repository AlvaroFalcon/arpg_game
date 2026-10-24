#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Net;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tests;

public partial class CoopTestHost
{
    /// <summary>A deliberate fall (the test keeps everyone out of harm's way otherwise).</summary>
    private static void Down(Player p)
    {
        p.Health!.IgnoreDamage = false;
        p.Health.ApplyResolvedDamage(float.MaxValue / 4f, null);
        p.Health.IgnoreDamage = true;
    }

    private static int Refusals(RiftHost host, PartyRules.ReviveStop why) => host.ReviveRefusals.GetValueOrDefault(why);

    /// <summary>
    /// docs/design/multiplayer.md -> "Deaths and revives" (owner decision 2026-10-24): a client
    /// (alpha) holds E next to a downed client (beta) and revives them in about 5 s standing still,
    /// seen on every machine; a second ally (the host) is refused; moving and dodging stop the
    /// channel and lose its progress; forged, out-of-range and flooded intents are refused; and
    /// the whole party down ends the Rift (the rule, caught by a test seam so the run goes on).
    /// </summary>
    private async Task ReviveChecks(RunController run, RiftHost host, LobbyMember alpha, LobbyMember beta)
    {
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
        RunPlayer betaRp = run.State.PlayerByPeer(beta.PeerId)!;
        Player a = alphaRp.Player!;
        Player b = betaRp.Player!;
        Player h = run.Player;
        await WaitFor(() => a.Health!.InvulnerableTime <= 0f && b.Health!.InvulnerableTime <= 0f && h.Health!.InvulnerableTime <= 0f, 4f);

        // Alpha stays where it is (its own machine predicts it); beta lies 40 px from it; the host far away.
        Vector2 Beside(float dx)
        {
            Vector2 at = run.Arena.Clamp(a.GlobalPosition + new Vector2(dx, 0f), 60f);
            return at.DistanceTo(a.GlobalPosition) < Mathf.Abs(dx) * 0.8f ? run.Arena.Clamp(a.GlobalPosition - new Vector2(dx, 0f), 60f) : at;
        }

        Vector2 Far()
        {
            Vector2 up = run.Arena.Clamp(a.GlobalPosition + new Vector2(0f, -600f), 60f);
            return up.DistanceTo(a.GlobalPosition) > 400f ? up : run.Arena.Clamp(a.GlobalPosition + new Vector2(0f, 600f), 60f);
        }

        string D(Dictionary<string, string> f) => string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}"));
        h.GlobalPosition = Far();

        // --- an ally's revive: hold E, stand still, about 5 s --------------------------------------
        Down(b);
        b.GlobalPosition = Beside(40f);
        await Seconds(0.5f);
        int revivesBefore = host.AllyRevives;
        Dictionary<string, string> hold = Fields(await Ask(alpha.PeerId, $"revive_hold peer={beta.PeerId}") ?? "");
        bool started = await WaitFor(() => betaRp.Reviver == alphaRp, 3f);
        await Seconds(2f);
        float hostChannel = b.ReviveProgress;
        bool hostBar = a.RevivingTarget == b;
        bool hostMap = run.Hud.PartyMap.Allies().Any(m => m.PeerId == beta.PeerId && m.Downed && m.Revive > 0f);
        Dictionary<string, string> alphaMid = Fields(await Ask(alpha.PeerId, $"revive_state peer={beta.PeerId}") ?? "");
        Dictionary<string, string> betaMid = Fields(await Ask(beta.PeerId, $"revive_state peer={beta.PeerId}") ?? "");

        // A second ally next to beta: nobody double-speeds a revive; their prompt says who is on it.
        h.GlobalPosition = b.GlobalPosition + new Vector2(0f, 36f);
        await Frames(3);
        PartyRules.ReviveStop second = host.LocalReviveIntent(true, beta.PeerId);
        await Frames(2);
        string hostPrompt = run.Revive?.PromptText ?? "";
        bool stillAlpha = betaRp.Reviver == alphaRp;
        h.GlobalPosition = Far();

        bool revived = await WaitFor(() => !betaRp.Downed, 6f);
        float took = host.LastReviveSeconds;
        float share = b.Stats.Life!.Current / Mathf.Max(1f, b.Stats.Life.Max);
        await Seconds(0.5f);
        Dictionary<string, string> betaUp = Fields(await Ask(beta.PeerId, $"downed peer={beta.PeerId}") ?? "");
        await Ask(alpha.PeerId, "revive_release");
        Check("REVIVE-ALLY-5S-STANDING-STILL", started && revived && host.AllyRevives == revivesBefore + 1 && took is >= 4.6f and <= 5.8f
                && share is >= 0.45f and <= 0.6f && F(betaUp, "dead") == 0f && F(betaUp, "life") is >= 0.45f and <= 0.6f,
            $"alpha held E ({D(hold)}): channel started on the host={started}; beta up={revived} after {took:0.00} s of channel at {share:P0} life (alone: {PartyRules.RespawnLife:P0}); beta sees itself: {D(betaUp)}");
        Check("REVIVE-PROGRESS-SHOWN-EVERYWHERE", hostChannel is > 0.25f and < 0.75f && hostBar && hostMap
                && alphaMid.GetValueOrDefault("prompt", "").StartsWith("Reviving_beta") && F(alphaMid, "me_reviving") == beta.PeerId && F(alphaMid, "channel") > 0.2f && F(alphaMid, "map_revive") > 0.2f
                && betaMid.GetValueOrDefault("prompt", "").StartsWith("alpha_is_reviving_you") && F(betaMid, "reviver") == alpha.PeerId && F(betaMid, "channel") > 0.2f,
            $"2 s in: host ring {hostChannel:0.00}, alpha's bar on the host={hostBar}, on the host's party map={hostMap}; alpha's machine: {D(alphaMid)}; beta's machine: {D(betaMid)}");
        Check("REVIVE-ONE-REVIVER-AT-A-TIME", second == PartyRules.ReviveStop.Taken && stillAlpha && hostPrompt == "alpha is reviving beta",
            $"the host next to beta asked too: {second}; alpha still the reviver={stillAlpha}; the host's prompt '{hostPrompt}'");

        // --- moving stops it, and the progress is lost --------------------------------------------
        await WaitFor(() => b.Health!.InvulnerableTime <= 0f, 3f);
        Down(b);
        b.GlobalPosition = Beside(40f);
        await Seconds(0.5f);
        await Ask(alpha.PeerId, $"revive_hold peer={beta.PeerId}");
        bool started2 = await WaitFor(() => betaRp.Reviver == alphaRp, 3f);
        await Seconds(1.2f);
        float before2 = betaRp.ReviveChannel;
        Dictionary<string, string> moved = Fields(await Ask(alpha.PeerId, "revive_move") ?? "");
        bool stopped = await WaitFor(() => betaRp.Reviver is null, 2f);
        PartyRules.ReviveStop why = host.LastReviveCancel;
        Check("REVIVE-MOVING-CANCELS", started2 && before2 > 0.15f && stopped && why == PartyRules.ReviveStop.Moved && betaRp.ReviveChannel == 0f && betaRp.Downed && host.AllyRevives == revivesBefore + 1,
            $"channel at {before2:0.00} when alpha walked ({D(moved)}): stopped={stopped} ({why}), progress now {betaRp.ReviveChannel:0.00}, beta still down={betaRp.Downed}");

        // --- dodging stops it --------------------------------------------------------------------
        b.GlobalPosition = Beside(40f);
        await Seconds(0.5f);
        await Ask(alpha.PeerId, $"revive_hold peer={beta.PeerId}");
        bool started3 = await WaitFor(() => betaRp.Reviver == alphaRp, 3f);
        await Seconds(1f);
        float before3 = betaRp.ReviveChannel;
        Dictionary<string, string> dodged = Fields(await Ask(alpha.PeerId, "revive_dodge") ?? "");
        bool stopped3 = await WaitFor(() => betaRp.Reviver is null, 2f);
        PartyRules.ReviveStop why3 = host.LastReviveCancel;
        Check("REVIVE-DODGE-CANCELS", started3 && before3 > 0.1f && stopped3 && why3 == PartyRules.ReviveStop.Dodged && betaRp.ReviveChannel == 0f && betaRp.Downed,
            $"channel at {before3:0.00} when alpha dodged ({D(dodged)}): stopped={stopped3} ({why3}), beta still down={betaRp.Downed}");

        // --- forged, out-of-range and flooded intents ----------------------------------------------
        b.Health!.Revive(1f);
        await Seconds(0.3f);
        Down(b);
        b.GlobalPosition = Beside(400f);
        await Seconds(0.5f);
        var counts = new[] { PartyRules.ReviveStop.Self, PartyRules.ReviveStop.TargetUp, PartyRules.ReviveStop.Unknown, PartyRules.ReviveStop.TooFar, PartyRules.ReviveStop.RateLimited }
            .ToDictionary(w => w, w => Refusals(host, w));
        await Seconds(1.5f); // a full rate bucket
        Dictionary<string, string> forged = Fields(await Ask(alpha.PeerId, $"revive_forge self={alpha.PeerId} up=1 far={beta.PeerId} unknown=999") ?? "");
        await Seconds(0.6f);
        string Delta(PartyRules.ReviveStop w) => $"{w} +{Refusals(host, w) - counts[w]}";
        bool allRefused = counts.Keys.All(w => Refusals(host, w) > counts[w]);
        Check("REVIVE-FORGED-INTENTS-REFUSED", allRefused && betaRp.Reviver is null && betaRp.Downed && a.RevivingTarget is null,
            $"{D(forged)} → refused: {string.Join(", ", counts.Keys.Select(Delta))}; beta's reviver={betaRp.Reviver is not null}, still down={betaRp.Downed}");
        b.Health.Revive(1f);
        await Seconds(0.5f);

        // --- the whole party down at once: the Rift ends (its rule, unchanged) ----------------------
        bool? fell = null;
        bool victory = true;
        run.State.EndRunInterceptForTest = (v, f) =>
        {
            fell = f;
            victory = v;
            return true;
        };
        Down(a);
        bool afterOne = fell is null;
        Down(b);
        bool afterTwo = fell is null;
        Down(h);
        bool wiped = fell == true && victory == run.Director.ColossusDefeated;
        run.State.EndRunInterceptForTest = null;
        foreach (Player p in new[] { a, b, h })
        {
            p.Health!.Revive(1f);
        }

        await Seconds(0.6f);
        Check("RIFT-WIPE-ENDS-THE-RUN", afterOne && afterTwo && wiped && !run.State.Ended,
            $"one down: ended={!afterOne}; two down: ended={!afterTwo}; all three: the run ends fell={fell} victory={victory} (the reward reached; test seam keeps it going)");
    }
}

public partial class CoopTestClient
{
    private static string Prompt(RunController run) => (run.Revive?.PromptText ?? "").Replace(' ', '_');

    private async Task<string> ReviveAnswer(RunController run, NetSession session, string question)
    {
        Dictionary<string, string> f = Fields(question);
        RiftClient client = run.NetClient!;
        switch (question.Split(' ')[0])
        {
            case "revive_hold":
            {
                Input.ActionPress("interact");
                await Seconds(0.15f);
                return $"holding starts={run.Revive?.StartsSent} prompt={Prompt(run)}";
            }

            case "revive_release":
                Input.ActionRelease("interact");
                await Frames(2);
                return $"released stops={run.Revive?.StopsSent}";
            case "revive_move":
            {
                // Walks away from the downed ally with E still held, then lets go.
                Vector2 from = run.Player.GlobalPosition;
                CoopClient.TestMove = Vector2.Left;
                await Seconds(0.35f);
                CoopClient.TestMove = null;
                Input.ActionRelease("interact");
                await Frames(2);
                return $"walked={Num(run.Player.GlobalPosition.DistanceTo(from))}";
            }

            case "revive_dodge":
            {
                bool dodged = run.Player.Dodge?.TryDodge(Vector2.Up) ?? false;
                await Seconds(0.3f);
                Input.ActionRelease("interact");
                await Frames(2);
                return $"dodged={(dodged ? 1 : 0)}";
            }

            case "revive_state":
            {
                long peer = long.TryParse(f.GetValueOrDefault("peer"), out long id) ? id : 0;
                Player? target = run.State.PlayerByPeer(peer)?.Player;
                Player? reviver = target is null ? null : run.State.Players.Select(rp => rp.Player).FirstOrDefault(p => p is not null && p.RevivingTarget == target);
                float map = run.Hud.PartyMap.Allies().Where(m => m.PeerId == peer).Select(m => m.Revive).DefaultIfEmpty(-1f).First();
                return $"channel={(target?.ReviveProgress ?? -1f).ToString("0.00", CultureInfo.InvariantCulture)} getup={(target?.GetUpProgress ?? -1f).ToString("0.00", CultureInfo.InvariantCulture)} "
                    + $"me_reviving={run.Player.RevivingTarget?.PeerId ?? 0} reviver={reviver?.PeerId ?? 0} map_revive={map.ToString("0.00", CultureInfo.InvariantCulture)} prompt={Prompt(run)}";
            }

            case "revive_forge":
            {
                // What a broken or modified client could send: all refused by the host.
                client.SendRevive(true, long.Parse(f["self"]));    // itself
                client.SendRevive(true, long.Parse(f["up"]));      // someone standing
                client.SendRevive(true, long.Parse(f["unknown"])); // nobody
                for (int i = 0; i < 13; i++)
                {
                    client.SendRevive(true, long.Parse(f["far"])); // across the map, then a flood
                }

                return "sent 16 forged revive intents";
            }
        }

        return $"unknown question '{question}'";
    }
}
