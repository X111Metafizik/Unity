using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;
namespace Ladir.Editor
{
    // Generate glyphs and OpenType pairs in the editor, not during first flight.
    public static class PlanetFontBake
    {
        const string Root="Assets/Ladir/Generated/Planet/";
        public static FontAsset Prepare(PanelSettings panel)
        {
            string ui=File.ReadAllText("Assets/Ladir/Planet/PlanetHUD.cs")+File.ReadAllText("Assets/Ladir/Planet/PlanetReferenceInspector.cs");
            var chars=new string(Enumerable.Range(32,352).Select(i=>(char)i).Concat(ui.Where(c=>!char.IsControl(c))).Distinct().ToArray());
            var symbols=Bake("HudSymbols","NotoSansSymbols2-Regular.ttf",new string(chars.Where(c=>c>383).ToArray()),1024);
            var font=Bake("HudLatin","LiberationSans.ttf",chars,2048);
            const string fallbackPath=Root+"HudDynamicFallback.asset";
            var fallback=AssetDatabase.LoadAssetAtPath<FontAsset>(fallbackPath);
            if(!fallback){fallback=FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>("Assets/Ladir/Source/Fonts/LiberationSans.ttf"),64,6,GlyphRenderMode.SDFAA,512,512,AtlasPopulationMode.Dynamic);fallback.name="LADIR user text fallback";Save(fallback,fallbackPath);}
            font.fallbackFontAssetTable=new List<FontAsset>{symbols,fallback};EditorUtility.SetDirty(font);
            var settings=AssetDatabase.LoadAssetAtPath<PanelTextSettings>(Root+"HudTextSettings.asset");
            if(!settings){settings=ScriptableObject.CreateInstance<PanelTextSettings>();AssetDatabase.CreateAsset(settings,Root+"HudTextSettings.asset");}
            settings.defaultFontAsset=font;settings.fallbackFontAssets=new List<FontAsset>{symbols,fallback};panel.textSettings=settings;EditorUtility.SetDirty(settings);EditorUtility.SetDirty(panel);return font;
        }
        static FontAsset Bake(string name,string source,string characters,int size)
        {
            string path=Root+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<FontAsset>(path);if(asset)return asset;
            var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Ladir/Source/Fonts/"+source);if(!font)throw new FileNotFoundException(source);
            asset=FontAsset.CreateFontAsset(font,64,6,GlyphRenderMode.SDFAA,size,size,AtlasPopulationMode.Dynamic);asset.name=name;
            asset.TryAddCharacters(characters,out string missing,true);asset.atlasPopulationMode=AtlasPopulationMode.Static;
            Save(asset,path);return asset;
        }
        static void Save(FontAsset font,string path)
        {AssetDatabase.CreateAsset(font,path);AssetDatabase.AddObjectToAsset(font.material,font);foreach(var texture in font.atlasTextures)AssetDatabase.AddObjectToAsset(texture,font);EditorUtility.SetDirty(font);}
    }
}
