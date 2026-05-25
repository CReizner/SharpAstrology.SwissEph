// Ported from swisseph-master/sweph.h struct sid_data and the per-context
// state managed by swe_set_sid_mode (sweph.c#L2861-L2928).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// Resolved sidereal-mode state. Replaces the C-side
/// <c>swed.sidd</c> globals with an immutable record. Construct via
/// <see cref="SiderealService.SetMode(SiderealMode, double, double, SiderealFlags)"/>;
/// the service exposes the current configuration through its
/// <c>Configuration</c> property.
/// </summary>
/// <param name="Mode">Selected mode (one of the predefined values or
/// <see cref="SiderealMode.UserDefined"/>).</param>
/// <param name="Flags">Modifier flags (<see cref="SiderealFlags"/>).</param>
/// <param name="T0">Reference epoch in Julian Day (TT, unless
/// <see cref="SiderealFlags.UserT0IsUt"/> is set on a user-defined mode).</param>
/// <param name="AyanT0">Ayanamsha value at <paramref name="T0"/>, in degrees.</param>
/// <param name="T0IsUt">Whether <paramref name="T0"/> is in UT (true) or TT (false).
/// Mirrors the C struct <c>sid_data.t0_is_UT</c>.</param>
/// <param name="PrecOffset">Precession model that was used to define this
/// ayanamsha. <c>null</c> when no offset correction applies (the C source
/// stores 0 or -1 for both meanings).</param>
public readonly record struct SiderealConfiguration(
    SiderealMode Mode,
    SiderealFlags Flags,
    double T0,
    double AyanT0,
    bool T0IsUt,
    SharpAstrology.SwissEphemerides.Domain.Frames.PrecessionModel? PrecOffset)
{
    /// <summary>
    /// Default configuration when no <c>SetMode</c> call has been made.
    /// Mirrors the C-side fallback in <c>swi_get_ayanamsa_ex</c>
    /// (<c>sweph.c#L3047-L3048</c>): "if (!swed.ayana_is_set)
    /// swe_set_sid_mode(SE_SIDM_FAGAN_BRADLEY, 0, 0);".
    /// </summary>
    public static SiderealConfiguration Default => FromPreset(SiderealMode.FaganBradley, SiderealFlags.None);

    internal static SiderealConfiguration FromPreset(SiderealMode mode, SiderealFlags flags)
    {
        var preset = AyanamshaTable.Presets[(int)mode];
        return new SiderealConfiguration(
            mode,
            flags,
            preset.T0,
            preset.AyanT0,
            preset.T0IsUt,
            preset.PrecOffset);
    }
}
