# SharpAstrology.SwissEph 0.4.0

A complete rewrite. **0.3.0 was a thin wrapper around
[SwissEphNet](https://github.com/ygrenier/SwissEphNet). 0.4.0 is a pure-C# port of the Swiss Ephemeris**, verified against the C library `swisseph` (v2.10.3).

The familiar `SwissEphemeridesService` factory and `IEphemerides`
adapter are preserved. Existing consumers can update
the package version and keep compiling and running.

## TL;DR

- ✅ **Drop-in compatible**: same `SwissEphemeridesService.CreateContext()` →
  `IEphemerides` surface.
- ✅ **No native dependency**: a managed `.dll` you can ship anywhere
  .NET runs — Linux, macOS, Windows, containers, single-file publish.
- ✅ **Faster and less allocations** then SwissEphNet for normal use cases.

## Why a rewrite?
The SwissEphNet library generated an extremely large number of allocations, which is problematic for projects like SharpAstrology.HumanDesign. Some charts required as much as 22 MB of memory.

0.4.0 is an idiomatic `net10.0` port whose hot path is
zero-allocation, whose state is held on an explicit `EphemerisContext`
you own, and whose composition surface is fluent and immutable.

## What's new

### Builder path (new)

```csharp
using SharpAstrology.SwissEphemerides;

using var ctx = new EphemerisContextBuilder()
    .UseSwissEphFiles("/usr/share/sweph")   // optional .se1 source
    .UseJplFile("/data/de441.eph")          // optional JPL source
    .UseFixedStarCatalog("/data/sefstars.txt")
    .WithLunarNodeStrategy(LunarNodeStrategy.MeanNode)
    .Build();

// Hand to SharpAstrology.Base consumers...
IEphemerides eph = ctx.AsEphemerides();

// ...or use the rich service surface directly.
var jdUt   = ctx.Calendar.UtcToJulianDay(DateTime.UtcNow);
var sun    = ctx.Bodies.Compute(CelestialBody.Sun, jdUt, EphemerisFlags.Speed);
var houses = ctx.Houses.Compute(jdUt, 52.520, 13.405, HouseSystem.Placidus);
```

Service surface on `EphemerisContext`:

| Service           | Replaces                                                |
|-------------------|---------------------------------------------------------|
| `Calendar`        | `swe_julday`, `swe_deltat`, `swe_sidtime`, equation-of-time |
| `Bodies`          | `swe_calc` / `swe_calc_ut`                              |
| `Houses`          | `swe_houses_ex2` — 21 house systems                     |
| `Sidereal`        | `swe_set_sid_mode` + ayanamsha — per-context, no globals |
| `Phenomena`       | Eclipses, occultations, rise/set, heliacal, Gauquelin, refraction |
| `FixedStars`      | `swe_fixstar2*` (opt-in via `UseFixedStarCatalog`)      |
| `NodesAndApsides` | `swe_nod_aps`, `swe_get_orbital_elements`               |
| `AsEphemerides()` | `SharpAstrology.Base.IEphemerides` adapter              |

### Per-context configuration (no more globals)

Sidereal mode, ΔT overrides, JPL kernel, fixed-star catalogue, segment
cache size, lunar-node strategy — all set on the builder, all immutable
after `Build()`, no process-wide mutable state. Multiple contexts can
run side by side with different configurations in the same process.

## Performance vs. 0.3.0

End-to-end Human Design chart construction (root-find for design date
+ 26 planetary lookups) on AMD Ryzen 9 5900X, .NET 10, ServerGC.

| Source  | 0.3.0 (µs) | 0.4.0 (µs) | Speedup | 0.3.0 alloc | 0.4.0 alloc | Alloc ratio |
|---------|-----------:|-----------:|--------:|------------:|------------:|------------:|
| Moshier |     6,063  |     4,208  | **1.44×** |   367.07 KB |    12.87 KB |   ~28× less |
| Swiss   |     1,761  |     1,336  | **1.32×** |   738.87 KB |    12.87 KB |   ~57× less |
| Jpl     |    17,545  |     1,338  | **13.11×**|    22.49 MB |    12.87 KB | ~1,786× less|

Cold context creation + one chart + dispose (per-request models):

| Source  | 0.3.0 (µs) | 0.4.0 (µs) | Speedup | 0.3.0 alloc | 0.4.0 alloc |
|---------|-----------:|-----------:|--------:|------------:|------------:|
| Moshier |       904  |       778  | 1.16×   |   365.19 KB |    14.21 KB |
| Swiss   |       616  |       553  | 1.11×   |   887.46 KB |    58.86 KB |
| Jpl     |     8,533  |       637  | **13.39×** |  23,401 KB  |    97.99 KB |

## Lunar Nodes

There have been reports that the lunar nodes do not always match those calculated by online tools. This implementation is based on the latest version of swisseph (2.10.3)

## Breaking changes

| Change | Impact | Migration |
|--------|--------|-----------|
| `TargetFramework` is `net10.0` (was `net8.0`) | Won't load in net8.0 host | Pin 0.3.0 if you can't update runtime |
| `SharpAstrology.Base` minimum is 0.12.0 | Transitive | Update or pin 0.3.0 |
| AGPL-3.0 license file is now `LICENSE.AGPL-3.0.txt` (was `LICENSE.md`) | None at runtime | Update any license-scanner allowlists |
| `Planets.NorthNode` default = `SE_TRUE_NODE` | Lunar-node longitude can change vs. 0.3.0 | Either accept the new default, or call `EphemerisContextBuilder.WithLunarNodeStrategy(LunarNodeStrategy.MeanNode)` for `SE_MEAN_NODE`. The exact 0.3.0 routine (`swe_nod_aps` with `SE_NODBIT_OSCU`) is still reachable via `ctx.NodesAndApsides` if you need bit-for-bit parity. |
| Public types of `SwissEph` from SwissEphNet no longer exposed | Source-incompatible if you reached past the `IEphemerides` surface | Switch to `EphemerisContext.Bodies` / `Houses` / `Phenomena` |

## Licensing

Unchanged: this port is **AGPL-3.0**
([LICENSE.AGPL-3.0.txt](LICENSE.AGPL-3.0.txt)), as a derivative work of
the Swiss Ephemeris C library. Users without a Swiss Ephemeris
professional license must comply with the AGPL terms of the upstream
project too — see [LICENSE.SwissEph.txt](LICENSE.SwissEph.txt). The
`.se1` / `.eph` / `sefstars.txt` data files keep their original
licenses and are not redistributed in this repo.

## Install

```shell
dotnet add package SharpAstrology.SwissEph --version 0.4.0
```
