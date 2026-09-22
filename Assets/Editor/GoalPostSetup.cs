using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility to add GoalPostBuilder to the scene and trigger goal post replacement.
/// Run from Tools menu after generating the stadium.
/// </summary>
public class GoalPostSetup : MonoBehaviour
{
    [MenuItem("Tools/Setup Goal Posts")]
    public static void Setup()
    {
        // Find or create a host object
        GameObject host = GameObject.Find("GoalPostSetup");
        if (host == null)
        {
            host = new GameObject("GoalPostSetup");
        }

        GoalPostBuilder builder = host.GetComponent<GoalPostBuilder>();
        if (builder == null)
        {
            builder = host.AddComponent<GoalPostBuilder>();
        }

        // Run immediately in editor
        builder.ReplaceGoalPosts();

        EditorUtility.SetDirty(host);
        Debug.Log("Goal posts set up with proper FIFA dimensions and nets!");
    }
}
