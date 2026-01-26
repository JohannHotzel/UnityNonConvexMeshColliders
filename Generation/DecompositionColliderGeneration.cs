using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class DecompositionColliderCreator : ColliderCreator<DecompositionColliderData> {
    
    public Vector3 worldScale;
    public Bounds Bounds;
    public DecompositionColliderCreator(Vector3 worldScale, Bounds bounds, DecompositionColliderData colliderData = null) : base(colliderData) {
        this.worldScale = worldScale;
        this.Bounds = bounds;
    }

    //Ways to improve:
    // Parallelize voxel processing
    // Store collider lists and reuse them to avoid adding / removing components repeatedly
    // Change to Async Implementation to avoid blocking main thread
    // Create BVH for way faster raycasting tests


    /// <summary>
    /// Bakes decomposition colliders from the input mesh data according to the user settings.
    /// </summary>
    /// <param name="meshData"></param>
    /// <param name="settings"></param>
    public override void BakeCollider(MeshColliderData meshData, UserSettings settings) {
        DecompositionUserSettings decompSettings = settings as DecompositionUserSettings;
        if(!VerifyType(settings)) {
            Debug.LogError("DecompositionColliderCreator: UserSettings type does not match ColliderData type.");
            return;
        }
        DecompositionColliderData decompData = GetColliderData();
        var verts = meshData.vertices;
        var tris = meshData.triangles;

        if (tris == null || tris.Count < 3)
        {
            Debug.LogWarning("VoxelTriangleMeshColliders: Mesh has no triangles.");
            return;
        }

        decompData.ClearMeshes();

        // Build LOCAL grid based on mesh.bounds (local space)
        Vector3 ls = worldScale;
        float sx = Mathf.Max(1e-8f, Mathf.Abs(ls.x));
        float sy = Mathf.Max(1e-8f, Mathf.Abs(ls.y));
        float sz = Mathf.Max(1e-8f, Mathf.Abs(ls.z));

        Vector3 spacingLocal = new Vector3(decompSettings.spacingWorld / sx, decompSettings.spacingWorld / sy, decompSettings.spacingWorld / sz);
        Vector3 paddingLocal = new Vector3(decompSettings.boundsPaddingWorld / sx, decompSettings.boundsPaddingWorld / sy, decompSettings.boundsPaddingWorld / sz);
        Bounds b = new Bounds(Bounds.center, Bounds.size);
        b.Expand(paddingLocal * 2f);

        Vector3 minL = b.min;
        Vector3 maxL = b.max;
        Vector3 sizeL = b.size;
        int nx, ny, nz;
        if(decompSettings.useBoxesPerEdge) {
            nx = decompSettings.boxesPerEdge;
            ny = decompSettings.boxesPerEdge;
            nz = decompSettings.boxesPerEdge;
            spacingLocal = new Vector3(sizeL.x / nx, sizeL.y / ny, sizeL.z / nz);
        } else {
            nx = Mathf.Max(1, Mathf.CeilToInt(sizeL.x / spacingLocal.x));
            ny = Mathf.Max(1, Mathf.CeilToInt(sizeL.y / spacingLocal.y));
            nz = Mathf.Max(1, Mathf.CeilToInt(sizeL.z / spacingLocal.z));
        }
        

        // Voxel buckets: voxel -> list of triangle indices (triangle = 3 ints in tris[])
        var buckets = new Dictionary<Vector3Int, List<int>>(4096);

        // Assign triangles to voxels (by triangle AABB overlap)
        int triCount = tris.Count / 3;
        for (int ti = 0; ti < triCount; ti++)
        {
            int i0 = tris[ti * 3 + 0];
            int i1 = tris[ti * 3 + 1];
            int i2 = tris[ti * 3 + 2];

            Vector3 p0 = verts[i0];
            Vector3 p1 = verts[i1];
            Vector3 p2 = verts[i2];

            Vector3 triMin = Vector3.Min(p0, Vector3.Min(p1, p2));
            Vector3 triMax = Vector3.Max(p0, Vector3.Max(p1, p2));

            // Clamp triangle AABB to grid bounds to avoid negative/out-of-range indices
            triMin = Vector3.Max(triMin, minL);
            triMax = Vector3.Min(triMax, maxL);

            // Convert AABB to voxel index range
            int x0 = Mathf.Clamp(WorldToCell(triMin.x, minL.x, spacingLocal.x), 0, nx - 1);
            int y0 = Mathf.Clamp(WorldToCell(triMin.y, minL.y, spacingLocal.y), 0, ny - 1);
            int z0 = Mathf.Clamp(WorldToCell(triMin.z, minL.z, spacingLocal.z), 0, nz - 1);

            int x1 = Mathf.Clamp(WorldToCell(triMax.x, minL.x, spacingLocal.x), 0, nx - 1);
            int y1 = Mathf.Clamp(WorldToCell(triMax.y, minL.y, spacingLocal.y), 0, ny - 1);
            int z1 = Mathf.Clamp(WorldToCell(triMax.z, minL.z, spacingLocal.z), 0, nz - 1);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!buckets.TryGetValue(key, out var list))
                        {
                            list = new List<int>(64);
                            buckets.Add(key, list);
                        }
                        list.Add(ti);
                    }
        }

        // Create output root
    

        int created = 0;
        int skippedSmall = 0;

        foreach (var kvp in buckets)
        {
            List<int> triIndices = kvp.Value;
            if (triIndices == null) continue;

            int triInVoxel = triIndices.Count;
            if (triInVoxel < decompSettings.minTrianglesPerVoxel)
            {
                skippedSmall++;
                continue;
            }

            if (triInVoxel > decompSettings.warnIfTrianglesPerVoxelAbove)
                Debug.LogWarning($"VoxelTriangleMeshColliders: Voxel {kvp.Key} has {triInVoxel} triangles. Consider smaller spacingWorld or different mesh.");

            if (created >= decompSettings.maxColliders)
            {
                Debug.LogWarning($"VoxelTriangleMeshColliders: Reached maxColliders={decompSettings.maxColliders}. Stopping.");
                break;
            }

            // Build mesh for this voxel group (deduplicated vertices)
            Mesh sub = BuildSubMesh(verts, tris, triIndices);
            decompData.AddMesh(sub);

            created++;
        }
    }

    private static int WorldToCell(float p, float min, float cell)
    {
        // Convert position into voxel index
        return Mathf.FloorToInt((p - min) / cell);
    }

    private static Mesh BuildSubMesh(IList<Vector3> verts, IList<int> tris, IList<int> triangleIndices)
    {
        // Map old vertex index -> new vertex index
        var map = new Dictionary<int, int>(triangleIndices.Count * 3);
        var newVerts = new List<VertexData>(triangleIndices.Count * 3);
        var newTris = new List<int>(triangleIndices.Count * 3);

        for (int k = 0; k < triangleIndices.Count; k++)
        {
            int ti = triangleIndices[k];
            int a = tris[ti * 3 + 0];
            int b = tris[ti * 3 + 1];
            int c = tris[ti * 3 + 2];

            newTris.Add(RemappedIndex(a, verts, map, newVerts));
            newTris.Add(RemappedIndex(b, verts, map, newVerts));
            newTris.Add(RemappedIndex(c, verts, map, newVerts));
        }

        var m = new Mesh();
        // If you expect large meshes per voxel:
        if (newVerts.Count > 65535)
            m.indexFormat = IndexFormat.UInt32;

        m.SetVertexBufferParams(newVerts.Count, 
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3) );
        m.SetIndexBufferParams(newTris.Count, IndexFormat.UInt32);
        m.SetIndexBufferData(newTris, 0, 0, newTris.Count);
        m.SetVertexBufferData(newVerts, 0, 0, newVerts.Count);
        
        m.RecalculateBounds();

        // Normals not required for MeshCollider, but harmless if you want debug rendering later.
        // m.RecalculateNormals();

        return m;
    }

    private static int RemappedIndex(int oldIndex, IList<Vector3> verts, Dictionary<int, int> map, List<VertexData> newVerts)
    {
        if (map.TryGetValue(oldIndex, out int newIndex))
            return newIndex;

        newIndex = newVerts.Count;
        map.Add(oldIndex, newIndex);
        newVerts.Add(new VertexData { position = verts[oldIndex] });
        return newIndex;
    }

}

public class DecompositionColliderData : ColliderData {
    List<Mesh> colliderMeshes;
    public override void AddDataAsColliders(UserSettings settings, GameObject gameObject) {
        DecompositionUserSettings decompSettings = settings as DecompositionUserSettings;
        if(decompSettings == null) {
            Debug.LogError("Invalid user settings for DecompositionColliderData");
            return;
        }

        foreach(var mesh in colliderMeshes) {
            MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = decompSettings.convex;
            meshCollider.isTrigger = decompSettings.isTrigger;
            meshCollider.material = decompSettings.material;
        }
    }
    public DecompositionColliderData() {
        colliderType = ColliderType.Decomposition;
        colliderMeshes = new List<Mesh>();
    }
    public void ClearMeshes() {
        colliderMeshes.Clear();
    }
    public void AddMesh(Mesh mesh) {
        colliderMeshes.Add(mesh);
    }
    public IList<Mesh> GetMeshes() {
        return colliderMeshes;
    }
    
    
}

public class DecompositionUserSettings : UserSettings {
    [Header("Decomposition Settings")]
    public bool useBoxesPerEdge = false;
    public float spacingWorld = 0.1f;
    public int boxesPerEdge = 10;
    public float boundsPaddingWorld = 0.0f;
    [Header("Collider Settings")]   
    public bool convex;
    public bool isTrigger;
    public PhysicsMaterial material;
    [Header("Output Settings")]
    public bool outputAsAssets = false;
    [Tooltip("Object name under which generated colliders are placed")]
    public string gameObjectName = "DecompositionColliders";
    [Tooltip("Hard cap so you don't accidentally create millions of colliders.")]
    public int maxColliders = 2000;
    [Tooltip("Minimum number of triangles in a voxel to create a collider for it.")]
    public int minTrianglesPerVoxel = 4;
    [Tooltip("If a voxel has more than this number of triangles, a warning is logged.")]
    public int warnIfTrianglesPerVoxelAbove = 5000;
}

public struct VertexData {
    public Vector3 position;
}