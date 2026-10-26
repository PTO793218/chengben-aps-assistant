"""Read-only final audit of the existing isolated SQL clone; no source connection."""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
from pathlib import Path

parser=argparse.ArgumentParser();parser.add_argument('--docker',required=True);args=parser.parse_args()
project=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('native_worker',project/'dev-config/native-demo/worker.py');worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)
folder=project/'output/verification/native-demo/final-audit';folder.mkdir(exist_ok=True)
sql=(project/'output/verification/native-demo/final-state.sql').read_text(encoding='utf-8').replace('AUDIT_JSON','NATIVE_JSON')
data=worker.execute_sql(args.docker,':out /tmp/native-demo-result.out\n'+sql,folder)
load=lambda name:json.loads((project/name).read_text(encoding='utf-8-sig'))
expected={x['name']:x['definition'] for x in load('output/audit/20261015-fullchain/current_selected_definitions.json')}
expected.update({x['name']:x['definition'] for x in load('output/audit/20261016-rootcause/extra_closure_objects.json') if x.get('definition')})
normal=lambda value:value.replace('\r\n','\n').rstrip()
assert len(expected)==20
assert {x['name']:normal(x['definition']) for x in data.pop('moduleDefinitions')}=={k:normal(v) for k,v in expected.items()}
keys=('sale_order_no','calc_guid','order_sn','default_order_sn','task_tag')
extract=lambda rows:sorted(tuple(r.get(k) for k in keys) for r in rows)
original=load('output/audit/20261015-fullchain/30-main-prepare-audit_json.json')['demand']
assert extract(data.pop('originalDemand'))==extract(original)
for key,value in data.items():
    if key.startswith('persisted') or key=='diagnosticProcedures':assert value==0,(key,value)
assert data['taskRows']==56 and data['tables']==60 and data['historicalMrpOrders']==199
data.update(status='PASS',unchangedNativeDefinitions=20,unchangedOriginalOrderPriorities=167,checkedAt=datetime.now(timezone.utc).isoformat())
(project/'output/verification/native-demo/final-state.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(data,ensure_ascii=True))
