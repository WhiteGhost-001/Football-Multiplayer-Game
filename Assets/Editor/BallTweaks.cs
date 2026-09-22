using UnityEngine;
using UnityEditor;

public class BallTweaks : MonoBehaviour
{
    [MenuItem("Tools/Make Ball Bigger")]
    public static void EnlargeBall()
    {
        string prefabPath = "Assets/Resources/SoccerBall_01.prefab";
        GameObject ballPrefab = PrefabUtility.LoadPrefabContents(prefabPath);
        
        if (ballPrefab != null)
        {
            // The default scale is probably 1,1,1. Let's make it 1.8x larger.
            ballPrefab.transform.localScale = new Vector3(1.8f, 1.8f, 1.8f);
            
            PrefabUtility.SaveAsPrefabAsset(ballPrefab, prefabPath);
            PrefabUtility.UnloadPrefabContents(ballPrefab);
            Debug.Log("SoccerBall_01 is now 1.8x bigger!");
        }
        else
        {
            Debug.LogError("Could not find SoccerBall_01 prefab.");
        }
    }
}
