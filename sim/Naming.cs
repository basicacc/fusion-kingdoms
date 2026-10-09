namespace FusionKingdoms.Sim;

// Names for minds whose profile matches nothing in the trait table. Deterministic: the same profile always
// gets the same name, built from its two most extreme axes.
public static class Naming
{
    static readonly (string hiHead, string hiTail, string loHead, string loTail)[] Parts =
    [
        ("Ember", "heart", "Ash", "shy"),        // Courage
        ("Thorn", "fang", "Moss", "calm"),       // Aggression
        ("Gild", "grasp", "Hollow", "palm"),     // Greed
        ("Crown", "ward", "Drift", "less"),      // Loyalty
        ("Iron", "march", "Wild", "will"),       // Discipline
        ("Far", "seeker", "Hearth", "stay"),     // Curiosity
        ("Still", "wait", "Quick", "spark"),     // Patience
        ("Kin", "song", "Lone", "walker"),       // Teamwork
        ("Shade", "slip", "Plain", "step"),      // Cunning
        ("Loom", "toil", "Idle", "dream"),       // Diligence
        ("Hold", "root", "Wander", "road"),      // Territoriality
        ("Balm", "keeper", "Cold", "eye"),       // Compassion
    ];

    public static string Title(TrialResult t)
    {
        var top = Enumerable.Range(0, Trial.AxisCount).OrderByDescending(i => MathF.Abs(t.Scores[i] - 50f)).ThenBy(i => i).Take(2).ToArray();
        var a = Parts[top[0]];
        var b = Parts[top[1]];
        string head = t.Scores[top[0]] >= 50f ? a.hiHead : a.loHead;
        string tail = t.Scores[top[1]] >= 50f ? b.hiTail : b.loTail;
        return head + tail;
    }
}
