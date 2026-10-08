<template>
  <div
    class="app-shell"
    :class="{
      collapsed: isCollapsed,
      viewing: !!viewer,
      compact: store.settings.compact,
    }"
  >
    <aside class="sidebar">
      <header class="brand-row">
        <button class="brand-mark" title="返回对话" @click="closeViewer">
          <Icon name="cube" />
        </button>
        <div class="brand-copy">
          <strong>诚本<span>APS 助手</span></strong
          ><small>从订单，看见生产全局</small>
        </div>
        <button
          class="icon-button collapse-button"
          aria-label="收起侧栏"
          @click="collapsed = !collapsed"
        >
          <Icon name="panel" />
        </button>
      </header>
      <nav class="primary-nav">
        <button
          class="nav-item new-chat"
          aria-label="新建对话"
          title="新建对话"
          @click="createConversation"
        >
          <Icon name="plus" /><span>新建对话</span><kbd>＋</kbd>
        </button>
        <button
          class="nav-item"
          :class="{ active: !viewer }"
          title="智能助手"
          @click="closeViewer"
        >
          <Icon name="chat" /><span>智能助手</span>
        </button>
        <button
          class="nav-item"
          :class="{ active: viewer?.type === 'image' }"
          title="图片空间"
          @click="openViewer(assets[0])"
        >
          <Icon name="image" /><span>图片空间</span><small>预览</small>
        </button>
        <button
          class="nav-item"
          :class="{ active: viewer?.type === 'scene' }"
          title="查询生产计划"
          @click="send('生产计划怎么样？')"
        >
          <Icon name="cube" /><span>生产计划</span><small>示例</small>
        </button>
        <button
          v-if="isCollapsed && !viewer"
          class="nav-item"
          title="展开侧栏"
          aria-label="展开侧栏"
          @click="collapsed = false"
        >
          <Icon name="panel" />
        </button>
      </nav>
      <section class="history-section">
        <div class="section-label">
          <span>最近对话</span><Icon name="history" />
        </div>
        <label class="history-search"
          ><Icon name="search" /><input
            v-model="search"
            placeholder="搜索对话"
            aria-label="搜索历史对话"
        /></label>
        <div class="history-list">
          <div
            v-for="item in filteredHistory"
            :key="item.id"
            class="history-row"
            :class="{ selected: item.id === store.current.id }"
          >
            <button class="history-open" @click="selectConversation(item.id)">
              <span>{{ item.title }}</span
              ><small>{{ formatDate(item.updatedAt) }}</small></button
            ><button
              class="icon-button delete-history"
              :aria-label="`删除 ${item.title}`"
              @click="removeConversation(item)"
            >
              <Icon name="trash" />
            </button>
          </div>
          <p v-if="!filteredHistory.length" class="history-empty">
            {{
              search ? "没有找到相关对话" : "从一个问题开始，灵感会留在这里。"
            }}
          </p>
        </div>
      </section>
      <div class="sidebar-bottom">
        <div class="local-note"><i></i><span>对话保存在当前浏览器</span></div>
        <div class="profile-area" ref="profileArea">
          <div v-if="userMenu" class="user-menu">
            <div class="menu-profile">
              <span class="user-avatar">{{ initials }}</span>
              <div>
                <strong>{{ store.settings.displayName || "体验用户" }}</strong
                ><small>本地工作空间</small>
              </div>
            </div>
            <div class="menu-divider"></div>
            <button @click="openSettings">
              <Icon name="settings" />设置<span>偏好与语音</span></button
            ><button @click="exportConversation">
              <Icon name="download" />导出当前对话</button
            ><button
              @click="
                showAbout = true;
                userMenu = false;
              "
            >
              <Icon name="help" />关于诚本 APS 助手
            </button>
          </div>
          <button
            class="profile-button"
            aria-label="打开用户菜单"
            :aria-expanded="userMenu"
            @click="userMenu = !userMenu"
          >
            <span class="user-avatar">{{ initials }}</span
            ><span class="profile-copy"
              ><strong>{{ store.settings.displayName || "体验用户" }}</strong
              ><small>本地工作空间</small></span
            ><Icon name="chevron" />
          </button>
        </div>
      </div>
    </aside>

    <aside v-if="viewer && ['image', 'scene'].includes(viewer.type)" class="asset-rail" aria-label="内容缩略图">
      <button
        class="icon-button"
        title="返回对话"
        aria-label="返回对话"
        @click="closeViewer"
      >
        <Icon name="chat" />
      </button>
      <div class="rail-items">
        <button
          v-for="asset in assets"
          :key="asset.id"
          class="asset-thumb"
          :class="{ selected: viewer.id === asset.id }"
          :title="asset.title"
          @click="viewer = asset"
        >
          <img :src="asset.url" :alt="asset.title" /></button
        >
      </div>
    </aside>
    <ApsAnalysisViewer v-if="viewer?.type === 'aps-analysis'" :key="viewer.id" :item="viewer" @close="closeViewer" />
    <OrderDispatchViewer v-else-if="viewer?.type === 'order-dispatch'" :key="viewer.id" :item="viewer" @close="closeViewer" />
    <PlanTaskAlignmentViewer v-else-if="viewer?.type === 'plan-task-alignment'" :key="viewer.id" :item="viewer" @close="closeViewer" />
    <MaterialReadinessViewer v-else-if="viewer?.type === 'material-readiness'" :key="viewer.id" :item="viewer" @close="closeViewer" />
    <ProductionPanorama v-else-if="viewer?.type === 'production'" :key="viewer.id" :item="viewer" @close="closeViewer" @ask="askFromPanorama" />
    <ContentViewer v-else-if="viewer" :item="viewer" @close="closeViewer" />

    <main v-show="!viewer" class="chat-main">
      <header class="main-header">
        <div>
          <button
            class="icon-button mobile-menu"
            aria-label="切换侧栏"
            @click="collapsed = !collapsed"
          >
            <Icon name="panel" /></button
          ><strong>智能助手</strong><span class="header-divider"></span
          ><span class="workspace-label">{{
            store.current.title === "新对话" ? "工作空间" : store.current.title
          }}</span>
        </div>
        <div>
          <span class="connection-state"
            ><i :class="{ offline: !health }"></i
            >{{
              health
                ? health.mode === "demo"
                  ? "演示模式"
                  : "已连接模型"
                : healthError ? "连接失败" : "连接中"
            }}</span
          ><button v-if="healthError" class="alignment-more" type="button" @click="checkHealth">重新连接</button><button
            class="icon-button"
            title="查询生产计划"
            aria-label="查询生产计划"
            @click="send('生产计划怎么样？')"
          >
            <Icon name="cube" />
          </button>
        </div>
      </header>
      <div class="message-scroll" ref="scrollEl" @scroll="trackScroll">
        <section v-if="!store.current.messages.length" class="welcome">
          <div class="welcome-eyebrow"><span></span> CHENGBEN PLANNING</div>
          <div class="welcome-logo"><Icon name="spark" /></div>
          <h1>让想法，从对话开始。</h1>
          <p>从生产计划聊起，让订单、产线与每一天的安排清晰可见。</p>
          <div class="suggestion-grid">
            <button @click="send('生产计划怎么样？')">
              <span class="suggestion-icon blue"><Icon name="chat" /></span
              ><strong>生产计划怎么样？</strong
              ><small>结论、关键指标、二维排程甘特与明细</small
              ><Icon name="chevron" /></button
            ><button @click="send('哪些订单有计划交期风险？')">
              <span class="suggestion-icon teal"><Icon name="spark" /></span
              ><strong>哪些订单有计划交期风险？</strong
              ><small>查看风险订单的计划结束与交期</small
              ><Icon name="chevron" /></button
            ><button @click="send('查询演示订单 Z9900001 的排程')">
              <span class="suggestion-icon violet"><Icon name="cube" /></span
              ><strong>演示订单 Z9900001 怎么安排？</strong
              ><small>按工段查看已有计划日期、班次与产线</small
              ><Icon name="chevron" /></button
            ><button @click="send('哪些订单还没有完整排入计划？')">
              <span class="suggestion-icon amber"><Icon name="cube" /></span
              ><strong>哪些订单没有排完整？</strong
              ><small>仅按订单排入状态查看，不推断缺失原因</small
              ><Icon name="chevron" />
            </button
            ><button @click="send('演示快照中哪些订单的物料齐套状态需要核对？')">
              <span class="suggestion-icon red"><Icon name="cube" /></span
              ><strong>哪些订单的物料齐套状态需核对？</strong
              ><small>按订单—物料矩阵核对快照中的齐套标记</small
              ><Icon name="chevron" />
            </button
            ><button @click="send('哪些订单已经形成派工？请重点看派工在制和暂未取得来源订单的任务。')">
              <span class="suggestion-icon violet"><Icon name="spark" /></span
              ><strong>哪些订单已经形成派工？</strong
              ><small>核对订单派工在制、任务状态与来源记录</small
              ><Icon name="chevron" />
            </button>
          </div>
          <div class="welcome-foot">
            <span class="blue-dot"></span>当前入口使用演示数据 · 真实订单可查询只读快照
          </div>
        </section>
        <div v-else class="message-list">
          <article
            v-for="(message, index) in store.current.messages"
            :key="message.id || index"
            class="message"
            :class="message.role"
          >
            <div class="message-avatar" v-if="message.role === 'assistant'">
              <Icon name="spark" />
            </div>
            <div class="message-body">
              <div class="message-meta">
                <strong>{{
                  message.role === "assistant"
                    ? "诚本 APS 助手"
                    : store.settings.displayName || "我"
                }}</strong
                ><time>{{ formatTime(message.createdAt) }}</time>
                <span v-if="message.superseded" class="subtle-badge">先前回答</span>
                <span v-else-if="message.retryOf" class="subtle-badge">重新查询</span>
              </div>
              <div class="message-content">
                <MarkdownContent :content="message.content" />
                <div
                  v-if="message.status === 'streaming' && !message.content"
                  class="thinking"
                >
                  <i></i><i></i><i></i><span>正在思考与查询…</span>
                </div>
                <span
                  v-else-if="message.status === 'streaming'"
                  class="stream-cursor"
                ></span>
              </div>
              <div v-for="(block, i) in message.uiBlocks" :key="i">
                <MessageChart v-if="block.type === 'chart'" :block="block" />
                <ApsAnalysisCard v-else-if="block.type === 'aps-analysis'" :block="block" compact @open="openViewer" />
                <OrderDispatchCard v-else-if="block.type === 'order-dispatch'" :block="block" compact @open="openViewer" />
                <PlanTaskAlignmentCard v-else-if="block.type === 'plan-task-alignment'" :block="block" compact @open="openViewer" />
                <MaterialReadinessCard v-else-if="block.type === 'material-readiness'" :block="block" compact @open="openViewer" />
                <ProductionCard v-else-if="block.type === 'production-plan'" :block="block" @open="openViewer" @ask="send" />
              </div>
              <div class="message-images" v-if="message.images?.length">
                <button
                  v-for="asset in message.images"
                  :key="asset.id"
                  @click="openViewer(asset)"
                >
                  <img :src="asset.url" :alt="asset.title" /><small
                    >{{ asset.title }} · 本地预览</small
                  >
                </button>
              </div>
              <div v-if="message.error" class="message-error">
                {{ message.error }}
              </div>
              <div v-if="message.status === 'interrupted'" class="interrupted">
                已停止生成
              </div>
              <div
                v-if="
                  message.role === 'assistant' && message.status !== 'streaming'
                "
                class="message-actions"
              >
                <button
                  class="icon-button"
                  title="复制回答"
                  aria-label="复制回答"
                  @click="copy(message.content)"
                >
                  <Icon name="copy" /></button
                ><button
                  class="icon-button"
                  title="朗读回答"
                  aria-label="朗读回答"
                  :disabled="!speechHealth?.configured"
                  @click="readAnswer(message.content)"
                >
                  <Icon name="volume" /></button
                ><button
                  v-if="index === store.current.messages.length - 1"
                  class="retry-button"
                  :disabled="busy"
                  @click="retry"
                >
                  重新生成
                </button>
              </div>
            </div>
            <span
              class="user-avatar message-user-avatar"
              v-if="message.role === 'user'"
              >{{ initials }}</span
            >
          </article>
        </div>
      </div>
      <div class="composer-area">
        <div
          v-if="notice || store.persistenceWarning"
          class="notice"
          role="status"
        >
          {{ notice || store.persistenceWarning
          }}<button @click="notice = ''" aria-label="关闭提示">×</button>
        </div>
        <div
          class="composer"
          :class="{ recording: voiceState === 'recording' }"
        >
          <div v-if="pendingImages.length" class="pending-images">
            <div v-for="asset in pendingImages" :key="asset.id">
              <button @click="openViewer(asset)">
                <img :src="asset.url" :alt="asset.title" /></button
              ><button
                class="remove-image"
                :aria-label="`移除 ${asset.title}`"
                @click="
                  pendingImages = pendingImages.filter((a) => a.id !== asset.id)
                "
              >
                ×
              </button>
            </div>
            <small>图片仅用于本地预览</small>
          </div>
          <textarea
            ref="inputEl"
            v-model="input"
            aria-label="输入问题"
            placeholder="问问生产计划、交期风险或订单安排…"
            rows="2"
            @keydown="onKeydown"
          ></textarea>
          <div class="composer-toolbar">
            <div>
              <button
                class="icon-button"
                title="添加本地预览图片"
                aria-label="添加本地预览图片"
                :disabled="busy"
                @click="fileInput.click()"
              >
                <Icon name="attach" /></button
              ><span class="composer-hint">{{ voiceLabel }}</span>
            </div>
            <div>
              <button v-if="speaking" class="voice-stop" @click="stopReading">
                <Icon name="stop" />停止播报</button
              ><button
                class="icon-button mic-button"
                :class="{ on: voiceState !== 'idle' }"
                :disabled="busy || !speechHealth?.configured"
                :title="
                  speechHealth?.configured ? '语音输入' : '语音服务尚未配置'
                "
                aria-label="语音输入"
                @click="toggleVoice"
              >
                <Icon :name="voiceState === 'idle' ? 'mic' : 'stop'" /></button
              ><button
                v-if="busy"
                class="send-button"
                aria-label="停止生成"
                @click="stopRequest"
              >
                <Icon name="stop" /></button
              ><button
                v-else
                class="send-button"
                aria-label="发送消息"
                :disabled="!input.trim() && !pendingImages.length"
                @click="send()"
              >
                <Icon name="arrow" />
              </button>
            </div>
          </div>
        </div>
        <div class="composer-caption">
          <span>AI 回答仅供参考，请核对重要信息</span
          ><span>Enter 发送 · Shift + Enter 换行</span>
        </div>
      </div>
    </main>

    <input
      ref="fileInput"
      type="file"
      accept="image/png,image/jpeg,image/webp,image/gif"
      multiple
      hidden
      @change="addImages"
    />
    <div
      v-if="settingsOpen"
      class="modal-backdrop"
      @click.self="settingsOpen = false"
    >
      <section
        class="settings-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="settings-title"
      >
        <header>
          <div>
            <small>WORKSPACE PREFERENCES</small>
            <h2 id="settings-title">设置</h2>
          </div>
          <button
            class="icon-button"
            aria-label="关闭设置"
            @click="settingsOpen = false"
          >
            <Icon name="close" />
          </button>
        </header>
        <div class="settings-section">
          <h3>个人偏好</h3>
          <label class="settings-row"
            ><span>显示名称<small>用于当前浏览器的对话界面</small></span
            ><input
              v-model="store.settings.displayName"
              maxlength="20"
              @change="store.saveSettings()" /></label
          ><label class="settings-row"
            ><span>紧凑布局<small>减少对话之间的留白</small></span
            ><input
              type="checkbox"
              class="switch"
              v-model="store.settings.compact"
              @change="store.saveSettings()"
          /></label>
        </div>
        <div class="settings-section">
          <h3>语音</h3>
          <label class="settings-row"
            ><span>自动朗读回答<small>使用 Ark 流式语音播报</small></span
            ><input
              type="checkbox"
              class="switch"
              v-model="store.settings.autoSpeak"
              @change="store.saveSettings()" /></label
          ><label class="settings-row"
            ><span
              >识别后自动发送<small>停止录音并收到识别结果后发送</small></span
            ><input
              type="checkbox"
              class="switch"
              v-model="store.settings.voiceAutoSend"
              @change="store.saveSettings()"
          /></label>
          <p class="settings-status">
            <i :class="{ online: speechHealth?.configured }"></i
            >{{
              speechHealth?.configured ? "语音服务已配置" : "语音服务未配置"
            }}
            · 麦克风需 localhost 或 HTTPS
          </p>
        </div>
        <div class="settings-section">
          <h3>本地数据</h3>
          <div class="settings-row">
            <span>清空对话记录<small>仅删除当前浏览器保存的对话</small></span
            ><button class="danger-button" @click="clearConversations">
              清空记录
            </button>
          </div>
        </div>
        <footer>
          <span>偏好自动保存到当前浏览器</span
          ><button class="primary-button" @click="settingsOpen = false">
            完成
          </button>
        </footer>
      </section>
    </div>
    <div
      v-if="showAbout"
      class="modal-backdrop"
      @click.self="showAbout = false"
    >
      <section
        class="about-modal"
        role="dialog"
        aria-modal="true"
        aria-label="关于诚本 APS 助手"
      >
        <div class="welcome-logo"><Icon name="spark" /></div>
        <h2>诚本 APS 助手</h2>
        <p>从对话出发，看见订单、产线与每一天的安排。</p>
        <p>对话 · 工具 · 图表 · 语音 · 内容空间</p>
        <small
          >首页入口均使用内置演示数据；明确查询真实单订单时，可读取已配置的业务库只读快照。结果标明来源、版本和取数时间，均非实时查询。</small
        ><button class="primary-button" @click="showAbout = false">
          知道了
        </button>
      </section>
    </div>
  </div>
</template>

<script setup>
import {
  ref,
  computed,
  onMounted,
  onBeforeUnmount,
  nextTick,
  watch,
  defineAsyncComponent,
} from "vue";
import Icon from "./components/Icon.vue";
import MarkdownContent from "./components/MarkdownContent.vue";
import MessageChart from "./components/MessageChart.vue";
import ContentViewer from "./components/ContentViewer.vue";
import ProductionCard from "./components/ProductionCard.vue";
import ApsAnalysisCard from "./components/ApsAnalysisCard.vue";
import ApsAnalysisViewer from "./components/ApsAnalysisViewer.vue";
import OrderDispatchCard from "./components/OrderDispatchCard.vue";
import OrderDispatchViewer from "./components/OrderDispatchViewer.vue";
import PlanTaskAlignmentCard from "./components/PlanTaskAlignmentCard.vue";
import PlanTaskAlignmentViewer from "./components/PlanTaskAlignmentViewer.vue";
import MaterialReadinessCard from "./components/MaterialReadinessCard.vue";
import MaterialReadinessViewer from "./components/MaterialReadinessViewer.vue";
import './production.css';
import './aps-analysis.css';
import './material-readiness.css';
const ProductionPanorama = defineAsyncComponent(() => import('./components/ProductionPanorama.vue'));
import { useChatStore } from "./stores/chat";
import { readSse } from "./services/sse";
import { createId } from "./services/id.js";
import { createVoiceCapture } from "./services/voice";
import {
  primeVolcTtsAudio,
  speakWithVolcTts,
  stopVolcTts,
} from "./services/volc-tts";
const store = useChatStore(),
  input = ref(""),
  search = ref(""),
  collapsed = ref(false),
  viewer = ref(null),
  userMenu = ref(false),
  settingsOpen = ref(false),
  showAbout = ref(false),
  busy = ref(false),
  speaking = ref(false),
  notice = ref(""),
  health = ref(null),
  healthError = ref(false),
  speechHealth = ref(null),
  voiceState = ref("idle"),
  pendingImages = ref([]);
const fileInput = ref(),
  scrollEl = ref(),
  inputEl = ref(),
  profileArea = ref();
let controller,
  activeTask,
  persistTimer,
  followScroll = true;
let viewerScrollTop = 0;
const demoImage = {
    id: "sample-image",
    type: "image",
    title: "示例图表.png",
    url: "/sample-board.svg",
  };
const assets = computed(() => {
  const list = [
    demoImage,
    ...store.current.messages.flatMap((m) => m.images || []),
    ...pendingImages.value,
  ];
  return [...new Map(list.map((a) => [a.id, a])).values()];
});
const isCollapsed = computed(() => collapsed.value || !!viewer.value),
  initials = computed(() =>
    Array.from(store.settings.displayName || "用户")
      .slice(0, 1)
      .join(""),
  );
const filteredHistory = computed(() =>
  store.conversations.filter((c) =>
    c.title.toLowerCase().includes(search.value.toLowerCase()),
  ),
);
const voiceLabel = computed(
  () =>
    ({
      idle: "支持语音输入",
      connecting: "正在连接麦克风…",
      recording: "正在聆听，点击停止完成输入",
      finishing: "正在完成识别…",
    })[voiceState.value],
);
function formatDate(value) {
  return new Intl.DateTimeFormat("zh-CN", {
    month: "short",
    day: "numeric",
  }).format(value || Date.now());
}
function formatTime(value) {
  return new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(value || Date.now());
}
function trackScroll() {
  const e = scrollEl.value;
  followScroll = e.scrollHeight - e.scrollTop - e.clientHeight < 100;
}
async function scroll() {
  await nextTick();
  if (viewer.value) return;
  if (followScroll && scrollEl.value)
    scrollEl.value.scrollTop = scrollEl.value.scrollHeight;
}
function openViewer(item) {
  if (!viewer.value) viewerScrollTop = scrollEl.value?.scrollTop || 0;
  viewer.value = item;
  userMenu.value = false;
  voice.cancel();
}
function closeViewer() {
  viewer.value = null;
  nextTick(() => { if (scrollEl.value) scrollEl.value.scrollTop = viewerScrollTop; });
}
function askFromPanorama(question) {
  closeViewer();
  input.value = question;
  nextTick(() => inputEl.value?.focus());
}
function openSettings() {
  settingsOpen.value = true;
  userMenu.value = false;
}
function stopReading() {
  stopVolcTts();
  speaking.value = false;
}
async function readAnswer(text) {
  try {
    await primeVolcTtsAudio();
    speaking.value = true;
    await speakWithVolcTts(text.replace(/[#*`]/g, ""));
  } catch (error) {
    if (error.name !== "AbortError") notice.value = error.message;
  } finally {
    speaking.value = false;
  }
}
function stopRequest() {
  controller?.abort();
  stopReading();
}
async function finishRequest() {
  stopRequest();
  if (activeTask) await activeTask;
  voice.cancel();
  stopReading();
}
async function createConversation() {
  await finishRequest();
  await store.create();
  input.value = "";
  pendingImages.value = [];
  viewer.value = null;
  userMenu.value = false;
}
async function selectConversation(id) {
  await finishRequest();
  await store.select(id);
  pendingImages.value = [];
  viewer.value = null;
  followScroll = true;
  scroll();
}
async function removeConversation(item) {
  if (confirm(`删除“${item.title}”？`)) {
    await finishRequest();
    await store.remove(item.id);
  }
}
async function clearConversations() {
  if (confirm("清空此浏览器中的全部对话记录？")) {
    await finishRequest();
    await store.clear();
    viewer.value = null;
  }
}
async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    notice.value = "已复制回答。";
  } catch {
    notice.value = "复制失败，请手动选择文字复制。";
  }
}
function exportConversation() {
  const blob = new Blob([JSON.stringify(store.current, null, 2)], {
      type: "application/json",
    }),
    url = URL.createObjectURL(blob),
    a = document.createElement("a");
  a.href = url;
  a.download = "wepilot-conversation.json";
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  userMenu.value = false;
}
async function retry() {
  const messages = store.current.messages;
  const index = messages.findLastIndex((m) => m.role === "user");
  if (index < 0 || busy.value) return;
  activeTask = performSend(messages[index].content, index);
  await activeTask;
}
function onKeydown(event) {
  if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
    event.preventDefault();
    send();
  }
}
function send(value) {
  if (busy.value) return activeTask;
  activeTask = performSend(typeof value === "string" ? value : input.value);
  return activeTask;
}
async function performSend(value, retryIndex = -1) {
  const text = String(value || "").trim();
  const retrying = retryIndex >= 0;
  if (!text && !pendingImages.value.length) return;
  voice.cancel();
  stopReading();
  busy.value = true;
  notice.value = "";
  followScroll = true;
  viewer.value = null;
  const previousMessages = retrying ? store.current.messages.slice(0, retryIndex) : store.current.messages;
  const eligibleHistory = previousMessages
    .filter((m) => m.content?.trim() && !m.superseded && !["error", "interrupted"].includes(m.status) && ["user", "assistant"].includes(m.role))
    .map(({ role, content }) => ({ role, content }));
  const droppedHistory = Math.max(0, eligibleHistory.length - 180),
    history = eligibleHistory.slice(droppedHistory);
  const images = retrying ? store.current.messages[retryIndex].images || [] : pendingImages.value;
  if (!retrying) {
    pendingImages.value = [];
    input.value = "";
    store.current.messages.push({
      id: createId(),
      role: "user",
      content: text || "查看图片",
      createdAt: Date.now(),
      images,
    });
  }
  if (!text) {
    await store.persist();
    busy.value = false;
    openViewer(images[0]);
    return;
  }
  const answer = {
    id: createId(),
    role: "assistant",
    content: "",
    uiBlocks: [],
    createdAt: Date.now(),
    status: "streaming",
    retryOf: retrying ? store.current.messages[retryIndex].id : null,
  };
  store.current.messages.push(answer);
  // Read the reactive proxy so stream updates render immediately.
  const target = store.current.messages.at(-1);
  controller = new AbortController();
  const signal = controller.signal;
  const deadline = setTimeout(() => controller?.abort("timeout"), 180000);
  try {
    await store.persist();
    scroll();
    if (store.settings.autoSpeak) {
      void primeVolcTtsAudio().catch(() => {
        notice.value = "浏览器尚未允许自动播放，可在回答下方手动朗读。";
      });
    }
    const response = await fetch("/api/agent/chat/stream", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        conversationId: store.current.id,
        message: text,
        history,
        summary: store.current.summary,
        summarizedMessageCount: Math.max(
          0,
          store.current.summarizedMessageCount - droppedHistory,
        ),
      }),
      signal,
    });
    if (!response.ok) throw new Error(`请求失败（HTTP ${response.status}）。`);
    await readSse(response.body, async (event, data) => {
      if (event === "delta") {
        target.content += data.text || "";
        scroll();
        clearTimeout(persistTimer);
        persistTimer = setTimeout(() => store.persist(), 500);
      }
      if (event === "summary") {
        store.current.summary = data.text || "";
        store.current.summarizedMessageCount =
          droppedHistory + (Number(data.summarizedMessageCount) || 0);
      }
      if (event === "ui_payload") {
        target.uiBlocks.push(...(data.blocks || []));
        scroll();
      }
      if (event === "done") target.status = "done";
      if (event === "error")
        throw new Error(data.message || "服务暂时不可用。");
    });
    if (target.status !== "done") throw new Error("响应未完成，请重试。");
    if (retrying) {
      for (const previous of store.current.messages.slice(retryIndex + 1, -1)) {
        if (previous.role === "assistant") previous.superseded = true;
      }
    }
    if (store.settings.autoSpeak && speechHealth.value?.configured)
      void readAnswer(target.content);
  } catch (error) {
    target.status =
      signal.aborted && signal.reason !== "timeout" ? "interrupted" : "error";
    if (target.status === "error")
      target.error =
        signal.reason === "timeout" ? "等待响应超时，请重试。" : error.message;
  } finally {
    clearTimeout(deadline);
    clearTimeout(persistTimer);
    controller = null;
    busy.value = false;
    await store.persist();
    scroll();
  }
}
const voice = createVoiceCapture({
  onText: (text) => (input.value = text),
  onState: (state) => (voiceState.value = state),
  onError: (message) => (notice.value = message),
  onFinish: (text) => {
    input.value = text;
    if (store.settings.voiceAutoSend) send(text);
  },
});
async function toggleVoice() {
  try {
    if (voiceState.value === "idle") {
      stopReading();
      await voice.start();
    } else voice.stop();
  } catch (error) {
    notice.value = error.message;
  }
}
async function addImages(event) {
  for (const file of [...event.target.files].slice(
    0,
    4 - pendingImages.value.length,
  )) {
    if (
      !["image/png", "image/jpeg", "image/webp", "image/gif"].includes(
        file.type,
      ) ||
      file.size > 5 * 1024 * 1024
    ) {
      notice.value = "请选择不超过 5 MB 的 PNG、JPEG、WebP 或 GIF 图片。";
      continue;
    }
    const url = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
    pendingImages.value.push({
      id: createId(),
      type: "image",
      title: file.name,
      url,
    });
  }
  event.target.value = "";
}
function outside(event) {
  if (!profileArea.value?.contains(event.target)) userMenu.value = false;
}
function escape(event) {
  if (event.key === "Escape") {
    settingsOpen.value = false;
    showAbout.value = false;
    userMenu.value = false;
    if (viewer.value) closeViewer();
  }
}
async function checkHealth() {
  health.value = null;
  healthError.value = false;
  try {
    const response = await fetch("/api/agent/health", { signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error("连接失败");
    const result = await response.json();
    if (result.status !== "ok" || !["demo", "model"].includes(result.mode)) throw new Error("连接失败");
    health.value = result;
  } catch {
    healthError.value = true;
  }
}
onMounted(async () => {
  await store.init();
  scroll();
  document.addEventListener("pointerdown", outside);
  document.addEventListener("keydown", escape);
  void checkHealth();
  speechHealth.value = await fetch("/api/speech-health")
    .then((r) => (r.ok ? r.json() : null))
    .catch(() => null);
});
watch(
  () => store.settings.autoSpeak,
  (value) => {
    if (!value) stopReading();
  },
);
onBeforeUnmount(() => {
  controller?.abort();
  clearTimeout(persistTimer);
  voice.cancel();
  stopReading();
  document.removeEventListener("pointerdown", outside);
  document.removeEventListener("keydown", escape);
});
</script>
