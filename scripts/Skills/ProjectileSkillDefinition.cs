using Godot;

namespace ArpgGame.Skills;

/// <summary>
/// The `Projectile` archetype. Authored with its full intended capability list —
/// count, spread angle, pierce, splits, chains, bounces, homing strength, size,
/// speed, range, on-hit-explode — all defaulted off, per the owner-confirmed answer to
/// open question 1 in docs/design/skills.md ("What capability list does the
/// projectile archetype claim?"). Only Count = 1 / Pierce = 0 (i.e. the trivial
/// single-projectile, no-pierce path) is actually implemented by
/// <see cref="Skills.SkillUser"/> / <see cref="Combat.Projectile"/> in this slice —
/// see docs/design/skills.md -> "Priority order" item 6 and "Seam 6 step 6" for why
/// the executor is written as a parameterised capability set rather than a minimal
/// implementation of only what fires today.
/// </summary>
[GlobalClass]
public partial class ProjectileSkillDefinition : SkillDefinition
{
    [Export]
    public float Speed = 600f;

    [Export]
    public float Range = 500f;

    /// <summary>
    /// Forward offset (px) from the caster's position where the projectile spawns,
    /// so it doesn't spawn inside the caster's own collider. Placeholder for a
    /// future muzzle/hand marker.
    /// </summary>
    [Export]
    public float SpawnOffset = 20f;

    /// <summary>
    /// How many projectiles one cast fires. 1 = off. Always fanned across
    /// <see cref="SpreadDegrees"/> centred on the aim direction — they do not
    /// spawn "around the player" unless the spread says so (see SpreadDegrees).
    /// </summary>
    [Export]
    public int Count = 1;

    /// <summary>
    /// Total angle (degrees) that <see cref="Count"/> projectiles are fanned
    /// across, centred on the aim direction. Two regimes:
    ///
    /// - **&lt; 360** — a cone. Projectiles are placed from -Spread/2 to +Spread/2
    ///   inclusive, so the outermost two sit exactly on the cone edges.
    /// - **&gt;= 360** — a nova *around the caster*: the full circle is divided
    ///   evenly by Count, so the first and last don't land on top of each other.
    ///
    /// 0 with Count &gt; 1 is legal and stacks every projectile on the same line —
    /// a "slug". Deliberately not auto-corrected: that's a real design (Hell
    /// Clock's Blunderbuss relic does exactly this), and silently fanning a
    /// spread the author didn't ask for would be worse than an odd-looking one.
    /// </summary>
    [Export]
    public float SpreadDegrees;

    [Export]
    public int Pierce;

    /// <summary>
    /// How many times this projectile may fork — split into two on hit, both
    /// children continuing past the target. 0 = off.
    ///
    /// Named "fork" rather than "split" to match genre vocabulary precisely: a
    /// *fork* happens on hit, a *split* happens at cast (which is what
    /// <see cref="Count"/> already does). The design doc's original phrasing was
    /// "splits in two on hit", which is a fork.
    ///
    /// Children inherit the *decremented* counter, so this is a generation cap,
    /// not a per-hit budget — without it, fork-on-hit is exponential.
    /// </summary>
    [Export]
    public int Forks;

    [Export]
    public int Chains;

    /// <summary>
    /// How many times this projectile may bounce off world geometry / the arena
    /// bounds before despawning. 0 = off (despawn on the first terrain impact,
    /// which is the current behaviour).
    ///
    /// Distinct from <see cref="Chains"/>, which redirects toward another *enemy*
    /// after a hit. Bounce redirects off *geometry* and consumes no hit. Both
    /// resolve through <see cref="Combat.OnHitOutcome.Redirect"/>, which is why
    /// neither needs a new seam.
    ///
    /// Not implemented — see the capability-set note on this class.
    /// </summary>
    [Export]
    public int Bounces;

    [Export]
    public float HomingStrength;

    /// <summary>Scale multiplier. 1.0 is "off" for this capability (a size of 0
    /// would be a degenerate, invisible projectile).</summary>
    [Export]
    public float Size = 1f;

    [Export]
    public bool OnHitExplode;

    /// <summary>Radius (px) of the blast where the projectile lands — on an enemy or a
    /// wall — hitting everything around for <see cref="ExplodeDamageFraction"/> of its
    /// damage (Fireball, docs/design/skills.md -> "Fireball and Spark, 2026-10-30"). 0 = none.</summary>
    [Export]
    public float ExplodeRadius;

    [Export]
    public float ExplodeDamageFraction = 0.5f;

    /// <summary>Sideways jag (px) of a lightning-like flight: the projectile zigzags
    /// along its heading through random knots and leaves a short crackling trail (Spark). 0 = straight.</summary>
    [Export]
    public float Zigzag;
}
