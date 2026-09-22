using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class ApplyRiggedPlayerSetup : MonoBehaviour
{
    [MenuItem("Tools/Setup NetworkPlayer Prefab")]
    public static void SetupPrefab()
    {
        string prefabPath = "Assets/Resources/NetworkPlayer.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

        // 1. Remove old Mesh components from the root
        MeshFilter mf = prefabRoot.GetComponent<MeshFilter>();
        if (mf != null) DestroyImmediate(mf);
        MeshRenderer mr = prefabRoot.GetComponent<MeshRenderer>();
        if (mr != null) DestroyImmediate(mr);

        // 2. Remove old placeholder child if it already exists
        Transform oldChild = prefabRoot.transform.Find("PlayerVisuals");
        if (oldChild != null) DestroyImmediate(oldChild.gameObject);
        
        // Also check for the old "PlaceholderModel" name
        Transform oldPlaceholder = prefabRoot.transform.Find("PlaceholderModel");
        if (oldPlaceholder != null) DestroyImmediate(oldPlaceholder.gameObject);

        // 3. Load the human player model from Floreswa
        string modelPath = "Assets/Texture/Floreswa/Models/male02_1.fbx";
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        
        GameObject childModel;
        if (modelAsset != null)
        {
            childModel = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            childModel.name = "PlayerVisuals";
            
            // Adjust scale if the model is too big/small
            childModel.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        }
        else
        {
            Debug.LogError("Could not find male02_1.fbx! Falling back to capsule.");
            childModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            childModel.name = "PlayerVisuals";
        }

        // Remove the collider from the child so it doesn't interfere with the root's CapsuleCollider
        Collider childCollider = childModel.GetComponent<Collider>();
        if (childCollider != null) DestroyImmediate(childCollider);

        childModel.transform.SetParent(prefabRoot.transform, false);
        // Offset so feet touch the ground (CapsuleCollider center is at 0, height is 2, so feet are at -1)
        childModel.transform.localPosition = new Vector3(0, -1f, 0);

        // 4. Ensure Animator exists and uses our Controller
        string controllerPath = "Assets/Resources/PlayerAnimatorController.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = rootStateMachine.AddState("Idle");
            AnimatorState runState = rootStateMachine.AddState("Run");
            rootStateMachine.defaultState = idleState;
            AnimatorStateTransition idleToRun = idleState.AddTransition(runState);
            idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            AnimatorStateTransition runToIdle = runState.AddTransition(idleState);
            runToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        }

        // 5. Add and assign Animator to the child model
        Animator anim = childModel.GetComponent<Animator>();
        if (anim == null) anim = childModel.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;

        // 6. Save the prefab perfectly back to resources
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        Debug.Log("NetworkPlayer Prefab updated with realistic Football Player model!");
    }
}
