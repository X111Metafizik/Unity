using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ladir.Editor {
 [InitializeOnLoad] public static class PrototypeSetup {
  const string Root="Assets/Ladir",ScenePath=Root+"/Scenes/ReferenceFlight.unity";
  static PrototypeSetup(){if(!Application.isBatchMode)EditorApplication.delayCall+=TrySetup;}
  static void TrySetup(){if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=TrySetup;return;}
   if(!File.Exists("UserSettings/LadirImported.txt")&&File.Exists(Root+"/Source/Voyager.bytes")&&File.Exists("Assets/StreamingAssets/Ladir/reference/manifest.json")){
    try{Create();Directory.CreateDirectory("UserSettings");File.WriteAllText("UserSettings/LadirImported.txt","reference-flight-1");}catch(Exception e){Debug.LogException(e);}
   }
  }
  [MenuItem("LADIR/1 · Create or open reference flight")]
  public static void Create(){Directory.CreateDirectory(Root+"/Generated");Directory.CreateDirectory(Root+"/Scenes");AssetDatabase.Refresh();
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Generated/Voyager.asset");if(!mesh){mesh=ImportShip();AssetDatabase.CreateAsset(mesh,Root+"/Generated/Voyager.asset");}
   ConfigureTexture("VoyagerAlbedo.png",false);ConfigureTexture("VoyagerNormal.png",true);ConfigureTexture("IonThruster.png",false);
   var ship=MaterialAsset("Voyager","Universal Render Pipeline/Lit");ship.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/VoyagerAlbedo.png"));ship.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/VoyagerNormal.png"));ship.EnableKeyword("_NORMALMAP");ship.SetFloat("_BumpScale",.8f);ship.SetFloat("_Metallic",.35f);ship.SetFloat("_Smoothness",.45f);
   var terrain=MaterialAsset("Terrain","Ladir/Region");var water=MaterialAsset("Water","Ladir/Water");
   var thruster=MaterialAsset("Ion","Universal Render Pipeline/Particles/Unlit");thruster.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/IonThruster.png"));thruster.SetColor("_BaseColor",new Color(1.5f,2.5f,3,.7f));thruster.SetFloat("_Surface",1);thruster.SetFloat("_Blend",2);thruster.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);thruster.SetFloat("_DstBlend",(float)BlendMode.One);thruster.SetFloat("_ZWrite",0);thruster.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");thruster.renderQueue=3000;
   var light=MaterialAsset("NavigationLight","Universal Render Pipeline/Lit");light.SetColor("_BaseColor",new Color(.1f,.8f,1));light.SetColor("_EmissionColor",new Color(.2f,2,3));light.EnableKeyword("_EMISSION");
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root+"/Generated/Renderer.asset");if(!renderer){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,Root+"/Generated/Renderer.asset");}
   var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root+"/Generated/Pipeline.asset");if(!pipeline){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,Root+"/Generated/Pipeline.asset");}
   pipeline.renderScale=1;pipeline.msaaSampleCount=4;pipeline.useSRPBatcher=true;pipeline.supportsHDR=true;pipeline.mainLightShadowmapResolution=2048;pipeline.shadowCascadeCount=4;pipeline.shadowDistance=2200;pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;
   var pipelineSettings=new SerializedObject(pipeline);pipelineSettings.FindProperty("m_MainLightShadowsSupported").boolValue=true;pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue=true;pipelineSettings.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)LightRenderingMode.Disabled;pipelineSettings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
   GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=0;
   PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.companyName="Ladir";PlayerSettings.productName="LADIR Reference Flight";PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;PlayerSettings.enableFrameTimingStats=true;PlayerSettings.SplashScreen.show=false;PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
   var playerSettings=new SerializedObject(Resources.FindObjectsOfTypeAll<PlayerSettings>()[0]);var input=playerSettings.FindProperty("activeInputHandler");if(input!=null&&input.intValue!=1){input.intValue=1;playerSettings.ApplyModifiedPropertiesWithoutUndo();}
   AssetDatabase.SaveAssets();
   if(!File.Exists(ScenePath)){
    var old=SceneManager.GetActiveScene();bool maySwitch=!Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty);
    if(!Application.isBatchMode&&string.IsNullOrEmpty(old.path)&&old.isDirty)throw new InvalidOperationException("Save your current untitled scene, then use LADIR > Create or open reference flight. Its unsaved changes were preserved.");
    var mode=Application.isBatchMode||string.IsNullOrEmpty(old.path)?NewSceneMode.Single:NewSceneMode.Additive;
    var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,mode);SceneManager.SetActiveScene(scene);var root=new GameObject("LADIR Prototype");var setup=root.AddComponent<PrototypeScene>();setup.shipMesh=mesh;setup.shipMaterial=ship;setup.terrainMaterial=terrain;setup.waterMaterial=water;setup.thrusterMaterial=thruster;setup.lightMaterial=light;EditorSceneManager.SaveScene(scene,ScenePath);
    if(maySwitch)EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);else{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(old);Debug.Log("LADIR scene created. Current unsaved scene preserved; open via LADIR menu.");}
   }else if(!Application.isBatchMode&&!SceneManager.GetActiveScene().isDirty)EditorSceneManager.OpenScene(ScenePath);
   Debug.Log("LADIR reference flight ready: "+ScenePath+". Press Play to fly.");
  }
  static Material MaterialAsset(string name,string shaderName){string path=Root+"/Generated/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;var shader=Shader.Find(shaderName);if(!shader)throw new InvalidOperationException("Shader missing: "+shaderName);m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,path);return m;}
  static void ConfigureTexture(string file,bool normal){var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"/Source/"+file);if(importer==null)throw new IOException("Texture not imported: "+file);var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
   if(importer.textureType==type&&importer.maxTextureSize==4096&&importer.anisoLevel==8)return;importer.textureType=type;importer.maxTextureSize=4096;importer.anisoLevel=8;importer.mipmapEnabled=true;importer.sRGBTexture=!normal;importer.alphaIsTransparency=!normal;importer.SaveAndReimport();
  }
  static Mesh ImportShip(){using(var input=new BinaryReader(File.OpenRead(Root+"/Source/Voyager.bytes"))){if(new string(input.ReadChars(8))!="LADIRMS1")throw new InvalidDataException("Ship header");int vertices=input.ReadInt32(),indices=input.ReadInt32();if(vertices<3||vertices>1000000||indices%3!=0)throw new InvalidDataException("Ship count");var positions=new Vector3[vertices];var normals=new Vector3[vertices];var uvs=new Vector2[vertices];
   for(int i=0;i<vertices;i++){positions[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());normals[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());uvs[i]=new Vector2(input.ReadSingle(),input.ReadSingle());}var triangles=new int[indices];for(int i=0;i<indices;i++){triangles[i]=(int)input.ReadUInt32();if(triangles[i]>=vertices)throw new InvalidDataException("Ship index");}var mesh=new Mesh {name="Starlight Voyager · 110 metres",indexFormat=IndexFormat.UInt32,vertices=positions,normals=normals,uv=uvs,triangles=triangles};mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
  }}
  [Serializable] class ValidationReport {public bool passed;public int tiles,edgeSamples,gridContracts,waterVertices;public double radiusMeters;public float diameterKm,modelLengthMeters;public string worldHash,editor;public string[] checks;}
  static void Assert(bool condition,string message){if(!condition)throw new InvalidDataException("LADIR validation failed: "+message);}
  [MenuItem("LADIR/2 · Validate data and LOD")]
  public static void Validate(){var root=Path.Combine(Application.streamingAssetsPath,"Ladir/reference");var manifest=JsonUtility.FromJson<RegionManifest>(File.ReadAllText(Path.Combine(root,"manifest.json")));
   Assert(manifest.radiusMeters==4000000,"planet radius");int edges=0,contracts=0,waterVertices=0;
   using(var overview=RegionData.Read(Path.Combine(root,"overview.bin.gz"),manifest.overviewSha256)){
    var previous=new RegionData[16];try{for(int z=0;z<16;z++){RegionData left=null;for(int x=0;x<16;x++){var record=manifest.tiles[z*16+x];var current=RegionData.Read(Path.Combine(root,record.file),record.sha256);Assert(current.side==257&&current.step==7.8125f,"fine sampling");Assert(current.minX==x*2000-16000&&current.minZ==z*2000-16000,"tile origin");
     if(left!=null)for(int i=0;i<257;i++){Assert(left.heights[i*257+256]==current.heights[i*257]&&left.water[i*257+256]==current.water[i*257],"horizontal shared border");edges++;}
     if(previous[x]!=null){for(int i=0;i<257;i++){Assert(previous[x].heights[256*257+i]==current.heights[i]&&previous[x].water[256*257+i]==current.water[i],"vertical shared border");edges++;}previous[x].Dispose();}previous[x]=current;left=current;
     // Every low-resolution source sample is identical to the fine source.
     for(int j=0;j<=256;j+=8)for(int i=0;i<=256;i+=8){float px=current.minX+i*current.step,pz=current.minZ+j*current.step;Assert(Mathf.Abs(current.heights[j*257+i]-overview.Height(px,pz))<.001f,"overview/fine canonical sample");}
    }}}finally{foreach(var d in previous)d?.Dispose();}
    var waterRecord=manifest.tiles[6*16+8];using(var fine=RegionData.Read(Path.Combine(root,waterRecord.file),waterRecord.sha256)){
     foreach(int n in new[]{32,64,128,256}){using(var vertices=new NativeArray<TerrainVertex>((n+1)*(n+1),Allocator.TempJob)){
      var job=new TerrainJob {heights=fine.heights,water=fine.water,colors=fine.colors,overview=overview.heights,side=257,cells=n,neighbors=new int4(16),overviewSide=overview.side,step=fine.step,minX=fine.minX,minZ=fine.minZ,tileX=fine.minX,tileZ=fine.minZ,overviewStep=overview.step,radius=4000000,vertices=vertices};job.Schedule(vertices.Length,128).Complete();
      for(int i=0;i<=n;i++){float q=(float)i*16/n,a=Mathf.Floor(q),t=q-a;float x0=fine.minX+a*125,x1=fine.minX+(a+1)*125,z=fine.minZ;float y0=overview.Height(x0,z)-TerrainJob.Sag(x0,z,4000000),y1=overview.Height(x1,z)-TerrainJob.Sag(x1,z,4000000);Assert(Mathf.Abs(vertices[i].position.y-Mathf.Lerp(y0,y1,t))<.002f,"fine/coarse edge contract");contracts++;}
     }}
     using(var water=new NativeList<TerrainVertex>(1024,Allocator.TempJob)){new WaterJob {heights=fine.heights,water=fine.water,side=257,cells=256,step=fine.step,minX=fine.minX,minZ=fine.minZ,tileX=fine.minX,tileZ=fine.minZ,radius=4000000,vertices=water}.Schedule().Complete();waterVertices=water.Length;Assert(waterVertices>0&&waterVertices%3==0,"lake water geometry");for(int i=0;i<water.Length;i++)Assert(math.all(math.isfinite(water[i].position)),"finite water vertex");}
    }
    bool rejected=false;try{using(var ignored=RegionData.Read(Path.Combine(root,"overview.bin.gz"),new string('0',64))) {}}catch(InvalidDataException){rejected=true;}Assert(rejected,"checksum corruption rejected");
   }
   Assert(Mathf.Abs(FlightController.CruiseMetersPerSecond*3.6f-700)<.001f&&Mathf.Abs(FlightController.BoostMetersPerSecond*3.6f-1800)<.001f,"physical flight units");var model=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Generated/Voyager.asset");Assert(model&&Mathf.Abs(model.bounds.size.z-110)<.01f,"model metric length");
   string output=OutputDirectory();Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(new ValidationReport {passed=true,tiles=256,edgeSamples=edges,gridContracts=contracts,waterVertices=waterVertices,radiusMeters=manifest.radiusMeters,diameterKm=(float)manifest.radiusMeters*2/1000,modelLengthMeters=model.bounds.size.z,worldHash=manifest.worldHash,editor=Application.unityVersion,checks=new[]{"All 256 tile hashes and headers","123360 shared height and water border samples","Overview/fine canonical sample identity","All four finer resolutions against coarse stitched edge","Burst terrain and water jobs execute","Finite clipped lake geometry","Corrupt checksum rejected","700/1800 km/h conversion","110 m model scale"}},true));Debug.Log("LADIR validation passed.");
  }
  static string OutputDirectory(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--ladir-output");return i>=0&&i+1<args.Length?args[i+1]:Path.GetFullPath("Builds/Validation");}
  [MenuItem("LADIR/3 · Build Windows prototype")]
  public static void Build(){try{Create();Validate();string output=OutputDirectory();Directory.CreateDirectory(output);var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},locationPathName=Path.Combine(output,"Ladir.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
    File.WriteAllText(Path.Combine(output,"build.json"),"{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+",\"bytes\":"+report.summary.totalSize+",\"seconds\":"+report.summary.totalTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
    if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+report.summary.result);if(Application.isBatchMode)EditorApplication.Exit(0);
   }catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);else throw;}
  }
 }
}
