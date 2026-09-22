using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class StadiumScaleSetup : MonoBehaviour
{
    [MenuItem("Tools/Enlarge Stadium & Add Boundaries")]
    public static void UpgradeStadium()
    {
        // 1. Group all environment objects into a single root to scale them together safely
        GameObject envRoot = GameObject.Find("StadiumEnvironment");
        if (envRoot == null)
        {
            envRoot = new GameObject("StadiumEnvironment");
            
            // Find all root objects
            List<GameObject> roots = new List<GameObject>();
            foreach (GameObject obj in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                // Exclude system objects
                if (obj.name == "Main Camera" || 
                    obj.name == "EventSystem" || 
                    obj.name == "NetworkLauncher" || 
                    obj.name == "StadiumEnvironment" ||
                    obj.GetComponent<Camera>() != null ||
                    obj.GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
                {
                    continue;
                }
                roots.Add(obj);
            }

            // Parent them
            foreach (GameObject root in roots)
            {
                root.transform.SetParent(envRoot.transform, true);
            }
        }

        // 2. Scale the entire environment by 1.75x to make the pitch and stadium significantly larger
        envRoot.transform.localScale = new Vector3(1.75f, 1.75f, 1.75f);

        // 3. Update the Goalkeepers' patrol limits so they cover the wider goals
        AIGoalkeeper[] keepers = FindObjectsByType<AIGoalkeeper>(FindObjectsSortMode.None);
        foreach (AIGoalkeeper keeper in keepers)
        {
            // Scale their original limits by the same 1.75 multiplier
            keeper.leftLimit = -1.8f * 1.75f;
            keeper.rightLimit = 1.8f * 1.75f;
            keeper.speed *= 1.25f; // Make them a bit faster to cover the larger goal
            EditorUtility.SetDirty(keeper);
        }

        // 2b. Rebuild goal posts with proper proportions and nets after scaling
        GameObject setup = GameObject.Find("GoalPostSetup");
        if (setup == null)
        {
            setup = new GameObject("GoalPostSetup");
        }
        GoalPostBuilder builder = setup.GetComponent<GoalPostBuilder>();
        if (builder == null)
        {
            builder = setup.AddComponent<GoalPostBuilder>();
        }
        builder.ReplaceGoalPosts();
        EditorUtility.SetDirty(setup);

        // 4. Create Invisible Boundaries to keep the ball in
        Transform boundsRoot = envRoot.transform.Find("PitchBoundaries");
        if (boundsRoot != null) DestroyImmediate(boundsRoot.gameObject);

        GameObject bounds = new GameObject("PitchBoundaries");
        bounds.transform.SetParent(envRoot.transform, false);

        // Boundary walls enclosing the real pitch. Field is X+-30, Z+-14.89
        // (goals on Z ends), fences at X+-30 and Z+-20. Add margin beyond the fences.
        float pitchWidth = 34f;  // X half-width of the enclosure
        float pitchLength = 22f; // Z half-length of the enclosure
        float wallHeight = 20f;
        float wallThickness = 5f;

        // Top Wall (+Z)
        CreateWall(bounds.transform, "Wall_Top", new Vector3(0, wallHeight/2f, pitchLength + wallThickness/2f), new Vector3(pitchWidth * 2 + wallThickness * 2, wallHeight, wallThickness));
        // Bottom Wall (-Z)
        CreateWall(bounds.transform, "Wall_Bottom", new Vector3(0, wallHeight/2f, -pitchLength - wallThickness/2f), new Vector3(pitchWidth * 2 + wallThickness * 2, wallHeight, wallThickness));
        // Right Wall (+X)
        CreateWall(bounds.transform, "Wall_Right", new Vector3(pitchWidth + wallThickness/2f, wallHeight/2f, 0), new Vector3(wallThickness, wallHeight, pitchLength * 2));
        // Left Wall (-X)
        CreateWall(bounds.transform, "Wall_Left", new Vector3(-pitchWidth - wallThickness/2f, wallHeight/2f, 0), new Vector3(wallThickness, wallHeight, pitchLength * 2));

        Debug.Log("Stadium Enlarged to 1.75x and Invisible Boundaries added!");
    }

    private static void CreateWall(Transform parent, string name, Vector3 pos, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = pos;
        
        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
        
        // No MeshRenderer, so it stays perfectly invisible
    }
}
