using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot scene surgery: strips the old Backrooms maze out of the active scene and
/// replaces it with the procedurally generated Landscape of Thorns spike field
/// (Tools/Blender/generate_landscape_of_thorns.py -> Assets/Models/LandscapeOfThorns.fbx),
/// wiring up MeshColliders and the vertex-color material so the result is immediately
/// playtestable.
///
/// Hand-editing the .unity YAML directly for this would risk corrupting fileID
/// cross-references across a 30k+ line scene; going through Unity's own object model
/// instead guarantees a structurally valid result. Every destructive step goes through
/// the Undo system, so Ctrl+Z immediately after running restores the maze if needed.
///
/// Does NOT rebake NavMesh - CognitiveDistortionCluster's NavMeshAgent was pathing
/// against the old maze's baked NavMesh, which no longer matches this terrain. Re-bake
/// (Window > AI > Navigation > Bake) if you want the enemy chasing correctly here.
///
/// Safe to run more than once (e.g. after regenerating a bigger/different FBX in
/// Blender): it removes any previously inserted "LandscapeOfThorns" instance before
/// inserting the current one, rather than duplicating it.
/// </summary>
public static class ReplaceMapWithLandscapeOfThorns
{
    private const string FbxPath = "Assets/Models/LandscapeOfThorns.fbx";
    private const string MaterialPath = "Assets/Materials/M_LandscapeOfThorns.mat";
    private const string InsertedRootName = "LandscapeOfThorns";
    private const string GroundObjectName = "DesertGround";
    private const float PlayerGroundClearance = 0.02f;
    private static readonly string[] OldMapRootNames = { "Backrooms", "Ground" };

    [MenuItem("Repsyche/Replace Map With Landscape Of Thorns")]
    public static void Run()
    {
        int removed = 0;
        foreach (var name in OldMapRootNames)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                continue;
            }
            Undo.DestroyObjectImmediate(go);
            removed++;
        }

        // Re-running this tool (e.g. after regenerating the FBX with different scale)
        // should replace the previous insertion, not duplicate it.
        var previous = GameObject.Find(InsertedRootName);
        if (previous != null)
        {
            Undo.DestroyObjectImmediate(previous);
        }

        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError(
                "[ReplaceMapWithLandscapeOfThorns] Could not load '" + FbxPath + "'. " +
                "Run generate_landscape_of_thorns.py in Blender first, then Assets > Refresh in Unity.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
        Undo.RegisterCreatedObjectUndo(instance, "Insert Landscape of Thorns");
        instance.name = InsertedRootName;
        instance.transform.position = Vector3.zero;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogWarning("[ReplaceMapWithLandscapeOfThorns] Could not load '" + MaterialPath +
                              "' - meshes will keep whatever material the FBX import assigned.");
        }

        int colliderCount = 0;
        var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var meshRenderer in renderers)
        {
            if (material != null)
            {
                meshRenderer.sharedMaterials = new[] { material };
            }

            var go = meshRenderer.gameObject;
            if (go.GetComponent<MeshCollider>() != null)
            {
                continue;
            }
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }
            var collider = Undo.AddComponent<MeshCollider>(go);
            // Explicit rather than relying on defaults: this collider must follow the
            // actual dune/spike vertex geometry, not an axis-aligned bounding box (a
            // BoxCollider fallback was tried here previously and made an invisible flat
            // floor at the top of the mesh bounds instead of the real terrain surface).
            collider.enabled = true;
            collider.convex = false;
            collider.sharedMesh = meshFilter.sharedMesh;
            colliderCount++;
        }

        LogGroundDiagnostics(instance);
        EnsureSafetyFloor(instance);

        string playerStatus = RepositionPlayerAboveSand(instance);

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Debug.Log(
            "[ReplaceMapWithLandscapeOfThorns] Removed " + removed + " old map root(s); inserted " +
            "LandscapeOfThorns (" + renderers.Length + " meshes, " + colliderCount +
            " MeshColliders added); " + playerStatus + " Scene saved. Press Play to test.");
    }

    /// <summary>
    /// Diagnostic-only: logs exactly where Unity thinks the ground plane is (world
    /// bounds, local transform, collider presence/type) so a raycast miss against it
    /// can be root-caused directly instead of inferred from indirect hit lists.
    /// </summary>
    private static void LogGroundDiagnostics(GameObject instance)
    {
        var groundTransform = instance.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.gameObject.name == GroundObjectName);
        if (groundTransform == null)
        {
            Debug.LogError("[ReplaceMapWithLandscapeOfThorns] DIAGNOSTIC: no child named '" +
                            GroundObjectName + "' exists under the instantiated FBX at all.");
            return;
        }

        var go = groundTransform.gameObject;
        var renderer = go.GetComponent<MeshRenderer>();
        var collider = go.GetComponent<MeshCollider>();
        var meshFilter = go.GetComponent<MeshFilter>();

        string bounds = renderer != null ? renderer.bounds.ToString() : "NO MeshRenderer";
        string colliderInfo = collider == null
            ? "NO MeshCollider"
            : "MeshCollider enabled=" + collider.enabled + " convex=" + collider.convex +
              " isTrigger=" + collider.isTrigger + " sharedMesh=" +
              (collider.sharedMesh != null ? collider.sharedMesh.name + " (" + collider.sharedMesh.vertexCount + " verts)" : "NULL");
        string meshInfo = meshFilter != null && meshFilter.sharedMesh != null
            ? meshFilter.sharedMesh.bounds.ToString()
            : "NO MeshFilter/sharedMesh";

        Debug.Log("[ReplaceMapWithLandscapeOfThorns] DIAGNOSTIC DesertGround: " +
                  "active=" + go.activeInHierarchy +
                  " localPos=" + groundTransform.localPosition +
                  " worldPos=" + groundTransform.position +
                  " lossyScale=" + groundTransform.lossyScale +
                  " rendererWorldBounds=" + bounds +
                  " localMeshBounds=" + meshInfo +
                  " collider=[" + colliderInfo + "]");
    }

    /// <summary>
    /// A large, simple BoxCollider placed far below the entire desert so the Player can
    /// never fall indefinitely, no matter what turns out to be wrong with a specific
    /// terrain MeshCollider. This is a safety net, not the intended resting surface -
    /// landing on it means the real ground detection failed and needs further
    /// investigation, not that this is "working as intended."
    /// </summary>
    private static void EnsureSafetyFloor(GameObject instance)
    {
        var existing = instance.transform.Find("SafetyFloor");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        var floor = new GameObject("SafetyFloor");
        Undo.RegisterCreatedObjectUndo(floor, "Add Safety Floor");
        floor.transform.SetParent(instance.transform, false);
        floor.transform.position = new Vector3(0f, -200f, 0f);
        var box = Undo.AddComponent<BoxCollider>(floor);
        box.size = new Vector3(4000f, 10f, 4000f);
    }

    /// <summary>
    /// The dune terrain's height varies heavily by X/Z now, so the Player's old fixed
    /// spawn Y (tuned for the old flat Ground at Y=0) can leave the capsule buried in a
    /// dune or floating far above it. Raycasts straight down at the Player's existing
    /// X/Z, prefers hitting the sand itself (DesertGround) over a nearby spike so the
    /// player doesn't spawn standing on a thorn, and lifts the CharacterController so
    /// its capsule rests fully above the sand with a small clearance gap.
    ///
    /// Only considers colliders that are actually part of the inserted terrain
    /// (children of `instance`) - the Player's own CharacterController and other scene
    /// objects like the enemy were previously polluting this raycast (visible in the
    /// diagnostic log as spurious "Player"/"CognitiveDistortionCluster" hits), which is
    /// why earlier attempts produced unstable, meaningless heights.
    /// </summary>
    private static string RepositionPlayerAboveSand(GameObject instance)
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            return "No 'Player' object found to reposition.";
        }

        Physics.SyncTransforms();

        const float castHeight = 1000f;
        Vector3 origin = player.transform.position + Vector3.up * castHeight;
        var allHits = Physics.RaycastAll(origin, Vector3.down, castHeight * 2f);
        var hits = System.Array.FindAll(allHits, h => h.collider.transform.IsChildOf(instance.transform));

        var hitLog = new System.Text.StringBuilder();
        foreach (var hit in allHits)
        {
            hitLog.Append("[" + hit.collider.gameObject.name + " y=" + hit.point.y.ToString("F2") + "] ");
        }
        Debug.Log("[ReplaceMapWithLandscapeOfThorns] Player raycast at X=" + player.transform.position.x +
                   " Z=" + player.transform.position.z + " found " + allHits.Length +
                   " total hit(s), " + hits.Length + " belong to the terrain: " + hitLog);

        if (hits.Length == 0)
        {
            return "No terrain collider hit at this X/Z (only the SafetyFloor will catch the player) - position left unchanged.";
        }

        float? sandY = null;
        float highestTerrainY = float.NegativeInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject.name == GroundObjectName &&
                (sandY == null || hit.point.y > sandY.Value))
            {
                sandY = hit.point.y;
            }
            if (hit.point.y > highestTerrainY)
            {
                highestTerrainY = hit.point.y;
            }
        }
        float groundY = sandY ?? highestTerrainY;

        var controller = player.GetComponent<CharacterController>();
        float halfHeight = controller != null ? controller.height * 0.5f : 1f;
        float centerY = controller != null ? controller.center.y : 0f;

        var pos = player.transform.position;
        Undo.RecordObject(player.transform, "Reposition Player Above Sand");
        pos.y = groundY + PlayerGroundClearance + halfHeight - centerY;
        player.transform.position = pos;

        string surface = sandY != null
            ? "DesertGround"
            : "a nearby spike (no DesertGround hit at this X/Z - only " + hits.Length + " other terrain hit(s))";
        return "Player moved to Y=" + pos.y.ToString("F2") + " (resting on " + surface + ").";
    }
}
