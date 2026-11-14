from pathlib import Path
import json, math

root=Path(__file__).resolve().parent.parent/'output/origami-three-player-flows-2026-11-12'
def read(session,name):return json.loads((root/('session-'+session)/name).read_text(encoding='utf-8-sig'))
def geom(session,id):return read(session,f'geometry-{id:03}.json')
def recipe(session,id):return read(session,f'recipe-{id:03}.json')['paperFolds']
def v(p):return [p.get(k,0.) for k in ['x','y','z']]
def matrix(m):return [[m['e'+str(i)+str(j)] for j in range(4)] for i in range(4)]
def add(a,b):return [a[i]+b[i] for i in range(3)]
def sub(a,b):return [a[i]-b[i] for i in range(3)]
def mul(a,k):return [x*k for x in a]
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def length(a):return math.sqrt(dot(a,a))
def cross(a,b):return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
def transform(m,p,direction=False):return [sum(m[i][j]*p[j] for j in range(3))+(0 if direction else m[i][3]) for i in range(3)]
def area(f):
 p=f['points'];return abs(sum(a['x']*b['y']-b['x']*a['y'] for a,b in zip(p,p[1:]+p[:1])))*.5
def material(f,p):return transform(matrix(f['material']),[p['x'],0,p['y']])
def world(f,p):return transform(matrix(f['frame']),[p['x'],0,p['y']])
def local(f,at):
 m=matrix(f['material']);x=at[0]-m[0][3];z=at[2]-m[2][3];det=m[0][0]*m[2][2]-m[0][2]*m[2][0]
 return {'x':(m[2][2]*x-m[0][2]*z)/det,'y':(-m[2][0]*x+m[0][0]*z)/det}
def inside(f,p):
 sign=0
 for a,b in zip(f['points'],f['points'][1:]+f['points'][:1]):
  c=(b['x']-a['x'])*(p['y']-a['y'])-(b['y']-a['y'])*(p['x']-a['x'])
  if abs(c)<.0005:continue
  if sign*c<0:return False
  sign=c
 return True
def samples(f):
 p=f['points'];c={'x':sum(q['x'] for q in p)/len(p),'y':sum(q['y'] for q in p)/len(p)}
 return p+[c]+[{'x':q['x']*.99+c['x']*.01,'y':q['y']*.99+c['y']*.01} for q in p]
def compare(expected,actual,pose=lambda p:p):
 maximum=0;count=0
 for f in expected:
  for p in samples(f):
   at=material(f,p);target=pose(world(f,p));found=False
   for g in actual:
    q=local(g,at)
    if not inside(g,q):continue
    found=True;error=length(sub(world(g,q),target));maximum=max(maximum,error);assert error<.003,('material mismatch',at,error)
   assert found,('missing material',at)
   count+=1
 return {'samples':count,'max_neutral_error':maximum}
def rotation(before,step):
 f=before['sheet'][step['anchor']];frame=matrix(f['frame']);n=step['creaseNormal'];ns=n['x']**2+n['y']**2
 pivot=transform(frame,[n['x']*step['creaseOffset']/ns,0,n['y']*step['creaseOffset']/ns]);axis=transform(frame,[-n['y']*step['side'],0,n['x']*step['side']],True);axis=mul(axis,1/length(axis));a=math.radians(step['degrees']);c=math.cos(a);s=math.sin(a)
 def rotate(p):
  q=sub(p,pivot);return add(pivot,add(add(mul(q,c),mul(cross(axis,q),s)),mul(axis,dot(axis,q)*(1-c))))
 return rotate
def transition(session,selected,committed):
 b=geom(session,selected);a=geom(session,committed);r=recipe(session,committed);assert b['moving'] and b['stationary']
 assert abs(sum(area(f) for f in a['sheet'])-121)<.01
 return {'session':session,'selected':selected,'committed':committed,'moving':compare(b['moving'],a['sheet'],rotation(b,r[-1])),'fixed':compare(b['stationary'],a['sheet'])}
def green(session,id):
 g=geom(session,id);p=g['meshVertices'];colors=g['meshColors'];ids=g['meshIndices'];total=0
 normals=[transform(matrix(g['rotation']),transform(matrix(f['frame']),[0,1,0],True),True) for f in g['moving']]
 for i in range(0,len(ids),3):
  a,b,c=ids[i:i+3];color=colors[a]
  if abs(color['r']-.62)>.001 or abs(color['g']-.83)>.001 or abs(color['b']-.73)>.001:continue
  normal=cross(sub(v(p[b]),v(p[a])),sub(v(p[c]),v(p[a])));size=length(normal)
  if size<1e-10 or not any(abs(dot(normal,n))/size/length(n)>.9999 for n in normals):continue
  total+=size*.5
 expected=2*sum(area(f) for f in g['moving']);assert abs(total-expected)<.015,(session,id,total,expected)
 return {'session':session,'id':id,'rendered_green_area':total,'expected_double_surface_area':expected}
def run(include_e=True):
 transitions=[transition(s,b,a) for s,b,a in [('b',3,4),('b',11,12),('b',13,15),('b',17,19),('c',3,4),('c',5,6),('c',8,10),('c',13,15),('d',6,11),('d',6,13)]]
 # A later re-selection uses the same source/side; compare cancellation and undo
 # against saved source, independently of the runtime's current region indices.
 unchanged=[compare(geom('b',4)['sheet'],geom('b',i)['sheet']) for i in [7,8,9,10]]
 unchanged += [compare(geom('d',4)['sheet'],geom('d',i)['sheet']) for i in [7,8,9,10,12]]
 unchanged += [compare(geom('c',6)['sheet'],geom('c',11)['sheet'])]
 reloads=[compare(geom('b',19)['sheet'],geom('b',21)['sheet']),compare(geom('b',19)['sheet'],geom('d',1)['sheet'])]
 for s,id,sign in [('b',7,0),('d',7,0)]:assert geom(s,id)['angle']==sign
 for f in geom('b',13)['moving']:
  for p in f['points']:assert material(f,p)[0]>=8.25-.0002,'upper opening selected underlying material'
 for f in geom('b',17)['moving']:
  for p in f['points']:assert material(f,p)[0]<=5.5+.0002,'underside opening selected upper material'
 for s,save,after in [('b',20,19),('c',16,15),('d',14,13)]:assert read(s,f'progress-{save:03}.json')['paperFolds']==recipe(s,after)
 greens=[green(s,id) for s,id in [('b',3),('b',6),('b',9),('b',11),('b',13),('b',17),('c',8),('c',13),('d',6),('d',9)]]
 if include_e:
  reloads.append(compare(geom('d',13)['sheet'],geom('e',1)['sheet']))
  reloads.append(compare(geom('e',8)['sheet'],geom('e',10)['sheet']))
  transitions.append(transition('e',7,8))
  unchanged += [compare(geom('e',1)['sheet'],geom('e',i)['sheet']) for i in [4,5,6]]
  greens += [green('e',i) for i in [2,3,5,7]]
  assert geom('e',5)['angle']==0 and len(recipe('e',6))==2
  assert read('e','progress-009.json')['paperFolds']==recipe('e',8)
 result={'status':'PASS','transitions':transitions,'cancel_undo_checks':unchanged,'actual_reload_checks':reloads,'rendered_selection_checks':greens,'material_units_tolerance':.003,'physical_shell_offset_excluded':True}
 (root/'measurements.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps({'status':'PASS','transitions':len(transitions),'unchanged_checks':len(unchanged),'reloads':len(reloads),'green_area_checks':len(greens)},ensure_ascii=False))
if __name__=='__main__':run('--without-e' not in __import__('sys').argv)
