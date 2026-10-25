using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ladir.Planet
{
    // Opt-in, allocation-free frame capture. Startup is part of the measurement,
    // unlike PlanetPortSmoke's visual warm-up. Write JSON only after the route.
    [DefaultExecutionOrder(-1000)]
    public sealed class PlanetPerformanceRun : MonoBehaviour
    {
        const int Capacity=65536;
        struct Frame
        {
            public int index,phase,nodes,queue,workers,visible;
            public double elapsed,frameMs,planMs,submitMs,stitchMs,uploadMs,simulationMs,controllerMs;
            public double cpuMs,cpuActiveMs,renderThreadMs,presentWaitMs,gpuMs,cellMeters,meshMiB,residentMiB,x,y,z,simulationTime;
            public long gcBytes,mainThreadAllocated;public int collections;
        }
        readonly Frame[] frames=new Frame[Capacity];
        readonly FrameTiming[] timing=new FrameTiming[1];
        readonly List<string> errors=new List<string>();
        readonly WaitForEndOfFrame endOfFrame=new WaitForEndOfFrame();
        ProfilerRecorder allocations;
        PlanetStudio app;string folder;int count,phase;double kernelReady=-1,firstSurface=-1,closeDetail=-1,flightStart=-1,previousEnd,firstFrame;
        long previousAllocated;
        static long AllocatedBytes()
        {
#if ENABLE_IL2CPP
            // Unity 6000.0's native GC implementation explicitly does not
            // implement GetAllocatedBytesForCurrentThread. Unknown is not zero.
            return -1;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--performance-run");if(at<0||at+1>=args.Length)return;
            var go=new GameObject("LADIR deterministic performance capture");DontDestroyOnLoad(go);go.AddComponent<PlanetPerformanceRun>().folder=args[at+1];
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);Application.logMessageReceived+=OnLog;
            allocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
            firstFrame=previousEnd=Time.realtimeSinceStartupAsDouble;
            previousAllocated=AllocatedBytes();
            while(count<Capacity)
            {
                yield return endOfFrame;
                double now=Time.realtimeSinceStartupAsDouble;
                if(!app)app=FindFirstObjectByType<PlanetStudio>();
                if(app){app.Automated=true;app.Sound=false;}
                var t=app?.Terrain;
                if(app?.Kernel!=null&&kernelReady<0)kernelReady=now;
                if(t!=null&&t.visibleTiles>0&&firstSurface<0)firstSurface=now;
                double cell=app?.Kernel==null?double.PositiveInfinity:t.RenderedCellSize(app.Flying?app.Flight.position.Unit:app.Focus);
                if(app!=null&&app.Flying&&cell<=8&&closeDetail<0)closeDetail=now;
                FrameTimingManager.CaptureFrameTimings();uint valid=FrameTimingManager.GetLatestTimings(1,timing);
                var p=app?.CameraPosition??new D3();
                long allocated=AllocatedBytes();
                frames[count]=new Frame{index=count,phase=phase,elapsed=now,frameMs=(now-previousEnd)*1000,planMs=t?.lastPlanMs??0,submitMs=t?.lastSubmitMs??0,stitchMs=t?.lastStitchMs??0,uploadMs=t?.lastUploadMs??0,simulationMs=app?.lastSimulationMs??0,controllerMs=app?.lastControllerMs??0,nodes=t?.NodeCount??0,queue=t?.queued??0,workers=t?.running??0,visible=t?.visibleTiles??0,cpuMs=valid>0?timing[0].cpuFrameTime:0,cpuActiveMs=valid>0?timing[0].cpuMainThreadFrameTime:0,renderThreadMs=valid>0?timing[0].cpuRenderThreadFrameTime:0,presentWaitMs=valid>0?timing[0].cpuMainThreadPresentWaitTime:0,gpuMs=valid>0?timing[0].gpuFrameTime:0,cellMeters=cell,meshMiB=(t?.meshBytes??0)/1048576.0,residentMiB=(t?.residentBytes??0)/1048576.0,gcBytes=allocations.Valid?allocations.LastValue:-1,mainThreadAllocated=allocated>=0&&previousAllocated>=0?allocated-previousAllocated:-1,collections=GC.CollectionCount(0),x=p.x,y=p.y,z=p.z,simulationTime=app?.Flight?.time??0};previousAllocated=allocated;
                count++;previousEnd=now;
                if((app?.Flight?.time??0)>=64||now>150)break;
            }
            var seams=app?.Terrain.CheckVisibleSeams()??(0,0.0);
            var surface=app?.Terrain.CheckUploadedSurface()??(0,0f,0);
            if(surface.Item1==0||surface.Item2>.001f||surface.Item3!=0)errors.Add("GPU/CPU terrain stream mismatch: "+surface);
            if(seams.Item1>0&&seams.Item2>.1)errors.Add("LOD gap exceeds 0.1 m: "+seams.Item2);
            var rows=new Frame[count];Array.Copy(frames,rows,count);
            File.WriteAllText(Path.Combine(folder,"frames.json"),JsonConvert.SerializeObject(new{schema=1,utc=DateTime.UtcNow,startupMs=app?.startupMs,graphics=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),resolution=new[]{Screen.width,Screen.height},quality=new{msaa=QualitySettings.antiAliasing,vSync=QualitySettings.vSyncCount,terrainGrid=app?.Terrain.grid,pixelError=app?.Terrain.pixelError},world=app?.Kernel?.World.Identity,firstFrameSeconds=firstFrame,kernelReadySeconds=kernelReady,firstSurfaceSeconds=firstSurface,flightStartSeconds=flightStart,closeDetailSeconds=closeDetail,allocatedMiB=Profiler.GetTotalAllocatedMemoryLong()/1048576.0,monoUsedMiB=Profiler.GetMonoUsedSizeLong()/1048576.0,gcRecorderAvailable=allocations.Valid,threadAllocationCounterAvailable=previousAllocated>=0,seams,surface,phases=new[]{"startup","flight-entry","cruise","boost-combat","boost-release","turn","descent","recovery"},errors,frames=rows}));
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"end.png"));yield return endOfFrame;yield return null;
            allocations.Dispose();Application.logMessageReceived-=OnLog;
            Application.Quit(errors.Count==0?0:1);
        }
        void Update()
        {
            if(!app||kernelReady<0)return;app.Automated=true;
            double now=Time.realtimeSinceStartupAsDouble;
            if(flightStart<0){if(now-kernelReady<10)return;app.StartFlight();flightStart=now;app.BeforeSimulationStep=SetFlightInput;}
        }
        void SetFlightInput(FlightModel f)
        {
            double t=f.time;phase=t<12?1:t<24?2:t<32?3:t<40?4:t<48?5:t<56?6:7;
            // Assign before EACH 60 Hz physics step, not once per render frame:
            // slow frames must not change turn timing or the collision route.
            f.health=100;f.input=new FlightModel.FlightInput{boost=phase==3,gun=phase==3||phase==4,missile=phase==3,x=phase==5?.45:0,y=phase==6?.12:phase==7?-.12:0};
        }
        void OnLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    }
}
