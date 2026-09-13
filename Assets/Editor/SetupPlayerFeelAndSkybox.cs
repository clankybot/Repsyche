using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sets up two independent, non-destructive additions to the active scene:
///  1. Attaches ThirdPersonCameraCollision to Main Camera so its fixed offset doesn't
///     stay embedded in a dune slope on this terrain's dramatic relief.
///  2. Builds a Skybox/Panoramic material from the generated yellow-sky/red-clouds
///     texture (Tools/Blender/generate_skybox.py -> Assets/Textures/Skybox_YellowRedClouds.png)
///     and assigns it as the scene's skybox.
///
/// Walk animation is handled separately by ReplacePlayerCharacter, since it replaces the
/// whole PlayerBody model (see that tool's doc comment) - PlayerBodyBob's whole-body bob
/// was a stopgap for the old boneless static-mesh body and is superseded now.
///
/// Neither of these two is destructive (unlike ReplaceMapWithLandscapeOfThorns, which
/// stays manual-only), so this runs automatically once per Editor session via
/// [InitializeOnLoad] as well as being available as an explicit menu command - no need
/// to remember to click it after every script/asset change.
///
/// Uses Shader.Find("Skybox/Panoramic") rather than a hand-authored .mat file with a
/// guessed built-in-shader GUID - built-in shader GUIDs are easy to get subtly wrong and
/// Shader.Find resolves correctly regardless of exact Unity/pipeline version.
/// </summary>
[InitializeOnLoad]
public static class SetupPlayerFeelAndSkybox
{
    private const string SkyboxTexturePath = "Assets/Textures/Skybox_YellowRedClouds.png";
    private const string SkyboxMaterialPath = "Assets/Materials/M_Skybox_YellowRedClouds.mat";

    static SetupPlayerFeelAndSkybox()
    {
        EditorApplication.delayCall += () => RunInternal(force: false);
    }

    [MenuItem("Repsyche/Setup Camera Collision And Skybox")]
    public static void Run()
    {
        RunInternal(force: true);
    }

    private static void RunInternal(bool force)
    {
        var (cameraChanged, cameraStatus) = AddCameraCollision();
        var (skyboxChanged, skyboxStatus) = ApplySkybox();

        if (!force && !cameraChanged && !skyboxChanged)
        {
            return; // nothing new to do - don't spam a save/log on every domain reload
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log("[SetupPlayerFeelAndSkybox] " + cameraStatus + " " + skyboxStatus + " Scene saved.");
    }

    private static (bool, string) AddCameraCollision()
    {
        var cameraObj = GameObject.FindWithTag("MainCamera");
        if (cameraObj == null)
        {
            return (false, "No MainCamera-tagged object found - skipped camera collision.");
        }

        if (cameraObj.GetComponent<ThirdPersonCameraCollision>() != null)
        {
            return (false, "ThirdPersonCameraCollision already present.");
        }

        Undo.AddComponent<ThirdPersonCameraCollision>(cameraObj);
        return (true, "Added ThirdPersonCameraCollision to Main Camera.");
    }

    private static (bool, string) ApplySkybox()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyboxTexturePath);
        if (texture == null)
        {
            return (false, "Could not load '" + SkyboxTexturePath + "' - run generate_skybox.py in Blender first, then Assets > Refresh.");
        }

        var shader = Shader.Find("Skybox/Panoramic");
        if (shader == null)
        {
            return (false, "Shader 'Skybox/Panoramic' not found in this project/pipeline - skybox not applied.");
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
        bool isNew = material == null;
        if (isNew)
        {
            material = new Material(shader) { name = "M_Skybox_YellowRedClouds" };
            AssetDatabase.CreateAsset(material, SkyboxMaterialPath);
        }

        bool alreadyApplied = !isNew && RenderSettings.skybox == material && material.GetTexture("_MainTex") == texture;
        if (alreadyApplied)
        {
            return (false, "Skybox already applied.");
        }

        material.shader = shader;
        material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_Exposure"))
        {
            material.SetFloat("_Exposure", 1.0f);
        }
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        RenderSettings.skybox = material;
        DynamicGI.UpdateEnvironment();

        return (true, (isNew ? "Created " : "Updated ") + SkyboxMaterialPath + " and assigned it as the scene skybox.");
    }
}
