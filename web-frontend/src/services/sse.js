export async function readSse(body, onEvent) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let pending = "",
    terminal = false;
  const consume = async (block) => {
    let event = "message";
    const data = [];
    for (const line of block.split(/\r?\n/)) {
      if (line.startsWith("event:")) event = line.slice(6).trim();
      if (line.startsWith("data:")) data.push(line.slice(5).trimStart());
    }
    if (!data.length) return;
    const payload = JSON.parse(data.join("\n"));
    await onEvent(event, payload);
    if (event === "done" || event === "error") terminal = true;
  };
  try {
    while (true) {
      const { value, done } = await reader.read();
      pending += done
        ? decoder.decode()
        : decoder.decode(value, { stream: true });
      let match;
      while ((match = /\r?\n\r?\n/.exec(pending))) {
        await consume(pending.slice(0, match.index));
        pending = pending.slice(match.index + match[0].length);
      }
      if (done) break;
    }
    if (pending.trim()) await consume(pending);
    if (!terminal) throw new Error("连接提前结束，请重试。");
  } finally {
    reader.releaseLock();
  }
}
