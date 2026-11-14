namespace AionDPS.Ui;

/// <summary>
/// The master volume of every sound the meter makes (Stacy, 2026-11-15: the sounds other than his end-of-boss ones were always too loud). It multiplies the volume each
/// sound already has (the cube volume, the end-of-boss volume), so the two sliders keep their meaning. 70 % unless chosen otherwise: 30 % quieter than before.
/// </summary>
public static class SoundMix
{
    public const double DefaultMaster = 0.7;

    /// <summary>0 to 1; set from the settings when they are loaded.</summary>
    public static double Master { get; set; } = DefaultMaster;

    /// <summary>The volume a player really gets for a sound whose own volume is <paramref name="volume"/>.</summary>
    public static double Scale(double volume) => Math.Clamp((double.IsFinite(volume) ? volume : 0.5) * Master, 0, 1);
}
