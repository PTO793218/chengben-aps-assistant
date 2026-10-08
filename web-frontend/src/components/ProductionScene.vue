<template>
  <div ref="host" class="production-scene" aria-label="三维排程画布" :data-view="currentView">
    <div class="production-axis-labels" aria-hidden="true"><span v-for="item in screenLabels" :key="item.key" class="production-axis-label" :title="item.text" :style="{left:item.x+'px',top:item.y+'px',color:item.color}">{{ item.text }}</span></div>
    <div v-if="error" class="production-render-error">{{ error }}<span>仍可使用右侧订单与任务明细查看数据。</span></div>
    <div v-if="hover" ref="tooltip" class="production-tooltip" @pointerleave="leave" :style="{left:hover.x+'px',top:hover.y+'px'}"><dl v-if="hover.rows"><template v-for="[name,value] in hover.rows" :key="name"><dt>{{ name }}</dt><dd>{{ value }}</dd></template></dl><span v-else>{{ hover.label }}</span></div>
  </div>
</template>
<script setup>
import {ref,onMounted,onBeforeUnmount,watch,nextTick} from 'vue';
import * as THREE from 'three';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {nearestPlane,avoidLabelOverlap} from '../services/scene-view.js';
const props=defineProps({model:Object,selectedId:String,autoSnap:{type:Boolean,default:true}});
const emit=defineEmits(['select','view-change']);
const host=ref(),tooltip=ref(),error=ref(''),hover=ref(null),screenLabels=ref([]),currentView=ref('free');
let scene,root,camera,renderer,controls,observer,frame,instances,startPointer,snapTimer,labelMeasure,labelsDirty=true,viewSign=1,lastContentKey,lastLayout;
const ray=new THREE.Raycaster(),pointer=new THREE.Vector2();
function disposeRoot(){
  root?.traverse(o=>{o.geometry?.dispose();const materials=Array.isArray(o.material)?o.material:[o.material];materials.forEach(m=>{m?.map?.dispose();m?.dispose();});});
  if(root)scene.remove(root);
}
function updateLabels(){
  if(!labelsDirty)return;labelsDirty=false;
  const {width,height}=host.value.getBoundingClientRect();
  labelMeasure ||= document.createElement('canvas').getContext('2d');
  labelMeasure.font='500 12px "Microsoft YaHei", sans-serif';
  camera.updateMatrixWorld();
  const hidden={top:'y',front:'z',side:'x'}[currentView.value];
  const projected=props.model.labels.flatMap((item,key)=>{
    if(item.axis===hidden)return [];
    const p=new THREE.Vector3(item.x,item.y,item.z).project(camera);
    if(p.z < -1 || p.z > 1)return [];
    return [{...item,key,x:(p.x+1)*width/2,y:(1-p.y)*height/2,width:labelMeasure.measureText(item.text).width+14}];
  });
  screenLabels.value=avoidLabelOverlap(projected,width,height);
}
function rebuild(){
  if(!scene)return;
  const layoutOnly=lastContentKey===props.model.contentKey && lastLayout!==JSON.stringify(props.model.layout);
  lastContentKey=props.model.contentKey;lastLayout=JSON.stringify(props.model.layout);
  disposeRoot();root=new THREE.Group();scene.add(root);hover.value=null;
  const cells=props.model.cells;
  instances=new THREE.InstancedMesh(new THREE.BoxGeometry(1,1,1),new THREE.MeshStandardMaterial({roughness:.6,metalness:.02}),cells.length);
  const dummy=new THREE.Object3D();
  cells.forEach((cell,i)=>{dummy.position.set(cell.x,cell.y,cell.z);dummy.scale.set(cell.w,cell.h,cell.d);dummy.updateMatrix();instances.setMatrixAt(i,dummy.matrix);instances.setColorAt(i,new THREE.Color(cell.color));});
  instances.instanceMatrix.needsUpdate=true;if(instances.instanceColor)instances.instanceColor.needsUpdate=true;
  root.add(instances);
  for(const guide of props.model.guides){
    const geometry=new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(...guide.from),new THREE.Vector3(...guide.to)]);
    root.add(new THREE.Line(geometry,new THREE.LineBasicMaterial({color:guide.color})));
  }
  if(layoutOnly){labelsDirty=true;}
  else setView(currentView.value,viewSign);
}
function setView(view='free',sign=1,fit=true){
  if(!camera)return;
  clearTimeout(snapTimer);
  currentView.value=view;viewSign=sign;emit('view-change',view);
  const {width,depth,height}=props.model;
  const center=fit?new THREE.Vector3(width/2,height/2,depth/2):controls.target.clone();
  const span=Math.max(width+8,depth+8,height*1.8,14);
  const directions={top:new THREE.Vector3(0,span*sign,.0001),front:new THREE.Vector3(0,0,span*sign),side:new THREE.Vector3(span*sign,0,0),free:new THREE.Vector3(span*.65,span*.65,span*.85)};
  controls.enableDamping=false;controls.update();
  controls.target.copy(center);
  camera.position.copy(center).add(directions[view]);
  if(fit)camera.zoom=1;
  camera.lookAt(center);resize();controls.update();controls.enableDamping=true;labelsDirty=true;
}
function reset(){setView('free');}
function endOrbit(){
  clearTimeout(snapTimer);
  snapTimer=setTimeout(()=>{
    if(!props.autoSnap)return;
    const nearest=nearestPlane(camera.position.clone().sub(controls.target));
    if(nearest)setView(nearest.view,nearest.sign,false);
  },220);
}
function resize(){
  if(!renderer)return;
  const {width,height}=host.value.getBoundingClientRect();if(!width||!height)return;
  const m=props.model,aspect=width/height;
  camera.updateMatrixWorld();
  const points=[];
  for(const x of [0,m.width])for(const y of [0,m.height])for(const z of [0,m.depth])points.push(new THREE.Vector3(x,y,z).applyMatrix4(camera.matrixWorldInverse));
  const hidden={top:'y',front:'z',side:'x'}[currentView.value];
  for(const item of m.labels)if(item.axis!==hidden)points.push(new THREE.Vector3(item.x,item.y,item.z).applyMatrix4(camera.matrixWorldInverse));
  const xs=points.map(p=>p.x),ys=points.map(p=>p.y),minX=Math.min(...xs),maxX=Math.max(...xs),minY=Math.min(...ys),maxY=Math.max(...ys);
  const size=Math.max((maxY-minY)/Math.max(.5,1-70/height),(maxX-minX)/aspect/Math.max(.5,1-150/width),4)/2;
  const cx=(minX+maxX)/2,cy=(minY+maxY)/2;
  camera.left=cx-size*aspect;camera.right=cx+size*aspect;camera.top=cy+size;camera.bottom=cy-size;camera.updateProjectionMatrix();renderer.setSize(width,height);
  labelsDirty=true;
}
function hit(event){
  const rect=renderer.domElement.getBoundingClientRect();
  pointer.set((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1);
  ray.setFromCamera(pointer,camera);const found=ray.intersectObject(instances)[0];
  return found?props.model.cells[found.instanceId]:null;
}
function move(event){
  const cell=hit(event),rect=host.value.getBoundingClientRect();
  const value=cell?{label:cell.label,rows:cell.tooltip,x:Math.max(8,event.clientX-rect.left+12),y:Math.max(8,event.clientY-rect.top+12)}:null;
  hover.value=value;
  if(value)nextTick(()=>{
    if(hover.value!==value && hover.value?.label!==value.label)return;
    const tip=tooltip.value?.getBoundingClientRect();if(!tip||!hover.value)return;
    const x=value.x+tip.width>rect.width-8?value.x-tip.width-28:value.x;
    const y=value.y+tip.height>rect.height-8?value.y-tip.height-28:value.y;
    hover.value.x=Math.max(8,Math.min(x,rect.width-tip.width-8));
    hover.value.y=Math.max(8,Math.min(y,rect.height-tip.height-8));
  });
  renderer.domElement.style.cursor=cell?'pointer':'grab';
}
function down(event){clearTimeout(snapTimer);startPointer=[event.clientX,event.clientY];}
function up(event){if(!startPointer||Math.hypot(event.clientX-startPointer[0],event.clientY-startPointer[1])>5)return;const cell=hit(event);if(cell)emit('select',cell);startPointer=null;}
function leave(event){if(event?.relatedTarget?.closest?.('.production-tooltip'))return;hover.value=null;}
onMounted(()=>{
  try{
    scene=new THREE.Scene();scene.background=new THREE.Color('#f5f8fc');
    camera=new THREE.OrthographicCamera(-20,20,20,-20,.1,2000);
    renderer=new THREE.WebGLRenderer({antialias:true});renderer.setPixelRatio(Math.min(devicePixelRatio,2));
    renderer.domElement.setAttribute('aria-label','可旋转的三维数据视图');host.value.appendChild(renderer.domElement);
    controls=new OrbitControls(camera,renderer.domElement);controls.enableDamping=true;controls.minZoom=.35;controls.maxZoom=12;
    controls.maxPolarAngle=Math.PI/2;
    controls.addEventListener('change',()=>{labelsDirty=true;});
    controls.addEventListener('start',()=>{clearTimeout(snapTimer);});
    controls.addEventListener('end',endOrbit);
    renderer.domElement.addEventListener('pointermove',event=>{
      if(startPointer && event.buttons===1 && Math.hypot(event.clientX-startPointer[0],event.clientY-startPointer[1])>5){currentView.value='free';emit('view-change','free');labelsDirty=true;}
    });
    scene.add(new THREE.AmbientLight('#ffffff',1.5));
    const light=new THREE.DirectionalLight('#ffffff',1.4);light.position.set(10,30,15);scene.add(light);
    rebuild();observer=new ResizeObserver(resize);observer.observe(host.value);
    renderer.domElement.addEventListener('pointermove',move);renderer.domElement.addEventListener('pointerdown',down);renderer.domElement.addEventListener('pointerup',up);renderer.domElement.addEventListener('pointerleave',leave);
    const animate=()=>{frame=requestAnimationFrame(animate);controls.update();renderer.render(scene,camera);updateLabels();};animate();
  }catch{error.value='当前设备无法开启三维渲染。';}
});
watch(()=>props.model,rebuild);
watch(()=>props.selectedId,()=>{
  if(!instances)return;
  props.model.cells.forEach((cell,i)=>instances.setColorAt(i,new THREE.Color(cell.id===props.selectedId?'#efaf62':cell.color)));
  if(instances.instanceColor)instances.instanceColor.needsUpdate=true;
});
onBeforeUnmount(()=>{clearTimeout(snapTimer);cancelAnimationFrame(frame);observer?.disconnect();controls?.dispose();disposeRoot();renderer?.dispose();});
defineExpose({reset,top:()=>setView('top'),setView});
</script>
