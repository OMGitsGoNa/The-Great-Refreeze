using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Rejilla de celdas cuadradas sobre el agua. Guarda qué celdas están ocupadas (muelle, plataforma o hielo)
/// y crea los cubos de hielo, que son sólidos: el jugador puede caminar por encima.
/// La celda (x, z) cubre el mundo desde (x * CellSize, z * CellSize) hasta (x + 1, z + 1) * CellSize.
/// Los sectores (anillos alrededor del muelle) están en SectorMap.
/// </summary>
public partial class IceGrid : Node3D
{
    /// <summary>Lado de una celda: igual que un cubo de hielo.</summary>
    public const float CellSize = 0.5f;

    public const float CubeHeight = 1f;

    /// <summary>Altura de la superficie del hielo: un pelín por encima del agua (y = -0.8).</summary>
    public const float SurfaceY = -0.75f;

    /// <summary>Lado de un cubo de hielo visible. Es el mismo tamaño que los cubos de la mesa del montón.</summary>
    public const float IceCubeSize = 0.5f;

    /// <summary>Tamaño visual de un cubo (también lo usa la sombra de previsualización).</summary>
    public static readonly Vector3 VisualSize = new(IceCubeSize, IceCubeSize, IceCubeSize);

    private static readonly Vector2I[] Neighbors = { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly HashSet<Vector2I> _dock = new(); // celdas fijas (muelle, plataformas): no cuentan como cubos

    /// <summary>Formas distintas de hielo. Se comparten entre cubos para no gastar memoria.</summary>
    private const int VariantCount = 12;

    private (ArrayMesh Mesh, ConvexPolygonShape3D Shape)[] _variants;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        _material = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.93f, 1f) };

        // Semilla fija: las formas son las mismas en todas las partidas.
        var rng = new Random(12345);
        _variants = new (ArrayMesh, ConvexPolygonShape3D)[VariantCount];
        for (int i = 0; i < VariantCount; i++)
            _variants[i] = BuildVariant(rng);
    }

    public static Vector2I WorldToCell(Vector3 point) =>
        new(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Z / CellSize));

    /// <summary>Centro de la celda a la altura de la superficie del hielo.</summary>
    public static Vector3 CellCenter(Vector2I cell) =>
        new((cell.X + 0.5f) * CellSize, SurfaceY, (cell.Y + 0.5f) * CellSize);

    public bool IsOccupied(Vector2I cell) => _occupied.Contains(cell);

    /// <summary>Celdas con hielo puesto por el jugador (sin muelle ni plataformas fijas), para guardar la partida.</summary>
    public IEnumerable<Vector2I> PlacedCells => _occupied.Where(c => !_dock.Contains(c));

    /// <summary>Marca una celda del muelle como ocupada sin crear hielo.</summary>
    public void MarkOccupied(Vector2I cell)
    {
        _occupied.Add(cell);
        _dock.Add(cell);
    }

    /// <summary>
    /// Se puede colocar si está dentro del mundo, la celda está libre y toca (en cruz) una celda ocupada.
    /// </summary>
    public bool CanPlace(Vector2I cell)
    {
        if (!SectorMap.World.Contains(cell))
            return false;

        if (_occupied.Contains(cell))
            return false;

        foreach (Vector2I direction in Neighbors)
        {
            if (_occupied.Contains(cell + direction))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Crea un cubo de hielo fijo que ya existe desde el inicio (por ejemplo, la plataforma del oso).
    /// No cuenta como cubo colocado ni como parte de un sector.
    /// </summary>
    public void PlaceFixed(Vector2I cell)
    {
        _occupied.Add(cell);
        _dock.Add(cell);
        AddCube(cell);
    }

    /// <summary>Crea un cubo de hielo en la celda. Devuelve false si no se puede colocar ahí.</summary>
    public bool Place(Vector2I cell)
    {
        if (!CanPlace(cell))
            return false;

        _occupied.Add(cell);
        AddCube(cell);
        return true;
    }

    /// <summary>
    /// Pone un cubo de hielo sin comprobar nada (por ejemplo, el que queda debajo de una rampa).
    /// No da peces: eso lo decide quien lo llama.
    /// </summary>
    public void PlaceForced(Vector2I cell)
    {
        _occupied.Add(cell);
        AddCube(cell);
    }

    private void AddCube(Vector2I cell)
    {
        // Cada celda tiene siempre la misma forma y el mismo giro, así no cambia al guardar y cargar.
        uint h = CellHash(cell);
        var variant = _variants[h % (uint)VariantCount];
        float yaw = (h >> 8 & 0xFFFF) / 65535f * 360f;

        // El cuerpo está en el centro del cubo; su cara de arriba queda en SurfaceY.
        var cube = new StaticBody3D
        {
            Position = CellCenter(cell) + new Vector3(0f, -IceCubeSize / 2f, 0f),
            RotationDegrees = new Vector3(0f, yaw, 0f),
        };
        cube.AddChild(new MeshInstance3D { Mesh = variant.Mesh, MaterialOverride = _material });
        cube.AddChild(new CollisionShape3D { Shape = variant.Shape });
        AddChild(cube);
    }

    private static uint CellHash(Vector2I c) =>
        unchecked(((uint)(c.X * 73856093) ^ (uint)(c.Y * 19349663)) * 2654435761u);

    /// <summary>
    /// Un trozo de hielo con los 8 vértices un poco desplazados: ni cuadrado ni igual a los demás.
    /// Devuelve la malla visible y la forma de colisión con los mismos vértices.
    /// </summary>
    private static (ArrayMesh Mesh, ConvexPolygonShape3D Shape) BuildVariant(Random rng)
    {
        const float h = IceCubeSize / 2f;
        const float jitter = 0.05f;
        float[] cx = { -h, h, h, -h };
        float[] cz = { -h, -h, h, h };

        // Vértices 0-3: cara de arriba. Vértices 4-7: cara de abajo, en el mismo orden.
        var v = new Vector3[8];
        for (int i = 0; i < 4; i++)
        {
            v[i] = Shake(rng, cx[i], h, cz[i], jitter);
            v[i + 4] = Shake(rng, cx[i], -h, cz[i], jitter);
        }

        int[][] quads =
        {
            new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 },
            new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 },
            new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
        };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int[] q in quads)
        {
            Vector3 a = v[q[0]], b = v[q[1]], c = v[q[2]], d = v[q[3]];
            Vector3 outward = (a + b + c + d) / 4f; // desde el centro del hielo hacia esta cara
            AddTriangle(st, a, b, c, outward);
            AddTriangle(st, a, c, d, outward);
        }
        st.GenerateNormals();

        return (st.Commit(), new ConvexPolygonShape3D { Points = v });
    }

    /// <summary>Añade un triángulo con el sentido que mira hacia fuera, para que se vea desde fuera.</summary>
    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 n = (b - a).Cross(c - a);
        if (n.Dot(outward) < 0)
            (b, c) = (c, b);

        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static Vector3 Shake(Random rng, float x, float y, float z, float amount) =>
        new(x + Signed(rng) * amount, y + Signed(rng) * amount * 0.5f, z + Signed(rng) * amount);

    private static float Signed(Random rng) => (float)(rng.NextDouble() * 2 - 1);
}