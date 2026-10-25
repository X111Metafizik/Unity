// Summarize saved measurements; this command does not run a new GPU benchmark.
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import assert from 'node:assert/strict';

const root=fileURLToPath(new URL('../',import.meta.url));
const at=process.argv.indexOf('--web-source');
if(at<0||!process.argv[at+1])throw Error('Usage: node tools/compare-benchmarks.mjs --web-source <LADIR web checkout>');
const webRoot=resolve(process.argv[at+1]),sources=[];
async function load(base,path){
 const raw=await readFile(resolve(base,path));
 sources.push({repository:base===root?'Unity':'ladir',path,sha256:createHash('sha256').update(raw).digest('hex')});
 return JSON.parse(raw);
}
const before=await load(root,'evidence/unity-v1/visible/benchmark.json');
const after=await load(root,'evidence/unity-v1/final/benchmark.json');
const webLoadingBefore=await load(webRoot,'evidence/v41/user-before/loading.json');
const webLoadingAfter=await load(webRoot,'evidence/v41/user-final/loading.json');
const webFlightBefore=await load(webRoot,'evidence/v41/flight-before/benchmark.json');
const webFlightAfter=await load(webRoot,'evidence/v41/flight-final/benchmark.json');
const webDescentBefore=await load(webRoot,'evidence/v42/baseline/descent.json');
const webDescentAfter=await load(webRoot,'evidence/v42/release/descent.json');
assert.equal(before.worldHash,after.worldHash);
assert.equal(before.width,after.width);assert.equal(before.height,after.height);
assert.equal(before.cpu,after.cpu);assert.equal(before.gpu,after.gpu);
assert.ok(webDescentAfter.runs.every(r=>r.world===after.worldHash));
const change=(a,b)=>({before:a,after:b,delta:b-a,percentChange:100*(b-a)/a});
const phase=entry=>({
 frames:entry.frames,medianMs:entry.medianMs,medianFpsEquivalent:1000/entry.medianMs,
 p95Ms:entry.p95Ms,p99Ms:entry.p99Ms,maxMs:entry.maxMs,
 cpuFrameMedianMs:entry.cpuMedianMs,gpuMedianMs:entry.gpuMedianMs,
 over16_667ms:entry.over16ms,over33_333ms:entry.over33ms,over50ms:entry.over50ms
});
function flightTotals(run){
 const phases=run.phases.filter(p=>p.phase!=='startup');
 const sum=key=>phases.reduce((a,p)=>a+p[key],0);
 return {frames:sum('frames'),over16_667ms:sum('over16ms'),over16_667Percent:100*sum('over16ms')/sum('frames'),over33_333ms:sum('over33ms'),over50ms:sum('over50ms'),maxMs:Math.max(...phases.map(p=>p.maxMs))};
}
const startupFields=['firstSurfaceMs','fullInitialDetailMs','uploadCpuTotalMs','uploadCpuMaxMs','readTaskLatencyTotalMs','meshJobLatencyTotalMs','regionCachePeakBytes','meshBufferPeakBytes','unityAllocatedBytes','managedHeapBytes','readTiles','terrainPlans'];
const unity={
 worldHash:after.worldHash,hardware:{cpu:after.cpu,gpu:after.gpu,api:after.graphicsApi,width:after.width,height:after.height},
 metrics:Object.fromEntries(startupFields.map(key=>[key,change(before[key],after[key])])),
 processToFirstSurfaceMs:after.processToFirstSurfaceMs,processToFullDetailMs:after.processToFullDetailMs,
 processWorkingSetBytes:null,
 startup:{before:phase(before.phases.find(p=>p.phase==='startup')),after:phase(after.phases.find(p=>p.phase==='startup'))},
 flight:{before:flightTotals(before),after:flightTotals(after)},
 phases:after.phases.filter(p=>p.phase!=='startup').map(p=>{
  const previous=before.phases.find(q=>q.phase===p.phase);assert.ok(previous);
  return {name:p.phase,before:phase(previous),after:phase(p),medianChange:change(previous.medianMs,p.medianMs),p95Change:change(previous.p95Ms,p.p95Ms),p99Change:change(previous.p99Ms,p.p99Ms),p95ReductionNeededFor10msPercent:100*(1-10/p.p95Ms)};
 })
};
const loading=d=>d.runs.map(r=>({cache:r.cache,worldHash:r.worldHash,editorReadyMs:r.editorReadyMs,cockpitReadyMs:r.cockpitReadyMs,detailReadyMs:r.detailReadyMs,camera:r.camera,fov:r.fov}));
const flight=d=>d.runs.map(r=>({cache:r.cache,durationMs:r.duration,renderFps:r.renderFps,cpu:r.summary.cpu,gpuRender:r.summary.gpuRender,frame:r.summary.frame,over50:r.summary.over50,over100:r.summary.over100,scenarios:Object.keys(r.scenarios)}));
const descent=d=>d.runs.map(r=>{
 const first=r.samples.find(s=>s.cellMeters>0&&s.cellMeters<=3.814697265625+.001);
 return {cache:r.cache,worldHash:r.world,frames:r.frames.length,firstReferenceDetailMs:first?.ms??null,arrivalAfterMs:(8500-450)/650*1000,settledMs:r.settledMs,lastCellMeters:r.samples.at(-1).cellMeters,peakTrackedTerrainBytes:Math.max(...r.samples.map(s=>s.terrain.cpuTotalBufferBytes??0)),largestTransitionGroup:Math.max(...r.samples.map(s=>s.largestGroup??0)),cancelledJobsDuringRecord:(r.samples.at(-1).terrain.cancelledJobs??0)-(r.samples[0].terrain.cancelledJobs??0),summary:r.summary};
});
const report={
 schema:'ladir-comparison-1',method:'Analysis of previously recorded runs; no new performance run. Negative percentChange is a decrease. FPS equivalents are 1000/median frame ms, not average or guaranteed minimum FPS.',
 sourceCommits:{Unity:'6a84c9e11d98eaa1de4dab1ef903839bb7ef5f9b',ladir:'34841d6abc1c1c12251c2ace18ffb853895db16f'},sources,unity,
 browser:{loadingV41:{before:loading(webLoadingBefore),after:loading(webLoadingAfter)},flightV41:{before:flight(webFlightBefore),after:flight(webFlightAfter)},descentV42:{before:descent(webDescentBefore),after:descent(webDescentAfter)}},
 limitations:[
  'One saved run per Unity revision, not a repeated randomized A/B experiment; material changes and warm operating-system cache confound attribution.',
  'Unity covers a finite 32 km region without trees, enemies, volumetric clouds or global warp; browser/native speedup ratios are not valid.',
  'Unity fixed sample spacing is 7.8125 m; browser reference-point meshes reached 0.9537-3.8147 m. Same world hash is not equal mesh or material quality.',
  'Browser V41 flight uses a different world from the Unity and V42 data; browser presentation is about 60 Hz while Unity VSync is disabled.',
  'Unity startup clocks, browser flight-click clocks and descent settlement clocks have different starting events and completion criteria.',
  'Unity CPU frame time includes waits and is not equivalent to browser CPU submission timing. CPU and GPU quantiles must not be summed.',
  'Unity benchmark boost-release motion advances at 300 m/s while its reported speed/prefetch velocity varies 500 to 194.444 m/s; interactive boost/landing is a separate functional smoke test.',
  'Unity screenshot frames are included. Saved Unity results contain phase aggregates, not per-frame traces, so combined p95/p99 and the percentage exceeding 10 ms cannot be reconstructed.',
  'Browser V42 recording includes post-descent settlement; before and after windows differ in duration. GPU timestamps are absent in that test.',
  'Zero working-set result means unavailable, not zero RAM; GPU mesh estimates exclude textures and driver allocations.'
 ]
};
const output=resolve(root,'evidence/comparison-v1/summary.json');
await mkdir(dirname(output),{recursive:true});await writeFile(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({output,unityMetrics:unity.metrics,flight:unity.flight,phases:unity.phases.map(p=>({name:p.name,medianChangePercent:p.medianChange.percentChange,p95ReductionNeededFor10msPercent:p.p95ReductionNeededFor10msPercent})),browserDescent:report.browser.descentV42},null,2));
