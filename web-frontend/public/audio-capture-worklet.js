class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.targetRate = 16000;
    this.chunk = [];
    this.chunkSamples = 3200;
    this.position = 0;
  }
  process(inputs) {
    const input = inputs[0]?.[0];
    if (!input) return true;
    const ratio = sampleRate / this.targetRate;
    while (this.position < input.length) {
      const index = Math.floor(this.position);
      const value = Math.max(-1, Math.min(1, input[index] || 0));
      this.chunk.push(value < 0 ? value * 32768 : value * 32767);
      this.position += ratio;
      if (this.chunk.length >= this.chunkSamples) {
        const pcm = new Int16Array(this.chunk.splice(0, this.chunkSamples));
        this.port.postMessage(pcm.buffer, [pcm.buffer]);
      }
    }
    this.position -= input.length;
    return true;
  }
}
registerProcessor('pcm-capture', PcmCaptureProcessor);
