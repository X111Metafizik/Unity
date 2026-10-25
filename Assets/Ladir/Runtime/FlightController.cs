using UnityEngine;
using UnityEngine.InputSystem;

namespace Ladir {
 public sealed class FlightController : MonoBehaviour {
  public RegionStreamer terrain;public Camera flightCamera;public ParticleSystem[] thrusters;
  public Vector3 Velocity {get;private set;} public float Speed {get;private set;}
  public float Health {get;private set;}=100;public float Altitude {get;private set;}
  public bool Landed {get;private set;} public bool Landing {get;private set;}
  public bool Boosting {get;private set;} public bool Paused {get;private set;}
  public string Status {get;private set;}="ARAZİ YÜKLENİYOR";
  public bool BenchmarkControlled;
  float yaw,pitch,bank,orbitYaw,orbitPitch=15,cameraDistance=255,repairLeft,repairCooldown,hudAt;bool spawned,orbit;
  GUIStyle title,label,small;Texture2D panel;string leftText,rightText,bottomText;
  const float Cruise=700/3.6f,Boost=1800/3.6f,Clearance=17.2f;
  public static float CruiseMetersPerSecond=>Cruise;public static float BoostMetersPerSecond=>Boost;
  public void SetBenchmarkPose(Vector3 point,float heading,float speed){transform.position=point;transform.rotation=Quaternion.Euler(0,heading,0);Speed=speed;Boosting=speed>300;Velocity=transform.forward*speed;Status="KAYITLI TEST ROTASI";}
  void Start(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
  public void ResetFlight(Vector3 point,float heading=0,float initialSpeed=Cruise){point.y=Mathf.Max(point.y,terrain.Surface(point.x,point.z)+Clearance+10);transform.position=point;yaw=heading;pitch=bank=0;Speed=initialSpeed;Landed=Landing=false;Health=100;transform.rotation=Quaternion.Euler(0,yaw,0);Velocity=transform.forward*Speed;}
  void Update(){if(!terrain.Ready)return;
   if(!spawned){spawned=true;ResetFlight(new Vector3(800,1900,-6700),-12);Status="SERBEST UÇUŞ";}
   if(BenchmarkControlled){Altitude=transform.position.y-terrain.Surface(transform.position.x,transform.position.z);UpdateEffects();UpdateHud();return;}
   var k=Keyboard.current;var m=Mouse.current;if(k==null||m==null)return;float dt=Mathf.Min(Time.deltaTime,.1f);
   if(k.escapeKey.wasPressedThisFrame){Paused=!Paused;Status=Paused?"DURAKLATILDI":"SERBEST UÇUŞ";}
   if(k.rKey.wasPressedThisFrame)ResetFlight(new Vector3(800,1900,-6700),-12);
   if(Paused){Velocity=Vector3.zero;UpdateEffects();UpdateHud();return;}
   orbit=(k.leftCtrlKey.isPressed||k.rightCtrlKey.isPressed)&&m.rightButton.isPressed;
   if(orbit){Vector2 delta=m.delta.ReadValue();orbitYaw+=delta.x*.15f;orbitPitch=Mathf.Clamp(orbitPitch-delta.y*.15f,-60,75);}
   else {orbitYaw=Mathf.LerpAngle(orbitYaw,0,dt*4);orbitPitch=Mathf.Lerp(orbitPitch,15,dt*4);
    Vector2 aim=m.position.ReadValue()/new Vector2(Screen.width,Screen.height)*2-Vector2.one;aim=Vector2.ClampMagnitude(aim,1);
    float turnX=Mathf.Abs(aim.x)<.06f?0:aim.x,turnY=Mathf.Abs(aim.y)<.06f?0:aim.y;
    if(!Landed){yaw+=turnX*75*dt;pitch=Mathf.Clamp(pitch-turnY*55*dt,-80,80);bank=Mathf.Lerp(bank,-turnX*40,dt*5);}
   }
   cameraDistance=Mathf.Clamp(cameraDistance-m.scroll.ReadValue().y*.25f,150,650);
   if(k.cKey.wasPressedThisFrame){if(Landed){Landed=false;Landing=false;transform.position+=Vector3.up*8;Speed=50;pitch=-10;Status="KALKIŞ";}else{Landing=!Landing;Status=Landing?"İNİŞ YARDIMCISI":"SERBEST UÇUŞ";}}
   if(k.digit2Key.wasPressedThisFrame&&repairCooldown<=0&&Health<100){repairLeft=5;repairCooldown=30;}
   if(repairLeft>0){float elapsed=Mathf.Min(repairLeft,dt);Health=Mathf.Min(100,Health+elapsed*10);repairLeft-=elapsed;}repairCooldown=Mathf.Max(0,repairCooldown-dt);
   Boosting=k.spaceKey.isPressed&&!Landing&&!Landed;
   float target=Landed?0:Landing?25:k.bKey.isPressed?0:k.sKey.isPressed?32:Boosting?Boost:Cruise;
   // Downhill acceleration is gravity over time, not an instantaneous speed jump.
   float gravityBonus=!Landing&&!Landed?Mathf.Max(0,Mathf.Sin(pitch*Mathf.Deg2Rad))*9.81f*dt:0;
   Speed=Mathf.MoveTowards(Speed,target,(target>Speed?100:130)*dt)+gravityBonus;
   transform.rotation=Quaternion.Euler(Landing?0:pitch,yaw,Landing?0:bank);
   if(!Landed){float strafe=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0);Velocity=transform.forward*Speed+transform.right*strafe*50;
    if(Landing)Velocity+=Vector3.down*25;
    Vector3 previous=transform.position,next=previous+Velocity*dt;next.x=Mathf.Clamp(next.x,-15400,15400);next.z=Mathf.Clamp(next.z,-15400,15400);
    if(Mathf.Abs(next.x)>15350||Mathf.Abs(next.z)>15350){yaw+=90*dt;Status="32 × 32 KM PROTOTİP SINIRI";}
    int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(previous,next)/8));
    for(int i=1;i<=steps;i++){Vector3 p=Vector3.Lerp(previous,next,(float)i/steps);float ground=terrain.Surface(p.x,p.z),water=terrain.Water(p.x,p.z),floor=Mathf.Max(ground,water)+Clearance;
     if(p.y>=floor)continue;p.y=floor;next=p;
     float slope=Mathf.Abs(terrain.Surface(p.x+8,p.z)-terrain.Surface(p.x-8,p.z))/16+Mathf.Abs(terrain.Surface(p.x,p.z+8)-terrain.Surface(p.x,p.z-8))/16;
     if(Landing&&Speed<38&&ground>=water-.5f&&slope<.45f){Landed=true;Landing=false;Speed=0;Velocity=Vector3.zero;Status="İNİŞ TAMAMLANDI · C İLE KALKIŞ";}
     else {float impact=Mathf.Max(0,-Vector3.Dot(Velocity,Vector3.up));Health=Mathf.Max(0,Health-Mathf.Clamp(impact*.15f,4,60));Speed*=.65f;pitch=-12;Status=ground<water?"SU TEMASI":"YÜZEY TEMASI";next.y+=3;}
     break;
    }
    transform.position=next;
   }else {Vector3 p=transform.position;p.y=terrain.Surface(p.x,p.z)+Clearance;transform.position=p;Velocity=Vector3.zero;}
   Altitude=transform.position.y-terrain.Surface(transform.position.x,transform.position.z);if(Health<=0){Status="GEMİ DEVRE DIŞI · R İLE YENİDEN BAŞLAT";Speed=0;Paused=true;}
   UpdateEffects();UpdateHud();
  }
  void UpdateEffects(){if(thrusters==null)return;foreach(var ps in thrusters){var emission=ps.emission;emission.rateOverTime=Landed||Paused?5:Boosting?95:50;var main=ps.main;main.startSize=Boosting?7:4.5f;main.startSpeed=Boosting?180:100;}}
  void LateUpdate(){if(!spawned||!flightCamera)return;Quaternion view=transform.rotation*Quaternion.Euler(orbitPitch,orbitYaw,0);Vector3 target=transform.position-view*Vector3.forward*cameraDistance+Vector3.up*25;
   float ground=terrain.Surface(target.x,target.z);target.y=Mathf.Max(target.y,ground+12);float blend=BenchmarkControlled?1:1-Mathf.Exp(-Time.unscaledDeltaTime*8);flightCamera.transform.position=Vector3.Lerp(flightCamera.transform.position,target,blend);flightCamera.transform.LookAt(transform.position+transform.forward*95,Vector3.up);
  }
  void UpdateHud(){if(Time.unscaledTime<hudAt)return;hudAt=Time.unscaledTime+.05f;leftText=$"{Speed*3.6f:0} km/sa\n{Altitude:0} m · yerden yükseklik\nGövde %{Health:0}";rightText="FARE  Yönelme\nA / D  Yanal hareket\nSPACE  1.800 km/sa boost\nS  Fren  ·  B  Hız kes\nC  İniş / kalkış\nCTRL + SAĞ FARE  Yörünge\nTEKERLEK  Kamera mesafesi\n2  5 sn’de %50 onarım\nR  Başlangıca dön  ·  ESC  Duraklat";
   bottomText=$"{Status}\n{(terrain.Detailed?"Bölge hazır":"Detaylar arka planda hazırlanıyor")} · {terrain.CellsAt(transform.position.x,transform.position.z)} hücre/tile · Okuma {terrain.pendingReads} · Mesh {terrain.pendingMeshes}";
  }
  void OnGUI(){if(title==null){title=new GUIStyle(GUI.skin.label){fontSize=26,fontStyle=FontStyle.Bold,normal={textColor=new Color(.8f,.95f,1)}};label=new GUIStyle(GUI.skin.label){fontSize=19,normal={textColor=Color.white}};small=new GUIStyle(GUI.skin.label){fontSize=14,normal={textColor=new Color(.75f,.9f,.95f)}};panel=new Texture2D(1,1);panel.SetPixel(0,0,new Color(.02f,.055f,.08f,.8f));panel.Apply();}
   float scale=Mathf.Max(.75f,Screen.height/1000f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);float w=Screen.width/scale,h=Screen.height/scale;
   GUI.DrawTexture(new Rect(20,20,320,155),panel);GUI.Label(new Rect(35,30,300,38),"LADİR / UNITY",title);GUI.Label(new Rect(35,75,300,100),leftText??"Arazi paketi yükleniyor…",label);
   GUI.DrawTexture(new Rect(w-335,20,315,260),panel);GUI.Label(new Rect(w-320,32,295,235),rightText??"URP · Windows prototipi",small);
   GUI.DrawTexture(new Rect(w/2-360,h-115,720,95),panel);GUI.Label(new Rect(w/2-340,h-105,690,76),terrain.Error.Length>0?terrain.Error:bottomText??"32 × 32 km referans bölge · LUMEN-742",small);
   GUI.Label(new Rect(w/2-8,h/2-15,30,30),"+",label);GUI.matrix=Matrix4x4.identity;
  }
  void OnDestroy(){if(panel)Destroy(panel);}
 }
}
