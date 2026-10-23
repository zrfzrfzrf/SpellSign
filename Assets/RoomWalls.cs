using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

// Whether the player's spells pass through the scanned room -- walls, ceiling,
// floor and furniture -- so they can reach a dragon that flies above a low
// ceiling. Chosen on the start menu or the sandbox panel, and remembered
// across restarts (PlayerPrefs), like the language. Off by default: the room
// blocks spells.
//
// Only the player's spells change. The room still places the dragon on the
// floor, and still stops the dragon's shots.
public static class RoomWalls
{
    const string PrefsKey = "SpellSign.SpellsThroughWalls";

    // Tells the menus to light the chosen button.
    public static event Action Changed;

    static bool loaded;
    static bool throughWalls;
    static readonly List<Collider> roomColliders = new List<Collider>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        loaded = false;
        Changed = null;
    }

    public static bool SpellsPassThrough
    {
        get
        {
            if (!loaded)
            {
                throughWalls = PlayerPrefs.GetInt(PrefsKey, 0) == 1;
                loaded = true;
            }
            return throughWalls;
        }
        set
        {
            if (value == SpellsPassThrough) return;
            throughWalls = value;
            PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[RoomWalls] spells pass through the room: {value}");
            Changed?.Invoke();
        }
    }

    // True when a spell should fly through this collider: the setting is on and
    // the collider is part of the scanned room (it sits under an MRUK anchor).
    public static bool Ignores(Collider collider) => SpellsPassThrough && IsRoom(collider);

    // True for a collider of the scanned room.
    public static bool IsRoom(Collider collider) =>
        collider != null && collider.GetComponentInParent<MRUKAnchor>() != null;

    // Every collider of the scanned room, for a projectile to ignore at launch.
    public static List<Collider> RoomColliders()
    {
        roomColliders.Clear();
        foreach (MRUKAnchor anchor in UnityEngine.Object.FindObjectsByType<MRUKAnchor>())
            roomColliders.AddRange(anchor.GetComponentsInChildren<Collider>());
        return roomColliders;
    }
}
