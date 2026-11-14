"""Run explicit Unity QA suites on an isolated project; never infer model passes."""
import argparse,hashlib,json,os,subprocess,sys
from pathlib import Path

def main():
 p=argparse.ArgumentParser();p.add_argument('--catalog',action='store_true');p.add_argument('--project',type=Path);p.add_argument('--unity',type=Path);p.add_argument('--suite',default='core');p.add_argument('--out',type=Path);p.add_argument('--timeout',type=int,default=600);a=p.parse_args()
 catalog=json.loads((Path(__file__).resolve().parents[1]/'references/catalog.json').read_text(encoding='utf-8-sig'))
 if a.catalog:
  for t in catalog['techniques']:print(f"{t['id']}: {t['status']} ({len(t['existing_tests'])} existing test families)")
  print(f"Full-model cases: {len(catalog['models'])}, all require independent current validation")
  return 0
 if not a.project or not a.unity or not a.out:p.error('--project, --unity and --out are required for execution')
 project=a.project.resolve();out=a.out.resolve();out.mkdir(parents=True,exist_ok=True)
 suite=next((s for s in catalog['suites'] if s['id']==a.suite),None)
 if not suite:p.error('unknown suite: '+a.suite)
 if not a.unity.is_file() or not (project/'ProjectSettings/ProjectVersion.txt').is_file():p.error('Unity executable or project is missing')
 # Runtime source projects contain user settings/progress; never target an open editor.
 if (project/'Temp/UnityLockfile').exists():p.error('project is open or has a lock; use an isolated closed test copy')
 if not (project/'Assets/_Project/Scripts/Editor/OrigamiTechniqueBaseline.cs').is_file():p.error('baseline entrypoint is not installed in test project')
 source_hash=hashlib.sha256()
 for source in sorted((project/'Assets/_Project/Scripts').rglob('*.cs')):
  source_hash.update(source.relative_to(project).as_posix().encode());source_hash.update(source.read_bytes())
 log=out/(a.suite+'.log');progress=out/'isolated-progress.json'
 if progress.exists():p.error('output already contains progress; use a new output directory')
 env=os.environ.copy();env['FLYEDITOR_TEST_PROGRESS']=str(progress)
 args=[str(a.unity.resolve()),'-batchmode','-force-d3d11','-projectPath',str(project),'-executeMethod',suite['method'],'-logFile',str(log),'-quit']
 flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
 try:
  run=subprocess.run(args,env=env,timeout=a.timeout,creationflags=flags,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  contents=log.read_text(encoding='utf-8-sig',errors='replace') if log.exists() else ''
  ok=run.returncode==0 and suite['pass_marker'] in contents and 'error CS' not in contents
  result={'status':'PASS' if ok else ('BLOCKED_ENVIRONMENT' if not contents or ('Licensing' in contents and 'No valid Unity' in contents) else 'FAIL'),'suite':a.suite,'returncode':run.returncode,'log':str(log),'scope':'baseline suite only; no complete-model certification'}
 except subprocess.TimeoutExpired:
  result={'status':'BLOCKED_ENVIRONMENT','suite':a.suite,'reason':'timeout','log':str(log)}
 result['source_sha256']=source_hash.hexdigest();result['project']=str(project);result['unity_version']=(project/'ProjectSettings/ProjectVersion.txt').read_text().strip()
 (out/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False));return 0 if result['status']=='PASS' else 1
if __name__=='__main__':sys.exit(main())
