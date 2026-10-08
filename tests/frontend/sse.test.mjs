import test from 'node:test';
import assert from 'node:assert/strict';
import { readSse } from '../../web-frontend/src/services/sse.js';
const stream = chunks => new ReadableStream({ start(c){for(const chunk of chunks)c.enqueue(chunk);c.close();} });
test('SSE supports split UTF-8, CRLF, multiple frames and terminal event', async()=>{
  const bytes=new TextEncoder().encode('event: delta\r\ndata: {"text":"中文"}\r\n\r\nevent: done\r\ndata: {}\r\n\r\n');
  const received=[];await readSse(stream([...bytes].map(b=>Uint8Array.of(b))),(event,data)=>received.push([event,data]));
  assert.deepEqual(received,[['delta',{text:'中文'}],['done',{}]]);
});
test('an abruptly ended answer is not reported as completed',async()=>{
  await assert.rejects(readSse(stream([new TextEncoder().encode('event: delta\ndata: {"text":"partial"}\n\n')]),()=>{}),/连接提前结束/);
});
test('multiline data and trailing frame are handled',async()=>{
  const events=[];await readSse(stream([new TextEncoder().encode('event: done\ndata: {\ndata: "toolCount": 1\ndata: }')]),(event,data)=>events.push(data));
  assert.equal(events[0].toolCount,1);
});
