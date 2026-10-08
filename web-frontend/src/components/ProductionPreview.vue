<template>
  <svg class="production-preview-svg" :viewBox="viewBox" aria-hidden="true">
    <polygon v-for="(polygon,i) in polygons" :key="i" :points="polygon.points" :fill="polygon.color" :opacity="polygon.opacity" stroke="#fff" stroke-width="0.22" />
  </svg>
</template>
<script setup>
import {computed} from 'vue';
import {makeProductionScene} from '../services/production';
const props=defineProps({snapshot:Object,mode:String,focus:Object});
const model=computed(()=>makeProductionScene(props.snapshot,props.mode,props.focus));
const project=(x,y,z)=>[x*10-z*6, x*2.1+z*3-y*9];
const polygons=computed(()=>model.value.cells.map(c=>{
  const points=[project(c.x-c.w/2,c.y+c.h/2,c.z-c.d/2),project(c.x+c.w/2,c.y+c.h/2,c.z-c.d/2),project(c.x+c.w/2,c.y+c.h/2,c.z+c.d/2),project(c.x-c.w/2,c.y+c.h/2,c.z+c.d/2)];
  return {points:points.map(p=>p.join(',')).join(' '),coordinates:points,color:c.color,opacity:c.state==='unknown'?.55:1};
}));
const viewBox=computed(()=>{
  const points=polygons.value.flatMap(p=>p.coordinates);
  if(!points.length)return '0 0 300 180';
  const xs=points.map(p=>p[0]),ys=points.map(p=>p[1]);
  const x=Math.min(...xs)-15,y=Math.min(...ys)-12;
  return `${x} ${y} ${Math.max(...xs)-x+15} ${Math.max(...ys)-y+12}`;
});
</script>
