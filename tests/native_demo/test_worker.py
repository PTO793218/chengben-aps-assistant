import importlib.util
import unittest
import tempfile
import json
from pathlib import Path

MODULE = Path(__file__).resolve().parents[2] / 'dev-config/native-demo/worker.py'
spec = importlib.util.spec_from_file_location('native_worker', MODULE)
worker = importlib.util.module_from_spec(spec) if MODULE.exists() else None
if worker is not None:
    spec.loader.exec_module(worker)


class WorkerContract(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(worker, '隔离任务校验尚未实现')
        self.ids = ['Z9900001', 'Z9900018']

    def test_validates_complete_order_set(self):
        self.assertEqual(worker.validate_request({'action': 'reschedule', 'orderIds': self.ids}, self.ids)['action'], 'reschedule')
        for ids in [[], self.ids[:1], [self.ids[0]] * 2, ['Z9900001', "x'; DROP TABLE x--"]]:
            with self.assertRaises(ValueError):
                worker.validate_request({'action': 'reschedule', 'orderIds': ids}, self.ids)

    def test_cannot_override_business_inputs(self):
        for key in ['sql', 'database', 'server', 'scenario', 'quantity', 'inventory', 'inTransit', 'path']:
            with self.assertRaises(ValueError):
                worker.validate_request({'action': 'mrp', key: 'anything'}, self.ids)

    def test_release_requires_saved_snapshot_and_no_path(self):
        for token in ['', '../baseline', '/tmp/a', 'baseline', 'a' * 33]:
            with self.assertRaises(ValueError):
                worker.validate_request({'action': 'release', 'resultId': token}, self.ids)
        valid = {'action': 'release', 'resultId': 'a' * 32}
        self.assertEqual(worker.validate_request(valid, self.ids), valid)

    def test_sql_values_are_quoted_and_numbers_finite(self):
        self.assertEqual(worker.sql_value("a'b"), "N'a''b'")
        with self.assertRaises(ValueError):
            worker.sql_value(float('nan'))

    def test_sqlcmd_wrapped_json_preserves_long_result(self):
        self.assertTrue(hasattr(worker, 'parse_sql_output'), '尚未处理sqlcmd输出折行')
        value = worker.parse_sql_output('NATIVE_JSON_BEGIN\n{"name":"long\nvalue"}\nNATIVE_JSON_END')
        self.assertEqual(value, {'name': 'longvalue'})

    def test_nonzero_offsets_use_fixed_labelled_input_records(self):
        sql = worker.build_sql({'action': 'mrp'}, self.ids)
        self.assertIn('SIM-NATIVE-PO-001', sql)
        self.assertIn('SIM-NATIVE-STOCK-001', sql)
        self.assertIn('ROLLBACK TRANSACTION', sql)
        self.assertNotIn('ALTER PROC', sql.upper())

    def test_only_one_worker_can_own_runtime_directory(self):
        self.assertTrue(hasattr(worker, 'acquire_worker_lock'), 'worker缺少操作系统互斥锁')
        with tempfile.TemporaryDirectory() as directory:
            handle = worker.acquire_worker_lock(Path(directory))
            try:
                with self.assertRaises(RuntimeError):
                    worker.acquire_worker_lock(Path(directory))
            finally:
                handle.close()
            worker.acquire_worker_lock(Path(directory)).close()

    def test_restart_recovers_complete_result_without_recalculation(self):
        self.assertTrue(hasattr(worker, 'recover_response'), '重启尚不能恢复已经完成的结果')
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'results').mkdir();job='a'*32
            payload={'resultId':job,'action':'baseline','inputOrderIds':self.ids,'source':'isolated-demo','rolledBack':True,'fixtureId':worker.fixture_config()['fixtureId'],'inputFingerprint':worker.input_fingerprint(worker.fixture_config())}
            (root/'results'/f'{job}.json').write_text(json.dumps(payload),encoding='utf-8')
            self.assertEqual(worker.recover_response(root,job,{'action':'baseline'},self.ids),{'success':True,'resultId':job})
            payload['action']='release';(root/'results'/f'{job}.json').write_text(json.dumps(payload),encoding='utf-8')
            with self.assertRaises(ValueError):worker.recover_response(root,job,{'action':'baseline'},self.ids)

    def test_incomplete_interrupted_job_is_not_silently_recomputed(self):
        self.assertTrue(hasattr(worker, 'recover_response'))
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);job='a'*32;(root/'evidence'/job).mkdir(parents=True)
            with self.assertRaisesRegex(RuntimeError,'中断'):
                worker.recover_response(root,job,{'action':'baseline'},self.ids)

    def test_release_uses_exact_saved_plan_without_rescheduling(self):
        snapshot = {'orders': [{'sale_order_no': x} for x in self.ids], 'plans': [{'sys_no': '731', 'plan_guid': 'one', 'plan_num': 5, 'demand_ver': '261016902'}], 'materials': []}
        sql = worker.restore_plan_sql(snapshot, self.ids)
        self.assertIn('plan_guid', sql)
        self.assertIn("N'one'", sql)
        self.assertNotIn('EXEC dbo.PA06_produce_plan_detail_active_bySeg', sql)
        with self.assertRaises(ValueError):
            worker.restore_plan_sql(snapshot, ['Z9900028'])


if __name__ == '__main__':
    unittest.main()
