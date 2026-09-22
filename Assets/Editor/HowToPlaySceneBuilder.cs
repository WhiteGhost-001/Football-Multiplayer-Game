using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the How To Play scene from scratch (Tools &gt; Build HowToPlay Scene).
///
/// The scene intentionally contains NO baked UI canvases — the pause menu and
/// the entire tutorial are built from code at runtime (see HowToPlayBootstrap
/// and PauseMenu.forceProcedural). That removes the entire class of bugs where
/// a scene-baked PauseMenu_Canvas lingered visible and unresponsive at load.
///
/// The scene ships with: a Main Camera carrying HowToPlayBootstrap, an
/// EventSystem (so tutorial clicks work), and nothing else. The real stadium is
/// loaded additively from TrainingScene at runtime.
/// </summary>
public static class HowToPlaySceneBuilder
{
    private const string ScenePath = "Assets/Scenes/HowToPlay.unity";

    [MenuItem("Tools/Build HowToPlay Scene")]
    public static void BuildScene()
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // The stadium (loaded additively from TrainingScene) provides its own
        // lighting — drop the ambient directional light the default scene adds.
        Light light = Object.FindFirstObjectByType<Light>();
        if (light != null)
            Object.DestroyImmediate(light.gameObject);

        // Camera — the tutorial's follow camera. Position/rotation are
        // irrelevant: ThirdPersonCameraFollow snaps to the player at Start.
        Camera cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.gameObject.name = "Main Camera";
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.1f, 0.15f);
        cam.depth = 0;

        if (cam.GetComponent<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();

        // Attach the tutorial bootstrap.
        if (cam.GetComponent<HowToPlayBootstrap>() == null)
            cam.gameObject.AddComponent<HowToPlayBootstrap>();

        // A real EventSystem, so EnsureEventSystem() finds one and the tutorial
        // overlay can be clicked away immediately.
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("[HowToPlaySceneBuilder] Failed to save " + ScenePath);
            return;
        }

        AddToBuildSettings(ScenePath);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[HowToPlaySceneBuilder] HowToPlay scene built at " + ScenePath);
    }

    private static void AddToBuildSettings(string path)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        foreach (EditorBuildSettingsScene existing in scenes)
        {
            if (existing.path == path)
                return;
        }

        List<EditorBuildSettingsScene> updated = new List<EditorBuildSettingsScene>(scenes)
        {
            new EditorBuildSettingsScene(path, true)
        };
        EditorBuildSettings.scenes = updated.ToArray();
    }
}