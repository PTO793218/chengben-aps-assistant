import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=fileURLToPath(new URL('../../../',import.meta.url));
const require=createRequire(path.join(root,'web-frontend/package.json'));
export const vue=require('vue');
const {parse,compileScript}=require('vue/compiler-sfc');

// Exercise the current SFC setup without a browser. Only child rendering and
// browser lifecycle hooks are substituted; refs, watches and services are real.
export async function setupSfc(file,props={},overrides={}){
 const filename=path.join(root,file),hooks=[],deps={};
 let code=compileScript(parse(fs.readFileSync(filename,'utf8')).descriptor,{id:'regression-test'}).content.replace(/import\s+['"][^'"]+\.css['"]\s*;/g,'');
 for(const match of [...code.matchAll(/import\s+([\s\S]*?)\s+from\s+(['"])(.*?)\2\s*;/g)]){
  const [,clause,,specifier]=match;
  let module;
  if(specifier==='vue')module={...vue,onMounted:()=>{},onBeforeUnmount:fn=>hooks.push(fn)};
  else if(specifier.endsWith('.vue'))module={default:{}};
  else{let location=path.resolve(path.dirname(filename),specifier);if(!path.extname(location))location+='.js';module=await import(pathToFileURL(location));}
  if(clause.trim().startsWith('{')){
   for(const binding of clause.trim().slice(1,-1).split(',').map(s=>s.trim()).filter(Boolean)){
    const [name,alias]=binding.split(/\s+as\s+/);deps[alias||name]=overrides[alias||name]??module[name];
   }
  }else deps[clause.trim()]=overrides[clause.trim()]??module.default;
  code=code.replace(match[0],'');
 }
 const component=new Function(...Object.keys(deps),code.replace('export default','return'))(...Object.values(deps));
 const scope=vue.effectScope(),events=[];
 const state=scope.run(()=>component.setup(vue.reactive(props),{expose(){},emit:(event,value)=>events.push({event,value})}));
 return {state,events,unmount(){hooks.forEach(fn=>fn());scope.stop();}};
}
