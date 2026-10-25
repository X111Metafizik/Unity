import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
const names=['frameMs','cpuActiveMs','renderThreadMs','presentWaitMs','gpuMs','planMs','submitMs','stitchMs','uploadMs','simulationMs','mainThreadAllocated','queue','residentMiB'];
function stats(values){const a=values.filter(Number.isFinite).sort((a,b)=>a-b);return a.length?{median:a[Math.floor(a.length*.5)],p95:a[Math.ceil(a.length*.95)-1],p99:a[Math.ceil(a.length*.99)-1],max:a.at(-1)}:null;}
for(const directory of process.argv.slice(2)){
 const file=path.join(directory,'frames.json');
 const raw=fs.existsSync(file)?fs.readFileSync(file):zlib.gunzipSync(fs.readFileSync(file+'.gz'));
 const r=JSON.parse(raw.toString('utf8'));
 const phases=r.phases.map((name,i)=>{const rows=r.frames.filter(x=>x.phase===i);return{name,frames:rows.length,gpuMeasuredFrames:rows.filter(x=>x.gpuMs>0).length,longFrames50ms:rows.filter(x=>x.frameMs>50).length,longFrames100ms:rows.filter(x=>x.frameMs>100).length,collections:rows.length?rows.at(-1).collections-rows[0].collections:0,...Object.fromEntries(names.map(k=>[k,stats(rows.map(x=>x[k]).filter(v=>k==='gpuMs'?v>0:k==='mainThreadAllocated'?v>=0:true))]))};});
 const summary={...r,phases};delete summary.frames;
 summary.firstPresentedSeconds=r.frames[0]?.elapsed;
 summary.closeDetailAfterFlightSeconds=r.closeDetailSeconds<0?null:r.closeDetailSeconds-r.flightStartSeconds;
 summary.slowestFrames=[...r.frames].sort((a,b)=>b.frameMs-a.frameMs).slice(0,12);
 const flight=r.frames.filter(f=>f.phase>0);
 summary.flight={frames:flight.length,meanFps:1000/(flight.reduce((s,f)=>s+f.frameMs,0)/flight.length),frameMs:stats(flight.map(f=>f.frameMs)),longFrames50ms:flight.filter(f=>f.frameMs>50).length,longFrames100ms:flight.filter(f=>f.frameMs>100).length,minimumCellMeters:Math.min(...flight.map(f=>f.cellMeters)),maxResidentMiB:Math.max(...flight.map(f=>f.residentMiB)),maxMeshMiB:Math.max(...flight.map(f=>f.meshMiB)),maxQueue:Math.max(...flight.map(f=>f.queue))};
 summary.visibilityChange=stats(r.frames.slice(1).map((f,i)=>f.visible-r.frames[i].visible));
 summary.finalCamera=r.frames.at(-1);
 fs.writeFileSync(path.join(directory,'summary.json'),JSON.stringify(summary,null,2)+'\n');
 fs.writeFileSync(file+'.gz',zlib.gzipSync(raw,{level:9}));
 console.log(directory,JSON.stringify({startup:r.startupMs,firstPresented:summary.firstPresentedSeconds,firstSurface:r.firstSurfaceSeconds,detailAfterFlight:summary.closeDetailAfterFlightSeconds,errors:r.errors,phases:phases.map(p=>({name:p.name,p95:p.frameMs?.p95,p99:p.frameMs?.p99,max:p.frameMs?.max,long50:p.longFrames50ms,gpu:p.gpuMs?.p95,cpuActive:p.cpuActiveMs?.p95,upload:p.uploadMs?.max,stitch:p.stitchMs?.max,collections:p.collections}))},null,2));
}
