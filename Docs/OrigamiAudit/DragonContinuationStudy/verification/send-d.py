import sys,json,time
from pathlib import Path
r=Path('C:/Users/doun0/OneDrive/Documents/ChatGPT/FlyEditor/output/dragon-continuation-2026-11-12/session-d');r.mkdir(parents=True,exist_ok=True)
n=int(sys.argv[1]);label=sys.argv[2];actions=[]
for value in sys.argv[3:]:
 k,_,v=value.partition(':')
 if k=='k':actions.extend([{'kind':'key','key':v,'down':True},{'kind':'key','key':v,'down':False}]);continue
 if k in ('kd','ku'):actions.append({'kind':'key','key':v,'down':k=='kd'});continue
 if k=='w':actions.append({'kind':'wait','wait':float(v)});continue
 a=[float(x) for x in v.split(',')];event={'kind':{'p':'paper','s':'screen','u':'ui','m':'move','o':'orbit','z':'scroll'}[k],'x':a[0],'y':a[1],'wait':.14}
 if k=='z':event['scroll']=a[2]
 else:event['down']=bool(a[2]) if len(a)>2 else False
 actions.append(event)
pending=r/'pending.json';pending.write_text(json.dumps({'id':n,'label':label,'actions':actions,'quit':label=='quit'}),encoding='utf-8')
for attempt in range(100):
 try:pending.replace(r/'command.json');break
 except PermissionError:time.sleep(.05)
else:raise SystemExit('Command file remained busy')
until=time.monotonic()+45
while time.monotonic()<until:
 try:
  if (r/'done.txt').read_text(encoding='utf-8-sig').strip()==str(n):print((r/'status.txt').read_text(encoding='utf-8-sig'));break
 except (OSError,ValueError):pass
 time.sleep(.2)
else:raise SystemExit('Timed out')
