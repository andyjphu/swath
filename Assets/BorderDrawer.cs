using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BorderDrawer : MonoBehaviour
{
    [Tooltip("Which layers count as terrain for claiming")]
    public LayerMask terrainMask;

    [Tooltip("Minimum distance (world units) between sampled points")]
    public float minPointDistance = 0.1f;

    [Tooltip("Player ID to assign territory to when drawing")]
    public int playerId = 0;

    private LineRenderer   line;
    private List<Vector3>  points    = new List<Vector3>();
    private bool           isDrawing = false;
    private VoxelTerrainGenerator terrainGen;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace    = true;
        line.loop             = true;
        line.startWidth       = line.endWidth = 0.05f;
        line.positionCount    = 0;

        // give it a visible material
        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = Color.red;
        line.material = mat;

        terrainGen = FindObjectOfType<VoxelTerrainGenerator>();
    }

    void Update()
    {
        // begin stroke
        if (Input.GetMouseButtonDown(0))
        {
            isDrawing       = true;
            points.Clear();
            line.positionCount = 0;
        }

        // sample while dragging
        if (isDrawing && Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity, terrainMask))
            {
                Vector3 p = hit.point;
                if (points.Count == 0 || Vector3.Distance(points[^1], p) > minPointDistance)
                {
                    points.Add(p);
                    line.positionCount = points.Count;
                    line.SetPositions(points.ToArray());
                }
            }
        }

        // finish stroke
        if (isDrawing && Input.GetMouseButtonUp(0))
        {
            isDrawing = false;

            if (points.Count >= 3 && terrainGen != null)
            {
                // build 2D polygon in XZ
                Vector2[] poly2D = new Vector2[points.Count];
                for (int i = 0; i < points.Count; i++)
                    poly2D[i] = new Vector2(points[i].x, points[i].z);

                terrainGen.ClaimTerritory(poly2D, playerId);
            }

            // clear the line
            line.positionCount = 0;
        }
    }
}
