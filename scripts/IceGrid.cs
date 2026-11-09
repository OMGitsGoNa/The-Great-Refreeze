using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Rejilla de celdas cuadradas sobre el agua. Guarda qué celdas están ocupadas (muelle, plataforma o hielo).
/// El hielo se dibuja por zonas de 16 x 16 celdas: cada zona es una sola malla con su colisión, y las caras
/// que dan a otro hielo no se dibujan, así que los hielos vecinos forman un bloque continuo.
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

    /// <summary>Lado de una zona de hielo, en celdas. Cada zona es una malla y una colisión.</summary>
    private const int ChunkSize = 16;

    /// <summary>Cuánto puede subir o bajar la superficie, para que no sea plana.</summary>
    private const float Bumpiness = 0.03f;

    private static readonly Vector2I[] Neighbors = { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly HashSet<Vector2I> _dock = new(); // celdas fijas (muelle, plataformas): no cuentan como cubos
    private readonly HashSet<Vector2I> _ice = new(); // celdas con hielo visible (incluye plataforma del oso y rampas)
    private readonly HashSet<Vector2I> _dirty = new(); // zonas que hay que reconstruir en este fotograma
    private readonly Dictionary<Vector2I, StaticBody3D> _chunks = new();
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        // Mismo material que el hielo de la mesa (solo color, sin brillo ni metal).
        _material = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.82f, 0.96f) };
    }

    /// <summary>Reconstruye las zonas que han cambiado. Así varios cubos en un mismo fotograma cuestan una sola vez.</summary>
    public override void _Process(double delta)
    {
        if (_dirty.Count == 0)
            return;

        foreach (Vector2I chunk in _dirty)
            RebuildChunk(chunk);
        _dirty.Clear();
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

    /// <summary>Añade el hielo de la celda y marca su zona y las vecinas de otra zona para reconstruirlas.</summary>
    private void AddCube(Vector2I cell)
    {
        _ice.Add(cell);
        Vector2I chunk = ChunkOf(cell);
        _dirty.Add(chunk);

        // Un vecino de otra zona cambia sus costados, así que también se reconstruye.
        foreach (Vector2I d in Neighbors)
        {
            Vector2I other = ChunkOf(cell + d);
            if (other != chunk)
                _dirty.Add(other);
        }
    }

    private static Vector2I ChunkOf(Vector2I cell) =>
        new(Mathf.FloorToInt(cell.X / (float)ChunkSize), Mathf.FloorToInt(cell.Y / (float)ChunkSize));

    /// <summary>Vuelve a construir la malla y la colisión de una zona a partir de sus celdas de hielo.</summary>
    private void RebuildChunk(Vector2I chunk)
    {
        if (_chunks.TryGetValue(chunk, out StaticBody3D old))
        {
            old.QueueFree();
            _chunks.Remove(chunk);
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        bool any = false;
        int x0 = chunk.X * ChunkSize;
        int z0 = chunk.Y * ChunkSize;
        for (int x = x0; x < x0 + ChunkSize; x++)
            for (int z = z0; z < z0 + ChunkSize; z++)
            {
                var cell = new Vector2I(x, z);
                if (!_ice.Contains(cell))
                    continue;

                AddCell(st, cell);
                any = true;
            }

        if (!any)
            return;

        ArrayMesh mesh = st.Commit();
        var body = new StaticBody3D();
        body.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, // el hielo no proyecta sombras propias
        });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        AddChild(body);
        _chunks[chunk] = body;
    }

    /// <summary>
    /// Añade una celda: la cara de arriba siempre, y cada costado solo si el vecino no tiene hielo.
    /// Los vértices de la superficie se comparten con las celdas vecinas, por eso no quedan huecos.
    /// </summary>
    private void AddCell(SurfaceTool st, Vector2I cell)
    {
        int x = cell.X;
        int z = cell.Y;
        Vector3 nw = Corner(x, z);
        Vector3 ne = Corner(x + 1, z);
        Vector3 se = Corner(x + 1, z + 1);
        Vector3 sw = Corner(x, z + 1);

        AddQuad(st, nw, ne, se, sw, Vector3.Up);

        if (!_ice.Contains(new Vector2I(x, z - 1)))
            AddQuad(st, nw, ne, Bottom(x + 1, z), Bottom(x, z), Vector3.Forward);
        if (!_ice.Contains(new Vector2I(x, z + 1)))
            AddQuad(st, sw, se, Bottom(x + 1, z + 1), Bottom(x, z + 1), Vector3.Back);
        if (!_ice.Contains(new Vector2I(x - 1, z)))
            AddQuad(st, nw, sw, Bottom(x, z + 1), Bottom(x, z), Vector3.Left);
        if (!_ice.Contains(new Vector2I(x + 1, z)))
            AddQuad(st, ne, se, Bottom(x + 1, z + 1), Bottom(x + 1, z), Vector3.Right);
    }

    /// <summary>Punto de la superficie en la esquina (x, z) de la rejilla, con una pequeña variación de altura.</summary>
    private static Vector3 Corner(int x, int z) =>
        new(x * CellSize, SurfaceY + Bump(x, z), z * CellSize);

    /// <summary>Punto de la base del hielo (hasta donde llega el cubo) en la esquina (x, z).</summary>
    private static Vector3 Bottom(int x, int z) =>
        new(x * CellSize, SurfaceY - IceCubeSize, z * CellSize);

    /// <summary>Variación de altura fija para cada esquina: la misma en todas las partidas.</summary>
    private static float Bump(int x, int z)
    {
        uint h = CellHash(new Vector2I(x, z));
        return (h % 1000u / 999f - 0.5f) * 2f * Bumpiness;
    }

    private static void AddQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        AddTriangle(st, a, b, c, normal);
        AddTriangle(st, a, c, d, normal);
    }

    /// <summary>
    /// Añade un triángulo con la normal dada y el orden de vértices correcto. Godot considera "de frente" el orden horario,
    /// así que la normal calculada a mano (regla de la mano derecha) tiene que apuntar hacia dentro.
    /// Si no, se ven las caras de dentro y el hielo parece transparente y sin color.
    /// </summary>
    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 n = (b - a).Cross(c - a);
        if (n.Dot(outward) > 0)
            (b, c) = (c, b);

        st.SetNormal(outward);
        st.AddVertex(a);
        st.SetNormal(outward);
        st.AddVertex(b);
        st.SetNormal(outward);
        st.AddVertex(c);
    }

    private static uint CellHash(Vector2I c) =>
        unchecked(((uint)(c.X * 73856093) ^ (uint)(c.Y * 19349663)) * 2654435761u);
}