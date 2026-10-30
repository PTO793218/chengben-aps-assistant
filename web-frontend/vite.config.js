import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import {resolveDataset} from '../dev-config/native-demo/dataset-selection.mjs';
import {readFileSync} from 'node:fs';
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, "..", "");
  const dataset = resolveDataset(process.env.APS_DEMO_DATASET);
  const native = JSON.parse(readFileSync(new URL('../'+dataset.dataDirectory+'/native-demo.json',import.meta.url),'utf8'));
  const identity = {datasetId:dataset.id,demandVersion:native.demandVersion,fixtureId:native.fixtureId,inputFingerprint:native.inputFingerprint};
  return {
    define: { __APS_DEMO_DATASET__: JSON.stringify(dataset.id) },
    cacheDir: process.env.VITE_RUNTIME_CACHE_DIR || "node_modules/.vite",
    plugins: [vue(), {name:'native-dataset-identity',generateBundle(){this.emitFile({type:'asset',fileName:'native-dataset.json',source:JSON.stringify(identity)});}}],
    server: {
      port: Number(process.env.VITE_DEV_PORT || env.VITE_DEV_PORT || 19443),
      strictPort: true,
      proxy: {
        "/api/agent": {
          target: `http://127.0.0.1:${process.env.AGENT_BACKEND_PORT || env.AGENT_BACKEND_PORT || 19441}`,
          rewrite: (p) => p.replace("/api/agent", "/api"),
        },
        "/api/speech": {
          target: `http://127.0.0.1:${process.env.SPEECH_BACKEND_PORT || env.SPEECH_BACKEND_PORT || 19442}`,
          ws: true,
        },
        "/api/speech-health": {
          target: `http://127.0.0.1:${process.env.SPEECH_BACKEND_PORT || env.SPEECH_BACKEND_PORT || 19442}`,
          rewrite: () => "/api/health",
        },
      },
    },
    build: {
      outDir: process.env.VITE_BUILD_OUT_DIR || env.VITE_BUILD_OUT_DIR || 'dist',
      emptyOutDir: true,
      chunkSizeWarningLimit: 1200,
    },
  };
});
