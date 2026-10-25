import {noise3JS,fbmJS} from '../core/noise-js.mjs';
export let activeTerrainKernel=null;
let loading=null,requested=null,epoch=0;
const state={memoPolicy:'explicit-only; canonical sample cache owns reuse',mode:'js',requested:'auto',loadMs:0,bytes:0,memoryBytes:0,reason:'not initialized'};
export function requestedTerrainKernel(scope=globalThis){
  return requested??(new URLSearchParams(scope.location?.search??'').get('terrainKernel')==='js'?'js':'auto');
}
export function terrainKernelStats(){
  const k=activeTerrainKernel;
  return {...state,memoryBytes:k?.memory.buffer.byteLength??0,cacheBytes:k?.cache_bytes()??0,
    cacheEntries:k?.cache_capacity()??0,cacheUsed:k?.cache_used()??0,hits:k?.cache_hits()??0,misses:k?.cache_misses()??0};
}
async function readBytes(url,signal){
  if(url.protocol==='file:'){const fs='node:fs/promises';return new Uint8Array(await (await import(fs)).readFile(url));}
  const response=await fetch(url,{signal});if(!response.ok)throw Error('WASM HTTP '+response.status);
  return new Uint8Array(await response.arrayBuffer());
}
export async function loadTerrainKernel({url=new URL('../assets/wasm/terrain-kernel.wasm',import.meta.url),read=readBytes,timeoutMs=4000}={}){
  if(typeof WebAssembly==='undefined')throw Error('WebAssembly unavailable');
  const controller=new AbortController();let timer;
  try{
    const bytes=await Promise.race([read(url,controller.signal),new Promise((_,reject)=>{timer=setTimeout(()=>{controller.abort();reject(Error('WASM load timeout'));},timeoutMs);})]);
    if(bytes.byteLength>128*1024)throw Error('Oversized WASM kernel');
    const module=await WebAssembly.compile(bytes);
    if(WebAssembly.Module.imports(module).length)throw Error('WASM kernel must have no host imports');
    const k=(await WebAssembly.instantiate(module)).exports;
    if(k.kernel_version?.()!==1||k.memory.buffer.byteLength>1024*1024||k.cache_capacity()!==8192)throw Error('Incompatible WASM kernel');
    // Every instance validates against the retained producer before activation.
    for(const [x,y,z,f,seed,oct] of [[.125,-.25,.75,2.5,42,5],[-900.1,127.2,.001,19,4294967295,3],[1,-1,0,4000,1973,4],[.5,.5,.5,.25,0,0]]){
      if(k.noise3(x,y,z,seed)!==noise3JS(x,y,z,seed)||k.fbm(x,y,z,f,seed,oct)!==fbmJS([x,y,z],f,seed,oct))throw Error('WASM parity check failed');
    }
    k.reset_cache();return {kernel:k,bytes:bytes.byteLength};
  }finally{clearTimeout(timer);controller.abort();}
}
export function initTerrainKernel({mode=requestedTerrainKernel(),...options}={}){
  mode=mode==='js'?'js':'auto';
  if(requested===mode&&loading)return loading;
  requested=mode;const version=++epoch;activeTerrainKernel=null;
  Object.assign(state,{requested:mode,mode:'js',reason:mode==='js'?'explicit JavaScript mode':'loading',loadMs:0,bytes:0});
  if(mode==='js')return loading=Promise.resolve(terrainKernelStats());
  loading=(async()=>{const start=performance.now();
    try{const {kernel,bytes}=await loadTerrainKernel(options);if(version===epoch){activeTerrainKernel=kernel;Object.assign(state,{mode:'wasm',bytes,reason:null});}}
    catch(error){if(version===epoch)state.reason=error.message;}
    finally{if(version===epoch)state.loadMs=performance.now()-start;}
    return terrainKernelStats();
  })();return loading;
}
