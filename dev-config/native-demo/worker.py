"""Local-only task runner. Docker SQL has network=none and no published ports.

No source connection strings, arbitrary SQL, or business input overrides are accepted.
Every calculation/release is rolled back. Results are immutable files.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time
import threading
import uuid
from datetime import date, timedelta

CONTAINER = 'aps-pilot-chengben-verify-sql'
DATABASE = 'ApsIsolatedDemo'
SOURCE_VERSION = '261016902'
PLAN_COLUMNS = 'sys_no plan_guid product_no product_line_no active_no craft_sequence start_date end_date shift_no active_sn plan_num made_time change_product_tag change_time demand_ver sale_order_no sale_order_guid customer_order_no assist_tag scheduling_tag demand_num calc_guid'.split()
MATERIAL_COLUMNS = 'sys_no detail_guid calc_guid material_no remain_num this_remain_num total_remain_num supply_order_no supply_order_guid order_num delivery_date this_order_num total_order_num full_tag material_num demand_ver'.split()


def fixture_config():
    return json.loads((Path(__file__).resolve().parents[2] / 'agent-backend/Data/native-demo.json').read_text(encoding='utf-8-sig'))


def fixture_inputs(config):
    """Only inputs enter this manifest. No completion dates or risk conclusions."""
    fields = ('fixtureId', 'businessDate', 'start', 'end', 'inventoryDate', 'templatePlanningStart', 'supply', 'orders')
    result = {key: config[key] for key in fields}
    if config.get('insertionOrders'):
        result['insertionOrders'] = config['insertionOrders']
    if config.get('simulationPolicy'):
        result['simulationPolicy'] = config['simulationPolicy']
    start = date.fromisoformat(result['start'])
    if result['businessDate'] != result['start'] or date.fromisoformat(result['inventoryDate']) > start:
        raise ValueError('业务日、起排日与库存日期不一致')
    if date.fromisoformat(result['supply']['arrival']) < start:
        raise ValueError('采购在途到货日早于业务日')
    original = min(date.fromisoformat(o['templateDueDate']) for o in result['orders'])
    for order in result['orders'] + [o for o in result.get('insertionOrders', []) if o.get('sourceExisting')]:
        if {'risk', 'late', 'expectedRisk', 'expectedFinish'} & order.keys():
            raise ValueError('输入不得预设排程或交期结论')
        expected = start + timedelta(days=3) + (date.fromisoformat(order['templateDueDate']) - original)
        if order['dueDate'] != expected.isoformat():
            raise ValueError('订单交期未遵循事先固定的相对间隔')
    return result


def runtime_identity(config):
    return {'datasetId': config.get('datasetId', 'small13'), 'demandVersion': config['demandVersion'],
            'fixtureId': config['fixtureId'], 'inputFingerprint': input_fingerprint(config)}


def input_fingerprint(config):
    return hashlib.sha256(json.dumps(fixture_inputs(config), ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest()


def simulation_sql(config):
    """Overlay on the local clone only, within build_sql's rollback transaction."""
    data = fixture_inputs(config)
    supply = data['supply']
    overlays = data['orders'] + [o for o in data.get('insertionOrders', []) if o.get('sourceExisting')]
    values = ','.join(f"({sql_value(o['orderId'])},{o['quantity']},{sql_value(o['dueDate'])})" for o in overlays)
    sql = f"""
DECLARE @SimStart date={sql_value(data['start'])},@SimStockDate date={sql_value(data['inventoryDate'])},@SimArrivalDate date={sql_value(supply['arrival'])};
DECLARE @SimStockQty float={supply['stock']},@SimTransitQty float={supply['inTransit']};
DECLARE @SimOrders TABLE(order_id varchar(40),quantity float,due_date date);
INSERT @SimOrders VALUES {values};
IF NOT EXISTS(SELECT 1 FROM TA05_work_calendar_detail WHERE work_date>=@SimStart AND work_date<=DATEADD(day,14,@SimStart))
 THROW 51000,'Simulation calendar coverage missing',1;
"""
    policy = data.get('simulationPolicy', {})
    if policy.get('addFixedSupply', True):
        sql += Path(__file__).with_name('fixture.sql').read_text(encoding='utf-8')
    # Update BOM need dates before changing each order's delivery date, preserving lead-time offsets.
    for suffix in ('F20122E', 'F201220'):
        sql += f"""
UPDATE b SET delivery_date=DATEADD(day,DATEDIFF(day,d.delivery_date,b.delivery_date),i.due_date)
 FROM T201_sale_order_for_calc_bom_{suffix} b JOIN T201_sale_order_for_calc_{suffix} d ON d.calc_guid=b.calc_guid
 JOIN @SimOrders i ON i.order_id=d.sale_order_no WHERE b.delivery_date>'1900-01-02';
UPDATE d SET delivery_date=i.due_date,order_num=i.quantity,calc_date=@SimStart
 FROM T201_sale_order_for_calc_{suffix} d JOIN @SimOrders i ON i.order_id=d.sale_order_no;
"""
    sql += """
-- The independent MRP fetch reads the original sales-order source, not the planning view.
UPDATE d SET delivery_date=i.due_date,ship_date=i.due_date,reaudit_date=i.due_date,plan_delivery_date=i.due_date,
 input_date=DATEADD(day,-7,@SimStart),order_num=i.quantity,delivery_num=0,multi_order_num=i.quantity
 FROM T201_sale_order_detail d JOIN @SimOrders i ON i.order_id=d.sale_order_no;
UPDATE m SET input_date=DATEADD(day,-7,@SimStart),edit_date=DATEADD(day,-7,@SimStart)
 FROM T201_sale_order_master m JOIN @SimOrders i ON i.order_id=m.sale_order_no;
-- Existing stock quantities/quality/units and purchase quantities retain their verified semantics.
"""
    if policy:
        for table, columns, anchor, target in [
            ('T205_stock_remain', ('input_date', 'first_input_date', 'production_date'), policy['inventorySourceAnchor'], '@SimStockDate'),
            ('T202_supply_order_detail', ('delivery_date', 'ship_date', 'input_date'), policy['purchaseSourceAnchor'], '@SimArrivalDate'),
            ('T202_supply_order_master', ('edit_date', 'input_date'), policy['purchaseSourceAnchor'], '@SimArrivalDate')]:
            date.fromisoformat(anchor)
            changes = ','.join(f"{col}=CASE WHEN {col}>'1900-01-02' THEN DATEADD(day,DATEDIFF(day,{sql_value(anchor)},{target}),{col}) ELSE {col} END" for col in columns)
            sql += f'UPDATE {table} SET {changes};\n'
    else:
        sql += """UPDATE T205_stock_remain SET input_date=@SimStockDate,first_input_date=@SimStockDate,production_date=@SimStockDate;
UPDATE T202_supply_order_detail SET delivery_date=@SimArrivalDate,ship_date=@SimArrivalDate,input_date=@SimStockDate;
UPDATE T202_supply_order_master SET edit_date=@SimStockDate,input_date=@SimStockDate;
"""
    sql += """
-- Preserve actual task identities, states and plan links. Do not invent tasks per order.
UPDATE t SET input_date=@SimStockDate,work_date=@SimStart,
 start_time=CASE WHEN start_time>'1900-01-02' THEN CAST(@SimStockDate AS datetime) ELSE start_time END,
 end_time=CASE WHEN end_time>'1900-01-02' THEN CAST(@SimStockDate AS datetime) ELSE end_time END,
 made_delivery_date=i.due_date
 FROM TA07_task_bill_detail t LEFT JOIN TA06_produce_plan_master m ON m.master_guid=t.pd_plan_guid
 LEFT JOIN TA06_mjg_produce_plan_master g ON g.master_guid=t.pd_plan_guid
 CROSS APPLY(SELECT MIN(i.due_date) AS due_date FROM @SimOrders i JOIN T201_sale_order_for_calc_F20122E d ON d.sale_order_no=i.order_id
 WHERE d.demand_ver='261016902' AND (d.sale_order_guid=m.sale_order_guid OR CHARINDEX(','+d.sale_order_guid+',',','+ISNULL(g.sale_order_guid,'')+',')>0)) i
 WHERE i.due_date IS NOT NULL;
"""
    # Historical in-progress plan dates are simulated too; state and quantity are not changed.
    for table in ('TA06_produce_plan_master', 'TA06_mjg_produce_plan_master'):
        sql += f"""
UPDATE m SET delivery_date=i.due_date,start_date=@SimStockDate,
 end_date=DATEADD(day,CASE WHEN DATEDIFF(day,m.start_date,m.end_date)>0 THEN DATEDIFF(day,m.start_date,m.end_date) ELSE 0 END,@SimStockDate),edit_date=@SimStockDate
 FROM {table} m CROSS APPLY(SELECT MIN(i.due_date) AS due_date FROM @SimOrders i JOIN T201_sale_order_for_calc_F20122E d ON d.sale_order_no=i.order_id
 WHERE d.demand_ver='261016902' AND (d.sale_order_guid=m.sale_order_guid OR CHARINDEX(','+d.sale_order_guid+',',','+ISNULL(m.sale_order_guid,'')+',')>0)) i
 WHERE i.due_date IS NOT NULL;
"""
    return sql


def validate_batch(order, ids, candidates=()):
    if not isinstance(order, list) or len(set(order)) != len(order) or not set(ids).issubset(order) or not set(order).issubset(set(ids) | set(candidates)):
        raise ValueError('必须保留已有订单，只能加入已核实的待排订单，且不得重复')


def validate_request(request, ids, candidates=()):
    if not isinstance(request, dict) or set(request) - {'action', 'orderIds', 'resultId', 'controlOrderIds'}:
        raise ValueError('禁止覆盖SQL、连接、库存、在途或需求数量')
    action = request.get('action')
    if action not in {'baseline', 'reschedule', 'insert', 'mrp', 'release'}:
        raise ValueError('未知隔离操作')
    order = request.get('orderIds', ids)
    validate_batch(order, ids, candidates)
    if action == 'insert' and (len(order) != len(ids) + 1 or order[0] not in candidates or request.get('controlOrderIds') is None):
        raise ValueError('插单须新增一张待排订单并保留插入前整批对照')
    if action == 'release' and not re.fullmatch('[a-f0-9]{32}', request.get('resultId', '')):
        raise ValueError('下发必须引用服务端已保存的试排结果')
    if 'resultId' in request and not re.fullmatch('[a-f0-9]{32}', request['resultId']):
        raise ValueError('无效结果编号')
    if 'controlOrderIds' in request:
        control = request['controlOrderIds']
        if not isinstance(control, list) or len(control) != len(ids) or set(control) != set(ids):
            if action == 'insert' or not isinstance(control, list) or len(control) != len(order) or set(control) != set(order):
                raise ValueError('同序控制必须保留插单前订单或当前完整批次')
    return request


def urgent_source_sql(config):
    sql = ''
    for item in config.get('insertionOrders', []):
        if item.get('sourceExisting'):
            continue
        sql += f"""
DECLARE @UrgentOrder varchar(40)={sql_value(item['orderId'])},@UrgentGuid varchar(40)={sql_value(item['saleOrderGuid'])},@UrgentTemplate varchar(40)={sql_value(item['templateOrderId'])};
DECLARE @UrgentQty float={item['quantity']},@UrgentDue date={sql_value(item['dueDate'])},@UrgentInput date={sql_value(config['businessDate'])};
"""
        sql += Path(__file__).with_name('urgent-order.sql').read_text(encoding='utf-8')
    return sql


def refresh_demand_sql(order):
    values = ','.join(sql_value(x) for x in order)
    sql = f"""
-- Original client flow: fetch eligible sales demand, split BOM, confirm version, transfer to scheduling.
EXEC dbo.P201_sale_order_for_calc_F201220 @strSysNo='731';
DELETE FROM T201_sale_order_for_calc_F201220 WHERE demand_ver='' AND sale_order_no NOT IN ({values});
EXEC dbo.P201_sale_order_for_calc_bom_F201220 @SysNo='731',@LoginWorker='ISO_DEMO';
DELETE FROM T201_sale_order_for_calc_bom_F201220 WHERE demand_ver='{SOURCE_VERSION}';
DELETE FROM T201_sale_order_for_calc_line_F201220 WHERE demand_ver='{SOURCE_VERSION}';
DELETE FROM T201_sale_order_for_calc_F201220 WHERE demand_ver='{SOURCE_VERSION}';
UPDATE T201_sale_order_for_calc_F201220 SET demand_ver='{SOURCE_VERSION}' WHERE demand_ver='';
UPDATE T201_sale_order_for_calc_bom_F201220 SET demand_ver='{SOURCE_VERSION}' WHERE demand_ver='';
UPDATE T201_sale_order_for_calc_line_F201220 SET demand_ver='{SOURCE_VERSION}' WHERE demand_ver='';
"""
    for i, order_id in enumerate(order, 1):
        sql += f"UPDATE T201_sale_order_for_calc_F201220 SET order_sn={i},default_order_sn={i} WHERE demand_ver='{SOURCE_VERSION}' AND sale_order_no={sql_value(order_id)};\n"
    return sql + f"EXEC dbo.P201_sale_order_for_calc_F20122E_Save @DemandVer='{SOURCE_VERSION}';\n"


def sql_value(value):
    if value is None:
        return 'NULL'
    if isinstance(value, bool):
        return '1' if value else '0'
    if isinstance(value, (int, float)):
        if not math.isfinite(value):
            raise ValueError('非有限数值')
        return str(value)
    if not isinstance(value, str) or len(value) > 2000:
        raise ValueError('非法结果字段')
    return "N'" + value.replace("'", "''") + "'"


def restore_plan_sql(snapshot, ids):
    if {o['sale_order_no'] for o in snapshot.get('orders', [])} != set(ids) or not snapshot.get('plans'):
        raise ValueError('已保存计划不属于本批订单或没有可下发计划')
    sql = []
    for table, rows, columns in [('TA06_produce_plan_detail_active', snapshot['plans'], PLAN_COLUMNS), ('TA06_produce_plan_detail_calc', snapshot.get('materials', []), MATERIAL_COLUMNS)]:
        sql.append(f"DELETE FROM {table} WHERE demand_ver='{SOURCE_VERSION}';")
        for row in rows:
            selected = [c for c in columns if c in row]
            if row.get('demand_ver') != SOURCE_VERSION:
                raise ValueError('计划来源版本不匹配')
            sql.append(f"INSERT INTO {table} ({','.join(selected)}) VALUES ({','.join(sql_value(row[c]) for c in selected)});")
    return '\n'.join(sql)


def release_cutoff(snapshot):
    # The original Submit filters start_date < @DemandDate. This is a horizon,
    # not the timestamp of the user's release operation.
    dates = [date.fromisoformat(p['end_date'][:10]) for p in snapshot.get('plans', []) if p.get('end_date', '')[:10] > '1900-01-02']
    if not dates:
        raise ValueError('已展示计划没有有效的下发日期范围')
    return (max(dates) + timedelta(days=1)).isoformat()


def seed_sql(ids, order):
    values = ','.join(sql_value(x) for x in ids)
    sql = f"DELETE FROM T201_sale_order_for_calc_F20122E WHERE demand_ver='{SOURCE_VERSION}' AND sale_order_no NOT IN ({values});\n"
    for i, order_id in enumerate(order, 1):
        sql += f"UPDATE T201_sale_order_for_calc_F20122E SET order_sn={i},default_order_sn={i} WHERE demand_ver='{SOURCE_VERSION}' AND sale_order_no={sql_value(order_id)};\n"
    return sql


def build_sql(request, ids, prior=None, config=None):
    config = config or fixture_config()
    action = request['action']
    order = request.get('orderIds', ids)
    sql = 'SET NOCOUNT ON; SET XACT_ABORT ON;\nCREATE TABLE #Output(payload nvarchar(max));\nBEGIN TRANSACTION;\n'
    sql += "DECLARE @NativeLock int; EXEC @NativeLock=sys.sp_getapplock @Resource='APS_NATIVE_DEMO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=120000; IF @NativeLock<0 THROW 51000,'Isolated calculation is busy; no result adopted',1;\n"
    sql += simulation_sql(config)
    sql += urgent_source_sql(config)
    if set(order) - set(ids):
        sql += refresh_demand_sql(order)
    sql += seed_sql(order, order)
    if action in {'baseline', 'reschedule', 'insert'}:
        sql += f"EXEC dbo.PA06_produce_plan_detail_active_bySeg @SysNo='731',@DemandVer='261016902',@dtStartDate={sql_value(config['start'])};\n"
    elif action == 'release':
        sql += restore_plan_sql(prior, order)
        sql += f"EXEC dbo.PA06_produce_plan_Submit @SourceDemandVer='261016902',@DemandVer='SIM-NATIVE-RELEASE',@DemandDate={sql_value(release_cutoff(prior))},@VerNo='SIM-NATIVE-',@VerSn=1,@WorkerNo='ISO_DEMO';\n"
    if action == 'mrp':
        # Remove the earlier audit's locked MRP batch inside this transaction only.
        sql += "DELETE h FROM TA06_mr_calc_half_demand_occupy h JOIN TA06_mr_calc_fetch_demand f ON f.fetch_guid=h.fetch_guid WHERE f.demand_ver='261016901';\nDELETE h FROM TA06_mr_calc_half_demand h JOIN TA06_mr_calc_fetch_demand f ON f.fetch_guid=h.fetch_guid WHERE f.demand_ver='261016901';\nDELETE FROM TA06_mr_calc_fetch_demand WHERE demand_ver='261016901';\n"
        sql += "EXEC dbo.PA06_FetchSaleOrderDetail @SysNo='731',@LoginWorker='ISO_DEMO';\n"
        sql += f"DELETE FROM TA06_mr_calc_fetch_demand WHERE demand_ver='' AND sale_order_no NOT IN ({','.join(sql_value(x) for x in order)});\n"
        sql += "EXEC dbo.PA06_MRCalcSplitActiveDetail @SysNo='731',@LoginWorker='ISO_DEMO';\n"
    orders = "SELECT d.calc_guid,d.sale_order_guid,d.sale_order_no,d.product_no,d.order_num,d.delivery_date,d.order_sn,d.task_tag,d.merge_no,d.product_no AS product_name,pc.unit_no FROM T201_sale_order_for_calc_F20122E d LEFT JOIN T200_product_code pc ON pc.product_no=d.product_no WHERE d.demand_ver='261016902' ORDER BY CASE WHEN d.task_tag='T' THEN 0 ELSE 1 END,d.order_sn FOR JSON PATH"
    plans = "SELECT * FROM TA06_produce_plan_detail_active WHERE demand_ver='261016902' ORDER BY sale_order_no,start_date,shift_no,active_no,product_line_no FOR JSON PATH"
    materials = "SELECT m.*,d.sale_order_no,m.material_no AS product_name,pc.unit_no FROM TA06_produce_plan_detail_calc m JOIN T201_sale_order_for_calc_F20122E d ON d.calc_guid=m.calc_guid LEFT JOIN T200_product_code pc ON pc.product_no=m.material_no WHERE m.demand_ver='261016902' ORDER BY d.sale_order_no,m.material_no FOR JSON PATH"
    tasks = "SELECT t.task_bill_guid,t.task_bill_no,COALESCE(m.sale_order_guid,g.sale_order_guid) AS sale_order_guid,t.product_no,t.start_active_no,t.product_line_no,t.task_num,t.delivery_num,t.state_type,t.pd_plan_guid,t.input_date,t.made_delivery_date,t.start_time,t.end_time,t.work_date FROM TA07_task_bill_detail t LEFT JOIN TA06_produce_plan_master m ON m.master_guid=t.pd_plan_guid LEFT JOIN TA06_mjg_produce_plan_master g ON g.master_guid=t.pd_plan_guid WHERE EXISTS(SELECT 1 FROM T201_sale_order_for_calc_F20122E d WHERE d.demand_ver='261016902' AND (d.sale_order_guid=m.sale_order_guid OR CHARINDEX(','+d.sale_order_guid+',',','+ISNULL(g.sale_order_guid,'')+',')>0)) ORDER BY t.task_bill_no FOR JSON PATH"
    mrp = "SELECT h.*,f.sale_order_no,f.product_no AS finished_product_no,f.undelivery_num,f.delivery_date AS order_delivery_date,h.material_no AS product_name,pc.unit_no FROM TA06_mr_calc_half_demand h JOIN TA06_mr_calc_fetch_demand f ON f.fetch_guid=h.fetch_guid LEFT JOIN T200_product_code pc ON pc.product_no=h.material_no WHERE f.demand_ver='' ORDER BY f.sale_order_no,h.bom_level,h.craft_sequence DESC FOR JSON PATH" if action == 'mrp' else "SELECT TOP 0 sale_order_no FROM T201_sale_order_for_calc_F20122E FOR JSON PATH"
    release = "SELECT (SELECT COUNT(*) FROM TA06_produce_plan_master WHERE demand_ver='SIM-NATIVE-RELEASE') AS masterRows,(SELECT COUNT(*) FROM TA06_produce_plan_detail WHERE demand_ver='SIM-NATIVE-RELEASE') AS detailRows,(SELECT COUNT(*) FROM TA07_task_bill_detail) AS actualTaskRows,(SELECT COUNT(*) FROM TA07_task_bill_detail_last_ver WHERE demand_ver='SIM-NATIVE-RELEASE') AS carriedTaskRows,(SELECT * FROM TA06_produce_plan_master WHERE demand_ver='SIM-NATIVE-RELEASE' FOR JSON PATH) AS masters,(SELECT * FROM TA06_produce_plan_detail WHERE demand_ver='SIM-NATIVE-RELEASE' FOR JSON PATH) AS details FOR JSON PATH,WITHOUT_ARRAY_WRAPPER" if action == 'release' else "SELECT 0 AS masterRows FOR JSON PATH,WITHOUT_ARRAY_WRAPPER"
    inputs = "SELECT JSON_QUERY((SELECT d.sale_order_no,d.sale_order_guid,d.product_no,d.delivery_date,d.input_date,d.multi_order_num,d.delivery_num FROM T201_sale_order_detail d JOIN @SimOrders i ON i.order_id=d.sale_order_no FOR JSON PATH)) AS salesOrders,JSON_QUERY((SELECT stock_guid,product_no,remain_num,input_date,unit_no FROM T205_stock_remain WHERE stock_guid='SIM-NATIVE-STOCK-001' FOR JSON PATH)) AS stock,JSON_QUERY((SELECT supply_order_guid,product_no,order_num,delivery_num,delivery_date,input_date,unit_no FROM T202_supply_order_detail WHERE supply_order_no='SIM-NATIVE-PO-001' FOR JSON PATH)) AS purchases FOR JSON PATH,WITHOUT_ARRAY_WRAPPER"
    sql += f"DECLARE @payload nvarchar(max)=(SELECT JSON_QUERY(({orders})) AS orders,JSON_QUERY(({plans})) AS plans,JSON_QUERY(({materials})) AS materials,JSON_QUERY(({tasks})) AS tasks,JSON_QUERY((SELECT unit_no,unit_name_cn FROM T200_unit_code FOR JSON PATH)) AS units,JSON_QUERY(({mrp})) AS mrp,JSON_QUERY(({release})) AS release,JSON_QUERY(({inputs})) AS inputs FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);\nROLLBACK TRANSACTION;\nINSERT #Output VALUES(@payload);\nGO\n:out /tmp/native-demo-result.out\nSELECT 'NATIVE_JSON_BEGIN'; SELECT payload FROM #Output; SELECT 'NATIVE_JSON_END';\n"
    return ':out /dev/null\n' + sql


def docker_run(docker, *args, timeout=240):
    result = subprocess.run([docker, *args], capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', 'replace')[-2000:] or 'Docker调用失败')
    return result.stdout.decode('utf-8', 'replace')


def check_isolation(docker):
    state = json.loads(docker_run(docker, 'inspect', CONTAINER))[0]
    if state['HostConfig']['NetworkMode'] != 'none' or state['HostConfig'].get('PortBindings'):
        raise RuntimeError('数据库隔离条件不满足，拒绝执行')
    if not state['State']['Running']:
        raise RuntimeError('隔离数据库尚未启动')


def parse_sql_output(text):
    payload = text.split('NATIVE_JSON_BEGIN', 1)[1].split('NATIVE_JSON_END', 1)[0]
    return json.loads(''.join(payload.strip().splitlines()))


def execute_sql(docker, sql, folder):
    check_isolation(docker)
    script = folder / 'input.sql'
    script.write_text(sql, encoding='utf-8')
    token = hashlib.sha256(str(folder.resolve()).encode('utf-8')).hexdigest()[:24]
    remote_in, remote_out = '/tmp/native-'+token+'.sql', '/tmp/native-'+token+'.out'
    script.write_text(sql.replace('/tmp/native-demo-result.out', remote_out), encoding='utf-8')
    docker_run(docker, 'cp', str(script), CONTAINER + ':'+remote_in)
    command = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S tcp:127.0.0.1,1433 -U sa -C -b -r1 -f 65001 -y 0 -w 65535 -d ApsIsolatedDemo -i '+remote_in
    docker_run(docker, 'exec', CONTAINER, 'sh', '-c', command)
    out = folder / 'output.log'
    docker_run(docker, 'cp', CONTAINER + ':'+remote_out, str(out))
    text = out.read_text(encoding='utf-8-sig')
    return parse_sql_output(text)


def process(request, config, root, docker, job_id):
    ids = [o['orderId'] for o in config['orders']]
    request = validate_request(request, ids, [o['orderId'] for o in config.get('insertionOrders', [])])
    prior = None
    if request['action'] == 'release':
        prior = json.loads((root / 'results' / (request['resultId'] + '.json')).read_text(encoding='utf-8'))
        if prior.get('action') not in {'baseline', 'reschedule', 'insert'} or prior.get('inputOrderIds') != request.get('orderIds', ids):
            raise ValueError('下发结果与当前已采用计划不匹配')
    folder = root / 'evidence' / job_id
    folder.mkdir(parents=True)
    if prior is not None and (prior.get('fixtureId') != config['fixtureId'] or prior.get('inputFingerprint') != input_fingerprint(config)):
        raise ValueError('旧样例计划不能在新输入中下发，请新建对话')
    payload = execute_sql(docker, build_sql(request, ids, prior, config), folder)
    validate_result_orders(payload, request.get('orderIds', ids))
    payload.update(resultId=job_id, action=request['action'], inputOrderIds=request.get('orderIds', ids), source='isolated-demo', sourceVersion=SOURCE_VERSION, asOf=config['businessDate']+'T09:00:00+08:00', originalApiInvoked=False, originalProcedureInvoked=True, rolledBack=True, fixtureId=config['fixtureId'], inputFingerprint=input_fingerprint(config), simulationInputs=fixture_inputs(config), fixedDemoInputs=config['supply'])
    payload['planSourceVersion'] = SOURCE_VERSION
    if request['action']=='mrp':
        payload['sourceVersion'] = ''
        payload['mrpSourceLabel'] = 'Frozen sale demand fetch -> independent MRP; unnumbered batch'
    if request['action'] == 'release':
        payload['sourceResultId'] = request['resultId']
        payload['releaseCutoffDate'] = release_cutoff(prior)
        old = {p['plan_guid'] for p in prior['plans']}
        released = {p['plan_guid'] for p in payload['release'].get('details', [])}
        if not old or not old.issubset(released):
            raise ValueError('原下发未完整承接已展示计划，拒绝发布结果')
    # Record a same-input control, without claiming every observed difference is causal.
    if request['action'] in {'reschedule', 'insert'}:
        control_folder = folder / 'control'
        control_folder.mkdir()
        control = execute_sql(docker, build_sql({'action': 'baseline', 'orderIds': request.get('controlOrderIds', config['baselineOrderIds'])}, ids, config=config), control_folder)
        payload['controlPlans'] = control['plans']
    return payload


def validate_result_orders(payload, expected):
    actual = [row['sale_order_no'] for row in payload.get('orders', [])]
    if len(actual) != len(set(actual)) or set(actual) != set(expected):
        missing = ','.join(sorted(set(expected) - set(actual)))
        raise ValueError('原提取未保留本批完整订单，结果未发布：' + (missing or '重复或范围外订单'))


def recover_response(root, job_id, request, ids, config=None):
    config = config or fixture_config()
    result = root / 'results' / (job_id+'.json')
    if result.exists():
        payload = json.loads(result.read_text(encoding='utf-8'))
        if payload.get('resultId') != job_id or payload.get('action') != request['action'] or payload.get('inputOrderIds') != request.get('orderIds', ids) or payload.get('source') != 'isolated-demo' or payload.get('rolledBack') is not True or payload.get('fixtureId') != config['fixtureId'] or payload.get('inputFingerprint') != input_fingerprint(config):
            raise ValueError('既有结果与请求不匹配，未覆盖或重新计算')
        if request['action']=='release' and payload.get('sourceResultId') != request.get('resultId'):
            raise ValueError('既有下发结果来源不一致')
        return {'success':True,'resultId':job_id}
    if (root / 'evidence' / job_id).exists():
        raise RuntimeError('上次任务中断且没有完整结果；当前计划保留，请重新计算取得新的结果编号')
    return None


def acquire_worker_lock(root):
    handle = (root / 'worker.lock').open('a+b')
    if handle.tell() == 0:
        handle.write(b'0')
        handle.flush()
    handle.seek(0)
    try:
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        handle.close()
        raise RuntimeError('本任务目录已有worker运行，拒绝重复启动')
    return handle


def heartbeat_loop(root, stop):
    while not stop.is_set():
        pending = root / 'heartbeat.tmp'
        pending.write_text(str(time.time()), encoding='ascii')
        pending.replace(root / 'heartbeat')
        stop.wait(1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    parser.add_argument('--config', required=True)
    parser.add_argument('--docker', required=True)
    parser.add_argument('--once', action='store_true')
    args = parser.parse_args()
    root = Path(args.root).resolve()
    config = json.loads(Path(args.config).read_text(encoding='utf-8-sig'))
    for name in ['requests', 'results', 'responses', 'evidence']:
        (root / name).mkdir(parents=True, exist_ok=True)
    lock = acquire_worker_lock(root)
    identity = root / 'identity.tmp'
    identity.write_text(json.dumps(runtime_identity(config), ensure_ascii=False), encoding='utf-8')
    identity.replace(root / 'identity.json')
    stop = threading.Event()
    threading.Thread(target=heartbeat_loop, args=(root, stop), daemon=True).start()
    while True:
        for path in sorted((root / 'requests').glob('*.json')):
            job_id = path.stem
            if not re.fullmatch('[a-f0-9]{32}', job_id):
                continue
            response_path = root / 'responses' / path.name
            if response_path.exists():
                continue
            try:
                ids = [o['orderId'] for o in config['orders']]
                request = validate_request(json.loads(path.read_text(encoding='utf-8')),ids,[o['orderId'] for o in config.get('insertionOrders', [])])
                response = recover_response(root,job_id,request,ids,config=config)
                if response is None:
                    payload = process(request, config, root, args.docker, job_id)
                    result = root / 'results' / path.name
                    if result.exists():
                        raise RuntimeError('结果编号已存在，禁止覆盖')
                    pending_result = result.with_suffix('.tmp')
                    pending_result.write_text(json.dumps(payload, ensure_ascii=False), encoding='utf-8')
                    pending_result.replace(result)
                    response = {'success': True, 'resultId': job_id}
            except Exception as error:
                response = {'success': False, 'message': str(error)[:1500]}
            tmp = response_path.with_suffix('.tmp')
            tmp.write_text(json.dumps(response, ensure_ascii=False), encoding='utf-8')
            tmp.replace(response_path)
        if args.once:
            stop.set()
            lock.close()
            break
        time.sleep(.25)


if __name__ == '__main__':
    main()
