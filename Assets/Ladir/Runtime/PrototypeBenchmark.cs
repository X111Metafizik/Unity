using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ladir {
 public sealed class PrototypeBenchmark : MonoBehaviour {
  public RegionStreamer terrain;public FlightController ship;
  [Serializable] public class PhaseResult {public string phase;public int frames,over16ms,over33ms,over50ms;public float medianMs,p95Ms,p99Ms,maxMs,cpuMedianMs,gpuMedianMs;}
  [Serializable] public class Report {
   public string unityVersion,os,cpu,gpu,graphicsApi,worldHash,notes;public int width,height,processorCount;
   public float firstSurfaceMs,fullInitialDetailMs,processToFirstSurfaceMs,processToFullDetailMs,readTaskLatencyTotalMs,meshJobLatencyTotalMs,uploadCpuTotalMs,uploadCpuMaxMs;
   public long regionCachePeakBytes,meshBufferPeakBytes,unityAllocatedBytes,managedHeapBytes,processWorkingSetBytes;
   public int readTiles,terrainPlans;public PhaseResult[] phases;
  }
  class Sample {public float frame,cpu,gpu;}
  readonly Dictionary<string,List<Sample>> samples=new Dictionary<string,List<Sample>>();readonly FrameTiming[] timing=new FrameTiming[1];
  string output,phase="startup";bool running,finished,noScreenshots;float started,routeStart=-1;int capture;
  void Start(){var args=Environment.GetCommandLineArgs();running=args.Contains("--ladir-benchmark");if(!running){enabled=false;return;}
   int at=Array.IndexOf(args,"--ladir-output");output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"benchmark");Directory.CreateDirectory(output);Screen.SetResolution(1440,1000,FullScreenMode.Windowed);started=Time.realtimeSinceStartup;
   ship.BenchmarkControlled=true;noScreenshots=args.Contains("--ladir-no-screenshots");
  }
  void Update(){if(!running||finished)return;float now=Time.realtimeSinceStartup;FrameTimingManager.CaptureFrameTimings();uint count=FrameTimingManager.GetLatestTimings(1,timing);
   if(!samples.TryGetValue(phase,out var list)){list=new List<Sample>();samples.Add(phase,list);}list.Add(new Sample {frame=Time.unscaledDeltaTime*1000,cpu=count>0?(float)timing[0].cpuFrameTime:0,gpu=count>0?(float)timing[0].gpuFrameTime:0});
   if(terrain.Error.Length>0){File.WriteAllText(Path.Combine(output,"error.txt"),terrain.Error);Application.Quit(2);return;}
   if(!terrain.Detailed){if(now-started>120){File.WriteAllText(Path.Combine(output,"error.txt"),"Detailed terrain startup timed out");Application.Quit(3);}return;}
   if(routeStart<0)routeStart=now;float t=now-routeStart;
   if(t<8){phase="lake-steady";ship.SetBenchmarkPose(new Vector3(800,1900,-6700+t*60),-12,194.444f);}
   else if(t<18){phase="river-flight";float q=t-8;float x=-1200+q*120,z=-4800-q*320;ship.SetBenchmarkPose(new Vector3(x,terrain.Surface(x,z)+350,z),160,500);}
   else if(t<28){phase="canyon-turn";float q=t-18;float x=8200+Mathf.Sin(q*.18f)*600,z=-7000+q*250;ship.SetBenchmarkPose(new Vector3(x,terrain.Surface(x,z)+500,z),Mathf.Cos(q*.18f)*25,250);}
   else if(t<38){phase="mountain-flight";float q=t-28;float x=-11200+q*180,z=8500+q*160;ship.SetBenchmarkPose(new Vector3(x,terrain.Surface(x,z)+600,z),48,250);}
   else if(t<48){phase="boost-release";float q=t-38;float speed=q<3?500:Mathf.Lerp(500,194.444f,Mathf.Clamp01((q-3)/3));float z=-12000+q*300;ship.SetBenchmarkPose(new Vector3(3000,terrain.Surface(3000,z)+250,z),0,speed);}
   else{finished=true;Finish();}
   int wanted=t<7?0:t<17?1:t<27?2:t<37?3:t<47?4:5;
   if(wanted>capture){capture=wanted;if(!noScreenshots)ScreenCapture.CaptureScreenshot(Path.Combine(output,$"{capture:00}-{phase}.png"));}
  }
  static float Percentile(float[] values,float q){if(values.Length==0)return 0;Array.Sort(values);return values[Mathf.Min(values.Length-1,Mathf.CeilToInt(values.Length*q)-1)];}
  void Finish(){var results=new List<PhaseResult>();foreach(var pair in samples){var s=pair.Value;var frames=s.Select(x=>x.frame).ToArray();results.Add(new PhaseResult {phase=pair.Key,frames=s.Count,over16ms=s.Count(x=>x.frame>16.667f),over33ms=s.Count(x=>x.frame>33.333f),over50ms=s.Count(x=>x.frame>50),medianMs=Percentile(frames,.5f),p95Ms=Percentile(frames,.95f),p99Ms=Percentile(frames,.99f),maxMs=frames.Max(),cpuMedianMs=Percentile(s.Where(x=>x.cpu>0).Select(x=>x.cpu).ToArray(),.5f),gpuMedianMs=Percentile(s.Where(x=>x.gpu>0).Select(x=>x.gpu).ToArray(),.5f)});}
   var report=new Report {unityVersion=Application.unityVersion,os=SystemInfo.operatingSystem,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),width=Screen.width,height=Screen.height,processorCount=SystemInfo.processorCount,worldHash=terrain.manifest.worldHash,firstSurfaceMs=terrain.firstSurfaceMs,fullInitialDetailMs=terrain.firstDetailMs,readTaskLatencyTotalMs=terrain.readTotalMs,meshJobLatencyTotalMs=terrain.jobTotalMs,uploadCpuTotalMs=terrain.uploadTotalMs,uploadCpuMaxMs=terrain.maxUploadMs,regionCachePeakBytes=terrain.PeakCacheBytes,meshBufferPeakBytes=terrain.PeakMeshBytes,unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),managedHeapBytes=GC.GetTotalMemory(false),processWorkingSetBytes=Process.GetCurrentProcess().WorkingSet64,readTiles=terrain.readCount,terrainPlans=terrain.committedPlans,phases=results.ToArray(),notes="Native Windows URP finite-region prototype. Automated camera route; scripted route is not a collision-control test. Tile resolution 7.8125 m, 16/32/64/128/256 cells. No trees, volumetric clouds, enemies or global warp. Not an equal-feature browser/native FPS comparison. CPU/GPU timing 0 means unavailable. Upload timing is CPU submission, not GPU transfer time; task latencies overlap and must not be added to frame time."};
   report.processToFirstSurfaceMs=terrain.processAgeAtSceneStartMs+terrain.firstSurfaceMs;report.processToFullDetailMs=terrain.processAgeAtSceneStartMs+terrain.firstDetailMs;
   File.WriteAllText(Path.Combine(output,"benchmark.json"),JsonUtility.ToJson(report,true));UnityEngine.Debug.Log("LADIR benchmark finished: "+output);Application.Quit(0);
  }
 }
}
