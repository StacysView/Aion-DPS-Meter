using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// How far one hit of a skill lowers a monster's stagger bar (the game's "Groggy guard energy"). The values are the game's own: the client's skill
/// tables name them in the effect type GroggyGuardDecreaseEnergy, 3,329 skills have one (assets/aion2/stagger/skill_stagger.json, SkeeveTV, MIT,
/// his release 1.18.2: checked against 109 skills seen in recordings, all equal - and equal to what this meter had measured on Ludra: Dark Crush 5,
/// Judgment 5, Combustion 3, Elemental Fusion 10). A buff of the attacker (a Gladiator's) raises the drop by half, rounded down.
/// </summary>
public static class Aion2StaggerTable
{
    private static Dictionary<int, int>? _values;

    /// <summary>A test sets its own table.</summary>
    public static Dictionary<int, int>? Override { get; set; }

    /// <summary>The drop one hit of the skill causes; 0 for a skill without one or not known. A skill id the table lacks is looked up without its
    /// last digit too (the variants of one skill rank).</summary>
    public static int ValueOf(int skillId)
    {
        if (skillId <= 0)
        {
            return 0;
        }

        Dictionary<int, int> values = Override ?? (_values ??= Load());
        if (values.TryGetValue(skillId, out int value) || values.TryGetValue(skillId / 10 * 10, out value))
        {
            return value;
        }

        // Another variant of the same skill (the same first four digits after the class: 1528xxxx): the value all its listed variants share.
        return (Override is null ? Families() : FamiliesOf(values)).GetValueOrDefault(skillId / 10000);
    }

    private static Dictionary<int, int>? _families;

    private static Dictionary<int, int> Families() => _families ??= FamiliesOf(_values ??= Load());

    private static Dictionary<int, int> FamiliesOf(Dictionary<int, int> values) => values
        .GroupBy(kv => kv.Key / 10000)
        .Where(g => g.Select(kv => kv.Value).Distinct().Count() == 1)
        .ToDictionary(g => g.Key, g => g.First().Value);

    private static Dictionary<int, int> Load()
    {
        var values = new Dictionary<int, int>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "stagger", "skill_stagger.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty skill in doc.RootElement.GetProperty("skills").EnumerateObject())
                {
                    if (int.TryParse(skill.Name, out int id) && skill.Value.TryGetInt32(out int value))
                    {
                        values[id] = value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            // No table: the amounts are learned from the fight, as before.
        }

        return values;
    }
}

