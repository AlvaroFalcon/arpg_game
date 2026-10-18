#nullable enable
using System.Collections.Generic;
using ArpgGame.Quests;

namespace ArpgGame.Core;

public partial class Profile
{
    /// <summary>One quest's saved state (docs/design/quests.md -> "Persistence and migration"):
    /// status, count, last change. Short names keep the join payload small.</summary>
    private sealed class SaveQuest
    {
        public int S { get; set; }
        public int C { get; set; }
        public long T { get; set; }
    }

    /// <summary>The saved form of a progress map (null when empty: the field is left out of the file).</summary>
    private static Dictionary<string, SaveQuest>? SaveQuests(Dictionary<string, QuestProgress> quests)
    {
        if (quests.Count == 0)
        {
            return null;
        }

        var saved = new Dictionary<string, SaveQuest>();
        foreach ((string id, QuestProgress p) in quests)
        {
            saved[id] = new SaveQuest { S = (int)p.Status, C = p.Count, T = p.Since };
        }

        return saved;
    }

    /// <summary>Unknown quest ids are dropped and statuses and counts clamped.</summary>
    private static Dictionary<string, QuestProgress> LoadQuests(Dictionary<string, SaveQuest>? saved)
    {
        var quests = new Dictionary<string, QuestProgress>();
        if (saved is null)
        {
            return quests;
        }

        foreach ((string id, SaveQuest? q) in saved)
        {
            if (q is null || !QuestCatalog.Exists(id))
            {
                continue;
            }

            quests[id] = new QuestProgress
            {
                Status = (QuestStatus)System.Math.Clamp(q.S, (int)QuestStatus.Locked, (int)QuestStatus.Skipped),
                Count = System.Math.Clamp(q.C, 0, Net.NetLimits.MaxQuestCount),
                Since = q.T,
            };
        }

        return quests;
    }
}
