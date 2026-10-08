// Ark ASR browser adapter: microphone audio never enters chat history.
export function createVoiceCapture({ onText, onState, onError, onFinish }) {
  let ws,
    stream,
    context,
    node,
    source,
    gain,
    timeout,
    closeTimer,
    run = 0,
    stopping = false,
    fullText = "";
  const finals = new Map();
  async function releaseAudio() {
    stream?.getTracks().forEach((t) => t.stop());
    stream = null;
    node?.disconnect();
    source?.disconnect();
    gain?.disconnect();
    node = source = gain = null;
    const old = context;
    context = null;
    if (old && old.state !== "closed") await old.close().catch(() => {});
  }
  function cancel() {
    run++;
    clearTimeout(timeout);
    clearTimeout(closeTimer);
    const old = ws;
    ws = null;
    try {
      old?.close();
    } catch {}
    releaseAudio();
    onState("idle");
  }
  async function start() {
    cancel();
    const id = run;
    stopping = false;
    fullText = "";
    finals.clear();
    if (!window.isSecureContext || !navigator.mediaDevices)
      throw new Error("请通过 localhost 或 HTTPS 使用麦克风。");
    onState("connecting");
    try {
      const acquired = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
        },
      });
      if (id !== run) {
        acquired.getTracks().forEach((t) => t.stop());
        return;
      }
      stream = acquired;
      context = new AudioContext();
      await context.resume();
      await context.audioWorklet.addModule("/audio-capture-worklet.js");
      if (id !== run) return;
      const socket = new WebSocket(
        `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/api/speech/asr/stream`,
      );
      ws = socket;
      timeout = setTimeout(() => {
        if (id === run) {
          onError("语音服务连接超时。");
          cancel();
        }
      }, 15000);
      socket.onmessage = (e) => {
        if (id !== run) return;
        try {
          const data = JSON.parse(e.data);
          if (data.type === "ready") {
            clearTimeout(timeout);
            source = context.createMediaStreamSource(stream);
            node = new AudioWorkletNode(context, "pcm-capture");
            gain = context.createGain();
            gain.gain.value = 0;
            node.port.onmessage = (event) => {
              if (socket.readyState === WebSocket.OPEN && !stopping)
                socket.send(event.data);
            };
            source.connect(node).connect(gain).connect(context.destination);
            onState("recording");
            timeout = setTimeout(stop, 60000);
          }
          if (data.type === "partial") {
            fullText = data.text || fullText;
            onText(fullText);
          }
          if (data.type === "final") {
            finals.set(data.startTime ?? data.text, data.text || "");
            fullText = [...finals.values()].join("");
            onText(fullText);
          }
          if (data.type === "error") {
            onError(data.message || "语音识别失败。");
            cancel();
          }
        } catch {
          onError("语音响应格式异常。");
          cancel();
        }
      };
      socket.onerror = () => {
        if (id === run) {
          onError("无法连接语音服务，请检查配置。");
          cancel();
        }
      };
      socket.onclose = () => {
        if (id !== run) return;
        clearTimeout(timeout);
        clearTimeout(closeTimer);
        ws = null;
        releaseAudio();
        onState("idle");
        if (stopping && fullText) onFinish(fullText);
      };
    } catch (error) {
      if (id === run) {
        cancel();
        throw error;
      }
    }
  }
  function stop() {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      cancel();
      return;
    }
    stopping = true;
    clearTimeout(timeout);
    onState("finishing");
    releaseAudio();
    ws.send(JSON.stringify({ type: "stop" }));
    closeTimer = setTimeout(() => {
      try {
        ws?.close();
      } catch {}
    }, 2500);
  }
  return { start, stop, cancel };
}
