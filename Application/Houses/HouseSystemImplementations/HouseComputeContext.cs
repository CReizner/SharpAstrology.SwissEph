// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

/// <summary>
/// Per-call computation context shared by all <see cref="IHouseSystemImpl"/>
/// implementations. Mirrors the local variables at the top of
/// <c>CalcH</c> (swehouse.c#L935-L987): every system needs sin/cos/tan of
/// ε and φ, the MC, the unfix-polar-shifted Asc, and a couple of working
/// spans that have to be writable in-place.
/// </summary>
internal ref struct HouseComputeContext
{
    public double Th;            // sidereal time / armc, deg
    public double Fi;            // geo lat, deg (clamped to ±(90-VERY_SMALL))
    public double Ekl;           // obliquity, deg
    public double Sine;          // sin(ε)
    public double Cose;          // cos(ε)
    public double Tane;          // tan(ε)
    public double Tanfi;         // tan(φ)

    public double Mc;
    public double Ac;            // = Asc1(armc + 90, lat, ...) — written by HouseService
    public double McSpeed;
    public double AcSpeed;
    public double ArmcSpeed;     // ARMCS = const for tropical houses
    public bool DoSpeed;
    public bool DoHspeed;

    public double SunDeclination;        // input for Sunshine ('I'/'i') systems
    public bool DoInterpol;              // set by systems that need numerical-derivative speeds
    public bool FellBackToPorphyry;
    public string? Warning;

    /// <summary>1-indexed cusps. Length must be ≥ 13 (≥ 37 for Gauquelin).</summary>
    public Span<double> Cusps;
    public Span<double> CuspSpeeds;
    public Span<double> Ascmc;       // length 8 — asc..polasc
    public Span<double> AscmcSpeed;  // length 8 — same layout

    public static HouseComputeContext Build(
        double armcDeg, double geolatDeg, double obliquityDeg,
        Span<double> cusps, Span<double> ascmc,
        Span<double> cuspSpeeds, Span<double> ascmcSpeeds,
        double sunDecDeg)
    {
        var dr = AstronomicalConstants.DegToRad;
        var ekl = obliquityDeg;
        var fi = geolatDeg;
        if (System.Math.Abs(System.Math.Abs(fi) - 90) < HouseAscendantMath.VerySmall)
            fi = fi < 0 ? -90 + HouseAscendantMath.VerySmall : 90 - HouseAscendantMath.VerySmall;

        return new HouseComputeContext
        {
            Th = armcDeg,
            Fi = fi,
            Ekl = ekl,
            Sine = System.Math.Sin(ekl * dr),
            Cose = System.Math.Cos(ekl * dr),
            Tane = System.Math.Tan(ekl * dr),
            Tanfi = System.Math.Tan(fi * dr),
            DoSpeed = !cuspSpeeds.IsEmpty || !ascmcSpeeds.IsEmpty,
            DoHspeed = !cuspSpeeds.IsEmpty,
            ArmcSpeed = HouseAscendantMath.ArmcDegreesPerDay,
            SunDeclination = sunDecDeg,
            Cusps = cusps,
            CuspSpeeds = cuspSpeeds,
            Ascmc = ascmc,
            AscmcSpeed = ascmcSpeeds,
        };
    }
}
