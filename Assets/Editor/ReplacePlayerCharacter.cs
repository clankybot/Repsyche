using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Replaces the old PlayerBody (PlayerBody.fbx - a single 48-vertex static mesh with no
/// bones, confirmed by reimporting it in Blender) with the new procedurally generated
/// humanoid (Tools/Blender/generate_player_character.py -> Assets/Models/PlayerCharacter.fbx),
/// built from separate limb objects so it supports a real per-limb walk cycle via
/// HumanoidWalkAnimator instead of PlayerBodyBob's whole-body bob.
///
/// Uses Shader.Find("Custom/VertexColorClay") rather than a hand-authored .mat file with
/// a manually-typed shader GUID, for the same reason SetupPlayerFeelAndSkybox does for
/// the skybox material: resolving by name at edit time is robust regardless of exactly
/// which GUID Unity assigned when it imported the shader.
///
/// Idempotent: destroys any previous PlayerCharacter.fbx instance before inserting a
/// fresh one, so re-running after regenerating the model in Blender is safe.
/// </summary>
public static class ReplacePlayerCharacter
{
    private const string FbxPath = "Assets/Models/PlayerCharacter.fbx";
    private const string MaterialPath = "Assets/Materials/M_PlayerCharacter.mat";
    private const string RootName = "PlayerBody";

    [MenuItem("Repsyche/Replace Player Character")]
    public static void Run()
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("[ReplacePlayerCharacter] No 'Player' object found in the active scene.");
            return;
        }

        // Remove whatever is currently occupying the PlayerBody slot - either the old
        // static mesh or a previous run of this same tool.
        Transform oldBody = player.transform.Find(RootName);
        if (oldBody != null)
        {
            Undo.DestroyObjectImmediate(oldBody.gameObject);
        }

        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError("[ReplacePlayerCharacter] Could not load '" + FbxPath +
                            "'. Run generate_player_character.py in Blender first, then Assets > Refresh.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
        Undo.RegisterCreatedObjectUndo(instance, "Insert Player Character");
        instance.name = RootName;
        instance.transform.SetParent(player.transform, false);
        // Matches the old PlayerBody's anchor convention: CharacterController center=0,
        // height=2 -> capsule bottom at local Y=-1, and this model's feet sit at its own
        // local origin, so this places the feet exactly on the capsule floor.
        instance.transform.localPosition = new Vector3(0f, -1f, 0f);
        instance.transform.localRotation = Quaternion.identity;

        var shader = Shader.Find("Custom/VertexColorClay");
        if (shader == null)
        {
            Debug.LogWarning("[ReplacePlayerCharacter] Shader 'Custom/VertexColorClay' not found - " +
                              "meshes will keep whatever material the FBX import assigned.");
        }
        else
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_PlayerCharacter" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials = new[] { material };
            }
        }

        var animator = Undo.AddComponent<HumanoidWalkAnimator>(instance);
        animator.controller = player.GetComponent<PlayerController>();
        animator.legL = FindChild(instance.transform, "LegL");
        animator.legR = FindChild(instance.transform, "LegR");
        animator.armL = FindChild(instance.transform, "ArmL");
        animator.armR = FindChild(instance.transform, "ArmR");

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Debug.Log("[ReplacePlayerCharacter] Replaced PlayerBody with the new humanoid " +
                  "(legL=" + (animator.legL != null) + " legR=" + (animator.legR != null) +
                  " armL=" + (animator.armL != null) + " armR=" + (animator.armR != null) +
                  "). Scene saved. Press Play to test.");
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject.name == name)
            {
                return t;
            }
        }
        return null;
    }
}
