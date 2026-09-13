using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Wires the procedurally generated worm boss (Tools/Blender/generate_worm_boss.py ->
/// Assets/Models/WormBoss.fbx) into the active scene: builds its two materials on this
/// project's existing Custom/PSXAesthetic shader (the same shader the Brutalist assets
/// use - triplanar-projected, no UVs needed, matching how this model was built), assigns
/// them by object name (flesh vs obsidian), and adds NavMeshAgent + WormBossAI + a
/// trigger damage collider, then places it a short distance from the Player for testing.
///
/// Idempotent like the other Repsyche scene tools: destroys any previous WormBoss
/// instance before inserting the current one.
/// </summary>
public static class SetupWormBoss
{
    private const string FbxPath = "Assets/Models/WormBoss.fbx";
    private const string FleshTexturePath = "Assets/Textures/WormBoss_Flesh.png";
    private const string ObsidianTexturePath = "Assets/Textures/WormBoss_Obsidian.png";
    private const string FleshMaterialPath = "Assets/Materials/M_WormBoss_Flesh.mat";
    private const string ObsidianMaterialPath = "Assets/Materials/M_WormBoss_Obsidian.mat";
    private const string RootName = "WormBoss";

    [MenuItem("Repsyche/Setup Worm Boss")]
    public static void Run()
    {
        var previous = GameObject.Find(RootName);
        if (previous != null)
        {
            Undo.DestroyObjectImmediate(previous);
        }

        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError("[SetupWormBoss] Could not load '" + FbxPath +
                            "'. Run generate_worm_boss.py in Blender first, then Assets > Refresh.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
        Undo.RegisterCreatedObjectUndo(instance, "Insert Worm Boss");
        instance.name = RootName;

        var player = GameObject.Find("Player");
        instance.transform.position = player != null
            ? player.transform.position + player.transform.forward * 12f
            : Vector3.zero;

        int fleshCount = 0;
        int obsidianCount = 0;
        var fleshMat = BuildPsxMaterial(FleshMaterialPath, FleshTexturePath, saturation: 0.7f, contrast: 1.6f, ambient: new Color(0.10f, 0.05f, 0.05f));
        var obsidianMat = BuildPsxMaterial(ObsidianMaterialPath, ObsidianTexturePath, saturation: 0.5f, contrast: 2.2f, ambient: new Color(0.03f, 0.03f, 0.04f));

        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            bool isObsidian = renderer.gameObject.name.Contains("Spike");
            if (isObsidian && obsidianMat != null)
            {
                renderer.sharedMaterials = new[] { obsidianMat };
                obsidianCount++;
            }
            else if (fleshMat != null)
            {
                renderer.sharedMaterials = new[] { fleshMat };
                fleshCount++;
            }
        }

        // A single simple capsule collider covering the body's approximate footprint for
        // NavMeshAgent movement + player contact damage - the actual visual meshes have
        // no colliders of their own (this is an enemy, not level geometry).
        var capsule = instance.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = instance.AddComponent<CapsuleCollider>();
        }
        capsule.isTrigger = true;
        // The body's length runs along Blender's X axis, and this FBX export's axis
        // settings (up=Y, forward=-Z) only remap Y/Z - X passes through unchanged, so
        // the length axis is still X after import. direction 0 = X-axis.
        capsule.direction = 0;
        capsule.radius = 2.2f;
        capsule.height = 16f;
        capsule.center = new Vector3(8f, 0f, 0f);

        var agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = instance.AddComponent<NavMeshAgent>();
        }
        agent.radius = 2.5f;
        agent.height = 3.5f;
        agent.speed = 5.5f;
        agent.acceleration = 12f;

        var ai = instance.GetComponent<WormBossAI>();
        if (ai == null)
        {
            ai = instance.AddComponent<WormBossAI>();
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Debug.Log("[SetupWormBoss] Placed WormBoss (" + fleshCount + " flesh renderers, " +
                  obsidianCount + " obsidian renderers). Remember: WormBossAI needs a baked " +
                  "NavMesh under the boss's surface position to chase - Window > AI > Navigation > Bake " +
                  "if it hasn't been re-baked since the terrain changed. Scene saved.");
    }

    private static Material BuildPsxMaterial(string path, string texturePath, float saturation, float contrast, Color ambient)
    {
        var shader = Shader.Find("Custom/PSXAesthetic");
        if (shader == null)
        {
            Debug.LogWarning("[SetupWormBoss] Shader 'Custom/PSXAesthetic' not found.");
            return null;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogWarning("[SetupWormBoss] Could not load texture '" + texturePath + "'.");
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        if (texture != null)
        {
            material.SetTexture("_MainTex", texture);
        }
        material.SetFloat("_Saturation", saturation);
        material.SetFloat("_Contrast", contrast);
        material.SetFloat("_Brightness", 1.1f);
        material.SetFloat("_SnapResolution", 90f);
        material.SetFloat("_TexScale", 0.4f);
        material.SetColor("_AmbientColor", ambient);
        material.SetColor("_Color", Color.white);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }
}
