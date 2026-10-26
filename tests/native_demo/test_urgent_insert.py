import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('insert_worker', ROOT / 'dev-config/native-demo/worker.py')
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class UrgentInsert(unittest.TestCase):
    def test_new_order_extends_batch_without_removing_existing_orders(self):
        ids = ['Z9900001', 'Z9900018']
        new = 'Z99100014'
        self.assertTrue(hasattr(worker, 'validate_batch'), '尚不支持新增订单进入批次')
        worker.validate_batch([new] + ids, ids, [new])
        for invalid in [[new], [new, ids[0]], [new] + ids + [new], ids + ['UNKNOWN']]:
            with self.assertRaises(ValueError):
                worker.validate_batch(invalid, ids, [new])

    def test_insert_requires_new_candidate_and_same_batch_control(self):
        self.assertTrue(hasattr(worker, 'validate_batch'))
        ids = ['Z9900001', 'Z9900018']
        new = 'Z99100014'
        worker.validate_request({'action': 'insert', 'orderIds': [new] + ids, 'controlOrderIds': ids}, ids, [new])
        for control in [[new] + ids, ids[:1], list(reversed(ids)) + ['UNKNOWN']]:
            with self.assertRaises(ValueError):
                worker.validate_request({'action': 'insert', 'orderIds': [new] + ids, 'controlOrderIds': control}, ids, [new])

    def test_new_order_uses_original_fetch_bom_save_and_scheduler(self):
        config = worker.fixture_config()
        self.assertIn('insertionOrders', config, '没有批次外的新订单来源')
        new = config['insertionOrders'][0]['orderId']
        self.assertNotIn(new, config['baselineOrderIds'])
        sql = worker.build_sql({'action': 'insert', 'orderIds': [new] + config['baselineOrderIds']}, config['baselineOrderIds'], config=config)
        for proc in ['P201_sale_order_for_calc_F201220', 'P201_sale_order_for_calc_bom_F201220', 'P201_sale_order_for_calc_F20122E_Save', 'PA06_produce_plan_detail_active_bySeg']:
            self.assertIn('EXEC dbo.' + proc, sql)
        self.assertIn('ROLLBACK TRANSACTION', sql)
        self.assertNotIn('ALTER PROC', sql.upper())


if __name__ == '__main__':
    unittest.main()
