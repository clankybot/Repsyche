using CustomPhysics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the custom physics system (Assets/Scripts/CustomPhysics/*) into the active
/// scene: ensures a CustomPhysicsWorld exists, gives the Player the ability to push
/// custom props around, and spawns a small stack of demo crates near the Player's
/// current position so the system is immediately visible and testable rather than
/// sitting as unused scripts - Play mode should show a stack of boxes settling and
/// (if you walk into it) toppling/scattering under the custom solver, not PhysX.
///
/// Player movement and terrain collision are untouched - this only adds new,
/// independent GameObjects and one small hook (PlayerPushesCustomProps) on Player.
/// </summary>
public static class SetupCustomPhysicsDemo
{
    private const string WorldName = "CustomPhysicsWorld";
    private const string DemoStackName = "CustomPhysicsDemoStack";

    [MenuItem("Repsyche/Setup Custom Physics Demo")]
    public static void Run()
    {
        var world = EnsurePhysicsWorld();
        var player = GameObject.Find("Player");

        string pushStatus = "No 'Player' object found - skipped push hook.";
        if (player != null)
        {
            if (player.GetComponent<PlayerPushesCustomProps>() == null)
            {
                Undo.AddComponent<PlayerPushesCustomProps>(player);
                pushStatus = "Added PlayerPushesCustomProps to Player.";
            }
            else
            {
                pushStatus = "PlayerPushesCustomProps already present.";
            }
        }

        string stackStatus = SpawnDemoStack(player);

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Debug.Log("[SetupCustomPhysicsDemo] CustomPhysicsWorld ready. " + pushStatus + " " + stackStatus + " Scene saved.");
    }

    private static CustomPhysicsWorld EnsurePhysicsWorld()
    {
        var existing = Object.FindFirstObjectByType<CustomPhysicsWorld>();
        if (existing != null)
        {
            return existing;
        }
        var go = new GameObject(WorldName);
        Undo.RegisterCreatedObjectUndo(go, "Create CustomPhysicsWorld");
        return Undo.AddComponent<CustomPhysicsWorld>(go);
    }

    private static string SpawnDemoStack(GameObject player)
    {
        var existingStack = GameObject.Find(DemoStackName);
        if (existingStack != null)
        {
            Undo.DestroyObjectImmediate(existingStack);
        }

        Vector3 basePos = player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero;

        // Drop the stack from slightly above wherever it's placed rather than assuming
        // a specific ground height - this dune terrain varies by dozens of metres, and
        // the custom solver's own environment contact will settle it onto whatever is
        // actually below, exactly like a real physics engine would.
        basePos.y += 6f;

        var root = new GameObject(DemoStackName);
        Undo.RegisterCreatedObjectUndo(root, "Spawn Custom Physics Demo Stack");

        for (int i = 0; i < 4; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "Spawn Custom Physics Demo Stack");
            cube.name = "DemoCrate_" + i;
            cube.transform.SetParent(root.transform, false);
            cube.transform.position = basePos + new Vector3(Random.Range(-0.05f, 0.05f), i * 1.05f, Random.Range(-0.05f, 0.05f));
            cube.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 15f), 0f);

            // CreatePrimitive adds its own BoxCollider - remove it so it doesn't get
            // simulated by PhysX; CustomCollider adds its own Rigidbody-less one.
            var defaultCollider = cube.GetComponent<BoxCollider>();
            if (defaultCollider != null)
            {
                Object.DestroyImmediate(defaultCollider);
            }

            var col = cube.AddComponent<CustomCollider>();
            col.shape = CustomColliderShape.Box;
            col.boxSize = Vector3.one;

            var body = cube.AddComponent<CustomRigidbody>();
            body.mass = 1f;
            body.restitution = 0.1f;
            body.friction = 0.7f;
        }

        return "Spawned a 4-crate demo stack near " + (player != null ? "the Player" : "the origin") + ".";
    }
}
