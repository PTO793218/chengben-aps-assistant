import test from 'node:test';
import assert from 'node:assert/strict';
const module=await import('../agent-evals/eval-text.mjs').catch(()=>({}));
const forbidden=module.hasForbiddenAssertion||((text,term)=>text.includes(term));
test('agent eval accepts explicit rejection of unsupported labels',()=>{
  const text='来源尚未查明，不能直接称为“孤立任务”或“异常”。';
  assert.equal(forbidden(text,'孤立任务'),false);assert.equal(forbidden(text,'异常'),false);
  assert.equal(forbidden('原因尚未查明，不能称为异常或孤立任务。','孤立任务'),false);
  const uncertainty='本次快照未查明原因，不能断定是否为孤立任务或系统异常。';
  assert.equal(forbidden(uncertainty,'孤立任务'),false);assert.equal(forbidden(uncertainty,'异常'),false);
  assert.equal(forbidden('未取得来源的任务也不等同于孤立任务。','孤立任务'),false);
  assert.equal(forbidden('原因尚未查明，不表示从未生成或系统异常。','异常'),false);
});
test('agent eval still rejects positive labels even after a negated occurrence',()=>{
  assert.equal(forbidden('不能直接称为孤立任务。但是这条就是孤立任务。','孤立任务'),true);
  assert.equal(forbidden('这是一条异常。','异常'),true);
  assert.equal(forbidden('可以断定是否为孤立任务。','孤立任务'),true);
  assert.equal(forbidden('不表示停工，这是一条异常。','异常'),true);
});
test('other forbidden phrases keep literal matching',()=>assert.equal(forbidden('不能说可开工。','可开工'),true));
