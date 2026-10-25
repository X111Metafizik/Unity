using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Ladir.Planet
{
    // The original generator is retained for seed creation, package signatures,
    // region bindings and navigation. Rendering/flight never execute JavaScript.
    // Keeping authoring exact avoids silently changing worlds during the port.
    public static class PlanetAuthoring
    {
        public static async Task<string> Generate(PlanetConfig config,string streaming,string output,Action<string> progress)
        {
            Directory.CreateDirectory(output);string stem=Guid.NewGuid().ToString("N"),settings=Path.Combine(output,stem+".json"),planet=Path.Combine(output,stem+".planet");File.WriteAllText(settings,JsonConvert.SerializeObject(config));
            string script=Path.Combine(streaming,"Ladir/authoring/generate.mjs");
            var info=new ProcessStartInfo{FileName="node",Arguments=Quote(script)+" "+Quote(settings)+" "+Quote(planet),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(script)};
            using(var process=new Process{StartInfo=info})
            {
                try{process.Start();}catch(Exception e){throw new InvalidOperationException("Yeni seed üretimi için Node.js 24 gerekli. Hazır .planet dünyaları Node olmadan çalışır. "+e.Message);}
                var error=process.StandardError.ReadToEndAsync();var read=Task.Run(()=>{string line;while((line=process.StandardOutput.ReadLine())!=null)progress?.Invoke(line);});await Task.Run(()=>process.WaitForExit());await read;string errors=await error;if(process.ExitCode!=0)throw new InvalidOperationException(errors);
            }
            File.Delete(settings);return planet;
        }
        static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
    }
}
