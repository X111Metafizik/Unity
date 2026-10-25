using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ladir.Planet
{
    // Run the unchanged web golden corpus in the ACTUAL player backend, not
    // only in the portable .NET harness. Separate from performance captures.
    public sealed class PlanetNativeParity : MonoBehaviour
    {
        string fixtures,output;
        sealed class Result
        {public int cases,failed;public double maxHeightErrorMeters,maxWaterErrorMeters,maxColorError;public string world,backend;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--native-parity");if(at<0||at+2>=args.Length)return;
            var go=new GameObject("Native terrain golden-corpus check");DontDestroyOnLoad(go);var check=go.AddComponent<PlanetNativeParity>();check.fixtures=args[at+1];check.output=args[at+2];
        }
        IEnumerator Start()
        {
            string world=Path.Combine(Application.streamingAssetsPath,"Ladir/planet/river-v42.planet");var task=Task.Run(()=>Run(world,fixtures));
            while(!task.IsCompleted)yield return null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            if(task.IsFaulted){File.WriteAllText(output,JsonConvert.SerializeObject(new{passed=false,error=task.Exception.ToString()}));Application.Quit(1);yield break;}
            File.WriteAllText(output,JsonConvert.SerializeObject(task.Result,Formatting.Indented));Application.Quit(task.Result.failed==0?0:1);
        }
        static Result Run(string path,string fixtures)
        {
            var expected=JObject.Parse(File.ReadAllText(fixtures));var world=PlanetWorld.Load(path);var result=new Result{world=world.Identity};
#if ENABLE_IL2CPP
            result.backend="IL2CPP";
#else
            result.backend="Mono";
#endif
            if(world.Identity!=(string)expected["worldFileSha256"])throw new InvalidDataException("Golden corpus/world identity mismatch.");
            TerrainKernel kernel=null;string type=null;
            try
            {
                foreach(var c in expected["cases"])
                {
                    if(type!=(string)c["type"]){kernel?.Dispose();type=(string)c["type"];world.generationConfig.worldType=type;kernel=new TerrainKernel(world);}
                    var s=kernel.Sample(new D3(c["d"].ToObject<double[]>()));double height=Math.Abs(s.height-(double)c["height"]),water=Math.Abs(s.water-(double)c["water"]),color=(TerrainKernel.Color(s)-new D3(c["color"].ToObject<double[]>())).Length;
                    result.cases++;result.maxHeightErrorMeters=Math.Max(result.maxHeightErrorMeters,height);result.maxWaterErrorMeters=Math.Max(result.maxWaterErrorMeters,water);result.maxColorError=Math.Max(result.maxColorError,color);
                    if(height>.001||water>.001||color>.00001||s.biome!=(int)c["biome"])result.failed++;
                }
            }
            finally{kernel?.Dispose();}
            return result;
        }
    }
}
