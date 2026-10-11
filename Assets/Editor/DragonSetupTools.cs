using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor helpers for rigging the dragon so the pieces that matter follow its
// animation, which moves the skeleton but leaves the root object on the floor:
//   - the muzzle, which shots and the charge-up effect come out of, and
//   - the hit colliders, which spells hit.
// Run from the Tools > Dragon menu in edit mode, with the scene open.
public static class DragonSetupTools
{
    const string Menu = "Tools/Dragon/";
    const string HitboxPrefix = "Hitbox_";
    const float MinThickness = 0.1f; // of the box's longest side, so no side is paper thin

    [MenuItem(Menu + "Move Muzzle To Head Bone")]
    static void MoveMuzzleToHead()
    {
        DragonBoss dragon = FindDragon();
        if (dragon == null) return;
        if (dragon.muzzle == null)
        {
            Debug.LogWarning("[DragonSetup] The dragon has no Muzzle assigned.");
            return;
        }

        Transform jaw = FindChildByName(dragon.transform, "jaw upper");
        if (jaw == null || jaw.parent == null)
        {
            Debug.LogWarning("[DragonSetup] Couldn't find the 'jaw upper' bone to locate the head.");
            return;
        }

        // Keeps its world position, so it stays where the mouth is now.
        Undo.SetTransformParent(dragon.muzzle, jaw.parent, "Move dragon muzzle to head bone");
        EditorSceneManager.MarkSceneDirty(dragon.gameObject.scene);
        Debug.Log("[DragonSetup] Muzzle is now a child of " + jaw.parent.name + ".");
    }

    [MenuItem(Menu + "Add Hitbox To Selected Bones (only this bone)")]
    static void AddHitboxThisBone() => AddHitboxes(includeChildBones: false);

    [MenuItem(Menu + "Add Hitbox To Selected Bones (with child bones)")]
    static void AddHitboxWithChildren() => AddHitboxes(includeChildBones: true);

    [MenuItem(Menu + "Add Hitbox To Selected Bones (only this bone)", true)]
    static bool CanAddHitboxThisBone() => CanAddHitboxes();

    [MenuItem(Menu + "Add Hitbox To Selected Bones (with child bones)", true)]
    static bool CanAddHitboxWithChildren() => CanAddHitboxes();

    static bool CanAddHitboxes() => !Application.isPlaying && Selection.transforms.Length > 0;

    [MenuItem(Menu + "Remove All Hitboxes")]
    static void RemoveHitboxes()
    {
        DragonBoss dragon = FindDragon();
        if (dragon == null) return;

        var found = new List<GameObject>();
        foreach (Transform t in dragon.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(HitboxPrefix)) found.Add(t.gameObject);

        foreach (GameObject hitbox in found)
            Undo.DestroyObjectImmediate(hitbox);

        EditorSceneManager.MarkSceneDirty(dragon.gameObject.scene);
        Debug.Log("[DragonSetup] Removed " + found.Count + " hitboxes.");
    }

    // A box fitted around the part of the mesh that the selected bone moves
    // (just this bone, or this bone and everything below it), as the mesh
    // looks in the pose it's in now.
    static void AddHitboxes(bool includeChildBones)
    {
        DragonBoss dragon = FindDragon();
        if (dragon == null) return;

        int added = 0;
        foreach (Transform bone in Selection.transforms)
        {
            if (bone == dragon.transform || !bone.IsChildOf(dragon.transform))
            {
                Debug.LogWarning("[DragonSetup] " + bone.name + " isn't a bone of the dragon -- skipped.");
                continue;
            }
            if (!TryFit(dragon, bone, includeChildBones, out Bounds fit))
            {
                Debug.LogWarning("[DragonSetup] No mesh is skinned to " + bone.name + " -- skipped.");
                continue;
            }

            Transform old = bone.Find(HitboxPrefix + bone.name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject(HitboxPrefix + bone.name);
            Undo.RegisterCreatedObjectUndo(go, "Add dragon hitbox");
            go.transform.SetParent(bone, false);

            var box = Undo.AddComponent<BoxCollider>(go);
            float longest = Mathf.Max(fit.size.x, fit.size.y, fit.size.z);
            box.center = fit.center;
            box.size = Vector3.Max(fit.size, Vector3.one * longest * MinThickness);
            added++;
        }

        if (added > 0) EditorSceneManager.MarkSceneDirty(dragon.gameObject.scene);
        Debug.Log("[DragonSetup] Added " + added + " hitboxes. Select one to see its box in the Scene view.");
    }

    // Bounds, in the bone's own space, of the vertices that this bone moves
    // the most.
    static bool TryFit(DragonBoss dragon, Transform bone, bool includeChildBones, out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        foreach (SkinnedMeshRenderer renderer in dragon.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = renderer.bones;
            if (weights.Length != vertices.Length) continue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Transform owner = bones[weights[i].boneIndex0];
                if (owner == null) continue;
                bool belongs = includeChildBones ? owner.IsChildOf(bone) : owner == bone;
                if (!belongs) continue;

                Vector3 inBoneSpace = bone.InverseTransformPoint(Skin(weights[i], vertices[i], bones, bindposes));
                if (!any) { bounds = new Bounds(inBoneSpace, Vector3.zero); any = true; }
                else bounds.Encapsulate(inBoneSpace);
            }
        }
        return any;
    }

    // Where the vertex is in the world right now: each bone that moves it
    // carries it along by its share.
    static Vector3 Skin(BoneWeight w, Vector3 vertex, Transform[] bones, Matrix4x4[] bindposes)
    {
        return Influence(w.boneIndex0, w.weight0, vertex, bones, bindposes)
             + Influence(w.boneIndex1, w.weight1, vertex, bones, bindposes)
             + Influence(w.boneIndex2, w.weight2, vertex, bones, bindposes)
             + Influence(w.boneIndex3, w.weight3, vertex, bones, bindposes);
    }

    static Vector3 Influence(int index, float weight, Vector3 vertex, Transform[] bones, Matrix4x4[] bindposes)
    {
        if (weight <= 0f || bones[index] == null) return Vector3.zero;
        return weight * (bones[index].localToWorldMatrix * bindposes[index]).MultiplyPoint3x4(vertex);
    }

    static DragonBoss FindDragon()
    {
        DragonBoss dragon = Object.FindFirstObjectByType<DragonBoss>();
        if (dragon == null) Debug.LogWarning("[DragonSetup] No DragonBoss in the open scene.");
        return dragon;
    }

    static Transform FindChildByName(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
