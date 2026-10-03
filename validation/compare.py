#!/usr/bin/env python3
"""Differential Swiss C oracle tests. No product mapping or external packages."""
import argparse,ctypes as C,datetime,hashlib,json,os,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--library',required=True);p.add_argument('--ephe',required=True);p.add_argument('--before',required=True);p.add_argument('--after',default='HEAD');p.add_argument('--patch',required=True);p.add_argument('--output',required=True);a=p.parse_args()
cases=json.loads((ROOT/'validation'/('cases-'+a.patch+'.json')).read_text())
lib=C.CDLL(a.library);lib.swe_set_ephe_path.argtypes=[C.c_char_p];lib.swe_set_ephe_path(a.ephe.encode());lib.swe_calc.argtypes=[C.c_double,C.c_int,C.c_int,C.POINTER(C.c_double),C.c_char_p];lib.swe_utc_to_jd.argtypes=[C.c_int,C.c_int,C.c_int,C.c_int,C.c_int,C.c_double,C.c_int,C.POINTER(C.c_double),C.c_char_p]
oracle=[]
for c in cases:
 err=C.create_string_buffer(1024)
 if 'utc' in c:
  d=datetime.datetime.fromisoformat(c['utc'].replace('Z','+00:00'));v=(C.c_double*2)();r=lib.swe_utc_to_jd(d.year,d.month,d.day,d.hour,d.minute,d.second+d.microsecond/1e6,1,v,err);assert r==0 and not err.value;oracle.append({'ut1':v[1]})
 else:
  v=(C.c_double*6)();r=lib.swe_calc(c['tt'],c['body'],c['flags'],v,err);assert r>=0 and r&7==2 and not err.value,(r,err.value);oracle.append({'longitude':v[0],'speed':v[3]})
def run(ref):
 dest=Path(tempfile.mkdtemp(prefix='swiss-parity-'));archive=subprocess.check_output(['git','archive',ref],cwd=ROOT);subprocess.run(['tar','-x','-C',str(dest)],input=archive,check=True)
 harness=Path(tempfile.mkdtemp(prefix='swiss-harness-'));(harness/'Program.cs').write_text((ROOT/'validation/Harness.cs.txt').read_text());(harness/'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'+str(dest/'SharpAstrology.SwissEph.csproj')+'" /></ItemGroup></Project>')
 dotnet=os.environ.get('DOTNET','dotnet');subprocess.run([dotnet,'build',str(harness/'Harness.csproj'),'-c','Release','-v','quiet','-m:1'],check=True,stdout=subprocess.DEVNULL)
 result=subprocess.run([dotnet,str(harness/'bin/Release/net10.0/Harness.dll'),a.ephe],input='\n'.join(map(json.dumps,cases))+'\n',text=True,capture_output=True,check=True);rows=[json.loads(l) for l in result.stdout.splitlines()];assert len(rows)==len(cases);return rows
before=run(a.before);after=run(a.after);rows=[]
guards=ROOT/'validation'/('guards-'+a.patch+'.cs.txt')
if guards.exists():
 dest=Path(tempfile.mkdtemp(prefix='swiss-guard-source-'));subprocess.run(['tar','-x','-C',str(dest)],input=subprocess.check_output(['git','archive',a.after],cwd=ROOT),check=True)
 h=Path(tempfile.mkdtemp(prefix='swiss-guards-'));(h/'Program.cs').write_text(guards.read_text());(h/'Guard.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>SharpAstrology.SwissEphemerides.IntegrationTests</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include="'+str(dest/'SharpAstrology.SwissEph.csproj')+'" /></ItemGroup></Project>')
 dotnet=os.environ.get('DOTNET','dotnet');subprocess.run([dotnet,'run','--project',str(h/'Guard.csproj'),'-c','Release'],check=True,stdout=subprocess.DEVNULL)

for case,b,n,o in zip(cases,before,after,oracle):
 if 'utc' in case:
  br=(b['ut1']-o['ut1'])*86400;ar=(n['ut1']-o['ut1'])*86400;assert abs(ar)<0.0001;assert abs(n['roundtripSeconds'])<0.0001 and n['utcKind']=='Utc';unit='seconds'
 else:
  residual=lambda x:((x-o['longitude']+180)%360-180)*3600000
  br=residual(b['longitude']);ar=residual(n['longitude']);unit='mas'
  if case.get('control'):assert n['vector']==b['vector'],('control changed',case)
  else:assert abs(ar)<(0.01 if a.patch=='b' else 0.001),(case,ar)
 rows.append({'case':case,'before':b,'after':n,'oracle':o,'beforeResidual':br,'afterResidual':ar,'residualUnit':unit})
assert any(abs(r['beforeResidual'])>abs(r['afterResidual'])+0.01 for r in rows if not r['case'].get('control')),'negative baseline did not detect bug'
result={'patch':a.patch,'beforeCommit':subprocess.check_output(['git','rev-parse',a.before],cwd=ROOT,text=True).strip(),'afterCommit':subprocess.check_output(['git','rev-parse',a.after],cwd=ROOT,text=True).strip(),'oracleLibrarySha256':hashlib.sha256(Path(a.library).read_bytes()).hexdigest(),'ephemerisSha256':{name:hashlib.sha256((Path(a.ephe)/name).read_bytes()).hexdigest() for name in ['sepl_18.se1','semo_18.se1']},'passed':True,'cases':rows}
Path(a.output).write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({'patch':a.patch,'comparisons':len(rows),'passed':True}))
