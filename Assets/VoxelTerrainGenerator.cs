using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class VoxelTerrainGenerator : MonoBehaviour
{
    [Header("Grid Size")]
    public int sizeX = 50;
    public int sizeY = 20;
    public int sizeZ = 50;
    public float isoLevel = 0.5f;

    [Header("Noise Settings")]
    public float noiseScale = 0.1f;
    public int seed = 12345;
    public int octaves = 3;
    public float lacunarity = 2f;
    public float persistence = 0.5f;

    [Header("Appearance")]
    [Tooltip("Assign your URP/Lit grass material here")]
    public Material grassMaterial;

    [Header("Territory")]
    [Tooltip("One color per player ID (0,1,2…); index -1 means unclaimed/transparent)")]
    public Color[] playerColors;

    // internal data
    private float[,,] density;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private int[,] ownerMap;      // [x,z] owner ID or -1
    private Texture2D territoryTex;  // 2D map painted per-owner

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        // initialize ownership grid
        ownerMap = new int[sizeX + 1, sizeZ + 1];
        for (int x = 0; x <= sizeX; x++)
            for (int z = 0; z <= sizeZ; z++)
                ownerMap[x, z] = -1;

        // build the world
        BuildDensityField();
        BuildMesh();
        SetupTerritoryTexture();
    }

    #region Terrain Generation

    void BuildDensityField()
    {
        density = new float[sizeX + 1, sizeY + 1, sizeZ + 1];
        var prng = new System.Random(seed);
        Vector3 offset = new Vector3(
            prng.Next(-10000, 10000),
            prng.Next(-10000, 10000),
            prng.Next(-10000, 10000)
        );

        for (int x = 0; x <= sizeX; x++)
            for (int y = 0; y <= sizeY; y++)
                for (int z = 0; z <= sizeZ; z++)
                {
                    float amp = 1f, freq = 1f, val = 0f;
                    for (int o = 0; o < octaves; o++)
                    {
                        float nx = (x + offset.x) * noiseScale * freq;
                        float nz = (z + offset.z) * noiseScale * freq;
                        val += (Mathf.PerlinNoise(nx, nz) - (float)y / sizeY) * amp;
                        amp *= persistence;
                        freq *= lacunarity;
                    }
                    density[x, y, z] = val;
                }
    }

    void BuildMesh()
    {
        // generate indexed mesh
        MarchingCubes.GenerateMesh(density, isoLevel,
            out List<Vector3> verts,
            out List<int> tris
        );
        var mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);

        // flat-shade: split triangles so each has its own normal
        var origVerts = mesh.vertices;
        var origTris = mesh.triangles;
        var flatVerts = new List<Vector3>(origTris.Length);
        var flatNormals = new List<Vector3>(origTris.Length);
        var flatTris = new List<int>(origTris.Length);

        for (int i = 0; i < origTris.Length; i += 3)
        {
            var v0 = origVerts[origTris[i]];
            var v1 = origVerts[origTris[i + 1]];
            var v2 = origVerts[origTris[i + 2]];
            var n = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            int b = flatVerts.Count;
            flatVerts.AddRange(new[] { v0, v1, v2 });
            flatNormals.AddRange(new[] { n, n, n });
            flatTris.AddRange(new[] { b, b + 1, b + 2 });
        }

        mesh.Clear();
        mesh.vertices = flatVerts.ToArray();
        mesh.normals = flatNormals.ToArray();
        mesh.triangles = flatTris.ToArray();

        // right after mesh.triangles = flatTris.ToArray();
        // build UVs so textures can map across XZ
        var uvs = new Vector2[mesh.vertexCount];
        for (int i = 0; i < uvs.Length; i++)
        {
            var v = mesh.vertices[i];
            // map x→0..1 across sizeX, z→0..1 across sizeZ
            uvs[i] = new Vector2(v.x / sizeX, v.z / sizeZ);
        }
        mesh.uv = uvs;


        mesh.RecalculateBounds();

        // assign mesh
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;

        // assign material
        if (grassMaterial != null)
            GetComponent<MeshRenderer>().sharedMaterial = grassMaterial;
    }

    #endregion

    #region Territory Painting

    void SetupTerritoryTexture()
    {
        // 1 px per column; point-filter so it looks blocky
        territoryTex = new Texture2D(sizeX + 1, sizeZ + 1, TextureFormat.RGBA32, false);
        territoryTex.filterMode = FilterMode.Point;

        // assign to detail map slot of URP/Lit
        grassMaterial.SetTexture("_DetailAlbedoMap", territoryTex);
        grassMaterial.SetFloat("_DetailNormalMapScale", 0f);

        // clear it initially
        UpdateTerritoryTexture();
    }

    /// <summary>
    /// Claim all columns under the given polygon for the given player ID.
    /// </summary>
    public void ClaimTerritory(Vector2[] poly2D, int playerId)
    {
        for (int x = 0; x <= sizeX; x++)
            for (int z = 0; z <= sizeZ; z++)
            {
                var sample = new Vector2(x + 0.5f, z + 0.5f);
                if (PointInPolygon(poly2D, sample))
                    ownerMap[x, z] = playerId;
            }
        UpdateTerritoryTexture();
    }

    void UpdateTerritoryTexture()
    {
        for (int x = 0; x <= sizeX; x++)
            for (int z = 0; z <= sizeZ; z++)
            {
                int owner = ownerMap[x, z];
                Color c = (owner >= 0 && owner < playerColors.Length)
                            ? playerColors[owner]
                            : Color.clear;
                territoryTex.SetPixel(x, z, c);
            }
        territoryTex.Apply();
    }

    // Standard 2D point-in-polygon
    bool PointInPolygon(Vector2[] poly, Vector2 pt)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (((poly[i].y > pt.y) != (poly[j].y > pt.y)) &&
                (pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) /
                        (poly[j].y - poly[i].y) + poly[i].x))
                inside = !inside;
        }
        return inside;
    }

    #endregion
}
