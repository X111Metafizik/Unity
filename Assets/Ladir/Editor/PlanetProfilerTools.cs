using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Build.Reporting;
namespace Ladir.Editor
{
    public static class PlanetProfilerTools
    {
        [MenuItem("LADIR/Diagnostics/Build IL2CPP player")]
        public static void BuildAot()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Stop Play and finish import before building.");
            var target=UnityEditor.Build.NamedBuildTarget.Standalone;
            var backend=PlayerSettings.GetScriptingBackend(target);var stripping=PlayerSettings.GetManagedStrippingLevel(target);
            try
            {
                PlanetSetup.Create();PlayerSettings.SetScriptingBackend(target,ScriptingImplementation.IL2CPP);PlayerSettings.SetManagedStrippingLevel(target,ManagedStrippingLevel.Low);
                Directory.CreateDirectory("Builds/PlanetAOT");
                var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlanetSetup.ScenePath},locationPathName="Builds/PlanetAOT/Ladir.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
                Directory.CreateDirectory("evidence/performance");File.WriteAllText("evidence/performance/aot-build.json",JsonConvert.SerializeObject(new{result=r.summary.result.ToString(),errors=r.summary.totalErrors,warnings=r.summary.totalWarnings,bytes=r.summary.totalSize,seconds=r.summary.totalTime.TotalSeconds,backend="IL2CPP",unity=Application.unityVersion}));
                if(r.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("IL2CPP build failed. See the build report.");
            }
            finally{PlayerSettings.SetScriptingBackend(target,backend);PlayerSettings.SetManagedStrippingLevel(target,stripping);AssetDatabase.SaveAssets();}
        }
        [MenuItem("LADIR/Diagnostics/Build development player")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Stop Play and finish import before profiling build.");
            PlanetSetup.Create();Directory.CreateDirectory("Builds/Profile");
            var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlanetSetup.ScenePath},locationPathName="Builds/Profile/Ladir.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Directory.CreateDirectory("evidence/performance");File.WriteAllText("evidence/performance/profile-build.json",JsonConvert.SerializeObject(new{result=r.summary.result.ToString(),errors=r.summary.totalErrors,warnings=r.summary.totalWarnings}));
        }
        [MenuItem("LADIR/Diagnostics/Summarize saved startup profile")]
        public static void Summarize()
        {
            const string request="UserSettings/LadirProfileSummary.request";if(!File.Exists(request))throw new FileNotFoundException(request);
            string path=File.ReadAllText(request).Trim();
            // Unity's default 300-frame history drops the very startup hitch
            // this tool is inspecting. Grow only this diagnostic history.
            var setHistory=typeof(ProfilerDriver).GetMethod("SetMaxFrameHistoryLength",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            setHistory?.Invoke(null,new object[]{2000});
            if(!ProfilerDriver.LoadProfile(path,false))throw new InvalidDataException("Profile could not be loaded.");
            var output=new List<object>();
            for(int frame=ProfilerDriver.firstFrameIndex;frame<=ProfilerDriver.lastFrameIndex;frame++)
            {
                using(var data=ProfilerDriver.GetRawFrameDataView(frame,0))
                {
                    if(!data.valid||data.frameTimeMs<45)continue;
                    var samples=new List<(string name,double ms)>();for(int i=0;i<data.sampleCount;i++){double time=data.GetSampleTimeMs(i);if(time>.2)samples.Add((data.GetSampleName(i),time));}
                    output.Add(new{frame,ms=data.frameTimeMs,thread=data.threadName,samples=samples.OrderByDescending(s=>s.ms).Take(45).Select(s=>new{s.name,s.ms}).ToArray()});
                }
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"profile-summary.json"),JsonConvert.SerializeObject(output,Formatting.Indented));
            Debug.Log("LADIR startup profile summary saved.");
        }
    }
}
