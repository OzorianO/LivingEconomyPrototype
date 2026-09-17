using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.CodeEditor;

// One-time setup for this project; does not affect later scene editing.
internal static class LivingEconomyProjectSetup
{
    [InitializeOnLoadMethod]
    private static void ScheduleSetup()
    {
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        const string editorPath = @"D:\Development\VisualStudio\Common7\IDE\devenv.exe";
        const string scenePath = "Assets/_Project/Scenes/MainSimulation.unity";
        string setupKey = "LivingEconomy.Setup.0.1." + Application.dataPath;
        if (EditorPrefs.GetBool(setupKey, false)) return;
        if (!System.IO.File.Exists(editorPath) || !System.IO.File.Exists(scenePath)) return;

        CodeEditor.SetExternalScriptEditor(editorPath);
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path == "Assets/Scenes/SampleScene.unity" && !scene.isDirty)
            EditorSceneManager.OpenScene(scenePath);
        EditorPrefs.SetBool(setupKey, true);
        Debug.Log("LivingEconomy setup complete. Script editor: " + CodeEditor.CurrentEditorInstallation
            + "; active scene: " + EditorSceneManager.GetActiveScene().path);
    }
}
