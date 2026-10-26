import { defineStore } from "pinia";
import { createId } from "../services/id.js";
const KEY = "wepilot-conversations-v1",
  SETTINGS = "wepilot-settings-v1";
const snapshot = (value) => JSON.parse(JSON.stringify(value));
const writes = new Map(), deleted = new Set();
const empty = () => ({
  id: createId(),
  title: "新对话",
  updatedAt: Date.now(),
  summary: "",
  summarizedMessageCount: 0,
  messages: [],
});
const parse = (key, fallback) => {
  try {
    return JSON.parse(localStorage.getItem(key)) ?? fallback;
  } catch {
    return fallback;
  }
};
async function dbAction(mode, action) {
  const db = await new Promise((resolve, reject) => {
    const request = indexedDB.open("wepilot", 1);
    request.onupgradeneeded = () =>
      request.result.createObjectStore("conversations", { keyPath: "id" });
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return new Promise((resolve, reject) => {
    const tx = db.transaction("conversations", mode);
    const request = action(tx.objectStore("conversations"));
    tx.oncomplete = () => {
      db.close();
      resolve(request.result);
    };
    tx.onerror = tx.onabort = () => {
      db.close();
      reject(tx.error || new Error("无法保存会话"));
    };
  });
}
export const useChatStore = defineStore("chat", {
  state: () => ({
    conversations: [],
    current: empty(),
    persistenceWarning: "",
    settings: {
      displayName: "体验用户",
      autoSpeak: false,
      voiceAutoSend: false,
      compact: false,
    },
  }),
  actions: {
    async init() {
      this.settings = { ...this.settings, ...parse(SETTINGS, {}) };
      try {
        this.conversations = await dbAction("readonly", (s) => s.getAll());
      } catch {
        this.conversations = parse(KEY, []);
      }
      if (!Array.isArray(this.conversations)) this.conversations = [];
      this.conversations.sort((a, b) => b.updatedAt - a.updatedAt);
      for (const conversation of this.conversations) {
        for (const message of conversation.messages || [])
          if (message.status === "streaming") message.status = "interrupted";
      }
      if (this.conversations.length)
        this.current = snapshot(this.conversations[0]);
    },
    async persist(conversation = this.current, {touch = true} = {}) {
      if (!conversation.messages.length || deleted.has(conversation.id)) return;
      if(touch) conversation.updatedAt = Date.now();
      if (conversation.title === "新对话")
        conversation.title = (
          conversation.messages.find((m) => m.role === "user")?.content ||
          "图片会话"
        ).slice(0, 24);
      const item = snapshot(conversation);
      this.conversations = !touch && this.conversations.some(c=>c.id===item.id)
        ? this.conversations.map(c=>c.id===item.id?item:c)
        : [
        item,
        ...this.conversations.filter((c) => c.id !== item.id),
      ];
      try {
        const write = (writes.get(item.id) || Promise.resolve()).catch(() => {}).then(() => deleted.has(item.id) ? undefined : dbAction("readwrite", (s) => s.put(item)));
        writes.set(item.id, write);
        await write;
        this.persistenceWarning = "";
      } catch {
        try {
          localStorage.setItem(KEY, JSON.stringify(this.conversations));
          this.persistenceWarning = "本地数据库不可用，已使用备用存储。";
        } catch {
          this.persistenceWarning = "浏览器存储空间不足，本次会话尚未保存。";
        }
      }
    },
    findConversation(id) {
      return deleted.has(id) ? null : this.current.id === id ? this.current : this.conversations.find(c => c.id === id);
    },
    async appendResult(id, message) {
      const conversation = this.findConversation(id);
      if (!conversation) return;
      conversation.messages.push(message);
      await this.persist(conversation);
    },
    async persistBlockUi(id, block) {
      const conversation = this.findConversation(id);
      if (!conversation) return;
      const target = conversation.messages.flatMap(m => m.uiBlocks || []).find(b => b.uiState?.operationId === block.uiState?.operationId);
      if (target) target.uiState = snapshot(block.uiState);
      await this.persist(conversation,{touch:false});
    },
    async create() {
      await this.persist(this.current,{touch:false});
      this.current = empty();
    },
    async select(id) {
      await this.persist(this.current,{touch:false});
      const item = this.conversations.find((c) => c.id === id);
      if (item) this.current = snapshot(item);
    },
    async remove(id) {
      deleted.add(id);
      await writes.get(id)?.catch(() => {});
      await dbAction("readwrite", (s) => s.delete(id)).catch(() => {});
      this.conversations = this.conversations.filter((c) => c.id !== id);
      if (this.current.id === id)
        this.current = this.conversations.length
          ? snapshot(this.conversations[0])
          : empty();
      try {
        localStorage.setItem(KEY, JSON.stringify(this.conversations));
      } catch {}
    },
    async clear() {
      for (const c of [...this.conversations, this.current]) deleted.add(c.id);
      await Promise.allSettled([...writes.values()]);
      await dbAction("readwrite", (s) => s.clear()).catch(() => {});
      this.conversations = [];
      this.current = empty();
      localStorage.removeItem(KEY);
    },
    saveSettings() {
      localStorage.setItem(SETTINGS, JSON.stringify(this.settings));
    },
  },
});
