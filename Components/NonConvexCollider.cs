using JetBrains.Annotations;
using UnityEngine;

public abstract class NonConvexCollider<T, U> : MonoBehaviour where T : UserSettings where U : ColliderData
{
    public T settings;
    [Header("Collider Generation Settings")]
    [Tooltip("Optional sub-mesh indices to process. If empty, all sub-meshes are processed.")]
    public int[] subMeshIndices;
    public bool GenerateColliders = true;
    [Header("Debugging")]
    public bool TimeIt = false;
    public bool PrintCreatedPoints = false;
    protected U colliderData;

    public abstract void BakeColliders();

    
    public void GenerateCollisionColliders() {
        if(colliderData == null) {
            Debug.LogWarning("No collider data to generate colliders from.");
            return;
        }
        colliderData.AddDataAsColliders(settings, GetColliderObj());
    }
    
    public void ClearColliders() {
        Collider[] colliders = GetColliderObj().GetComponents<Collider>();
        foreach(var c in colliders) {
            DestroyImmediate(c);
        }
    }

    protected virtual GameObject GetColliderObj() {
        return gameObject;
    }

}
