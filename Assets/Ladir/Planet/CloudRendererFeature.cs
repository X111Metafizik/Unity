using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Ladir.Planet
{
    public sealed class CloudRendererFeature : ScriptableRendererFeature
    {
        public Material material;CloudPass pass;
        public override void Create(){pass=new CloudPass{renderPassEvent=RenderPassEvent.BeforeRenderingTransparents};}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {if(material==null||data.cameraData.cameraType!=CameraType.Game)return;pass.material=material;renderer.EnqueuePass(pass);}
        sealed class CloudPass : ScriptableRenderPass
        {
            public Material material;
            public CloudPass(){requiresIntermediateTexture=true;ConfigureInput(ScriptableRenderPassInput.Depth);}
            class PassData {public TextureHandle source,cloud,depth;public Material material;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var data=frame.Get<UniversalResourceData>();if(data.isActiveTargetBackBuffer)return;var source=data.activeColorTexture;var desc=graph.GetTextureDesc(source);desc.name="LADIR half-resolution cloud scattering";desc.width=Mathf.Max(1,desc.width/2);desc.height=Mathf.Max(1,desc.height/2);desc.msaaSamples=MSAASamples.None;desc.clearBuffer=false;desc.colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;var cloud=graph.CreateTexture(desc);
                using(var b=graph.AddRasterRenderPass<PassData>("LADIR cloud raymarch · half resolution",out var p))
                {p.source=source;p.cloud=cloud;p.depth=data.cameraDepthTexture;p.material=material;b.UseTexture(p.source);b.UseTexture(p.depth);b.SetRenderAttachment(cloud,0);b.SetRenderFunc((PassData x,RasterGraphContext ctx)=>{Blitter.BlitTexture(ctx.cmd,x.source,new Vector4(1,1,0,0),x.material,0);});}
                var outputDesc=graph.GetTextureDesc(source);outputDesc.name="LADIR depth-aware atmosphere composite";outputDesc.clearBuffer=false;var output=graph.CreateTexture(outputDesc);
                using(var b=graph.AddRasterRenderPass<PassData>("LADIR cloud depth composite",out var p))
                {p.source=source;p.cloud=cloud;p.depth=data.cameraDepthTexture;p.material=material;b.UseTexture(source);b.UseTexture(cloud);b.UseTexture(p.depth);b.SetRenderAttachment(output,0);b.AllowGlobalStateModification(true);b.SetRenderFunc((PassData x,RasterGraphContext ctx)=>{ctx.cmd.SetGlobalTexture("_LadirCloudTexture",x.cloud);Blitter.BlitTexture(ctx.cmd,x.source,new Vector4(1,1,0,0),x.material,1);});}
                data.cameraColor=output;
            }
        }
    }
}
