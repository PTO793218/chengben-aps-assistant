import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

PROJECT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('large_worker',PROJECT/'dev-config/native-demo/worker.py')
worker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)

class LargeDatasetContract(unittest.TestCase):
    def config(self):
        config=copy.deepcopy(worker.fixture_config())
        config['datasetId']='large85'
        config['fixtureId']='TEST-LARGE'
        config['simulationPolicy']={'addFixedSupply':False,'inventorySourceAnchor':'2026-10-08','purchaseSourceAnchor':'2026-07-30'}
        config['insertionOrders']=[{'orderId':'Z99200020','saleOrderGuid':'source-guid','productNo':'A.01.0151','quantity':50000,'dueDate':'2026-11-13','sourceExisting':True,'templateDueDate':'2026-07-25'}]
        return config

    def test_policy_participates_in_input_identity_but_small_fingerprint_is_unchanged(self):
        small=worker.fixture_config()
        self.assertEqual(worker.input_fingerprint(small),small['inputFingerprint'])
        config=self.config()
        self.assertIn('simulationPolicy',worker.fixture_inputs(config))
        changed=copy.deepcopy(config);changed['simulationPolicy']['inventorySourceAnchor']='2026-10-07'
        self.assertNotEqual(worker.input_fingerprint(config),worker.input_fingerprint(changed))

    def test_large_uses_frozen_supply_and_preserves_date_offsets(self):
        config=self.config();sql=worker.simulation_sql(config)
        self.assertNotIn('INSERT INTO T205_stock_remain',sql)
        self.assertNotIn('SIM-NATIVE-PO-001',sql)
        self.assertIn('DATEDIFF(day',sql)
        self.assertIn("2026-10-08",sql)
        self.assertIn("2026-07-30",sql)
        self.assertIn('1900-01-02',sql)

    def test_existing_candidate_is_overlaid_but_never_cloned(self):
        config=self.config()
        self.assertIn('Z99200020',worker.simulation_sql(config))
        self.assertEqual(worker.urgent_source_sql(config),'')

    def test_recovery_checks_selected_config_instead_of_small_default(self):
        config=self.config();ids=config['baselineOrderIds'];job='d'*32
        payload={'resultId':job,'action':'baseline','inputOrderIds':ids,'source':'isolated-demo','rolledBack':True,'fixtureId':config['fixtureId'],'inputFingerprint':worker.input_fingerprint(config)}
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'results').mkdir();(root/'results'/f'{job}.json').write_text(json.dumps(payload),encoding='utf-8')
            self.assertTrue(worker.recover_response(root,job,{'action':'baseline'},ids,config=config)['success'])
            with self.assertRaises(ValueError):worker.recover_response(root,job,{'action':'baseline'},ids,config=worker.fixture_config())

    def test_worker_identity_identifies_dataset_and_exact_frozen_input(self):
        config=self.config()
        identity=worker.runtime_identity(config)
        self.assertEqual(identity,{'datasetId':'large85','demandVersion':config['demandVersion'],'fixtureId':'TEST-LARGE','inputFingerprint':worker.input_fingerprint(config)})

    def test_datasets_serialize_transactions_on_the_shared_isolated_sql_copy(self):
        config=self.config();sql=worker.build_sql({'action':'baseline'},config['baselineOrderIds'],config=config)
        self.assertIn('sp_getapplock',sql)
        self.assertLess(sql.index('sp_getapplock'),sql.index('UPDATE T205_stock_remain'))

    def test_original_fetch_cannot_publish_a_result_that_silently_drops_an_order(self):
        with self.assertRaisesRegex(ValueError,'未保留'):
            worker.validate_result_orders({'orders':[{'sale_order_no':'A'}]},['A','B'])
        with self.assertRaises(ValueError):
            worker.validate_result_orders({'orders':[{'sale_order_no':'A'},{'sale_order_no':'A'}]},['A'])
        worker.validate_result_orders({'orders':[{'sale_order_no':'B'},{'sale_order_no':'A'}]},['A','B'])

    def test_fixture_is_limited_to_verified_sales_source_and_keeps_85th_candidate(self):
        config=json.loads((PROJECT/'agent-backend/Data/datasets/large85/native-demo.json').read_text(encoding='utf-8'))
        self.assertEqual(len(config['baselineOrderIds']),84)
        self.assertNotIn('FORECAST-DEMO-001',config['baselineOrderIds'])
        self.assertIn('Z99200019',config['baselineOrderIds'])
        self.assertEqual([o['orderId'] for o in config['insertionOrders']],['Z99200020'])

if __name__=='__main__':unittest.main()
