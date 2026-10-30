import selection from './dataset.json' with {type:'json'};

export function resolveDataset(override, configured = selection) {
  const id = override || configured.active;
  if (!['small13','large85'].includes(id)) throw new Error('未知数据集，只支持 small13 或 large85。');
  return {
    id,
    dataDirectory: id === 'small13' ? 'agent-backend/Data' : 'agent-backend/Data/datasets/large85',
    runtimeDirectory: id === 'small13' ? '' : 'datasets/large85',
  };
}
