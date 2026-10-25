using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ladir.Planet
{
    public sealed class PlanetReferenceInspector:MonoBehaviour
    {
        PlanetStudio app;VisualElement panel;Image map;Label summary;Texture2D image;CancellationTokenSource cancel;string identity;bool busy;
        public bool Busy=>busy;
        public bool Ready=>image&&app.Kernel!=null&&identity==app.Kernel.World.Identity;
        public void Initialize(PlanetStudio studio,VisualElement parent)
        {
            app=studio;panel=new VisualElement();panel.AddToClassList("basin-map");parent.Add(panel);panel.style.display=DisplayStyle.None;
            summary=new Label("Son yüzeyin drenaj analizi");summary.style.whiteSpace=WhiteSpace.Normal;panel.Add(summary);map=new Image();map.style.width=280;map.style.height=280;panel.Add(map);
            var close=new Button(()=>panel.style.display=DisplayStyle.None){text="Kapat"};panel.Add(close);
            panel.RegisterCallback<PointerEnterEvent>(_=>app.PointerOverUI=true);panel.RegisterCallback<PointerLeaveEvent>(_=>app.PointerOverUI=false);
            map.RegisterCallback<PointerDownEvent>(e=>{if(image==null||app.Kernel==null)return;double x=(e.localPosition.x/280-.5)*32000,y=(.5-e.localPosition.y/280)*32000;app.Visit(app.Kernel.World.Direction((-19200+x)/app.Kernel.World.radiusMeters,(48000+y)/app.Kernel.World.radiusMeters),4500);});
        }
        public async void Open()
        {
            if(app.Kernel==null)return;app.ExitFlight();app.inspectLayer=4;panel.style.display=DisplayStyle.Flex;
            if(identity==app.Kernel.World.Identity&&image)return;if(busy)return;busy=true;cancel?.Cancel();cancel?.Dispose();cancel=new CancellationTokenSource();var token=cancel.Token;var kernel=app.Kernel;var requested=kernel.World.Identity;var lease=kernel.Retain();summary.text="32 × 32 km son yüzey ve drenaj hesaplanıyor…";
            try{
                var result=await Task.Run(()=>{using(lease)return ReferenceAnalysis.Run(kernel,256,token);});if(!this||app.Kernel.World.Identity!=requested)return;
                int side=result.resolution+1;var pixels=new Color32[side*side];for(int y=0;y<side;y++)for(int x=0;x<side;x++){int j=y*side+x,id=result.basin[j];double h=result.heights[j],flow=Math.Log(1+result.accumulation[j])/Math.Log(1+result.maxAccumulation);pixels[j]=new Color32((byte)Math.Min(255,40+(id*17%45)+Math.Min(70,h/60)),(byte)(60+(id*31%45)+flow*65),(byte)(70+(id*11%45)+flow*85),255);}
                for(int y=4;y<side-4;y+=12)for(int x=4;x<side-4;x+=12){int id=y*side+x,r=result.receiver[id];if(r<0)continue;int dx=r%side-x,dy=r/side-y;for(int k=0;k<=4;k++)pixels[(y+dy*k)*side+x+dx*k]=new Color32(216,255,255,255);}
                if(image)Destroy(image);image=new Texture2D(side,side,TextureFormat.RGBA32,false){name="Measured reference basin and flow",filterMode=FilterMode.Bilinear};image.SetPixels32(pixels);image.Apply(false,true);map.image=image;identity=requested;
                summary.text=$"32 × 32 km · {result.spacing:F0} m / hücre\nTers akış: {result.downhillErrors} · kütle hatası {result.massError:E2}\nAçık sınır · renk: havza · çizgi: akış\nHaritaya tıkla: bölgeye git";
            }catch(OperationCanceledException){}catch(Exception e){summary.text="Analiz tamamlanamadı: "+e.Message;}finally{busy=false;}
        }
        void Update(){if(panel!=null&&app.Flying)panel.style.display=DisplayStyle.None;}
        void OnDestroy(){cancel?.Cancel();cancel?.Dispose();if(image)Destroy(image);}
    }
}
