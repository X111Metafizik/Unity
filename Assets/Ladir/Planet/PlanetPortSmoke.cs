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
            var errors=new List<string>();Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(message);};var app=GetComponent<PlanetStudio>();app.Automated=true;double start=Time.realtimeSinceStartupAsDouble;
            while(app.Kernel==null&&Time.realtimeSinceStartupAsDouble-start<60)yield return null;
            if(app.Kernel==null){File.WriteAllText(Path.Combine(folder,"smoke.json"),"{\"loaded\":false}");Finish(2);yield break;}
            yield return new WaitForSecondsRealtime(16);ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-editor.png"));yield return new WaitForSecondsRealtime(.5f);
            app.StartFlight();app.Flight.input=new FlightModel.FlightInput();var frames=new List<double>();double flightStart=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-flightStart<12){frames.Add(Time.unscaledDeltaTime*1000);yield return null;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-flight.png"));yield return new WaitForSecondsRealtime(.5f);
            app.ExitFlight();app.SetView(0);yield return new WaitForSecondsRealtime(8);ScreenCapture.CaptureScreenshot(Path.Combine(folder,"03-orbit.png"));yield return new WaitForSecondsRealtime(.5f);
            frames.Sort();var report=new{loaded=true,diameterKm=app.Kernel.World.radiusMeters*2/1000,worldFileSha256=app.Kernel.World.Identity,frames=frames.Count,p95=frames[(int)(frames.Count*.95)],p99=frames[(int)(frames.Count*.99)],longFrames50ms=frames.FindAll(x=>x>50).Count,visibleTiles=app.Terrain.visibleTiles,meshMiB=app.Terrain.meshBytes/1048576.0,residentMiB=app.Terrain.residentBytes/1048576.0,errors};File.WriteAllText(Path.Combine(folder,"smoke.json"),JsonConvert.SerializeObject(report,Formatting.Indented));Finish(errors.Count==0?0:1);
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
