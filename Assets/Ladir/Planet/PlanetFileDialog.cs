using System;
using System.Runtime.InteropServices;
namespace Ladir.Planet
{
    public static class PlanetFileDialog
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]class FileName
        {public int structSize=Marshal.SizeOf(typeof(FileName));public IntPtr owner,instance;public string filter="LADİR planet\0*.planet\0\0";public string customFilter;public int maxCustomFilter,filterIndex=1;public string file=new string('\0',2048);public int maxFile=2048;public string fileTitle;public int maxFileTitle;public string initialDir,title;public int flags=0x00080000|0x00000008;public short fileOffset,extensionOffset;public string defaultExt="planet";public IntPtr customData,hook;public string template;public IntPtr reserved;public int reserved2,flagsEx;}
        [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetOpenFileName([In,Out]FileName p);
        [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetSaveFileName([In,Out]FileName p);
        public static string Open(string extension="planet"){
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("LADİR dosyası aç","",extension);
#elif UNITY_STANDALONE_WIN
            var f=new FileName{title="LADİR dosyası aç",filter="LADİR\0*."+extension+"\0\0",defaultExt=extension,flags=0x00080000|0x00000008|0x00001000};return GetOpenFileName(f)?f.file.Split('\0')[0]:null;
#else
            return null;
#endif
        }
        public static string Save(string extension="planet"){
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.SaveFilePanel("LADİR dosyasını kaydet","","Ladir."+extension,extension);
#elif UNITY_STANDALONE_WIN
            var f=new FileName{title="LADİR dosyasını kaydet",filter="LADİR\0*."+extension+"\0\0",defaultExt=extension,flags=0x00080000|0x00000008|0x00000002};return GetSaveFileName(f)?f.file.Split('\0')[0]:null;
#else
            return null;
#endif
        }
    }
}
