using System;
using System.Collections.Generic;
using UnityEngine;

public class VoxelColliderCreator : ColliderCreator<VoxelizedColliderData> {

    public Vector3 transformScale;
    public Bounds bounds;
    public VoxelColliderCreator(Vector3 scale, Bounds bounds, VoxelizedColliderData colliderData = null) : base(colliderData) {
        this.transformScale = scale;
        this.bounds = bounds;

    }
    public override void BakeCollider(MeshColliderData meshData, UserSettings settings) {
        VoxelizedUserSettings voxelSettings = settings as VoxelizedUserSettings;
        VoxelizedColliderData voxelData = GetColliderData();
        
        // RemoveVoxelColliders();

        

        // --- Build a LOCAL grid (axis-aligned with the object's local axes)
        // Convert world spacing/padding into local spacing/padding per axis.
        Vector3 ls = transformScale;
        float sx = Mathf.Max(1e-8f, Mathf.Abs(ls.x));
        float sy = Mathf.Max(1e-8f, Mathf.Abs(ls.y));
        float sz = Mathf.Max(1e-8f, Mathf.Abs(ls.z));

        Vector3 paddingLocal = new Vector3(voxelSettings.boundsPaddingWorld / sx, voxelSettings.boundsPaddingWorld / sy, voxelSettings.boundsPaddingWorld / sz);

        Bounds b = bounds;
        b.Expand(paddingLocal * 2f);

        Vector3 minL = b.min;
        Vector3 stepSize = new Vector3(voxelSettings.spacingWorld / sx, voxelSettings.spacingWorld / sy, voxelSettings.spacingWorld / sz);
        int nx, ny, nz;
        if (voxelSettings.useBoxesPerEdge)
        {
            nx = Mathf.Max(1, voxelSettings.boxesPerEdge);
            ny = Mathf.Max(1, voxelSettings.boxesPerEdge);
            nz = Mathf.Max(1, voxelSettings.boxesPerEdge);
            stepSize = new Vector3(b.size.x / nx, b.size.y / ny, b.size.z / nz);
        } else{
            Vector3 sizeL = b.size;
            // number of CELLS (voxel volumes)
            nx = Mathf.Max(1, Mathf.CeilToInt(sizeL.x / stepSize.x));
            ny = Mathf.Max(1, Mathf.CeilToInt(sizeL.y / stepSize.y));
            nz = Mathf.Max(1, Mathf.CeilToInt(sizeL.z / stepSize.z));
        }
        voxelData.Resize(nx * ny * nz);
        // mesh data local
        IList<Vector3> v = meshData.vertices;
        IList<int> t = meshData.triangles;

        bool[,,] filled = new bool[nx, ny, nz];

        int tested = 0;
        int inside = 0;
        
        // Sample CELL CENTERS: min + (i + 0.5) * spacing
        for (int x = 0; x < nx; x++)
        {
            float px = minL.x + (x + 0.5f) * stepSize.x;
            for (int y = 0; y < ny; y++)
            {
                float py = minL.y + (y + 0.5f) * stepSize.y;
                for (int z = 0; z < nz; z++)
                {
                    float pz = minL.z + (z + 0.5f) * stepSize.z;

                    tested++;
                    Vector3 pLocal = new Vector3(px, py, pz);

                    if (IsPointInsideMeshByTrianglesLocal(v, t, pLocal, voxelSettings.rayDirections))
                    {
                        filled[x, y, z] = true;
                        inside++;
                    }
                }
            }
        }
       
        int created = 0;

        if (!voxelSettings.mergeBoxes)
        {
            // One box per filled cell
            Vector3 cellSizeLocal = Vector3.Scale(stepSize, Vector3.one * voxelSettings.boxSizeMultiplier);
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        if (!filled[x, y, z]) continue;

                        Vector3 center = new Vector3(
                            minL.x + (x + 0.5f) * stepSize.x,
                            minL.y + (y + 0.5f) * stepSize.y,
                            minL.z + (z + 0.5f) * stepSize.z
                        );

                        Vector3 size = cellSizeLocal;
                        voxelData.AddData(ref center, ref size);
                        created++;
                        if (created >= voxelSettings.maxColliders)
                        {
                            Debug.LogWarning($"VoxelCollider: Reached maxColliders={voxelSettings.maxColliders}. Stopping early.");
                            break;
                        }
                    }
        }
        else
        {
            _ = CreateMergedBoxCollidersLocal(filled, nx, ny, nz, minL, stepSize, voxelSettings);
        }
        

    }



    // ------------------------------------------------------------
    // Greedy merge in LOCAL grid
    // ------------------------------------------------------------
    private int CreateMergedBoxCollidersLocal(bool[,,] filled, int nx, int ny, int nz, Vector3 minL, Vector3 spacingLocal, VoxelizedUserSettings voxelSettings)
    {
        bool[,,] used = new bool[nx, ny, nz];
        int created = 0;
        VoxelizedColliderData voxelData = GetColliderData();
        for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                {
                    if (!filled[x, y, z] || used[x, y, z])
                        continue;

                    // Expand X
                    int dx = 1;
                    while (x + dx < nx && filled[x + dx, y, z] && !used[x + dx, y, z])
                        dx++;

                    // Expand Y for full X-span
                    int dy = 1;
                    while (y + dy < ny && RowAllFreeAndFilledX(filled, used, x, y + dy, z, dx))
                        dy++;

                    // Expand Z for full X*Y area
                    int dz = 1;
                    while (z + dz < nz && SlabAllFreeAndFilledXY(filled, used, x, y, z + dz, dx, dy))
                        dz++;

                    // mark used
                    for (int ix = x; ix < x + dx; ix++)
                        for (int iy = y; iy < y + dy; iy++)
                            for (int iz = z; iz < z + dz; iz++)
                                used[ix, iy, iz] = true;

                    // Local box size in local units
                    Vector3 sizeLocal = new Vector3(
                        dx * spacingLocal.x,
                        dy * spacingLocal.y,
                        dz * spacingLocal.z
                    ) * voxelSettings.boxSizeMultiplier;

                    // Local center: block min corner + half size (UNSCALED by multiplier!)
                    // We keep center consistent with the cell grid; multiplier only affects size.
                    Vector3 centerLocal = new Vector3(
                        minL.x + (x + dx * 0.5f) * spacingLocal.x,
                        minL.y + (y + dy * 0.5f) * spacingLocal.y,
                        minL.z + (z + dz * 0.5f) * spacingLocal.z
                    );

                    voxelData.AddData(ref centerLocal, ref sizeLocal);
                    created++;
                    if (created >= voxelSettings.maxColliders)
                    {
                        Debug.LogWarning($"VoxelCollider: Reached maxColliders={voxelSettings.maxColliders} while merging. Stopping early.");
                        return created;
                    }
                }

        return created;
    }

    private static bool RowAllFreeAndFilledX(bool[,,] filled, bool[,,] used, int x0, int y, int z, int dx)
    {
        for (int x = x0; x < x0 + dx; x++)
            if (!filled[x, y, z] || used[x, y, z])
                return false;
        return true;
    }

    private static bool SlabAllFreeAndFilledXY(bool[,,] filled, bool[,,] used, int x0, int y0, int z, int dx, int dy)
    {
        for (int y = y0; y < y0 + dy; y++)
            for (int x = x0; x < x0 + dx; x++)
                if (!filled[x, y, z] || used[x, y, z])
                    return false;
        return true;
    }

    // ------------------------------------------------------------
    // Point-in-mesh via triangle ray intersection (odd/even rule) - LOCAL
    // (no TransformPoint per triangle!)
    // ------------------------------------------------------------
    private static bool IsPointInsideMeshByTrianglesLocal(IList<Vector3> vertsLocal, IList<int> triangles, Vector3 pointLocal, int directionsToVote)
    {
        if (directionsToVote <= 1)
            return IsInsideOddEvenLocal(vertsLocal, triangles, pointLocal, Vector3.right);

        int votes = 0;
        if (IsInsideOddEvenLocal(vertsLocal, triangles, pointLocal, Vector3.right)) votes++;
        if (IsInsideOddEvenLocal(vertsLocal, triangles, pointLocal, Vector3.up)) votes++;
        if (IsInsideOddEvenLocal(vertsLocal, triangles, pointLocal, Vector3.forward)) votes++;

        return votes >= 2;
    }

    private static bool IsInsideOddEvenLocal(IList<Vector3> vertsLocal, IList<int> triangles, Vector3 rayOriginLocal, Vector3 rayDirLocal)
    {
        int hits = 0;

        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 v0 = vertsLocal[triangles[i]];
            Vector3 v1 = vertsLocal[triangles[i + 1]];
            Vector3 v2 = vertsLocal[triangles[i + 2]];

            if (RayIntersectsTriangle(rayOriginLocal, rayDirLocal, v0, v1, v2))
                hits++;
        }

        return (hits & 1) == 1;
    }

    // M�ller�Trumbore ray-triangle intersection
    private static bool RayIntersectsTriangle(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        const float EPSILON = 1e-8f;

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;

        Vector3 h = Vector3.Cross(rayDir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -EPSILON && a < EPSILON)
            return false;

        float f = 1.0f / a;
        Vector3 s = rayOrigin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(rayDir, q);

        if (v < 0.0f || (u + v) > 1.0f)
            return false;

        float t = f * Vector3.Dot(edge2, q);

        return t > EPSILON;
    }
}

public class VoxelizedColliderData : ColliderData {
    List<Vector3> voxelCenters;
    List<Vector3> boxSizes;

    public VoxelizedColliderData() {
        colliderType = ColliderType.Voxelized;
        voxelCenters = new List<Vector3>();
        boxSizes = new List<Vector3>();
    }

    public VoxelizedColliderData(List<Vector3> voxelCenters, List<Vector3> boxSizes) {
        this.voxelCenters = voxelCenters;
        this.boxSizes = boxSizes;
        colliderType = ColliderType.Voxelized;
    }

    public VoxelizedColliderData(int capacity) {
        voxelCenters = new List<Vector3>(capacity);
        boxSizes = new List<Vector3>(capacity);
        colliderType = ColliderType.Voxelized;
    }

    public IList<Vector3> VoxelCenters => voxelCenters;
    public IList<Vector3> BoxSizes => boxSizes;


    public override void AddDataAsColliders(UserSettings settings, GameObject gameObject) {
        VoxelizedUserSettings voxelSettings = settings as VoxelizedUserSettings;
        for(int i = 0; i < voxelCenters.Count; i++) {
            BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.center = voxelCenters[i];
            boxCollider.size = boxSizes[i];
            boxCollider.isTrigger = voxelSettings.makeTriggers;
        }
    } 
    public void AddData(ref Vector3 center, ref Vector3 size) {
        voxelCenters.Add(center);
        boxSizes.Add(size);
    }

    public void Resize(int newSize) {
        voxelCenters.Capacity = newSize;
        boxSizes.Capacity = newSize;
    }
}
[Serializable]
public class VoxelizedUserSettings : UserSettings {
    [Header("Voxel Size")]
    public bool useBoxesPerEdge = false;   // if true, overrides spacingWorld
    public float spacingWorld = 0.1f;     // desired voxel size in world units
    public int boxesPerEdge = 10;          // number of voxels along the longest axis
    [Header("Voxelization Settings")]
    public float boundsPaddingWorld = 0.0f;    // expand bounds a bit (world units)
    public float boxSizeMultiplier = 1.0f;
    [Header("Merging")]
    public bool mergeBoxes = true; 
    [Header("Safety")]
    [Tooltip("Hard cap so you don't accidentally create millions of colliders.")]
    public int maxColliders = 20000;

    [Tooltip("Number of ray directions to use for point-in-mesh test (more = better accuracy, slower).")]
    public int rayDirections = 3;
    [Header("Collider Settings")]
    [Tooltip("Whether generated BoxColliders are triggers.")]
    public bool makeTriggers = false;
    [Tooltip("Physics Material to assign to generated BoxColliders.")]
    public PhysicsMaterial material = null;
    

    public VoxelizedUserSettings() {
        colliderType = ColliderType.Voxelized;
    }
}