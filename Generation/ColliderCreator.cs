using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;



/// <summary>
/// Base class for collider creators.
/// </summary>
/// Notes: Use this for creating different collider generation strategies.
public abstract class ColliderCreator<T>  where T : ColliderData
{
    T colliderData;
    public abstract void BakeCollider(MeshColliderData meshData, UserSettings settings);

    public T GetColliderData() {
        return colliderData;
    }
    public bool VerifyType(UserSettings settings) {
        return settings.colliderType == colliderData.colliderType;
    }
    public ColliderCreator(T colliderData) {
        if(colliderData == null) {
            colliderData = Activator.CreateInstance<T>();
        }
        this.colliderData = colliderData;
    }
    public void SetColliderData(T colliderData) {
        this.colliderData = colliderData;
    }
    public ColliderCreator() {
        this.colliderData = default;
    }

}

//Optimization note: Storing the colliders in an array and modifying their properties 
// directly would be more efficient than creating/destroying colliders each time.

/// <summary>
/// Base class for collider data storage.
/// </summary>
public abstract class ColliderData {
    [HideInInspector]
    public ColliderType colliderType;
    public abstract void AddDataAsColliders(UserSettings settings, GameObject gameObject); 
}

/// <summary>
/// Base class for user settings for collider generation.
/// </summary>
public abstract class UserSettings {
    [HideInInspector]
    public ColliderType colliderType;
}

public enum ColliderType {
    Poisson,
    Voxelized,
    Decomposition
}

/// <summary>
/// Holds mesh data needed for collider generation.
/// This allows an abstraction over Unity's Mesh class, so that we can easily work with subsets of meshes (submeshes) or other mesh representations.
/// </summary>
/// 
public class MeshColliderData {
    public IList<Vector3> vertices;
    public IList<int> triangles;

    public MeshColliderData(IList<Vector3> vertices, IList<int> triangles) {
        this.vertices = vertices;
        this.triangles = triangles;
    }
    public MeshColliderData() {
        this.vertices = new List<Vector3>();
        this.triangles = new List<int>();
    }
    // <summary>
    /// Constructs MeshColliderData from a Unity Mesh, optionally only including specified submeshes.
    /// </summary>
    /// <param name="mesh">Mesh to extract data from.</param>
    /// <param name="subMeshIndices">If null or empty, includes all submeshes.</param>
    public MeshColliderData(Mesh mesh, int[] subMeshIndices) {
        if(subMeshIndices == null || subMeshIndices.Length == 0) {
            this.vertices = mesh.vertices;
            this.triangles = mesh.triangles;
            return;
        }
        int[] allTris = mesh.triangles;
        int total = 0;
        
        SubMeshDescriptor[] subMeshDescriptors = new SubMeshDescriptor[subMeshIndices.Length];
        for(int i = 0; i < subMeshIndices.Length; i++) {
            subMeshDescriptors[i] = mesh.GetSubMesh(subMeshIndices[i]);
            total += (int)subMeshDescriptors[i].indexCount;
        }
        List<int> triangles = new List<int>(total);
        for(int i = 0; i < subMeshIndices.Length; i++) {
            var desc = subMeshDescriptors[i];
            triangles.AddRange(allTris[desc.indexStart..(desc.indexStart + desc.indexCount)]);
        }
        this.vertices = mesh.vertices;
        this.triangles = triangles;
    }
    /// <summary>
    /// Gets the bounds of the specified submeshes of the mesh, or the whole mesh if no submeshes are specified.
    /// </summary>
    public static Bounds GetBounds(Mesh mesh, int[] subMeshIndices = null) {
        if(subMeshIndices == null || subMeshIndices.Length == 0) {
            return mesh.bounds;
        }
        var b = mesh.GetSubMesh(subMeshIndices[0]).bounds;
        for(int i = 1; i < subMeshIndices.Length; i++) {
            b.Encapsulate(mesh.GetSubMesh(subMeshIndices[i]).bounds);
        }
        
        return b;
    }

    
}