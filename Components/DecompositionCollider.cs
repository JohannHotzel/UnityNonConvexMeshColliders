using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class DecompositionCollider : NonConvexCollider<DecompositionUserSettings, DecompositionColliderData> {

    [ContextMenu("Bake Colliders")]
    public override void BakeColliders() {

        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        
        MeshFilter mf = GetComponent<MeshFilter>();
        if(mf == null || mf.sharedMesh == null) {
            Debug.LogWarning("DecompositionCollider: No MeshFilter / Mesh found on this GameObject.");
            return;
        }
        Mesh mesh = mf.sharedMesh;
        Bounds b = MeshColliderData.GetBounds(mesh, subMeshIndices);
        MeshColliderData meshData = new MeshColliderData(mesh, subMeshIndices);
        colliderData = new DecompositionColliderData();
        DecompositionColliderCreator decompositionCreator = new DecompositionColliderCreator(transform.lossyScale, b, colliderData);
        decompositionCreator.BakeCollider(meshData, settings);

        stopwatch.Stop();
        if(TimeIt) {
            Debug.Log($"DecompositionCollider: Collider baking took {stopwatch.ElapsedMilliseconds} ms.");
        }
        if(PrintCreatedPoints) {
            Debug.Log($"DecompositionCollider: Created {colliderData.GetMeshes().Count} convex colliders points.");
        }


        if(GenerateColliders) {
            stopwatch.Reset();
            stopwatch.Start();
            ClearColliders();
            GenerateCollisionColliders();
            stopwatch.Stop();
            if(TimeIt) {
                Debug.Log($"DecompositionCollider: Collider generation took {stopwatch.ElapsedMilliseconds} ms.");
            }
        }

    }
    [ContextMenu("Generate Collisions")]
    public void GenerateCollisions() {
        ClearColliders();
        GenerateCollisionColliders();
    }
    [ContextMenu("Clear Colliders")]
    public void ClearAllColliders() {
        ClearColliders();
    }

    protected override GameObject GetColliderObj() {
        foreach(Transform child in transform) {
            if(child.name == settings.gameObjectName) {
                return child.gameObject;
            }
        }

        GameObject go = new GameObject(settings.gameObjectName);
        go.transform.parent = transform;
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        return go;
    }
}