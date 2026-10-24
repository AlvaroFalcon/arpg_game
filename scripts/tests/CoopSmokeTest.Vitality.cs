#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.Net;
using ArpgGame.Progression;
using ArpgGame.Run;

namespace ArpgGame.Tests;

public partial class CoopTestHost
{
    /// <summary>One level-up round in which alpha takes <paramref name="key"/> (the host and beta take their first card).</summary>
    private async Task<bool> RoundWhereAlphaTakes(RunController run, RiftHost host, RunPlayer alphaRp, LobbyMember alpha, LobbyMember beta, string key)
    {
        float missing = run.State.ExperienceNeeded(run.State.PartyLevel) - run.State.PartyExperience;
        run.State.AddExperience(alphaRp, missing + 0.1f);
        if (!await WaitFor(() => host.RoundOpen, 3f))
        {
            return false;
        }

        run.LevelUp.ChooseIndex(0);
        await Ask(beta.PeerId, "pick");
        Dictionary<string, string> took = Fields(await Ask(alpha.PeerId, $"pick_key key={key}") ?? "");
        bool closed = await WaitFor(() => !host.RoundOpen && !GetTree().Paused, 5f);
        await Seconds(1.2f); // the resume grace and the client's PickApplied
        return closed && F(took, "applied") == 1f;
    }

    /// <summary>
    /// The owner's report (2026-10-24, from a co-op game): "with Vampiric Pact (no life regen, heal
    /// on kill) and then the Vitality card (+max life), you can never reach the new maximum life".
    /// 347d17d fixed the card on the host; this traces the client: alpha takes Pact, then Vitality,
    /// through real rounds, and its own HUD (its local life pool) must show the host's
    /// authoritative life and maximum, and kills must fill the bar to the new maximum on its screen.
    /// </summary>
    private async Task VitalityPactClientChecks(RunController run, RiftHost host, LobbyMember alpha, LobbyMember beta)
    {
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
        Player a = alphaRp.Player!;
        bool pact = await RoundWhereAlphaTakes(run, host, alphaRp, alpha, beta, "k:vampiric_pact");
        Dictionary<string, string> afterPact = Fields(await Ask(alpha.PeerId, "life") ?? "");
        float maxBefore = a.Stats.Life!.Max;
        bool vitality = await RoundWhereAlphaTakes(run, host, alphaRp, alpha, beta, "p:max_life:2");
        await Seconds(0.4f);
        Dictionary<string, string> afterCard = Fields(await Ask(alpha.PeerId, "life") ?? "");
        float hostMax = a.Stats.Life.Max;
        float hostLife = a.Stats.Life.Current;
        string L(Dictionary<string, string> f) => string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}"));
        Check("PACT-VITALITY-CLIENT-MAX-MATCHES-HOST", pact && vitality && alphaRp.Build!.HasKeystone("vampiric_pact") && F(afterPact, "pact") == 1f && hostMax > maxBefore
                && Mathf.Abs(F(afterCard, "max") - hostMax) < 0.6f && Mathf.Abs(F(afterCard, "cur") - hostLife) <= 1f && F(afterCard, "regen") <= 0f
                && afterCard.GetValueOrDefault("hud") == $"{Mathf.Min(Mathf.CeilToInt(hostLife), Mathf.RoundToInt(hostMax))}/{Mathf.RoundToInt(hostMax)}",
            $"alpha took Pact={pact}, Vitality={vitality}; the host's copy: {hostLife:0.##} / {hostMax:0.##} (max was {maxBefore:0.##}), regen {a.Stats.Life.RegenPerSecond:0.##}; alpha's own HUD: {L(afterCard)}");

        // Kills (credited to alpha on the host) heal by Pact's life on kill: alpha's bar must reach
        // the new maximum on alpha's screen, not stop at a stale ceiling.
        a.Stats.Life.Current = a.Stats.Life.Max * 0.3f;
        await Seconds(0.4f);
        Dictionary<string, string> low = Fields(await Ask(alpha.PeerId, "life") ?? "");
        Dictionary<string, string> betaLow = Fields(await Ask(beta.PeerId, $"life peer={alpha.PeerId}") ?? "");
        Vector2 at = run.Arena.Clamp(a.GlobalPosition + new Vector2(0f, 220f), 60f);
        int kills = 0;
        for (int i = 0; i < 80 && a.Stats.Life.Current < a.Stats.Life.Max - 0.01f; i++)
        {
            Enemy slime = run.Director.SpawnAt(run.Director.Slime!, at);
            slime.Dormant = true;
            await Frames(1);
            slime.Health.ApplyResolvedDamage(float.MaxValue / 4f, a);
            kills++;
            await Frames(1);
        }

        bool hostFull = a.Stats.Life.Current >= a.Stats.Life.Max - 0.01f;
        await Seconds(0.6f);
        Dictionary<string, string> full = Fields(await Ask(alpha.PeerId, "life") ?? "");
        Dictionary<string, string> betaFull = Fields(await Ask(beta.PeerId, $"life peer={alpha.PeerId}") ?? "");
        int maxShown = Mathf.RoundToInt(a.Stats.Life.Max);
        Check("PACT-VITALITY-CLIENT-KILLS-FILL-THE-BAR", hostFull && kills > 0 && F(low, "frac") is > 0.25f and < 0.35f
                && F(full, "frac") >= 0.999f && Mathf.Abs(F(full, "max") - a.Stats.Life.Max) < 0.6f && full.GetValueOrDefault("hud") == $"{maxShown}/{maxShown}",
            $"{kills} kills by alpha with {a.Stats.Get(Stats.StatId.LifeOnKill).Value:0.#} life on kill: the host's copy {a.Stats.Life.Current:0.##} / {a.Stats.Life.Max:0.##}; alpha's HUD at 30 %: {L(low)}; after the kills: {L(full)}");
        // The other client draws alpha's bar over a puppet whose build never took alpha's run cards
        // (a client applies only its own): it must use the host's maximum too, not clamp to its own.
        Check("PACT-VITALITY-ALLY-BAR-ON-OTHER-CLIENT", F(betaLow, "local") == 0f && Mathf.Abs(F(betaLow, "max") - a.Stats.Life.Max) < 0.6f && F(betaLow, "frac") is > 0.28f and < 0.32f
                && Mathf.Abs(F(betaFull, "max") - a.Stats.Life.Max) < 0.6f && F(betaFull, "frac") >= 0.999f,
            $"beta's view of alpha at 30 %: {L(betaLow)}; after the kills: {L(betaFull)}; the host's copy {a.Stats.Life.Current:0.##} / {a.Stats.Life.Max:0.##}");
        foreach (Node n in GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup))
        {
            n.QueueFree();
        }
    }
}

public partial class CoopTestClient
{
    private async Task<string> VitalityAnswer(RunController run, string question)
    {
        Dictionary<string, string> f = Fields(question);
        switch (question.Split(' ')[0])
        {
            case "pick_key":
            {
                // This machine's screen shows a random row: report a row holding the card the test
                // wants, then pick it. The host checks it is on the row and applies it to its copy;
                // only its PickApplied applies it here (the real client path).
                string key = f.GetValueOrDefault("key", "");
                int round = run.LevelUp.PartyRound;
                int before = run.Build.History.Count;
                run.NetClient!.SendOffers(round, new[] { new UpgradeOffer { Key = key, Title = key } });
                run.NetClient.SendPick(round, key);
                bool applied = await WaitFor(() => run.Build.History.Count > before, 4f);
                return $"applied={(applied ? 1 : 0)} round={round} history={string.Join(",", run.Build.History.Skip(before))}";
            }

            case "life":
            {
                // What this player's own HUD draws (PlayerHudBar: the local life pool), or with
                // peer=, what this machine shows for that ally (their overhead bar's pool).
                Player who = long.TryParse(f.GetValueOrDefault("peer"), out long peer) && run.State.PlayerByPeer(peer)?.Player is { } other ? other : run.Player;
                Stats.ResourcePool life = who.Stats.Life!;
                int max = Mathf.RoundToInt(life.Max);
                string hud = $"{Mathf.Min(Mathf.CeilToInt(life.Current), max)}/{max}";
                float frac = life.Max > 0f ? life.Current / life.Max : 0f;
                return $"cur={life.Current.ToString("0.###", CultureInfo.InvariantCulture)} max={life.Max.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"frac={frac.ToString("0.0000", CultureInfo.InvariantCulture)} regen={life.RegenPerSecond.ToString("0.###", CultureInfo.InvariantCulture)} hud={hud} pact={(run.Build.HasKeystone("vampiric_pact") ? 1 : 0)} local={(who.IsLocal ? 1 : 0)}";
            }
        }

        return $"unknown question '{question}'";
    }
}
