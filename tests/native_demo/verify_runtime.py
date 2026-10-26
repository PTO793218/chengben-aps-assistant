"""Local integration acceptance. Only the approved no-network Docker replica is used."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--docker',required=True);args=parser.parse_args()
root=Path(args.root).resolve();project=Path(__file__).resolve().parents[2]
config_path=project/'agent-backend/Data/native-demo.json';config=json.loads(config_path.read_text(encoding='utf-8'));ids=config['baselineOrderIds']
records={};jobs={}
for name,request in [('first',{'action':'baseline','orderIds':ids}),('second',{'action':'baseline','orderIds':list(reversed(ids))}),('mrp',{'action':'mrp','orderIds':ids})]:
 token=uuid.uuid4().hex;jobs[name]=(token,request);path=root/'requests'/f'{token}.json';tmp=path.with_suffix('.tmp');tmp.write_text(json.dumps(request),encoding='utf-8');tmp.replace(path)
deadline=time.monotonic()+150;max_age=0
while any(not (root/'responses'/f'{token}.json').exists() for token,_ in jobs.values()):
 assert time.monotonic()<deadline,'local worker timeout'
 try:max_age=max(max_age,time.time()-float((root/'heartbeat').read_text(encoding='ascii')))
 except ValueError:pass
 time.sleep(.2)
for name,(token,request) in jobs.items():
 response=json.loads((root/'responses'/f'{token}.json').read_text(encoding='utf-8'));assert response['success'],response
 data=json.loads((root/'results'/f'{token}.json').read_text(encoding='utf-8'));assert data['inputOrderIds']==request['orderIds'];assert data['resultId']==token;assert data['rolledBack'];assert {x['sale_order_no'] for x in data['orders']}==set(ids)
 records[name]={'resultId':token,'orderHead':data['inputOrderIds'][0],'planRows':len(data.get('plans',[])),'mrpRows':len(data.get('mrp',[]))}
 if name=='mrp':
  assert data['sourceVersion']=='' and data['planSourceVersion']=='261016902'
  row=next(x for x in data['mrp'] if x['sale_order_no']=='Z9900006' and x['material_no']=='C.02.02.0031');assert row['remain_occupy_num']==500 and row['supply_order_occupy_num']==2000 and row['lack_num']==3520.7
  (project/'agent-backend/Data/native-mrp-baseline.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8');config['baselineMrpResultId']=token;config_path.write_text(json.dumps(config,ensure_ascii=False,indent=2),encoding='utf-8')
assert max_age<5,('heartbeat stopped while calculating',max_age)
# Recover an existing, genuine result without altering/recalculating it.
token=jobs['first'][0];result=root/'results'/f'{token}.json';digest=hashlib.sha256(result.read_bytes()).hexdigest();response=root/'responses'/f'{token}.json';backup=root/'evidence'/token/'response-before-recovery.json';response.rename(backup)
deadline=time.monotonic()+10
while not response.exists():assert time.monotonic()<deadline;time.sleep(.1)
assert json.loads(response.read_text(encoding='utf-8'))['success'];assert hashlib.sha256(result.read_bytes()).hexdigest()==digest
# A second worker must fail at the OS lock before starting a calculation.
duplicate=subprocess.run([sys.executable,str(project/'dev-config/native-demo/worker.py'),'--root',str(root),'--config',str(config_path),'--docker',args.docker,'--once'],capture_output=True,env={**os.environ,'PYTHONIOENCODING':'utf-8'},timeout=10)
assert duplicate.returncode!=0 and '已有worker' in duplicate.stderr.decode('utf-8','replace')
report={'status':'PASS','jobs':records,'maxHeartbeatAgeSeconds':max_age,'recoveryKeptResultSha256':digest,'secondWorkerRejected':True}
(project/'output/verification/native-demo/worker-runtime.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=True))
