"""Business input contracts, independent of any desired scheduling outcome."""
import importlib.util
import json
import unittest
from datetime import date, timedelta
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('consistent_worker', PROJECT / 'dev-config/native-demo/worker.py')
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class ConsistentInputs(unittest.TestCase):
    def setUp(self):
        self.config = json.loads((PROJECT / 'agent-backend/Data/native-demo.json').read_text(encoding='utf-8-sig'))
        self.ids = self.config['baselineOrderIds']

    def test_order_deadlines_share_the_business_clock_and_preserve_original_spacing(self):
        start = date.fromisoformat(self.config['start'])
        self.assertEqual(self.config.get('businessDate'), self.config['start'])
        self.assertNotEqual(self.config['demandVersion'], 'SIM-261016-12A')
        earliest = min(date.fromisoformat(o['dueDate']) for o in self.config['orders'])
        original = min(date.fromisoformat(o['templateDueDate']) for o in self.config['orders'])
        self.assertEqual(earliest, start + timedelta(days=3))
        for order in self.config['orders']:
            self.assertEqual(date.fromisoformat(order['dueDate']) - earliest,
                             date.fromisoformat(order['templateDueDate']) - original)
            self.assertFalse({'risk', 'late', 'expectedRisk', 'expectedFinish'} & order.keys())

    def test_inventory_and_purchase_are_on_the_same_timeline(self):
        start = date.fromisoformat(self.config['start'])
        self.assertIn('inventoryDate', self.config, '库存快照日期尚未统一')
        self.assertLessEqual(date.fromisoformat(self.config['inventoryDate']), start)
        self.assertEqual(date.fromisoformat(self.config['supply']['arrival']), start + timedelta(days=2))
        self.assertGreater(self.config['supply']['stock'], 0)
        self.assertGreater(self.config['supply']['inTransit'], 0)

    def test_worker_exposes_immutable_inputs_before_calculation(self):
        self.assertTrue(hasattr(worker, 'fixture_inputs'), '统一模拟输入清单尚未实现')
        inputs = worker.fixture_inputs(self.config)
        self.assertEqual(inputs['orders'], self.config['orders'])
        self.assertEqual(inputs['start'], self.config['start'])
        changed = dict(self.config, baselineResultId='0' * 32)
        self.assertEqual(worker.fixture_inputs(changed), inputs, '输出编号不能影响输入清单')

    def test_each_native_path_uses_the_same_order_source_and_supply_dates(self):
        self.assertTrue(hasattr(worker, 'fixture_inputs'), '统一模拟输入清单尚未实现')
        plan = worker.build_sql({'action': 'baseline'}, self.ids, config=self.config)
        mrp = worker.build_sql({'action': 'mrp'}, self.ids, config=self.config)
        for sql in [plan, mrp]:
            self.assertIn('@SimOrders', sql)
            self.assertIn('T201_sale_order_detail', sql)
            self.assertIn('T201_sale_order_for_calc_F20122E', sql)
            self.assertIn(self.config['start'], sql)
            self.assertIn(self.config['supply']['arrival'], sql)
            self.assertIn('ROLLBACK TRANSACTION', sql)
            self.assertNotIn('ALTER PROC', sql.upper())
            self.assertNotIn("@DemandDate='2027-01-01'", sql)
        self.assertIn('PA06_produce_plan_detail_active_bySeg', plan)
        self.assertIn('PA06_FetchSaleOrderDetail', mrp)
        self.assertIn('PA06_MRCalcSplitActiveDetail', mrp)
        self.assertNotIn('EXEC dbo.PA06_produce_plan_detail_active_bySeg', mrp)

    def test_embedded_plan_and_mrp_were_calculated_from_exactly_these_inputs(self):
        self.assertTrue(hasattr(worker, 'fixture_inputs'), '统一模拟输入清单尚未实现')
        expected = {o['orderId']: o for o in self.config['orders']}
        for name in ['native-baseline.json', 'native-mrp-baseline.json']:
            result = json.loads((PROJECT / 'agent-backend/Data' / name).read_text(encoding='utf-8-sig'))
            self.assertEqual(result['fixtureId'], self.config['fixtureId'])
            self.assertEqual(result['simulationInputs'], worker.fixture_inputs(self.config))
            self.assertEqual({o['sale_order_no'] for o in result['orders']}, set(expected))
            for order in result['orders']:
                self.assertEqual(order['delivery_date'][:10], expected[order['sale_order_no']]['dueDate'])
            self.assertTrue(result['rolledBack'])
            self.assertFalse(result['originalApiInvoked'])

    def test_native_release_date_is_an_exclusive_plan_cutoff_not_the_operation_day(self):
        prior = {'orders': [{'sale_order_no': x} for x in self.ids],
                 'plans': [{'plan_guid': 'saved', 'start_date': '2026-10-28T00:00:00',
                            'end_date': '2026-10-30T00:00:00', 'demand_ver': '261016902'}], 'materials': []}
        sql = worker.build_sql({'action': 'release'}, self.ids, prior, config=self.config)
        self.assertIn("@DemandDate=N'2026-10-31'", sql)
        self.assertNotIn("@DemandDate=N'2026-10-19'", sql)


if __name__ == '__main__':
    unittest.main()
