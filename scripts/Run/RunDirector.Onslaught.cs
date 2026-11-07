#nullable enable
using Godot;

namespace ArpgGame.Run;

/// <summary>
/// The climax and the endless milestones — docs/design/survival-roadmap.md -> M6. The last
/// <see cref="OnslaughtSeconds"/> before the Colossus are the busiest of the run (twice the
/// spawns, two swarm rings, no elites); the endless phase pays a 5-chest per player and raises a
/// quest event at 5, 10, 20 and 30 minutes.
/// </summary>
public partial class RunDirector
{
    public const float OnslaughtSeconds = 30f;
    public const float OnslaughtSpawn = 2f;
    public const float OnslaughtCap = 1.25f;
    public static readonly int[] EndlessMilestones = { 5, 10, 20, 30 };

    private bool _onslaughtStarted;
    private bool _onslaughtSecondRing;
    private int _milestonesReached;

    public float OnslaughtStart => ColossusTime - OnslaughtSeconds;

    /// <summary>The Onslaught is on (6:30 to the Colossus).</summary>
    public bool InOnslaught => Core.Features.RiftOnslaught && !_colossusSpawned && !ColossusDefeated
        && State.Elapsed >= OnslaughtStart && State.Elapsed < ColossusTime;

    /// <summary>Endless milestones reached (tests, the HUD).</summary>
    public int MilestonesReached => _milestonesReached;

    /// <summary>The next endless milestone in minutes, or 0 when none is left.</summary>
    public int NextMilestone => _milestonesReached < EndlessMilestones.Length ? EndlessMilestones[_milestonesReached] : 0;

    private void StepOnslaught(float t)
    {
        if (!InOnslaught)
        {
            return;
        }

        if (!_onslaughtStarted)
        {
            _onslaughtStarted = true;
            EmitSignal(SignalName.Announced, "The Onslaught", "The Rift throws everything at you before its heart wakes", new Color(1f, 0.35f, 0.3f));
            Core.Sfx.Play(this, "boss_warn", -4f);
            SpawnSwarm(t);
        }
        else if (!_onslaughtSecondRing && t >= OnslaughtStart + OnslaughtSeconds / 2f)
        {
            _onslaughtSecondRing = true;
            SpawnSwarm(t);
        }
    }

    private void StepEndlessMilestones(float t)
    {
        if (!Core.Features.RiftOnslaught || !EndlessStarted || _milestonesReached >= EndlessMilestones.Length)
        {
            return;
        }

        int minutes = EndlessMilestones[_milestonesReached];
        if (t - EndlessSince < minutes * 60f)
        {
            return;
        }

        _milestonesReached++;
        EmitSignal(SignalName.Announced, $"{minutes} minutes in the deep", "A golden chest for everyone", new Color(1f, 0.8f, 0.3f));
        foreach (Player p in LivingPlayers())
        {
            State.DropChest(Arena.Clamp(p.GlobalPosition + new Vector2(0f, -60f), 60f), 5, State.Coop ? p : null);
        }

        Core.GameEvents.Raise(Core.GameEvents.RiftEndlessMinute, minutes, State.Context);
    }
}
