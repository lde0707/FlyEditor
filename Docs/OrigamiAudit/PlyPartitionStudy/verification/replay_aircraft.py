import json,time
from pathlib import Path
source=Path('C:/Users/doun0/AppData/Local/Temp/FlyPlyPlaneInput12')
target=Path('C:/Users/doun0/AppData/Local/Temp/FlyPlyFinalBInput12')
deadline=time.monotonic()+90
while not (target/'ready.txt').exists():
 if time.monotonic()>deadline:raise SystemExit('Editor not ready')
 time.sleep(.5)
time.sleep(4)
for line in (source/'input.jsonl').read_text(encoding='utf-8').splitlines():
 command=json.loads(line);pending=target/'pending.json';pending.write_text(json.dumps(command),encoding='utf-8')
 for retry in range(100):
  try:pending.replace(target/'command.json');break
  except PermissionError:time.sleep(.05)
 else:raise SystemExit('Command busy')
 deadline=time.monotonic()+45
 while True:
  try:
   if (target/'done.txt').read_text(encoding='utf-8-sig').strip()==str(command['id']):break
  except OSError:pass
  if time.monotonic()>deadline:raise SystemExit('Input timed out '+str(command['id']))
  time.sleep(.2)
 print((target/'status.txt').read_text(encoding='utf-8-sig'),flush=True)
