using UnityEngine;
using System;
using System.Threading.Tasks;
using UnityEngine.Rendering;

namespace Ladir.Planet
{
    public sealed class PlanetOcean:MonoBehaviour
    {
        PlanetStudio app;ComputeShader shader;ComputeBuffer spectrum,frequency,a,b;RenderTexture waves;Texture2D fallback;
        OceanSpectrum source;float[] heights;string identity;double clock;int initial,stage,store;
        public void Initialize(PlanetStudio studio,ComputeShader compute){app=studio;shader=compute;if(shader&&SystemInfo.supportsComputeShaders){initial=shader.FindKernel("Initialize");stage=shader.FindKernel("Butterfly");store=shader.FindKernel("Store");}}
        void Update()
        {
            if(app.Kernel==null)return;if(identity!=app.Kernel.World.Identity)Prepare();if(!app.pausedSky)clock+=Time.unscaledDeltaTime;
            if(waves)
            {
                shader.SetFloat("_Clock",(float)clock);shader.SetBuffer(initial,"_Spectrum",spectrum);shader.SetBuffer(initial,"_Frequency",frequency);shader.SetBuffer(initial,"_Output",a);shader.Dispatch(initial,8,8,1);var src=a;var dst=b;
                for(int axis=0;axis<2;axis++)for(int length=2;length<=64;length*=2){shader.SetInt("_Axis",axis);shader.SetInt("_Length",length);shader.SetBuffer(stage,"_Input",src);shader.SetBuffer(stage,"_Output",dst);shader.Dispatch(stage,8,8,1);var swap=src;src=dst;dst=swap;}
                shader.SetBuffer(store,"_Input",src);shader.SetTexture(store,"_Height",waves);shader.Dispatch(store,8,8,1);
            }
            else{source.Evaluate(clock,heights);fallback.SetPixelData(heights,0);fallback.Apply(false,false);}
            Shader.SetGlobalFloat("_LadirOceanTime",(float)clock);
        }
        void Prepare()
        {
            Release();identity=app.Kernel.World.Identity;source=new OceanSpectrum(app.Kernel.World.terrainSeed);clock=0;
            if(shader&&SystemInfo.supportsComputeShaders)
            {
                spectrum=new ComputeBuffer(4096,16);frequency=new ComputeBuffer(4096,4);a=new ComputeBuffer(4096,8);b=new ComputeBuffer(4096,8);
                var values=new Vector4[4096];for(int i=0;i<values.Length;i++)values[i]=new Vector4(source.packed[i*4],source.packed[i*4+1],source.packed[i*4+2],source.packed[i*4+3]);spectrum.SetData(values);frequency.SetData(source.frequency);
                waves=new RenderTexture(64,64,0,RenderTextureFormat.RFloat){name="Original 64 x 64 FFT ocean",enableRandomWrite=true,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};waves.Create();Shader.SetGlobalTexture("_LadirOcean",waves);
            }
            else{heights=new float[4096];fallback=new Texture2D(64,64,TextureFormat.RFloat,false,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};Shader.SetGlobalTexture("_LadirOcean",fallback);}
        }
        void Release(){spectrum?.Dispose();frequency?.Dispose();a?.Dispose();b?.Dispose();spectrum=frequency=a=b=null;if(waves){waves.Release();Destroy(waves);waves=null;}if(fallback){Destroy(fallback);fallback=null;}}
        // Explicit QA only: one asynchronous readback, never in normal play.
        public Task<double> VerifyGpu()
        {
            if(!waves)return Task.FromResult(-1.0);var expected=new float[4096];source.Evaluate((float)clock,expected);var done=new TaskCompletionSource<double>();
            AsyncGPUReadback.Request(waves,0,TextureFormat.RFloat,result=>{if(result.hasError){done.SetException(new InvalidOperationException("FFT GPU readback failed"));return;}var actual=result.GetData<float>();double error=0;for(int i=0;i<4096;i++)error=Math.Max(error,Math.Abs(actual[i]-expected[i]));done.SetResult(error);});return done.Task;
        }
        void OnDestroy()=>Release();
    }
}
