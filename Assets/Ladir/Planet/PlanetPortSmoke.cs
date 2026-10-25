using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace Ladir.Planet
{
    // Only enabled by an explicit command line flag; normal play is unaffected.
    public sealed class PlanetPortSmoke : MonoBehaviour
    {
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--planet-smoke");string folder=null;
#if UNITY_EDITOR
            const string request="UserSettings/LadirPlanetSmoke.request";if(File.Exists(request)){folder=File.ReadAllText(request).Trim();File.Delete(request);}
#endif
            if(folder==null){if(at<0)yield break;folder=at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"PlanetSmoke");}Directory.CreateDirectory(folder);
            var errors=new List<string>();Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(message);};var app=GetComponent<PlanetStudio>();app.Automated=true;app.Sound=false;double start=Time.realtimeSinceStartupAsDouble;bool extended=Array.IndexOf(args,"--extended")>=0;
            while(app.Kernel==null&&Time.realtimeSinceStartupAsDouble-start<60)yield return null;
            if(app.Kernel==null){File.WriteAllText(Path.Combine(folder,"smoke.json"),"{\"loaded\":false}");Finish(2);yield break;}
            yield return new WaitForSecondsRealtime(16);ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-editor.png"));yield return new WaitForSecondsRealtime(.5f);
            var gpuCheck=app.GetComponent<PlanetOcean>().VerifyGpu();while(!gpuCheck.IsCompleted)yield return null;double fftError=gpuCheck.IsCompletedSuccessfully?gpuCheck.Result:-1;if(gpuCheck.IsFaulted||fftError>1e-4)errors.Add("Ocean GPU parity failed: "+fftError);
            app.StartFlight();app.Flight.input=new FlightModel.FlightInput();var frames=new List<double>();var samples=new List<object>();var timing=new FrameTiming[1];double flightStart=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-flightStart<24){FrameTimingManager.CaptureFrameTimings();FrameTimingManager.GetLatestTimings(1,timing);frames.Add(Time.unscaledDeltaTime*1000);var t=app.Terrain;samples.Add(new{frameMs=Time.unscaledDeltaTime*1000,planMs=t.lastPlanMs,submitMs=t.lastSubmitMs,stitchMs=t.lastStitchMs,uploadMs=t.lastUploadMs,buildMs=t.lastBuildMs,simulationMs=app.lastSimulationMs,controllerMs=app.lastControllerMs,nodes=t.NodeCount,t.queued,t.running,t.visibleTiles,cellMeters=t.RenderedCellSize(app.Flight.position.Unit),cpuMs=timing[0].cpuFrameTime,gpuMs=timing[0].gpuFrameTime});yield return null;}
            var seams=app.Terrain.CheckVisibleSeams();if(seams.vertices==0||seams.maxGapMeters>.1)errors.Add("Mixed LOD edge gap: "+seams.maxGapMeters+" m / "+seams.vertices+" vertices");
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-flight.png"));yield return new WaitForSecondsRealtime(.5f);
            var scenarios=new List<object>();
            IEnumerator Measure(string name,double seconds,Action<double> input)
            {
                double beginning=Time.realtimeSinceStartupAsDouble;var intervals=new List<double>();var gpu=new List<double>();double maxQueue=0,maxUpload=0;
                while(Time.realtimeSinceStartupAsDouble-beginning<seconds){input?.Invoke(Time.realtimeSinceStartupAsDouble-beginning);FrameTimingManager.CaptureFrameTimings();FrameTimingManager.GetLatestTimings(1,timing);intervals.Add(Time.unscaledDeltaTime*1000);if(timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);maxQueue=Math.Max(maxQueue,app.Terrain.queued);maxUpload=Math.Max(maxUpload,app.Terrain.lastUploadMs);yield return null;}
                intervals.Sort();gpu.Sort();scenarios.Add(new{name,frames=intervals.Count,p95=intervals[(int)(intervals.Count*.95)],p99=intervals[(int)(intervals.Count*.99)],max=intervals[intervals.Count-1],longFrames50ms=intervals.FindAll(v=>v>50).Count,gpuP95=gpu.Count==0?0:gpu[(int)(gpu.Count*.95)],maxQueue,maxUpload,meshMiB=app.Terrain.meshBytes/1048576.0});ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));yield return new WaitForSecondsRealtime(.5f);
            }
            if(extended){
                app.clouds=.8f;app.Flight.health=100;yield return Measure("04-cloud-boost-combat",8,t=>app.Flight.input=new FlightModel.FlightInput{boost=true,gun=true,missile=true});
                yield return Measure("05-boost-release",8,t=>app.Flight.input=new FlightModel.FlightInput{gun=true});
                app.StartFlight();var f=app.Flight;foreach(var e in f.enemies){e.health=0;e.respawn=double.PositiveInfinity;}f.position=f.up*(app.Kernel.World.radiusMeters+16000);f.previousPosition=f.position;f.forward=f.up;f.pitch=1.55;f.Activate(1);f.SelectWarp(0);
                yield return Measure("06-warp-charge",2,null);yield return Measure("07-warp-tunnel",2,null);yield return Measure("08-warp-arrival",4,null);
                if(f.state!="flying"||f.warpCooldown<=0||f.warpIndex!=0)errors.Add("1000 km warp did not finish with cooldown.");
                app.ExitFlight();app.clouds=.55f;app.SetView(0);
            }
            app.ExitFlight();app.SetView(0);yield return new WaitForSecondsRealtime(8);ScreenCapture.CaptureScreenshot(Path.Combine(folder,"03-orbit.png"));yield return new WaitForSecondsRealtime(.5f);
            if(extended){var reference=app.GetComponent<PlanetReferenceInspector>();reference.Open();double wait=Time.realtimeSinceStartupAsDouble;while(reference.Busy&&Time.realtimeSinceStartupAsDouble-wait<60)yield return null;if(!reference.Ready)errors.Add("Reference basin inspector did not produce a map.");yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(folder,"09-reference-analysis.png"));yield return new WaitForSecondsRealtime(.5f);}
            frames.Sort();var report=new{loaded=true,diameterKm=app.Kernel.World.radiusMeters*2/1000,worldFileSha256=app.Kernel.World.Identity,editor=Application.isEditor,graphics=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),resolution=new[]{Screen.width,Screen.height},frames=frames.Count,p95=frames[(int)(frames.Count*.95)],p99=frames[(int)(frames.Count*.99)],longFrames50ms=frames.FindAll(x=>x>50).Count,visibleTiles=app.Terrain.visibleTiles,meshMiB=app.Terrain.meshBytes/1048576.0,residentMiB=app.Terrain.residentBytes/1048576.0,totalAllocatedMiB=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576.0,fftMaxErrorMeters=fftError,seamVertices=seams.vertices,maxSeamGapMeters=seams.maxGapMeters,scenarios,samples,errors};File.WriteAllText(Path.Combine(folder,"smoke.json"),JsonConvert.SerializeObject(report,Formatting.Indented));Finish(errors.Count==0?0:1);
        }
        static void Finish(int code){
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(code);
#endif
        }
    }
}
