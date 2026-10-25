using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Newtonsoft.Json.Linq;

namespace Ladir.Planet
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlanetStudio : MonoBehaviour
    {
        public Mesh shipMesh;public Material shipMaterial,terrainMaterial,waterMaterial,thrusterMaterial,lightMaterial,cloudMaterial,warpMaterial,skyMaterial,lineMaterial,vegetationMaterial;
        public Texture2D rockTexture;
        public TextAsset vegetationGeometry,volumeNoise;
        public UnityEngine.UIElements.PanelSettings panelSettings;public UnityEngine.UIElements.StyleSheet styleSheet;
        public PlanetTerrain Terrain {get;private set;}public TerrainKernel Kernel{get;private set;}public FlightModel Flight{get;private set;}
        public Camera Camera{get;private set;}public Light Sun{get;private set;}public PlanetHUD Hud{get;private set;}
        public PlanetRoutePreview Routes{get;private set;}
        public D3 Origin,CameraPosition,Focus;public bool Flying,Busy,PointerOverUI,clean,Automated,Sound=true;public string Status="Dünya paketi açılıyor…",ViewMode="Bölge";
        public string SelectedFeature;public D3 LightAnchor;double lastClick=-10;
        public double range=18000,orbitYaw=0,orbitTilt=.85,editorHeight;
        public float sensitivity=1,sunAzimuth=35,sunElevation=42,exposure=1.1f,clouds=.22f,atmosphere=.45f,fog=.1f,tintOpacity=.28f;
        public int tintMode,inspectLayer;public bool pausedSky;
        Transform ship;PlanetEffects effects;PlanetVegetation vegetation;PlanetClouds cloudRenderer;Material sky;
        double accumulator,followDistance=255,orbitAzimuth,orbitElevation=.22,lastA=-10,lastD=-10;bool cockpit;float nextHud;D3 spawn;
        string currentPath;int loadTicket;Task<TerrainKernel> loading;
        async void Start()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;Time.fixedDeltaTime=1f/60;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.46f,.57f,.69f);RenderSettings.ambientEquatorColor=new Color(.28f,.32f,.36f);RenderSettings.ambientGroundColor=new Color(.18f,.2f,.21f);
            Sun=new GameObject("Sun · spherical world").AddComponent<Light>();Sun.type=LightType.Directional;Sun.intensity=1.6f;Sun.color=new Color(1,.94f,.85f);Sun.shadows=LightShadows.Soft;Sun.shadowBias=.03f;Sun.shadowNormalBias=.2f;RenderSettings.sun=Sun;
            Camera=new GameObject("LADİR Camera").AddComponent<Camera>();Camera.tag="MainCamera";Camera.nearClipPlane=.5f;Camera.fieldOfView=64;Camera.allowHDR=true;Camera.clearFlags=CameraClearFlags.Skybox;Camera.gameObject.AddComponent<AudioListener>();var cd=Camera.GetUniversalAdditionalCameraData();cd.renderPostProcessing=true;cd.requiresDepthTexture=true;
            // Serialized materials also retain these shaders in a standalone build.
            sky=new Material(skyMaterial);RenderSettings.skybox=sky;
            Terrain=new GameObject("Planet · canonical quadtree").AddComponent<PlanetTerrain>();Terrain.landMaterial=new Material(terrainMaterial);Terrain.waterMaterial=new Material(waterMaterial);Terrain.viewCamera=Camera;
            ship=new GameObject("Starlight Voyager · 110 m").transform;ship.gameObject.AddComponent<MeshFilter>().sharedMesh=shipMesh;ship.gameObject.AddComponent<MeshRenderer>().sharedMaterial=shipMaterial;ship.gameObject.SetActive(false);
            effects=gameObject.AddComponent<PlanetEffects>();effects.Initialize(this,ship,thrusterMaterial,lightMaterial,warpMaterial);
            vegetation=gameObject.AddComponent<PlanetVegetation>();vegetation.Initialize(this);
            gameObject.AddComponent<PlanetHydrologyOverlay>().Initialize(this);
            Routes=gameObject.AddComponent<PlanetRoutePreview>();Routes.Initialize(this);
            cloudRenderer=gameObject.AddComponent<PlanetClouds>();cloudRenderer.Initialize(this,cloudMaterial);
            Hud=gameObject.AddComponent<PlanetHUD>();Hud.Initialize(this,panelSettings,styleSheet);
            await Load(Path.Combine(Application.streamingAssetsPath,"Ladir/planet/river-v42.planet"));
        }
        public async Task Load(string path)
        {
            int ticket=++loadTicket;Busy=true;Status="Dünya paketi doğrulanıyor ve havza indeksi açılıyor…";
            try
            {
                loading=Task.Run(()=>new TerrainKernel(PlanetWorld.Load(path)));var kernel=await loading;
                if(!this||ticket!=loadTicket){kernel.Dispose();return;}ExitFlight();var old=Kernel;Kernel=kernel;Terrain.Initialize(kernel);currentPath=path;
                // This is the same river-v42 world and local frame as the web fixture.
                Focus=Kernel.World.Direction((-19200+800)/Kernel.World.radiusMeters,(48000-6700)/Kernel.World.radiusMeters);spawn=Focus;LightAnchor=Focus;range=18000;orbitTilt=.85;ApplyAppearance(Kernel.World.Metadata["appearanceConfig"] as JObject);Status="Dünya hazır · "+Kernel.World.generationConfig.seed;Hud.WorldChanged();
                // Active jobs retain their own lease until cancellation drains.
                old?.Dispose();
            }
            catch(Exception e){Status="Dünya açılamadı: "+e.Message;Debug.LogException(e);}finally{if(ticket==loadTicket)Busy=false;}
        }
        void ApplyAppearance(JObject a)
        {if(a==null)return;if(a["lightAnchor"] is JArray anchor)LightAnchor=new D3(anchor.ToObject<double[]>());sunAzimuth=(float?)a["sunAzimuth"]??35;sunElevation=(float?)a["sunElevation"]??42;exposure=(float?)a["exposure"]??1.1f;clouds=(float?)a["clouds"]??.22f;atmosphere=(float?)a["atmosphere"]??.45f;fog=(float?)a["fog"]??.1f;tintMode=(int?)a["tintMode"]??0;tintOpacity=(float?)a["tintOpacity"]??.28f;}
        public JObject Appearance()=>new JObject{{"lightAnchor",new JArray(LightAnchor.Array())},{"sunAzimuth",sunAzimuth},{"sunElevation",sunElevation},{"exposure",exposure},{"clouds",clouds},{"atmosphere",atmosphere},{"fog",fog},{"tintMode",tintMode},{"tintOpacity",tintOpacity}};
        public void Save(string path)
        {try{Kernel.World.Save(path,Appearance());Status="Kaydedildi: "+path;}catch(Exception e){Status="Kayıt başarısız: "+e.Message;}}
        public async void Generate(PlanetConfig config)
        {
            if(Busy)return;Busy=true;ExitFlight();Status="Aynı web çekirdeği ile gezegen üretiliyor…";
            string streaming=Application.streamingAssetsPath,output=Path.Combine(Application.persistentDataPath,"Worlds");
            try{string path=await PlanetAuthoring.Generate(config,streaming,output,progress=>{try{var p=JObject.Parse(progress);if(p["label"]!=null)Status=(string)p["label"];}catch{}});await Load(path);}
            catch(Exception e){Status="Üretim tamamlanamadı: "+e.Message;Debug.LogException(e);}finally{Busy=false;}
        }
        public void StartFlight()
        {
            if(Kernel==null||Busy)return;spawn=Focus;Flight=new FlightModel(Kernel,Focus,FlightModel.North(Focus));Flight.Message=s=>Status=s;Flight.Shot=missile=>effects.PlayShot(missile);Flying=true;ship.gameObject.SetActive(true);accumulator=0;orbitAzimuth=0;orbitElevation=.22;Status="Serbest uçuş · C ile iniş · 1 warp · 2 onarım";Hud?.RefreshMode();effects.ResetFlight();
        }
        public void ExitFlight()
        {if(Flying&&Flight!=null)Focus=Flight.position.Unit;Flying=false;Flight=null;if(ship)ship.gameObject.SetActive(false);range=Math.Max(6000,range);Hud?.RefreshMode();}
        public void Retry(){Focus=spawn;StartFlight();}
        public void SetView(int index)
        {if(Kernel==null)return;switch(index){case 0:ViewMode="Yörünge";range=Kernel.World.radiusMeters*1.4;orbitTilt=0;break;case 1:ViewMode="Bölge";range=18000;orbitTilt=.85;break;case 2:ViewMode="Yüzey";range=700;orbitTilt=1.25;break;case 3:ViewMode="Harita";range=150000;orbitTilt=0;break;}}
        public void Visit(D3 direction,double distance=10000){Focus=direction.Unit;LightAnchor=Focus;range=distance;orbitTilt=.85;}
        void Update()
        {
            if(Kernel==null){Hud?.Tick();return;}var mouse=Mouse.current;var keyboard=Keyboard.current;
            if(keyboard!=null&&keyboard.hKey.wasPressedThisFrame){clean=!clean;Hud.SetClean(clean);}
            bool orbit=false;
            if(Flying&&Flight!=null)
            {
                orbit=keyboard!=null&&(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)&&mouse!=null&&mouse.rightButton.isPressed;
                if(keyboard!=null&&!Hud.Typing&&!Automated)
                {
                    if(keyboard.escapeKey.wasPressedThisFrame)Flight.paused=!Flight.paused;if(keyboard.cKey.wasPressedThisFrame)Flight.Landing();if(keyboard.digit1Key.wasPressedThisFrame)Flight.Activate(1);if(keyboard.digit2Key.wasPressedThisFrame)Flight.Activate(2);
                    if(keyboard.bKey.wasPressedThisFrame){Flight.cruising=false;Flight.pitch=0;}if(keyboard.wKey.wasPressedThisFrame)Flight.cruising=true;if(keyboard.leftShiftKey.wasPressedThisFrame||keyboard.rightShiftKey.wasPressedThisFrame)Flight.CycleTarget();if(keyboard.deleteKey.wasPressedThisFrame)cockpit=!cockpit;
                    if(keyboard.aKey.wasPressedThisFrame){if(Time.unscaledTimeAsDouble-lastA<.3)Flight.Roll(-1);lastA=Time.unscaledTimeAsDouble;}if(keyboard.dKey.wasPressedThisFrame){if(Time.unscaledTimeAsDouble-lastD<.3)Flight.Roll(1);lastD=Time.unscaledTimeAsDouble;}
                    Vector2 pointer=mouse?.position.ReadValue()??new Vector2(Screen.width*.5f,Screen.height*.5f);double x=(pointer.x/Screen.width-.5)*2,y=(.5-pointer.y/Screen.height)*2;
                    Flight.input=new FlightModel.FlightInput{x=PointerOverUI||orbit?0:x*sensitivity,y=PointerOverUI||orbit?0:y*sensitivity,strafe=keyboard.aKey.isPressed?-1:keyboard.dKey.isPressed?1:0,boost=keyboard.spaceKey.isPressed,brake=keyboard.sKey.isPressed,gun=!PointerOverUI&&!orbit&&(mouse?.leftButton.isPressed??false),missile=!PointerOverUI&&!orbit&&(mouse?.rightButton.isPressed??false)};
                    float wheel=mouse?.scroll.ReadValue().y??0;if(wheel!=0&&!PointerOverUI){if(Flight.warpCharge>0)Flight.SelectWarp(Flight.warpIndex+(wheel>0?1:-1));else followDistance=PM.Clamp(followDistance*(wheel>0?.9:1.1),100,900);}
                    if(orbit){Vector2 delta=mouse.delta.ReadValue();orbitAzimuth+=delta.x*.005;orbitElevation=PM.Clamp(orbitElevation-delta.y*.005,-1.3,1.3);}else{orbitAzimuth*=Math.Exp(-Time.unscaledDeltaTime*3);orbitElevation=PM.Mix(orbitElevation,.22,1-Math.Exp(-Time.unscaledDeltaTime*3));}
                }
                accumulator+=Time.unscaledDeltaTime;int steps=Math.Min(6,(int)(accumulator*60));accumulator%=1.0/60;for(int i=0;i<steps;i++)Flight.Step(1.0/60);
                // Interpolated presentation never changes physics or terrain coordinates.
                double alpha=accumulator*60;var position=Flight.previousPosition+(Flight.position-Flight.previousPosition)*alpha;var forward=(Flight.previousForward+(Flight.forward-Flight.previousForward)*alpha).Unit;var up=position.Unit;var right=D3.Cross(forward,up).Unit;
                if(cockpit){CameraPosition=position+forward*42+up*9;Camera.transform.rotation=Quaternion.LookRotation(Coordinates.Float(forward),Coordinates.Float(up));}
                else{D3 offset=-forward*Math.Cos(orbitAzimuth)*Math.Cos(orbitElevation)+right*Math.Sin(orbitAzimuth)*Math.Cos(orbitElevation)+up*Math.Sin(orbitElevation);CameraPosition=position+offset*followDistance+up*32;Camera.transform.rotation=Quaternion.LookRotation(Coordinates.Float(position+forward*65-CameraPosition),Coordinates.Float(up));}
                Rebase(CameraPosition);ship.position=Coordinates.Float(position-Origin);ship.rotation=Quaternion.LookRotation(Coordinates.Float(forward),Coordinates.Float(up))*Quaternion.Euler(0,0,(float)((Flight.bank+Flight.roll)*180/Math.PI));
                Terrain.Observer=Flight.position;Terrain.Forward=forward;Terrain.Predicted=Flight.warpCharge>0?Flight.position+Flight.forward*FlightModel.WarpDistances[Flight.warpIndex]:Flight.state=="warping"?Flight.warpTo:Flight.position+Flight.velocity*3;
            }
            else
            {
                if(keyboard!=null&&!Hud.Typing){if(keyboard.digit1Key.wasPressedThisFrame)SetView(0);if(keyboard.digit2Key.wasPressedThisFrame)SetView(1);if(keyboard.digit3Key.wasPressedThisFrame)SetView(2);if(keyboard.digit4Key.wasPressedThisFrame)SetView(3);if(keyboard.homeKey.wasPressedThisFrame){Focus=Kernel.World.Center;SetView(0);}if(keyboard.nKey.wasPressedThisFrame)orbitYaw=0;}
                if(mouse!=null&&!PointerOverUI&&!Hud.Typing&&!Automated)
                {
                    if(mouse.leftButton.wasPressedThisFrame&&Pick(mouse.position.ReadValue(),out var selected)){SelectedFeature=null;Hud.SelectPosition(selected);if(Time.unscaledTimeAsDouble-lastClick<.3){Focus=selected;range=Math.Max(70,range*.5);}lastClick=Time.unscaledTimeAsDouble;}
                    var delta=mouse.delta.ReadValue();D3 north=FlightModel.North(Focus),east=D3.Cross(north,Focus).Unit;
                    if(mouse.leftButton.isPressed){double factor=range/Kernel.World.radiusMeters/Screen.height;Focus=(Focus-east*delta.x*factor-north*delta.y*factor).Unit;}
                    if(mouse.rightButton.isPressed){orbitYaw+=delta.x*.004;orbitTilt=PM.Clamp(orbitTilt-delta.y*.004,0,1.48);}
                    float wheel=mouse.scroll.ReadValue().y;if(wheel!=0){if(wheel>0&&Pick(mouse.position.ReadValue(),out var underMouse))Focus=(Focus+(underMouse-Focus)*.14).Unit;range=PM.Clamp(range*Math.Exp(-Math.Sign(wheel)*.14),70,Kernel.World.radiusMeters*12);}
                    if(keyboard!=null){double dx=(keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),dy=(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0),factor=range*.4*Time.unscaledDeltaTime/Kernel.World.radiusMeters;if(keyboard.leftShiftKey.isPressed)factor*=3;Focus=(Focus+east*dx*factor+north*dy*factor).Unit;}
                }
                D3 n=FlightModel.North(Focus),e=D3.Cross(n,Focus).Unit,tangent=n*Math.Cos(orbitYaw)+e*Math.Sin(orbitYaw);editorHeight=Kernel.Sample(Focus).height;D3 target=Focus*(Kernel.World.radiusMeters+editorHeight);CameraPosition=target+Focus*(range*Math.Cos(orbitTilt))-tangent*(range*Math.Sin(orbitTilt));
                Rebase(CameraPosition);Camera.transform.rotation=Quaternion.LookRotation(Coordinates.Float(target-CameraPosition),Coordinates.Float(n*Math.Cos(orbitTilt)+Focus*Math.Sin(orbitTilt)));
                Terrain.Observer=CameraPosition;Terrain.Forward=Coordinates.Double(Camera.transform.forward);Terrain.Predicted=range>2000&&range<40000?Focus*(Kernel.World.radiusMeters+editorHeight+320):CameraPosition;
            }
            Camera.transform.position=Coordinates.Float(CameraPosition-Origin);Camera.nearClipPlane=Flying?.5f:(float)Math.Max(.5,range*.00002);Camera.farClipPlane=(float)Math.Min(Kernel.World.radiusMeters*30,Math.Max(150000,(CameraPosition.Length-Kernel.World.radiusMeters)*4+Math.Sqrt(Math.Max(0,CameraPosition.Length*CameraPosition.Length-Kernel.World.radiusMeters*Kernel.World.radiusMeters))*2));
            double altitude=Math.Max(0,CameraPosition.Length-Kernel.World.radiusMeters);float space=(float)PM.Smooth(30000,160000,altitude);sky.SetFloat("_AtmosphereThickness",atmosphere*1.8f*(1-space));sky.SetFloat("_Exposure",exposure);RenderSettings.fog=space<1&&fog>0;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=new Color(.53f,.68f,.78f);RenderSettings.fogDensity=.00035f*fog*(1-space);
            D3 localUp=LightAnchor.Length>.5?LightAnchor:Kernel.World.Center,northSun=FlightModel.North(localUp),eastSun=D3.Cross(northSun,localUp).Unit;double az=sunAzimuth*Math.PI/180,el=sunElevation*Math.PI/180;D3 sun=(localUp*Math.Sin(el)+(northSun*Math.Cos(az)+eastSun*Math.Sin(az))*Math.Cos(el)).Unit;Sun.transform.rotation=Quaternion.LookRotation(-Coordinates.Float(sun),Coordinates.Float(northSun));
            Shader.SetGlobalVector("_LadirOrigin",new Vector4((float)-Origin.x,(float)Origin.y,(float)Origin.z,(float)Kernel.World.radiusMeters));Shader.SetGlobalVector("_LadirNoiseOrigin",new Vector4((float)(-Origin.x%65536),(float)(Origin.y%65536),(float)(Origin.z%65536),0));Shader.SetGlobalFloat("_LadirTint",tintMode);Shader.SetGlobalFloat("_LadirTintOpacity",tintOpacity);Shader.SetGlobalFloat("_LadirInspect",inspectLayer);Shader.SetGlobalFloat("_LadirTactical",!Flying&&altitude>38000?1:0);
            var sunVector=Coordinates.Float(sun);Shader.SetGlobalVector("_LadirSun",new Vector4(sunVector.x,sunVector.y,sunVector.z,0));
            Shader.SetGlobalFloat("_LadirAlien",Kernel.World.generationConfig.worldType=="alien"?1:0);
            if(Time.unscaledTime>=nextHud){nextHud=Time.unscaledTime+.05f;Hud.Tick();}
        }
        void Rebase(D3 p){Origin=new D3(Math.Floor(p.x/1024)*1024,Math.Floor(p.y/1024)*1024,Math.Floor(p.z/1024)*1024);Terrain.Origin=Origin;}
        public bool Pick(Vector2 screen,out D3 direction)
        {
            direction=Focus;if(Kernel==null)return false;var ray=Camera.ScreenPointToRay(screen);D3 origin=Origin+Coordinates.Double(ray.origin),d=Coordinates.Double(ray.direction).Unit;double r=Kernel.World.radiusMeters,b=D3.Dot(origin,d),h=b*b-origin.Length*origin.Length+(r+15000)*(r+15000);if(h<0)return false;double t=Math.Max(0,-b-Math.Sqrt(h)),end=-b+Math.Sqrt(h),previous=t;
            for(int i=0;i<192&&t<=end;i++){D3 p=origin+d*t;var s=Kernel.Sample(p.Unit);double gap=p.Length-r-Math.Max(s.height,s.water);if(gap<.5){for(int k=0;k<10;k++){double mid=(previous+t)*.5;var q=origin+d*mid;var terrain=Kernel.Sample(q.Unit);if(q.Length-r-Math.Max(terrain.height,terrain.water)>0)previous=mid;else t=mid;}direction=(origin+d*t).Unit;return true;}previous=t;t+=PM.Clamp(gap*.65,5,10000);}return false;
        }
        public void Screenshot(){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Ladir");Directory.CreateDirectory(folder);string file=Path.Combine(folder,"Ladir-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".png");ScreenCapture.CaptureScreenshot(file);Status="Fotoğraf: "+file;}
        void OnDestroy(){loadTicket++;Kernel?.Dispose();if(sky)Destroy(sky);}
    }
}
