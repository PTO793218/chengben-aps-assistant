<template>
  <div ref="el" class="scene-host">
    <div v-if="error" class="scene-fallback">{{ error }}</div>
    <div class="scene-caption">
      拖动旋转 · 滚轮缩放 · 右键平移<span>通用三维示例 · 非排程结果</span>
    </div>
  </div>
</template>
<script setup>
import { ref, onMounted, onBeforeUnmount } from "vue";
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
const el = ref(),
  error = ref("");
let renderer, controls, observer, scene, frame;
onMounted(() => {
  try {
    scene = new THREE.Scene();
    scene.background = new THREE.Color("#f7faff");
    const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 100);
    camera.position.set(9, 8, 11);
    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.shadowMap.enabled = true;
    el.value.appendChild(renderer.domElement);
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.target.set(0, 0.5, 0);
    controls.minDistance = 4;
    controls.maxDistance = 24;
    scene.add(new THREE.AmbientLight(0xffffff, 2.2));
    const light = new THREE.DirectionalLight(0xffffff, 3);
    light.position.set(3, 8, 5);
    light.castShadow = true;
    scene.add(light);
    const floor = new THREE.Mesh(
      new THREE.PlaneGeometry(14, 12),
      new THREE.MeshStandardMaterial({ color: 0xf0f5fc, roughness: 0.9 }),
    );
    floor.rotation.x = -Math.PI / 2;
    floor.receiveShadow = true;
    scene.add(floor);
    const grid = new THREE.GridHelper(12, 12, 0xd4e1f1, 0xe1eaf5);
    grid.position.y = 0.01;
    scene.add(grid);
    for (let x = 0; x < 6; x++)
      for (let z = 0; z < 4; z++) {
        const h = 0.3 + ((x * 3 + z * 2) % 7) * 0.18;
        const block = new THREE.Mesh(
          new THREE.BoxGeometry(0.82, h, 0.82),
          new THREE.MeshStandardMaterial({
            color: [0x398bff, 0x6bb4ff, 0x64d3bd, 0xa1c5ff][(x + z) % 4],
            roughness: 0.35,
          }),
        );
        block.position.set(x - 2.5, h / 2, z - 1.5);
        block.castShadow = true;
        block.receiveShadow = true;
        scene.add(block);
      }
    observer = new ResizeObserver(() => {
      const { width, height } = el.value.getBoundingClientRect();
      if (!width || !height) return;
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      renderer.setSize(width, height);
    });
    observer.observe(el.value);
    const animate = () => {
      frame = requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    };
    animate();
  } catch {
    error.value = "当前设备无法开启 WebGL，请在支持三维渲染的浏览器中查看。";
  }
});
onBeforeUnmount(() => {
  cancelAnimationFrame(frame);
  observer?.disconnect();
  controls?.dispose();
  scene?.traverse((o) => {
    o.geometry?.dispose();
    if (Array.isArray(o.material)) o.material.forEach((m) => m.dispose());
    else o.material?.dispose();
  });
  renderer?.dispose();
});
</script>
