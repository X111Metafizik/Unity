using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Profiling;
namespace Ladir.Planet
{
    // Development-build diagnostic only. Not a benchmark: profiling has overhead.
    public sealed class PlanetStartupProfile:MonoBehaviour
    {
        string folder;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--startup-profile");if(at<0||at+1>=args.Length)return;
            string path=args[at+1];Directory.CreateDirectory(path);Profiler.logFile=Path.Combine(path,"startup.raw");Profiler.enableBinaryLog=true;Profiler.enabled=true;
            var go=new GameObject("Startup profiler");DontDestroyOnLoad(go);go.AddComponent<PlanetStartupProfile>().folder=path;
        }
        IEnumerator Start()
        {
            while(Time.realtimeSinceStartupAsDouble<10)yield return null;
            Profiler.enabled=false;Profiler.enableBinaryLog=false;var app=FindFirstObjectByType<PlanetStudio>();
            File.WriteAllText(Path.Combine(folder,"startup.json"),JsonConvert.SerializeObject(app?.startupMs,Formatting.Indented));Application.Quit();
        }
    }
}
