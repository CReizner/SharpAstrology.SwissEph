#!/usr/bin/env python3
"""Compare all six body-state components against locally compiled Swiss C.

No production files are changed. Baseline/candidate commits are exported into
isolated temporary directories; all runners use net10.0 to keep runtime effects
out of the comparison. The report records tolerances and source hashes.

Example (candidate repository is a checkout containing the PR commit):
python3 Tests/tools/compare_bodies.py \
  --source ../Old/Experiments.RewriteSwissEph/swisseph-master --ephe ../ephe \
  --candidate-repository /path/to/pr-checkout --candidate-ref 052835c94c43650b2a9985110cbb9f404d346600 \
  --output /tmp/body-comparison
"""
import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import subprocess
import tempfile

REPO = Path(__file__).resolve().parents[2]
BODIES = {0: "Sun", 2: "Mercury", 3: "Venus", 4: "Mars", 5: "Jupiter", 6: "Saturn", 7: "Uranus", 8: "Neptune", 9: "Pluto", 11: "TrueNode"}
DATES = [2446100.5, 2450460.5, 2453560.5, 2457057.7896664813, 2460680.5]
MODES = {"standard": 256, "position_only": 0, "true_position": 256|16,
         "no_nutation": 256|64, "no_aberration": 256|1024, "j2000": 256|32,
         "equatorial": 256|2048, "icrs": 256|131072, "sidereal": 256|65536,
         "topocentric": 256|32768, "topocentric_no_aberration": 256|32768|1024,
         "heliocentric": 256|8, "speed3": 128}
AU_METERS = 149597870700.0
# Screening thresholds, not an assertion of universal model accuracy.
TOLERANCES = [.001, .001, 1.0, .001, .001, 1.0]
UNITS = ["arcsec", "arcsec", "m", "arcsec/day", "arcsec/day", "m/day"]
HARNESS = r"""using System.Text.Json;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Time;
using var swiss = new EphemerisContextBuilder().UseSwissEphFiles(args[0]).DisableMoshier().UseSiderealMode(SiderealMode.Lahiri).Build();
using var moshier = new EphemerisContextBuilder().UseSiderealMode(SiderealMode.Lahiri).Build();
while (Console.ReadLine() is {} line) {
    try {
        using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
        var flags = (EphemerisFlags)r.GetProperty("flags").GetInt32();
        var ctx = (flags & EphemerisFlags.SwissEph) != 0 ? swiss : moshier;
        var s = ctx.Bodies.Compute((CelestialBody)r.GetProperty("body").GetInt32(),
            new JulianDay(r.GetProperty("tt").GetDouble()), flags, new ObserverLocation(13.405, 52.52, 34));
        Console.WriteLine(JsonSerializer.Serialize(new { vector = new[] {s.Position.X,s.Position.Y,s.Position.Z,s.Velocity.X,s.Velocity.Y,s.Velocity.Z}, source = s.Source.ToString() }));
    } catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new {error=ex.ToString()})); }
}
"""


def run(command, **kwargs):
    result = subprocess.run(command, text=True, capture_output=True, **kwargs)
    if result.returncode:
        raise RuntimeError(f"{command}: {result.stdout}\n{result.stderr}")
    return result.stdout


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def polar(v):
    x,y,z,vx,vy,vz = v
    xy=x*x+y*y; rr=xy+z*z; radius=math.sqrt(rr)
    if xy == 0:
        raise ValueError("Undefined polar direction in case matrix")
    return [math.degrees(math.atan2(y,x))%360, math.degrees(math.atan2(z,math.sqrt(xy))), radius,
            math.degrees((x*vy-y*vx)/xy), math.degrees((vz*xy-z*(x*vx+y*vy))/(rr*math.sqrt(xy))),
            (x*vx+y*vy+z*vz)/radius]


def delta(a,b):
    return [((a[0]-b[0]+180)%360-180)*3600, (a[1]-b[1])*3600, (a[2]-b[2])*AU_METERS,
            (a[3]-b[3])*3600, (a[4]-b[4])*3600, (a[5]-b[5])*AU_METERS]


def archive(repo, ref, dest):
    dest.mkdir()
    with tempfile.TemporaryFile() as stream:
        subprocess.run(["git", "archive", ref], cwd=repo, stdout=stream, check=True)
        stream.seek(0)
        subprocess.run(["tar", "-x", "-C", str(dest)], stdin=stream, check=True)
    return run(["git", "rev-parse", ref], cwd=repo).strip()


def execute(project, label, temp, cases, ephe):
    h=temp/label; h.mkdir()
    (h/"Program.cs").write_text(HARNESS)
    from xml.sax.saxutils import escape
    (h/"Harness.csproj").write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="{escape(str(project))}" /></ItemGroup></Project>')
    build = run(["dotnet", "build", str(h/"Harness.csproj"), "-c", "Release", "-v", "quiet", "-p:TargetFramework=net10.0", "-p:LangVersion=14"])
    output=run(["dotnet", str(h/"bin/Release/net10.0/Harness.dll"), str(ephe)], input="".join(json.dumps(c)+"\n" for c in cases))
    rows=[json.loads(line) for line in output.splitlines()]
    if len(rows)!=len(cases): raise RuntimeError(f"{label}: missing runner output")
    print(f"{label}: {len(rows)} cases", flush=True)
    return rows,build


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--ephe", type=Path, required=True)
    parser.add_argument("--baseline-ref", default="342a57997c1b987e7949acc98897c8b73d05939a")
    parser.add_argument("--candidate-repository", type=Path)
    parser.add_argument("--candidate-ref", default="HEAD")
    parser.add_argument("--output", type=Path, required=True)
    args=parser.parse_args(); source=args.source.resolve(); ephe=args.ephe.resolve(); out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    cases=[]
    for backend in (2,4):
        for body,name in BODIES.items():
            for tt in DATES:
                for mode,flags in MODES.items():
                    if body==11 and mode in ("heliocentric","topocentric","topocentric_no_aberration"): continue
                    if body==0 and mode=="heliocentric": continue
                    cases.append(dict(body=body,bodyName=name,tt=tt,mode=mode,flags=backend|flags|4096))
    provenance={"baselineCommit": args.baseline_ref,"workingCommit":run(["git","rev-parse","HEAD"],cwd=REPO).strip(),
        "workingSourceSha256":{str(p.relative_to(REPO)):sha(p) for p in sorted(REPO.rglob("*.cs")) if not ({"bin","obj"}&set(p.relative_to(REPO).parts))},
        "workingProjectSha256":sha(REPO/"SharpAstrology.SwissEph.csproj"),"sourceSha256":{p.name:sha(p) for p in sorted(source.glob("*.c"))+sorted(source.glob("*.h"))},
        "ephemerisSha256":{f:sha(ephe/f) for f in ("sepl_18.se1","semo_18.se1")},"dotnet":run(["dotnet","--version"]).strip()}
    with tempfile.TemporaryDirectory(prefix="body-comparison-") as temporary:
        temp=Path(temporary);libpath=temp/"libswe.so"
        files=["swedate.c","swehouse.c","swejpl.c","swemmoon.c","swemplan.c","sweph.c","swephlib.c","swecl.c","swehel.c"]
        run(["cc","-O2","-fPIC","-shared","-o",str(libpath),*files,"-lm"],cwd=source)
        lib=C.CDLL(str(libpath));lib.swe_set_ephe_path.argtypes=[C.c_char_p];lib.swe_set_ephe_path(str(ephe).encode())
        lib.swe_set_topo.argtypes=[C.c_double]*3;lib.swe_set_topo(13.405,52.52,34)
        lib.swe_set_sid_mode.argtypes=[C.c_int,C.c_double,C.c_double];lib.swe_set_sid_mode(1,0,0)
        lib.swe_calc.argtypes=[C.c_double,C.c_int,C.c_int,C.POINTER(C.c_double),C.c_char_p]
        version=C.create_string_buffer(256);lib.swe_version(version);provenance["oracleVersion"]=version.value.decode()
        oracle=[]
        for case in cases:
            vector=(C.c_double*6)();error=C.create_string_buffer(1024)
            returned=lib.swe_calc(case["tt"],case["body"],case["flags"],vector,error)
            if returned<0 or returned&7 != case["flags"]&7 or error.value: raise RuntimeError((case,returned,error.value))
            oracle.append(dict(vector=list(vector),polar=polar(vector),returnedFlags=returned))
        baseline=temp/"baseline-source";provenance["baselineCommit"]=archive(REPO,args.baseline_ref,baseline)
        projects={"baseline":baseline/"SharpAstrology.SwissEph.csproj"}
        if args.candidate_repository:
            candidate=temp/"candidate-source";provenance["candidateCommit"]=archive(args.candidate_repository.resolve(),args.candidate_ref,candidate)
            projects["pr"]=candidate/"SharpAstrology.SwissEph.csproj"
        projects["working"]=REPO/"SharpAstrology.SwissEph.csproj"
        values={}
        for label,project in projects.items():
            values[label],build=execute(project,label,temp,cases,ephe);(out/(label+"-build.log")).write_text(build)
        rows=[]
        for index,case in enumerate(cases):
            row={"case":case,"oracle":oracle[index],"results":{}}
            for label,results in values.items():
                actual=results[index];entry=dict(actual)
                if "error" not in actual:
                    expectedSource="SwissEph" if case["flags"]&7==2 else "Moshier"
                    if actual["source"]!=expectedSource: raise RuntimeError((case,label,actual["source"]))
                    entry["polar"]=polar(actual["vector"]);entry["delta"]=delta(entry["polar"],oracle[index]["polar"])
                    entry["cartesianDelta"]=[a-b for a,b in zip(actual["vector"],oracle[index]["vector"])]
                    entry["withinTolerance"]=all(abs(v)<=t for v,t in zip(entry["delta"],TOLERANCES))
                row["results"][label]=entry
            rows.append(row)
        report={"provenance":provenance,"components":["longitude","latitude","distance","longitudeSpeed","latitudeSpeed","radialSpeed"],"units":UNITS,"tolerances":TOLERANCES,
            "reference":"BodyService Cartesian outputs versus swe_calc with XYZ. Polar errors are computed by the same conversion on both Cartesian vectors; C's independently differenced spherical SPEED3 output is not mixed into this comparison.",
            "observer":[13.405,52.52,34],"siderealMode":"Lahiri","rows":rows}
        (out/"results.json").write_text(json.dumps(report,indent=2)+"\n")
        summary={"cases":len(rows),"versions":{},"groups":[]}
        for label in values:
            valid=[r["results"][label] for r in rows if "error" not in r["results"][label]]
            summary["versions"][label]={"withinTolerance":sum(v["withinTolerance"] for v in valid),"errors":len(rows)-len(valid),"maxAbsDelta":[max(abs(v["delta"][i]) for v in valid) for i in range(6)]}
        for backend in (2,4):
            for mode in MODES:
                subset=[r for r in rows if r["case"]["flags"]&7==backend and r["case"]["mode"]==mode]
                summary["groups"].append(dict(source=backend,mode=mode,cases=len(subset),versions={label:{"withinTolerance":sum(r["results"][label].get("withinTolerance",False) for r in subset),"maxAbsDelta":[max(abs(r["results"][label]["delta"][i]) for r in subset if "delta" in r["results"][label]) for i in range(6)]} for label in values}))
        (out/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
        print(json.dumps({"cases":summary["cases"],"versions":summary["versions"]},indent=2))


if __name__=="__main__":
    main()
