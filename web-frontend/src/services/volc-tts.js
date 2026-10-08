let context = null;
let activeController = null;
let activePlayer = null;

export async function primeVolcTtsAudio() {
  if (!context || context.state === "closed") context = new AudioContext();
  if (context.state === "suspended") await context.resume();
  return context;
}

class PcmPlayer {
  constructor(audioContext, rate) {
    this.context = audioContext;
    this.rate = rate;
    this.next = 0;
    this.pending = new Uint8Array();
    this.sources = new Set();
    this.stopped = false;
  }
  push(bytes) {
    if (this.stopped) return;
    const merged = new Uint8Array(this.pending.length + bytes.length);
    merged.set(this.pending);
    merged.set(bytes, this.pending.length);
    const size = merged.length - (merged.length % 2);
    this.pending = merged.slice(size);
    if (!size) return;
    const view = new DataView(merged.buffer, merged.byteOffset, size),
      samples = new Float32Array(size / 2);
    for (let i = 0; i < samples.length; i++)
      samples[i] = view.getInt16(i * 2, true) / 32768;
    const buffer = this.context.createBuffer(1, samples.length, this.rate);
    buffer.copyToChannel(samples, 0);
    const source = this.context.createBufferSource();
    source.buffer = buffer;
    source.connect(this.context.destination);
    this.sources.add(source);
    source.onended = () => this.sources.delete(source);
    this.next = Math.max(this.next, this.context.currentTime + 0.04);
    source.start(this.next);
    this.next += buffer.duration;
  }
  async finish() {
    while (!this.stopped && this.context.currentTime < this.next)
      await new Promise((resolve) => setTimeout(resolve, 50));
  }
  stop() {
    this.stopped = true;
    this.pending = new Uint8Array();
    this.next = this.context.currentTime;
    for (const source of this.sources) {
      try {
        source.stop();
      } catch {}
    }
    this.sources.clear();
  }
}

export function stopVolcTts() {
  activeController?.abort();
  activeController = null;
  activePlayer?.stop();
  activePlayer = null;
}

export async function speakWithVolcTts(text) {
  const content = String(text || "").trim();
  if (!content) return;
  stopVolcTts();
  const controller = new AbortController();
  activeController = controller;
  const response = await fetch("/api/speech/tts/stream", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text: content, format: "pcm", sampleRate: 24000 }),
    signal: controller.signal,
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error || `TTS ${response.status}`);
  }
  const audioContext = await primeVolcTtsAudio();
  const player = new PcmPlayer(
      audioContext,
      Number(response.headers.get("X-Audio-Sample-Rate")) || 24000,
    ),
    reader = response.body.getReader();
  activePlayer = player;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      if (value?.length) player.push(value);
    }
    await player.finish();
  } catch (error) {
    player.stop();
    throw error;
  } finally {
    reader.releaseLock();
    if (activeController === controller) activeController = null;
    if (activePlayer === player) activePlayer = null;
  }
}
