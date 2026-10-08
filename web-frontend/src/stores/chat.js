import { defineStore } from "pinia";
import { createId } from "../services/id.js";
const KEY = "wepilot-conversations-v1",
  SETTINGS = "wepilot-settings-v1";
const snapshot = (value) => JSON.parse(JSON.stringify(value));
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
    async persist() {
      if (!this.current.messages.length) return;
      this.current.updatedAt = Date.now();
      if (this.current.title === "新对话")
        this.current.title = (
          this.current.messages.find((m) => m.role === "user")?.content ||
          "图片会话"
        ).slice(0, 24);
      const item = snapshot(this.current);
      this.conversations = [
        item,
        ...this.conversations.filter((c) => c.id !== item.id),
      ];
      try {
        await dbAction("readwrite", (s) => s.put(item));
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
    async create() {
      await this.persist();
      this.current = empty();
    },
    async select(id) {
      const item = this.conversations.find((c) => c.id === id);
      if (item) this.current = snapshot(item);
    },
    async remove(id) {
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
