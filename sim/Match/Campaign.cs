using System.Numerics;

namespace FusionKingdoms.Sim;

// Solo roguelite: ten regions, each a short match with a twist. Minds carried between regions form the Legacy Library.
public sealed record Region(string Name, string Twist, AiPersonality Foe, float Minutes, Action<Match> Apply);

public static class Campaign
{
    public const int Keep = 2;

    public static readonly Region[] Regions =
    [
        new("The Quiet Vale", "A gentle start. The Breeder is still learning too.", AiPersonality.Breeder, 10, _ => { }),
        new("The Night Realm", "The sun never rises here. Vision is short; Night Owls shine.", AiPersonality.Warden, 10, m => m.PermanentNight = true),
        new("The Heartless Plains", "The Lumen Heart is dry. Lumen must come from moths and Ruins.", AiPersonality.Breeder, 11,
            m => { foreach (var n in m.W.Nodes) if (n.Type == ResourceType.Lumen && Vector2.Distance(n.Pos, new Vector2(m.W.Width / 2f, m.W.Height / 2f)) < 22f) n.Amount = 0f; }),
        new("Sprite Isle", "Wild tribes everywhere. Hunters will eat well.", AiPersonality.Archmage, 11,
            m => { foreach (var t in m.Tribes) t.Target *= 3; }),
        new("The Iron March", "The Warden has already reached its second Age.", AiPersonality.Warden, 12,
            m => { m.Kingdoms[1].Age = 2; m.Kingdoms[1].Stored[(int)ResourceType.Stone] += 60f; }),
        new("The Rich Valley", "Both crowns start wealthy. Spend fast.", AiPersonality.Breeder, 12,
            m => { foreach (var k in m.Kingdoms) { k.Stored[1] += 200f; k.Stored[2] += 120f; k.Stored[3] += 80f; k.Stored[4] += 60f; } }),
        new("The Raiders' Coast", "Mire Foxes swarm the marshes and steal from everyone.", AiPersonality.Archmage, 12,
            m => { foreach (var t in m.Tribes.Where(t => t.Raids)) t.Target *= 4; }),
        new("The Sealed Vault", "The Sealed One wakes at minute three.", AiPersonality.Warden, 13, m => m.SealedWakes = 180f),
        new("The Twin Crowns", "The Breeder starts with a hoard of Lumen.", AiPersonality.Breeder, 14,
            m => m.Kingdoms[1].Stored[(int)ResourceType.Lumen] += 150f),
        new("The Mad Archmage", "The final realm. The Archmage starts in Age 3 with a warped army.", AiPersonality.Archmage, 15,
            m =>
            {
                var k = m.Kingdoms[1];
                k.Age = 3;
                k.Stored[(int)ResourceType.Lumen] += 120f;
                foreach (var u in m.Subjects(k).ToList())
                {
                    var warped = m.Factory.Make($"Warped {u.Mind!.Name}", Mutation.Mutate(u.Mind.Brain, 2, m.Rng), [u.Mind], 1f, ["warped by the Archmage"]);
                    warped.Vessel = u.Mind.Vessel;
                    Match.SetMind(u, warped);
                    k.Library.Add(warped);
                }
            }),
    ];

    public static void Setup(Match m, Region r)
    {
        m.Length = r.Minutes * 60f;
        r.Apply(m);
    }
}
