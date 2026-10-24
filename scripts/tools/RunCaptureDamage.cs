#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Tools;

/// <summary>
/// `--only=damage`: damage numbers (docs/design/menus.md -> "Damage numbers").
/// The hideout's immortal training dummies hit by the demo warrior's own skills
/// (a capture-only crit bonus so crits show up in a short take), then a busy
/// Survival Rift moment with a built-up character. Prefix with
/// `--damage-prefix=before` to keep a baseline next to the new shots.
/// </summary>
public partial class RunCapture
{
    private async Task DamageScript()
    {
        string prefix = "damage-numbers";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--damage-prefix="))
            {
                prefix = arg[16..];
            }
        }

        SetupDemoProfile();
        if (Core.Profile.Get(this) is { } profile)
        {
            profile.Hideout.GuideSeen = true;
        }

        GetTree().ChangeSceneToFile(Core.Scenes.Hideout);
        await Wait(1.5f);
        if (GetTree().CurrentScene is Hideout hideout)
        {
            List<TargetDummy> dummies = hideout.FindChildren("*", "Area2D", true, false).OfType<TargetDummy>().ToList();
            Player player = hideout.Player;
            player.Health!.IgnoreDamage = true;
            if (dummies.Count > 0)
            {
                Vector2 centre = dummies.Aggregate(Vector2.Zero, (s, d) => s + d.GlobalPosition) / dummies.Count;
                TargetDummy main = dummies.OrderBy(d => d.GlobalPosition.DistanceTo(centre)).First();
                player.GlobalPosition = centre + new Vector2(70f, -55f);
                player.Stats.IgnoresResourceCosts = true;

                // Town dummies take no ailments; give them the component for this take only,
                // so the tag line can show IGNITE / CHILL / SHOCK.
                foreach (TargetDummy d in dummies)
                {
                    d.AddChild(new Ailments { Name = "Ailments" });
                }


                // Capture only: a mixed bar (fire, cold, lightning, physical) and higher
                // crit/ailment chances, so one short take shows every colour and tag.
                int slotIndex = 0;
                foreach (string id in new[] { "fireball", "frost_shards", "spark", "glacial_hammer" })
                {
                    if (GD.Load<SkillDefinition>($"res://resources/skills/run/{id}.tres") is { } def)
                    {
                        player.SkillUser.EquipSkill((SkillSlotId)slotIndex++, new SkillInstance(def));
                    }
                }

                player.Stats.CritChance.AddModifier(new StatModifier(ModifierType.Flat, 0.3f, this));
                player.Stats.Get(StatId.IgniteChance).AddModifier(new StatModifier(ModifierType.Flat, 0.5f, this));
                player.Stats.Get(StatId.ShockChance).AddModifier(new StatModifier(ModifierType.Flat, 0.5f, this));
                await HitDummies(player, dummies, main, 2.4f);
                ShowIncoming(player);
                await Wait(0.25f);
                await Snap($"{prefix}-dummies.png");
                await HitDummies(player, dummies, main, 1.2f);
                await Snap($"{prefix}-dummies-2.png");
                player.Stats.CritChance.RemoveModifiersFromSource(this);
            }
        }

        // A busy Rift: a built-up spark character in the middle of a swarm.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 260f;
        RunLaunchOptions.StartSkill = "spark";
        RunLaunchOptions.AutoLevels = 14;
        _autoPick = true;
        await Launch();
        await Wait(18f);
        await Snap($"{prefix}-horde.png");
        await Wait(2.5f);
        await Snap($"{prefix}-horde-2.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>What the damage the player takes looks like (other players' hits show no number).</summary>
    private static void ShowIncoming(Player player)
    {
        if (UI.DamageNumberLayer.Get(player) is not null)
        {
            UI.DamageNumberLayer.ShowIncoming(player, 37f, player.Stats.Life?.Max ?? 0f);
        }
    }

    /// <summary>Casts every equipped skill at <paramref name="main"/> as fast as cooldowns allow.</summary>
    private async Task HitDummies(Player player, List<TargetDummy> dummies, TargetDummy main, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            Vector2 aim = player.GlobalPosition.DirectionTo(main.GlobalPosition);
            foreach ((SkillSlotId slot, SkillInstance _) in player.SkillUser.EquippedSkills().ToList())
            {
                player.SkillUser.TryCast(slot, aim, main.GlobalPosition);
            }

            await Wait(0.05f);
            t += 0.05f;
        }
    }
}
