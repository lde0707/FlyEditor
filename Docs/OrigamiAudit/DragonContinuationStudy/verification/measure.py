from pathlib import Path
import importlib.util,json
cwd=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('material_measure',Path(__file__).with_name('material_measure.py'));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.root=Path(__file__).resolve().parents[1]/'evidence'
checks=[m.transition(s,b,a) for s,b,a in [('a',3,4),('b',9,10),('c',3,5),('c',6,7)]]
unchanged=m.compare(m.geom('a',6)['sheet'],m.geom('a',10)['sheet'])
round_trip=m.compare(m.geom('c',1)['sheet'],m.geom('c',7)['sheet'])
reload=m.compare(m.geom('c',7)['sheet'],m.geom('d',1)['sheet'])
before=m.read('b','material-004.json');preview=m.read('b','material-006.json');after=m.geom('b',7)['sheet']
def samples_match(samples):
 maximum=0
 for p in samples:
  at=m.v(p['material']);expected=m.v(p['world']);found=False
  for f in after:
   q=m.local(f,at)
   if not m.inside(f,q):continue
   found=True;error=m.length(m.sub(m.world(f,q),expected));assert error<.003,(at,error);maximum=max(maximum,error)
  assert found,at
 return {'samples':len(samples),'max_neutral_error':maximum}
fixed=samples_match(before['fixedFaces']);preview_commit=samples_match(preview['rendered'])
original=json.loads((Path(__file__).resolve().parents[1]/'fixtures/opposite-petal.json').read_text());actual=m.recipe('b',7)[-1]
deltas={k:max(abs(original[k][a]-actual[k][a]) for a in ['x','y']) for k in ['pullTip','pullStop','pullLeft','pullRight']}
assert max(deltas.values())<.00001
assert m.recipe('c',5)[-1]['contactPacket'] and len(m.recipe('c',7))==14
assert m.read('c','progress-008.json')['paperFolds']==m.recipe('d',1)
result={'status':'PASS','rigid_transitions':checks,'failed_attempt_unchanged':unchanged,'packet_round_trip':round_trip,'actual_cold_reload':reload,'coordinated_fixed_material':fixed,'coordinated_preview_commit':preview_commit,'original_handle_coordinate_deltas':deltas,'fold_history':{'initial':8,'before_fix':10,'opposite_petal':11,'front_flap':12,'packet_contact':13,'packet_reopened':14},'complete_model':False,'material_neutral_surface_only':True}
(m.root/'measurements.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'status':'PASS','rigid_transitions':len(checks),'coordinated_fixed_samples':fixed['samples'],'preview_commit_samples':preview_commit['samples'],'fold_count':14,'complete_model':False}))
