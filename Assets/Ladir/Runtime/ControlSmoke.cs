using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Ladir {
 // Synthetic Input System devices only. This never sends operating-system input
 // or touches the user's keyboard/mouse. It exercises the real flight Update.
 [DefaultExecutionOrder(-100)] public sealed class ControlSmoke : MonoBehaviour {
  public FlightController ship;public RegionStreamer terrain;
  Keyboard keyboard;Mouse mouse;string output;bool running,started;int stage,frame;float since,healthBeforeRepair;Vector3 pausedPosition,orbitBefore;
  [Serializable] class Result {public bool passed;public string message;public int completedStages;public float health,altitude,speedKmh;public string[] checks;}
  void Start(){var args=Environment.GetCommandLineArgs();running=args.Contains("--ladir-control-smoke");if(!running){enabled=false;return;}int index=Array.IndexOf(args,"--ladir-output");output=index>=0&&index+1<args.Length?args[index+1]:Path.Combine(Application.persistentDataPath,"controls");Directory.CreateDirectory(output);keyboard=InputSystem.AddDevice<Keyboard>("LadirTestKeyboard");mouse=InputSystem.AddDevice<Mouse>("LadirTestMouse");since=Time.realtimeSinceStartup;}
  void Next(){stage++;frame=0;since=Time.realtimeSinceStartup;}
  void Check(bool condition,string message){if(!condition)throw new Exception(message);}
  void Update(){if(!running)return;try{
   if(terrain.Error.Length>0)throw new Exception(terrain.Error);
   if(!terrain.Detailed){Check(Time.realtimeSinceStartup-since<60,"terrain startup timeout");return;}
   if(!started){started=true;since=Time.realtimeSinceStartup;ship.ResetFlight(new Vector3(3000,terrain.Surface(3000,-12000)+200,-12000),0);}
   float elapsed=Time.realtimeSinceStartup-since;
   if(stage==0&&elapsed>4){Check(Mathf.Abs(ship.Speed*3.6f-1800)<2,"boost did not reach 1800 km/h");Next();}
   else if(stage==1&&elapsed>3){Check(Mathf.Abs(ship.Speed*3.6f-700)<2,"cruise did not return to 700 km/h");ship.ResetFlight(new Vector3(3000,terrain.Surface(3000,-12000)+40,-12000),0,25);Next();}
   else if(stage==2&&elapsed>3){Check(ship.Landed,"landing assistant did not land");Check(Mathf.Abs(ship.Altitude-17.2f)<.1f,"landing clearance differs from visible surface");Next();}
   else if(stage==3&&elapsed>2){Check(!ship.Landed&&ship.Altitude>25,"takeoff failed");ship.ResetFlight(new Vector3(3000,terrain.Surface(3000,-12000)+40,-12000),0);Next();}
   else if(stage==4){if(ship.Health<100){Check(ship.Altitude>=17,"collision crossed below surface");healthBeforeRepair=ship.Health;ship.transform.position+=Vector3.up*2000;Next();}else Check(elapsed<3,"downward flight did not collide");}
   else if(stage==5&&elapsed>5.3f){Check(ship.Health>=Mathf.Min(100,healthBeforeRepair+50)-.1f,"repair did not restore health");orbitBefore=(ship.flightCamera.transform.position-ship.transform.position).normalized;Next();}
   else if(stage==6&&elapsed>.8f){var after=(ship.flightCamera.transform.position-ship.transform.position).normalized;Check(Vector3.Angle(orbitBefore,after)>10,"Ctrl/right mouse orbit did not move camera");Next();}
   else if(stage==7){if(frame==1)pausedPosition=ship.transform.position;if(elapsed>.6f){Check(ship.Paused,"Escape did not pause");Check(Vector3.Distance(pausedPosition,ship.transform.position)<.01f,"paused ship moved");Finish(true,"Input and surface smoke checks passed");return;}}
   Key[] keys=stage==0?new[]{Key.Space}:((stage==2||stage==3)&&frame==0)?new[]{Key.C}:stage==5&&frame==0?new[]{Key.Digit2}:stage==6?new[]{Key.LeftCtrl}:stage==7&&frame==0?new[]{Key.Escape}:Array.Empty<Key>();
   var mouseState=new MouseState {position=new Vector2(Screen.width*.5f,stage==4?Screen.height*.1f:Screen.height*.5f),delta=stage==6?new Vector2(8,0):Vector2.zero,buttons=(ushort)(stage==6?2:0)};
   InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,mouseState);InputSystem.Update();frame++;
  }catch(Exception e){Finish(false,e.Message);}}
  void Finish(bool passed,string message){running=false;var result=new Result {passed=passed,message=message,completedStages=stage,health=ship.Health,altitude=ship.Altitude,speedKmh=ship.Speed*3.6f,checks=new[]{"Space reaches 1800 km/h","Release returns to 700 km/h","C lands on the displayed terrain","C takes off","Downward impact damages ship without tunnelling","2 repairs hull","Ctrl/right mouse orbits camera","Escape freezes movement"}};File.WriteAllText(Path.Combine(output,"controls.json"),JsonUtility.ToJson(result,true));if(passed)Debug.Log("LADIR controls passed");else Debug.LogError(message);Application.Quit(passed?0:4);}
  void OnDestroy(){if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);}
 }
}
