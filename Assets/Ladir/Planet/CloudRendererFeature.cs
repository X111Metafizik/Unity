using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

namespace Ladir.Planet
{
    public sealed class CloudRendererFeature : ScriptableRendererFeature
    {
        public Material material;CloudPass pass;
        public override void Create(){pass?.Release();pass=new CloudPass{renderPassEvent=RenderPassEvent.BeforeRenderingTransparents};}
        protected override void Dispose(bool disposing){pass?.Release();}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {if(material==null||data.cameraData.cameraType!=CameraType.Game)return;pass.material=material;renderer.EnqueuePass(pass);}
        sealed class CloudPass : ScriptableRenderPass
        {
            public Material material;
            readonly RTHandle[] colors=new RTHandle[2],metadata=new RTHandle[2];
            Matrix4x4 previousVP;D3 previousAbsolute;Vector3 previousOrigin,previousSun;float previousFov,previousWorld;int frames,lastCamera;
            public CloudPass(){requiresIntermediateTexture=true;ConfigureInput(ScriptableRenderPassInput.Depth);}
            public void Release(){for(int i=0;i<2;i++){colors[i]?.Release();metadata[i]?.Release();colors[i]=metadata[i]=null;}frames=0;}
            class PassData {public TextureHandle source,cloud,depth,history,historyMeta;public Material material;public Matrix4x4 previousVP;public Vector4 control;public Vector3 delta;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var data=frame.Get<UniversalResourceData>();if(data.isActiveTargetBackBuffer)return;
                var cameraData=frame.Get<UniversalCameraData>();var camera=cameraData.camera;
                var desc=cameraData.cameraTargetDescriptor;desc.width=Mathf.Max(1,desc.width/2);desc.height=Mathf.Max(1,desc.height/2);desc.msaaSamples=1;desc.depthBufferBits=0;desc.depthStencilFormat=GraphicsFormat.None;desc.bindMS=false;desc.useMipMap=false;
                bool resized=false;for(int i=0;i<2;i++){desc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;resized|=RenderingUtils.ReAllocateHandleIfNeeded(ref colors[i],desc,FilterMode.Bilinear,TextureWrapMode.Clamp,name:"LADIR volume history "+i);desc.graphicsFormat=GraphicsFormat.R32G32_SFloat;resized|=RenderingUtils.ReAllocateHandleIfNeeded(ref metadata[i],desc,FilterMode.Point,TextureWrapMode.Clamp,name:"LADIR depth and age "+i);}
                Vector3 origin=Shader.GetGlobalVector("_LadirOrigin"),sun=Shader.GetGlobalVector("_LadirSun");var local=camera.transform.position;D3 absolute=new D3((double)origin.x+local.x,(double)origin.y+local.y,(double)origin.z+local.z),delta=(absolute-previousAbsolute)*.001;float world=Shader.GetGlobalFloat("_LadirWorldVersion");
                bool valid=frames>0&&!resized&&lastCamera==camera.GetInstanceID()&&delta.Length<=1.5&&Mathf.Abs(previousFov-camera.fieldOfView)<=1&&origin==previousOrigin&&world==previousWorld&&Vector3.Dot(sun,previousSun)>.9999f&&Shader.GetGlobalVector("_LadirLightning").w<.001f;
                int write=frames%2,read=1-write;var source=data.activeColorTexture;var cloud=graph.ImportTexture(colors[write]);var meta=graph.ImportTexture(metadata[write]);
                using(var b=graph.AddRasterRenderPass<PassData>("LADIR atmosphere + five-phase reprojection",out var p))
                {
                    p.source=source;p.depth=data.cameraDepthTexture;p.material=material;p.history=graph.ImportTexture(colors[read]);p.historyMeta=graph.ImportTexture(metadata[read]);p.previousVP=previousVP;p.delta=new Vector3((float)delta.x,(float)delta.y,(float)delta.z);p.control=new Vector4(valid?1:0,frames%5,0,0);
                    b.UseTexture(source);b.UseTexture(p.depth);b.UseTexture(p.history);b.UseTexture(p.historyMeta);b.SetRenderAttachment(cloud,0);b.SetRenderAttachment(meta,1);b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData x,RasterGraphContext ctx)=>{ctx.cmd.SetGlobalTexture("_LadirHistory",x.history);ctx.cmd.SetGlobalTexture("_LadirHistoryMeta",x.historyMeta);ctx.cmd.SetGlobalVector("_LadirHistoryControl",x.control);ctx.cmd.SetGlobalMatrix("_LadirPreviousVP",x.previousVP);ctx.cmd.SetGlobalVector("_LadirCameraDelta",x.delta);Blitter.BlitTexture(ctx.cmd,x.source,new Vector4(1,1,0,0),x.material,0);});
                }
                var outputDesc=graph.GetTextureDesc(source);outputDesc.name="LADIR depth-aware atmosphere composite";outputDesc.clearBuffer=false;var output=graph.CreateTexture(outputDesc);
                using(var b=graph.AddRasterRenderPass<PassData>("LADIR full-resolution geometry composite",out var p))
                {p.source=source;p.cloud=cloud;p.depth=data.cameraDepthTexture;p.material=material;b.UseTexture(source);b.UseTexture(cloud);b.UseTexture(p.depth);b.SetRenderAttachment(output,0);b.AllowGlobalStateModification(true);b.SetRenderFunc((PassData x,RasterGraphContext ctx)=>{ctx.cmd.SetGlobalTexture("_LadirCloudTexture",x.cloud);Blitter.BlitTexture(ctx.cmd,x.source,new Vector4(1,1,0,0),x.material,1);});}
                data.cameraColor=output;var rotation=camera.worldToCameraMatrix;rotation.SetColumn(3,new Vector4(0,0,0,1));previousVP=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*rotation;previousAbsolute=absolute;previousOrigin=origin;previousFov=camera.fieldOfView;previousWorld=world;previousSun=sun;lastCamera=camera.GetInstanceID();frames++;
            }
        }
    }
}
