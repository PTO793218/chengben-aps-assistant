import small from '../../../agent-backend/Data/native-demo.json' with {type:'json'};
import large from '../../../agent-backend/Data/datasets/large85/native-demo.json' with {type:'json'};
import {resolveDataset} from '../../../dev-config/native-demo/dataset-selection.mjs';

const override = typeof __APS_DEMO_DATASET__ !== 'undefined' ? __APS_DEMO_DATASET__ : globalThis.process?.env?.APS_DEMO_DATASET;
export const nativeDataset = resolveDataset(override);
export const nativeDemo = nativeDataset.id === 'small13' ? small : large;
export const nativeDatasetIdentity = {datasetId:nativeDataset.id,demandVersion:nativeDemo.demandVersion,fixtureId:nativeDemo.fixtureId,inputFingerprint:nativeDemo.inputFingerprint};

const identityFields = ['datasetId', 'demandVersion', 'fixtureId', 'inputFingerprint'];
const completeIdentity = identity => identity && typeof identity === 'object' && !Array.isArray(identity)
  && identityFields.every(field => typeof identity[field] === 'string' && identity[field].trim().length > 0);

export function validateDatasetIdentity(apiIdentity, frontendIdentity = nativeDatasetIdentity) {
  let message = '';
  if (!completeIdentity(apiIdentity) || !completeIdentity(frontendIdentity)) {
    message = '无法确认当前数据版本，请刷新页面后重试。';
  } else if (![apiIdentity.datasetId, frontendIdentity.datasetId].every(id => ['small13', 'large85'].includes(id))) {
    message = '当前数据集无法识别，请刷新页面后重试。';
  } else if (identityFields.some(field => apiIdentity[field] !== frontendIdentity[field])) {
    message = '页面数据与服务数据不一致，请刷新页面后重试。';
  }
  return message ? {ok: false, refreshRequired: true, message} : {ok: true, refreshRequired: false, identity: frontendIdentity};
}

export function assertDatasetIdentity(apiIdentity, frontendIdentity = nativeDatasetIdentity) {
  const result = validateDatasetIdentity(apiIdentity, frontendIdentity);
  if (!result.ok) throw Object.assign(new Error(result.message), {code: 'DATASET_REFRESH_REQUIRED', refreshRequired: true});
  return result.identity;
}

export async function verifyDatasetConnection({signal, fetcher = globalThis.fetch} = {}) {
  const requestSignal = signal ?? AbortSignal.timeout(10000);
  try {
    requestSignal.throwIfAborted();
    const response = await fetcher('/api/agent/health', {method: 'GET', cache: 'no-store', signal: requestSignal});
    if (!response.ok) throw new Error('连接失败');
    const result = await response.json();
    requestSignal.throwIfAborted();
    if (result?.status !== 'ok' || !['demo', 'model'].includes(result.mode)) throw new Error('连接失败');
    assertDatasetIdentity(result.dataset);
    return result;
  } catch (error) {
    if (requestSignal.aborted) throw requestSignal.reason;
    if (error?.code === 'DATASET_REFRESH_REQUIRED') throw error;
    throw new Error('连接失败，请重新连接后重试。');
  }
}
