using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode validation helper: opens every game scene and reports
/// what is (or is not) present, so scene YAML edits can be verified.
/// </summary>
public static class SceneValidator
{
    public static void ValidateScenes()
    {
        string[] scenes = {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/HowToPlay.unity",
            "Assets/Scenes/TrainingScene.unity",
            "Assets/Scenes/SampleScene.unity"
        };

        foreach (string s in scenes)
        {
            Debug.Log($"[SceneValidator] ---- Opening {s} ----");
            try
            {
                var scene = EditorSceneManager.OpenScene(s, OpenSceneMode.Single);
                Debug.Log($"[SceneValidator] {s}: OK  rootObjects={scene.rootCount} path={scene.path}");

                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    Debug.Log($"[SceneValidator]   root: '{go.name}'  components={go.GetComponents<Component>().Length}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneValidator] {s} FAILED TO OPEN: {e.Message}");
            }
        }

        Debug.Log("[SceneValidator] validation complete.");
    }
}