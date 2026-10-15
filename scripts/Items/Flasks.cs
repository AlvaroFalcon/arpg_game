#nullable enable
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// One flask — docs/design/items.md -> "Flasks and refill orbs". Holds a few
/// charges; drinking spends one and restores a share of the resource over a
/// short time. Charges come back slowly on their own and from refill orbs.
/// </summary>
public sealed class Flask
{
    public string Name = "";
    public int MaxCharges = 3;

    /// <summary>Charges including the fractional one being regenerated.</summary>
    public float Charges = 3f;

    /// <summary>Share of the maximum restored by one drink.</summary>
    public float Recovery = 0.4f;

    /// <summary>Seconds the recovery is spread over.</summary>
    public float Duration = 1.4f;

    /// <summary>Charges regenerated per second — slower than combat drains them,
    /// so refill orbs stay worth picking up.</summary>
    public float Regen = 1f / 30f;

    public bool Enabled = true;

    /// <summary>Recovery still to be applied, and its rate.</summary>
    public float Pending;
    public float PendingRate;

    public int Usable => Mathf.FloorToInt(Charges + 0.0001f);
    public bool Full => Charges >= MaxCharges - 0.0001f;
    public bool Active => Pending > 0f;

    /// <summary>Progress towards the next charge, 0..1.</summary>
    public float Partial => Full ? 0f : Charges - Mathf.Floor(Charges);
}

/// <summary>
/// The player's life (Q) and mana (R) flasks. Lives under the Player node
/// (created by <see cref="Player"/>), reads the two flask actions, ticks
/// regeneration and applies recovery over time.
/// </summary>
public partial class Flasks : Node
{
    public const string LifeAction = "flask_life";
    public const string ManaAction = "flask_mana";

    public readonly Flask Life = new() { Name = "Life flask", Recovery = 0.4f };
    public readonly Flask Mana = new() { Name = "Mana flask", Recovery = 0.5f };

    [Signal]
    public delegate void DrankEventHandler(string flask);

    [Signal]
    public delegate void RefusedEventHandler(string reason);

    private Player _player = null!;

    public static Flasks? Of(Node player) => player.GetNodeOrNull<Flasks>("Flasks");

    /// <summary>True when there's nothing for a refill orb to fill.</summary>
    public bool AllFull => (Life.Full || !Life.Enabled) && (Mana.Full || !Mana.Enabled);

    public override void _Ready()
    {
        _player = GetParent<Player>();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool alive = !(_player.Health?.IsDead ?? true);
        if (alive && _player.Command is { } command)
        {
            // A remote co-op player: presses arrive as counters.
            if (command.ConsumeLifeFlask())
            {
                TryDrink(Life);
            }

            if (command.ConsumeManaFlask())
            {
                TryDrink(Mana);
            }
        }
        else if (alive && !GetTree().Paused && _player.IsLocal)
        {
            if (Input.IsActionJustPressed(LifeAction))
            {
                TryDrink(Life);
            }

            if (Input.IsActionJustPressed(ManaAction))
            {
                TryDrink(Mana);
            }
        }

        foreach (Flask f in new[] { Life, Mana })
        {
            if (!f.Full)
            {
                f.Charges = Mathf.Min(f.MaxCharges, f.Charges + f.Regen * dt);
            }

            if (f.Pending > 0f && alive)
            {
                float step = Mathf.Min(f.Pending, f.PendingRate * dt);
                f.Pending -= step;
                Apply(f, step);
            }
        }
    }

    /// <summary>Drinks one charge. Refuses (without spending) when empty or the
    /// resource is already full, so a stray key press never wastes a charge.</summary>
    public bool TryDrink(Flask flask)
    {
        if (!flask.Enabled)
        {
            return false;
        }

        Stats.ResourcePool? pool = flask == Life ? _player.Stats.Life : _player.Stats.Mana;
        if (pool is null)
        {
            return false;
        }

        if (flask.Usable < 1)
        {
            EmitSignal(SignalName.Refused, $"{flask.Name} is empty");
            return false;
        }

        if (pool.Current >= pool.Max - 0.5f && !flask.Active)
        {
            EmitSignal(SignalName.Refused, flask == Life ? "Already at full life" : "Already at full mana");
            return false;
        }

        flask.Charges -= 1f;
        float amount = pool.Max * flask.Recovery;
        flask.Pending += amount;
        flask.PendingRate = flask.Pending / flask.Duration;
        Core.Sfx.Play(this, "pickup", -2f);
        EmitSignal(SignalName.Drank, flask.Name);
        return true;
    }

    /// <summary>
    /// A refill orb was collected: one charge to each flask. A player without
    /// flasks (old scenes, tests) gets the orb's old effect, a direct heal of
    /// <paramref name="heal"/> of max life.
    /// </summary>
    public static void CollectOrb(Player player, float heal)
    {
        if (Of(player) is { } flasks)
        {
            flasks.Refill();
        }
        else
        {
            player.Health?.Heal((player.Stats.Life?.Max ?? 0f) * heal);
        }
    }

    /// <summary>A refill orb: one charge to each flask that isn't full.</summary>
    public bool Refill(float charges = 1f)
    {
        bool any = false;
        foreach (Flask f in new[] { Life, Mana })
        {
            if (f.Enabled && !f.Full)
            {
                f.Charges = Mathf.Min(f.MaxCharges, f.Charges + charges);
                any = true;
            }
        }

        return any;
    }

    private void Apply(Flask flask, float amount)
    {
        if (flask == Life)
        {
            _player.Health?.Heal(amount);
        }
        else if (_player.Stats.Mana is { } mana)
        {
            mana.Current = Mathf.Min(mana.Max, mana.Current + amount);
        }
    }
}
