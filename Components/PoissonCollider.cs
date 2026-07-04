using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class PoissonCollider : NonConvexCollider<PoissonUserSettings, PoissonColliderData> {
    
    
    [ContextMenu("Bake Colliders")]
    public override void BakeColliders() {
        
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) {
            Debug.LogWarning("MeshPoisson: No MeshFilter or Mesh assigned.");
            return;
        }
        Mesh mesh = meshFilter.sharedMesh;
        MeshColliderData meshData = new MeshColliderData(mesh, subMeshIndices);
        
        PoissonColliderCreator creator = new PoissonColliderCreator(meshFilter.transform );
        creator.BakeCollider(meshData, settings);
        

        colliderData = creator.GetColliderData();

        stopwatch.Stop();
        if(PrintCreatedPoints) Debug.Log($"Generated {colliderData.Points.Count} Poisson points with attempts {settings.maxAttempts}.");
        if(TimeIt) {
            Debug.Log($"Poisson collider generation took {stopwatch.ElapsedMilliseconds} ms.");
        }


        if(GenerateColliders) {
            stopwatch.Reset();
            stopwatch.Start();
            ClearColliders();
            GenerateCollisionColliders();
            stopwatch.Stop();
            if(TimeIt) {
                Debug.Log($"Collision Creation took {stopwatch.ElapsedMilliseconds} ms.");
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

    
}

