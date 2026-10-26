import fs from 'node:fs';import assert from 'node:assert/strict';
const base=process.env.BASE_URL||'http://localhost:18440';
const response=await fetch(base+'/api/speech/tts/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:'你好，这是一段语音测试。请展示示例数据。',format:'pcm',sampleRate:24000}),signal:AbortSignal.timeout(45000)});
assert.equal(response.status,200,'TTS request failed');assert.match(response.headers.get('content-type'),/audio\/pcm/);
const pcm=Buffer.from(await response.arrayBuffer());assert.ok(pcm.length>1000,'TTS must return audio');
fs.mkdirSync(new URL('../../temp/',import.meta.url),{recursive:true});fs.writeFileSync(new URL('../../temp/speech-test.pcm',import.meta.url),pcm);
const rate=Number(response.headers.get('x-audio-sample-rate'))||24000;
const size=Math.floor(pcm.length/2*16000/rate),audio=Buffer.alloc(size*2);
for(let i=0;i<size;i++){const index=Math.min(Math.floor(i*rate/16000),pcm.length/2-1);audio.writeInt16LE(pcm.readInt16LE(index*2),i*2);}
const socket=new WebSocket(base.replace(/^http/,'ws')+'/api/speech/asr/stream');let heard='';
await new Promise((resolve,reject)=>{
  const timeout=setTimeout(()=>{socket.close();reject(new Error('ASR timed out'));},30000);
  let ready=false;socket.onmessage=async event=>{
    const data=JSON.parse(event.data);
    if(data.type==='error'){clearTimeout(timeout);socket.close();reject(new Error(data.message));}
    if(data.type==='partial'||data.type==='final')heard=data.text||heard;
    if(data.type==='ready'&&!ready){ready=true;try{
      for(let offset=0;offset<audio.length;offset+=6400){socket.send(audio.subarray(offset,offset+6400));await new Promise(r=>setTimeout(r,200));}
      socket.send(JSON.stringify({type:'stop'}));
    }catch(error){clearTimeout(timeout);socket.close();reject(error);}}
  };
  socket.onerror=()=>{clearTimeout(timeout);reject(new Error('ASR WebSocket failed'));};
  socket.onclose=()=>{clearTimeout(timeout);heard?resolve():reject(new Error('No ASR text returned'));};
});
assert.match(heard,/测试|示例|数据/);console.log('PASS: TTS PCM bytes='+pcm.length+'; ASR recognized: '+heard);
