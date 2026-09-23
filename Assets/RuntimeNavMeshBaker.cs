using UnityEngine;
using System.Collections;
using Unity.AI.Navigation;
using Meta.XR.MRUtilityKit;

// Bakes the NavMesh at RUNTIME, right after MRUK finishes loading the
// scanned room -- because RealWorldMesh's colliders don't exist until
// then, a normal Editor-time bake (Window > AI > Navigation > Bake)
// would have nothing real to work with.
//
// Setup:
//   1. If you don't have it yet: Window -> Package Manager -> search
//      "AI Navigation" -> Install. This gives you the NavMeshSurface
//      component used below.
//   2. Create an empty GameObject (name it "NavMeshBaker"), add a
//      NavMeshSurface component to it AND this script.
//   3. On the NavMeshSurface component: set "Collect Objects" to "All",
//      and "Include Layers" to whatever layer your RealWorldMesh
//      colliders are on (Default, per your setup).
//   4. Leave this script to do the rest -- it automatically bakes once
//      MRUK reports the room is loaded.
[RequireComponent(typeof(NavMeshSurface))]
public class RuntimeNavMeshBaker : MonoBehaviour
{
    private NavMeshSurface surface;

    void Awake()
    {
        surface = GetComponent<NavMeshSurface>();
    }

    void OnEnable()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    IEnumerator SubscribeWhenReady()
    {
        // MRUK's own Awake() might not have run yet when THIS object's
        // OnEnable() runs -- Unity doesn't guarantee GameObject init
        // order across different objects. Wait until the singleton
        // actually exists before subscribing.
        while (MRUK.Instance == null)
        {
            yield return null;
        }

        // Per Meta's docs: if MRUK already finished loading the scene
        // by the time we subscribe, this fires OnSceneLoaded immediately
        // -- so we're covered whether we subscribe early or late.
        MRUK.Instance.SceneLoadedEvent.AddListener(OnSceneLoaded);
        Debug.Log("[RuntimeNavMeshBaker] Subscribed to MRUK.SceneLoadedEvent.");
    }

    void OnDisable()
    {
        if (MRUK.Instance != null)
        {
            MRUK.Instance.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
        }
    }

    void OnSceneLoaded()
    {
        // Give EffectMesh time to finish spawning colliders for every
        // anchor in the room before we scan the scene for the bake --
        // a bedroom+bathroom worth of furniture is a lot of colliders,
        // and MRUK's SceneLoadedEvent can fire almost instantly if the
        // room was already scanned before, potentially ahead of
        // EffectMesh actually finishing. 1 second is generous on
        // purpose -- we'd rather wait a beat too long than bake empty.
        Invoke(nameof(BakeNow), 1.0f);
    }

    // Anything that needs to know "the NavMesh is ready now" (like
    // BossStandIn re-placing its agent) subscribes to this.
    public static event System.Action OnBaked;

    void BakeNow()
    {
        surface.BuildNavMesh();

        // Self-check: is there actually anything walkable near where we
        // know the room should be (world origin, roughly where the
        // player started)? This tells us definitively whether the bake
        // produced real data, without waiting on a second script's log.
        bool foundNearby = UnityEngine.AI.NavMesh.SamplePosition(
            Vector3.zero, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas);

        Debug.Log($"[RuntimeNavMeshBaker] NavMesh baked over real room. " +
                  $"Sanity check near origin: foundNearby={foundNearby}" +
                  (foundNearby ? $" at {hit.position}" : " -- BAKE LIKELY EMPTY."));

        OnBaked?.Invoke();
    }
}