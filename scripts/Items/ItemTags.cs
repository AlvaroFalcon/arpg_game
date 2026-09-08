using System;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Item filter": "Tags // escape hatch for anything not
/// anticipated." Deliberately empty in milestone 1 — no producer needs a tag yet.
///
/// A [Flags] enum, mirroring the representation choice in
/// <see cref="Combat.SkillTags"/> (fast, typo-proof, editor checkboxes; the
/// trade-off is a 64-value cap and a recompile per new tag, accepted there for the
/// same reasons here). This is a *separate* enum from <c>SkillTags</c> — items and
/// skills are different vocabularies that happen to share one representation
/// idiom, not the same set of labels.
/// </summary>
[Flags]
public enum ItemTags
{
    None = 0,
}
