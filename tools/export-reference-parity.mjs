import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
const root=resolve(import.meta.dirname,'..'),web=resolve(process.argv[2]??'../2026-10-20/files-mentioned-by-the-user-starfront/outputs/planet-studio');
const {deserializePlanet}=await import(pathToFileURL(resolve(web,'io/bundle.mjs')));
const {analyzeReference}=await import(pathToFileURL(resolve(web,'render/reference-analysis.mjs')));
const world=await deserializePlanet(await readFile(resolve(root,'Assets/StreamingAssets/Ladir/planet/river-v42.planet'))),result=analyzeReference(world,32);
await writeFile(resolve(root,'evidence/planet-port/reference-fixtures.json'),JSON.stringify(result,(_,v)=>ArrayBuffer.isView(v)?Array.from(v):v));
console.log('Original open-boundary drainage fixture exported at 32 x 32 cells.');
