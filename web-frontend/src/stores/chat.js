import { defineStore } from "pinia";
import { createId } from "../services/id.js";
import { browserChatStorage } from "../services/chat-storage.js";
const SETTINGS = "wepilot-settings-v1";
const snapshot = (value) => JSON.parse(JSON.stringify(value));
const repositories = new WeakMap();
const storageFor = store => {
  if (!repositories.has(store)) repositories.set(store, browserChatStorage());
  return repositories.get(store);
};
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
      const recovered = await storageFor(this).read();
      this.persistenceWarning = recovered.warning;
      if (!recovered.available) return;
      this.conversations = recovered.conversations;
      this.conversations.sort((a, b) => b.updatedAt - a.updatedAt);
      for (const conversation of this.conversations) {
        for (const message of conversation.messages || [])
          if (message.status === "streaming") message.status = "interrupted";
      }
      if (this.conversations.length)
        this.current = snapshot(this.conversations[0]);
      else this.current = empty();
    },
    async persist(conversation = this.current, {touch = true} = {}) {
      const storage = storageFor(this);
      if (!conversation.messages.length || storage.isDeleted(conversation.id)) return;
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
      const result = await storage.save(item);
      if (result.blocked) return;
      this.persistenceWarning = result.warning;
      if (result.deleted) {
        this.conversations = this.conversations.filter(c => c.id !== item.id);
        if (this.current.id === item.id) this.current = this.conversations.length ? snapshot(this.conversations[0]) : empty();
      }
    },
    findConversation(id) {
      return storageFor(this).isDeleted(id) ? null : this.current.id === id ? this.current : this.conversations.find(c => c.id === id);
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
      storageFor(this).track(this.current.id);
    },
    async select(id) {
      await this.persist(this.current,{touch:false});
      const item = this.conversations.find((c) => c.id === id);
      if (item) this.current = snapshot(item);
    },
    async remove(id) {
      const storage = storageFor(this);
      storage.block(id);
      const result = await storage.remove(id);
      this.persistenceWarning = result.warning;
      storage.unblock(id);
      if (!result.saved) return false;
      this.conversations = this.conversations.filter((c) => c.id !== id);
      if (this.current.id === id)
        this.current = this.conversations.length
          ? snapshot(this.conversations[0])
          : empty();
      return true;
    },
    async clear() {
      const storage = storageFor(this), ids = [...this.conversations, this.current].map(c => c.id);
      for (const id of ids) storage.block(id);
      const result = await storage.clear();
      this.persistenceWarning = result.warning;
      for (const id of ids) storage.unblock(id);
      if (!result.saved) return false;
      this.conversations = [];
      this.current = empty();
      storage.track(this.current.id);
      return true;
    },
    saveSettings() {
      try { localStorage.setItem(SETTINGS, JSON.stringify(this.settings)); }
      catch { this.persistenceWarning = "浏览器存储不可用，设置尚未保存，请重试。"; }
    },
  },
});
