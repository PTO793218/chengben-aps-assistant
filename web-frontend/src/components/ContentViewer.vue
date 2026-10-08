<template>
  <section class="content-viewer" aria-label="内容查看区">
    <header class="viewer-header">
      <div>
        <button
          class="icon-button"
          aria-label="关闭查看区"
          @click="$emit('close')"
        >
          <Icon name="close" /></button
        ><Icon :name="item.type === 'scene' ? 'cube' : 'image'" /><strong>{{
          item.title
        }}</strong
        ><span class="subtle-badge">{{
          item.type === "scene" ? "3D 场景" : "图片预览"
        }}</span>
      </div>
      <button
        class="icon-button"
        aria-label="切换查看区全屏"
        @click="toggleFullscreen"
      >
        <Icon name="expand" />
      </button>
    </header>
    <div class="viewer-content" ref="host">
      <SceneViewer v-if="item.type === 'scene'" />
      <div v-else class="image-stage">
        <img
          :src="item.url"
          :alt="item.title"
          :style="{ width: `${zoom * 100}%`, maxWidth: 'none' }"
        />
      </div>
    </div>
    <footer class="viewer-footer">
      <span>{{
        item.type === "scene"
          ? "自由查看三维内容"
          : "仅在本机预览，图片未发送给模型"
      }}</span>
      <div v-if="item.type !== 'scene'">
        <button
          @click="zoom = Math.max(0.25, zoom - 0.25)"
          aria-label="缩小图片"
        >
          −</button
        ><button @click="zoom = 1">{{ Math.round(zoom * 100) }}%</button
        ><button @click="zoom = Math.min(3, zoom + 0.25)" aria-label="放大图片">
          ＋</button
        ><a
          :href="item.url"
          :download="item.title"
          class="icon-button"
          aria-label="下载图片"
          ><Icon name="download"
        /></a>
      </div>
    </footer>
  </section>
</template>
<script setup>
import { ref, watch, defineAsyncComponent } from "vue";
import Icon from "./Icon.vue";
const SceneViewer = defineAsyncComponent(() => import("./SceneViewer.vue"));
const props = defineProps({ item: Object });
defineEmits(["close"]);
const zoom = ref(1),
  host = ref();
watch(
  () => props.item,
  () => (zoom.value = 1),
);
async function toggleFullscreen() {
  if (document.fullscreenElement) await document.exitFullscreen();
  else await host.value?.requestFullscreen?.();
}
</script>
