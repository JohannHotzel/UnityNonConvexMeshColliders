using System;
using System.Collections.Generic;
using UnityEngine;

public class PoissonColliderCreator : ColliderCreator<PoissonColliderData> {
    public Transform tr;

    public PoissonColliderCreator(Transform transform, PoissonColliderData colliderData = null) {
        tr = transform;
        if(colliderData != null) {
            SetColliderData(colliderData);
        } else {
            SetColliderData(new PoissonColliderData());
        }

    }

    
    /// <summary>
    /// Bakes Poisson disk sampled points from the given mesh data into the collider data.
    /// </summary>
    /// <param name="meshData"></param>
    /// <param name="settings"></param>
    public override void BakeCollider(MeshColliderData meshData, UserSettings settings) {
        
        if(!VerifyType(settings)) {
            Debug.LogError("PoissonColliderCreator: UserSettings type does not match ColliderData type.");
            return;
        }
        PoissonUserSettings poissonSettings = settings as PoissonUserSettings;
        PoissonColliderData poissonData = GetColliderData();
        List<Vector3> pointsWorld = new List<Vector3>(poissonSettings.targetCount);
        poissonData.Points = pointsWorld;

        IList<Vector3> v = meshData.vertices;
        IList<int> t = meshData.triangles;
        


        if (t == null || t.Count < 3)
        {
            Debug.LogWarning("MeshPoisson: Mesh has no triangles.");
            return;
        }

        // Build area-weighted CDF for triangle picking
        float[] cdf = BuildTriangleAreaCDF(v, t, out float totalArea);
        if (totalArea <= 0f)
        {
            Debug.LogWarning("MeshPoisson: Total mesh surface area is zero.");
            return;
        }
        // Algorithm 
        // totalArea / targetCount = area per point, apr / pir^2 = radius for poisson test for full coverage, percent coverage
        // apr / pi = 1/percent * r^2
        
        float radius = poissonSettings.radius;
        float r2  = poissonSettings.BasedOnSurfaceArea ?
            ((totalArea / poissonSettings.targetCount) * poissonSettings.PercentSurfaceCoverage)/ Mathf.PI :   
            radius * radius;
        if(poissonSettings.BasedOnSurfaceArea) {
            float adjustedRadius = Mathf.Sqrt(r2);
            poissonSettings.radius = adjustedRadius;
            poissonSettings.colliderRadius = adjustedRadius / poissonSettings.ColliderToSampleRatio;
            poissonSettings.insetDistance = adjustedRadius * poissonSettings.InsetPercent;
        }
        Dictionary<int, List<int>> usedTriangleIndices = new Dictionary<int, List<int>>(poissonSettings.targetCount);
        int attempts = 0;
        while (attempts < poissonSettings.maxAttempts && pointsWorld.Count < poissonSettings.targetCount)
        {
            attempts++;

            int triIndex = PickTriangleIndex(cdf, UnityEngine.Random.value);
            
            int i0 = t[triIndex * 3 + 0];
            int i1 = t[triIndex * 3 + 1];
            int i2 = t[triIndex * 3 + 2];

            Vector3 aL = v[i0];
            Vector3 bL = v[i1];
            Vector3 cL = v[i2];

            Vector3 pL = SamplePointInTriangle(ref aL, ref bL,  ref cL);
            // Vector3 pW = tr.TransformPoint(pL);


            //push inward along triangle normal
            if (poissonSettings.insetDistance > 0f)
            {
                Vector3 nL = Vector3.Cross(bL - aL, cL - aL);
                if (nL.sqrMagnitude > 1e-12f)
                {
                    
                    // Vector3 nW = tr.TransformDirection(nL).normalized;

                    // "Inward" = just go opposite to the triangle normal
                    pL -= nL * poissonSettings.insetDistance;
                }
            }

            if(usedTriangleIndices.TryGetValue(triIndex, out List<int> existingIndices)) {
                if(!IsPointCanadidateValid(ref pL, pointsWorld, existingIndices, r2)) {
                    continue;
                }
                
            } else {
                usedTriangleIndices[triIndex] = new List<int>();
            }
            
            bool ok = true;
            for (int i = 0; i < pointsWorld.Count; i++)
            {
                if ((pointsWorld[i] - pL).sqrMagnitude < r2)
                {
                    ok = false;
                    break;
                }
            }

            if (ok){
                usedTriangleIndices[triIndex].Add(pointsWorld.Count);
                pointsWorld.Add(pL);
            }
        }
    }


    #region Private Helpers
    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    /// <summary>
    /// Builds a cumulative distribution function (CDF) over triangle areas
    /// for area-weighted random triangle selection.
    /// </summary>
    private static float[] BuildTriangleAreaCDF(IList<Vector3> verts, IList<int> tris, out float totalArea)
    {
        int triCount = tris.Count / 3;
        float[] cdf = new float[triCount];

        totalArea = 0f;
        for (int k = 0; k < triCount; k++)
        {
            Vector3 a = verts[tris[k * 3 + 0]];
            Vector3 b = verts[tris[k * 3 + 1]];
            Vector3 c = verts[tris[k * 3 + 2]];

            float area = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
            totalArea += area;
            cdf[k] = totalArea;
        }

        if (totalArea > 0f)
        {
            for (int k = 0; k < triCount; k++)
                cdf[k] /= totalArea;
        }

        return cdf;
    }

    private static bool IsPointCanadidateValid(ref Vector3 point, IList<Vector3> existingPoints, IList<int> indices, float minDistSquared)
    {
        for (int i = 0; i < indices.Count; i++)
        {
            Vector3 existingPoint = existingPoints[indices[i]];
            if ((point - existingPoint).sqrMagnitude < minDistSquared)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Picks a triangle index using a normalized CDF and a random value in [0, 1].
    /// </summary>
    private static int PickTriangleIndex(float[] cdf, float u01)
    {
        int idx = Array.BinarySearch(cdf, u01);
        if (idx < 0)
            idx = ~idx;

        if (idx >= cdf.Length)
            idx = cdf.Length - 1;

        return idx;
    }

    /// <summary>
    /// Uniformly samples a random point inside a triangle using barycentric coordinates.
    /// </summary>
    private static Vector3 SamplePointInTriangle(ref Vector3 a, ref Vector3 b, ref Vector3 c)
    {
        float u = UnityEngine.Random.value;
        float v = UnityEngine.Random.value;

        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }

        return a + u * (b - a) + v * (c - a);
    }


    #endregion
}

#region Data Types
public  class PoissonColliderData : ColliderData {
    public List<SphereCollider> SphereColliders;
    public IList<Vector3> Points;


    public PoissonColliderData() {
        colliderType = ColliderType.Poisson;
    }

    public override void AddDataAsColliders(UserSettings settings, GameObject gameObject) {
        PoissonUserSettings poissonSettings = settings as PoissonUserSettings;
        
        // Span<Vector3> ptsArray = new Span<Vector3>(Points.ToArray());
        // gameObject.transform.InverseTransformPoints(ptsArray);
        foreach(var p in Points) {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.center = p;
            sc.radius = poissonSettings.colliderRadius;
            sc.isTrigger = poissonSettings.isTrigger;
        }
    }
}
[Serializable]
public class PoissonUserSettings : UserSettings {

    [Header("Poisson Disk")]
    [Tooltip("Sampled Point Radius"), Min(0.0001f)]
    public float radius = 0.25f;
    [Min(1)]
    public int targetCount = 500;
    [Tooltip("How many candidate samples are tested in total. More = denser/better, but slower.")]
    public int maxAttempts = 200000;
    [Tooltip("Inset sampled points along triangle normal.")]
    [Min(0f)]
    public float insetDistance = 0.02f;
    [Tooltip("Radius of the generated SphereColliders (independent from Poisson radius).")]
    public float colliderRadius = 0.05f;
    [Tooltip("Whether generated SphereColliders are triggers.")]
    public bool isTrigger = false;
    [Tooltip("Physics Material assigned to generated SphereColliders.")]
    public PhysicsMaterial colliderMaterial = null;
    [Tooltip("Whether to base the number of colliders on the surface area of the mesh. (Overrides radius and target count)")]

    public bool BasedOnSurfaceArea;
    [Tooltip("Collider to sample ratio when BasedOnSurfaceArea is enabled.")]
    public float ColliderToSampleRatio = 2;
    [Tooltip("inset percent of radius for poisson sampling")]
    public float InsetPercent = 0.2f;
    [Tooltip("Percent of surface area to aim to cover with colliders when BasedOnSurfaceArea is enabled. \n recommended values: .5 to 3")]
    public float PercentSurfaceCoverage = 1;
    // total area of the mesh to estimate, sample radius test, colliders to estimate, int targetsamples per surface meter
    // float ratio of sample to collider, if you could also estimate the volume, then base colliders on surface to volume
    // inset persent of collider size, 
    // small surface area should keep same target, lower radius test

    public PoissonUserSettings() {
        colliderType = ColliderType.Poisson;
    }

    
}
#endregion