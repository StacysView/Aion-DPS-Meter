using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The "Defiance" (Dissipation de choc) heal of exactly 100,000 is a placeholder, not a heal (Stacy, 2026-11-02): it must not swell the healing bar.</summary>
public static class SelfCheckPlaceholderHeal
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Placeholder heal of the break-free skill:");
        const int Defiance = 11260240;
        bool ok = Aion2SkillNames.IsPlaceholderHeal(Defiance, 100_000) && !Aion2SkillNames.IsPlaceholderHeal(Defiance, 8_677)
            && !Aion2SkillNames.IsPlaceholderHeal(17160027, 100_000); // a real heal skill of exactly 100,000 stays
        Console.WriteLine($"  -> Defiance with 100,000 is dropped, with a real amount or another skill it is kept: {ok}");
        return ok;
    }
}
