using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ladir.Editor
{
    // Optional local test handoff for an already-open editor. It never discards
    // a dirty user scene, changes Play mode without a request file, or runs twice.
    [InitializeOnLoad]public static class PlanetPortEditorQA
    {
        const string Request="UserSettings/LadirPlanetEditorQA.request";
        static PlanetPortEditorQA(){EditorApplication.delayCall+=Run;}
        static void Run()
        {
            if(!File.Exists(Request)||Application.isBatchMode)return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=Run;return;}
            string folder=File.ReadAllText(Request).Trim();Directory.CreateDirectory(folder);
            if(EditorApplication.isPlaying){File.WriteAllText(Path.Combine(folder,"editor-blocked.txt"),"Editor is already playing; preserved the active session.");return;}
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){File.WriteAllText(Path.Combine(folder,"editor-blocked.txt"),"An open scene has unsaved changes; it was not replaced.");return;}
            File.Delete(Request);
            try{PlanetSetup.Create();File.WriteAllText("UserSettings/LadirPlanetSmoke.request",folder);var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();EditorApplication.isPlaying=true;}
            catch(Exception e){File.WriteAllText(Path.Combine(folder,"editor-setup-error.txt"),e.ToString());Debug.LogException(e);}
        }
    }
}
