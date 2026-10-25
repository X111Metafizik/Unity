using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Ladir.Planet;

namespace Ladir.Editor
{
    public static class PlanetSetup
    {
        public const string ScenePath="Assets/Ladir/Scenes/PlanetStudio.unity";
        const string Generated="Assets/Ladir/Generated/Planet";
        [MenuItem("LADIR/4 · Create or open full Planet Studio")]
        public static void Create()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Planet Studio kurulumunu Play modu bittikten sonra çalıştır.");
            if(!AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Ladir/Generated/Voyager.asset"))PrototypeSetup.Create();
            Directory.CreateDirectory(Generated);AssetDatabase.Refresh();
            Material terrain=Material("PlanetTerrain","Ladir/Planet"),water=Material("PlanetWater","Ladir/PlanetWater"),cloud=Material("VolumetricClouds","Ladir/Clouds"),warp=Material("Wormhole","Ladir/Warp"),sky=Material("PlanetSky","Ladir/Sky"),lines=Material("FlightLines","Ladir/FlightLine"),vegetation=Material("Vegetation","Ladir/Vegetation");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Generated+"/Mineral.asset");
            if(!texture)
            {
                var pixels=File.ReadAllBytes("Assets/Ladir/Source/Mineral.bytes");
                if(pixels.Length!=512*512*4)throw new InvalidDataException("Mineral texture must contain one 512 x 512 RGBA mip level.");
                texture=new Texture2D(512,512,TextureFormat.RGBA32,true,true){name="Web mineral albedo, height and cracks",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
                texture.SetPixelData(pixels,0);texture.Apply(true,false);AssetDatabase.CreateAsset(texture,Generated+"/Mineral.asset");
            }
            terrain.SetTexture("_Rock",texture);
            vegetation.enableInstancing=true;EditorUtility.SetDirty(vegetation);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Generated+"/Renderer.asset");if(!renderer){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,Generated+"/Renderer.asset");}
            CloudRendererFeature clouds=null;foreach(var f in renderer.rendererFeatures)if(f is CloudRendererFeature c)clouds=c;if(!clouds){clouds=ScriptableObject.CreateInstance<CloudRendererFeature>();clouds.name="LADIR half-resolution clouds";AssetDatabase.AddObjectToAsset(clouds,renderer);renderer.rendererFeatures.Add(clouds);}clouds.material=cloud;clouds.Create();EditorUtility.SetDirty(clouds);EditorUtility.SetDirty(renderer);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Generated+"/Pipeline.asset");if(!pipeline){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,Generated+"/Pipeline.asset");}pipeline.renderScale=1;pipeline.msaaSampleCount=4;pipeline.useSRPBatcher=true;pipeline.supportsHDR=true;pipeline.mainLightShadowmapResolution=2048;pipeline.shadowCascadeCount=4;pipeline.shadowDistance=2200;pipeline.supportsCameraDepthTexture=true;pipeline.supportsCameraOpaqueTexture=true;
            var settings=new SerializedObject(pipeline);settings.FindProperty("m_MainLightShadowsSupported").boolValue=true;settings.FindProperty("m_SoftShadowsSupported").boolValue=true;settings.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)LightRenderingMode.Disabled;settings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            var panel=AssetDatabase.LoadAssetAtPath<PanelSettings>(Generated+"/Panel.asset");if(!panel){panel=ScriptableObject.CreateInstance<PanelSettings>();AssetDatabase.CreateAsset(panel,Generated+"/Panel.asset");}panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1440,1000);panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;panel.match=.5f;panel.sortingOrder=10;EditorUtility.SetDirty(panel);
            panel.themeStyleSheet=AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Ladir/Planet/Ladir.tss");EditorUtility.SetDirty(panel);
            var hudFont=PlanetFontBake.Prepare(panel);
            bool dirty=false;for(int i=0;i<SceneManager.sceneCount;i++)dirty|=SceneManager.GetSceneAt(i).isDirty;var old=SceneManager.GetActiveScene();
            var scene=SceneManager.GetSceneByPath(ScenePath);bool wasLoaded=scene.IsValid()&&scene.isLoaded;
            if(wasLoaded&&scene.isDirty)throw new InvalidOperationException("Planet Studio sahnesinde kaydedilmemiş değişiklikler var; otomatik kurulum bunları değiştirmedi.");
            if(!wasLoaded)scene=File.Exists(ScenePath)?EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive):EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            PlanetStudio app=null;foreach(var root in scene.GetRootGameObjects())if(root.TryGetComponent<PlanetStudio>(out var found)){app=found;break;}
            if(!app){var obj=new GameObject("LADİR · native Planet Studio");SceneManager.MoveGameObjectToScene(obj,scene);app=obj.AddComponent<PlanetStudio>();}
            app.shipMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Ladir/Generated/Voyager.asset");app.shipMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Ladir/Generated/Voyager.mat");app.terrainMaterial=terrain;app.waterMaterial=water;app.cloudMaterial=cloud;app.warpMaterial=warp;app.skyMaterial=sky;app.lineMaterial=lines;app.vegetationMaterial=vegetation;app.thrusterMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Ladir/Generated/Ion.mat");app.lightMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Ladir/Generated/NavigationLight.mat");app.panelSettings=panel;app.styleSheet=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Ladir/Planet/Ladir.uss");app.rockTexture=texture;
            if(!app.GetComponent<PlanetPortSmoke>())app.gameObject.AddComponent<PlanetPortSmoke>();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            app.vegetationGeometry=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Ladir/Source/Vegetation.bytes");app.volumeNoise=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Ladir/Source/VolumeNoise.bytes");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            app.oceanCompute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Ladir/Shaders/Ocean.compute");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            app.hudFont=hudFont;EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            if(!wasLoaded){EditorSceneManager.CloseScene(scene,true);if(old.IsValid())SceneManager.SetActiveScene(old);}
            if(!dirty||Application.isBatchMode)EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true),new EditorBuildSettingsScene("Assets/Ladir/Scenes/ReferenceFlight.unity",false)};
            PlayerSettings.productName="LADİR Planet Studio";PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;PlayerSettings.SplashScreen.show=false;AssetDatabase.SaveAssets();Debug.Log("LADIR Planet Studio scene ready.");
        }
        static Material Material(string name,string shader){string p=Generated+"/"+name+".mat";var s=Shader.Find(shader);if(!s)throw new InvalidOperationException("Shader eksik: "+shader);var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(!m){m=new Material(s);AssetDatabase.CreateAsset(m,p);}else if(m.shader!=s){m.shader=s;EditorUtility.SetDirty(m);}return m;}
        [MenuItem("LADIR/5 · Build Planet Studio for Windows")]
        public static void Build()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer){Debug.LogWarning("LADIR: import/compilation is still running; build was not started.");return;}
            try{Create();string dir=Path.GetFullPath("Builds/Planet");Directory.CreateDirectory(dir);var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=Path.Combine(dir,"Ladir.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});Directory.CreateDirectory("evidence/planet-port");File.WriteAllText("evidence/planet-port/build.json","{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+",\"bytes\":"+report.summary.totalSize+"}");if(Application.isBatchMode)EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);}
            catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
        }
    }
}
