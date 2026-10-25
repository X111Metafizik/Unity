import {sha256} from './math.mjs';
import {STAMP_ATLAS_SHA256} from './stamp-catalog.mjs';
let loading;
// The source GLBs never enter the renderer. A verified linear-height atlas is
// copied into signed world buffers, then transferred to terrain/flight workers.
export async function loadStampHeights(){
 loading??=(async()=>{
  const url=new URL('../assets/terrain-stamps/atlas.bin',import.meta.url);let bytes;
  if(url.protocol==='file:'){const moduleName='node:fs/promises';const {readFile}=await import(moduleName);bytes=new Uint8Array(await readFile(url));}
  else{const response=await fetch(url);if(!response.ok)throw Error('Arazi yükseklik arşivi yüklenemedi.');bytes=new Uint8Array(await response.arrayBuffer());}
  if(await sha256(bytes)!==STAMP_ATLAS_SHA256)throw Error('Arazi yükseklik arşivi imzası eşleşmiyor.');return bytes;
 })();
 try{return (await loading).slice();}catch(error){loading=null;throw error;}
}
