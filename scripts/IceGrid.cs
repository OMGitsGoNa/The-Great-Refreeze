using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Rejilla de celdas cuadradas sobre el agua. Guarda qué celdas están ocupadas (muelle, plataforma o hielo)
/// y la altura de cada hielo. El hielo se dibuja por zonas de 16 x 16 celdas: cada zona es una malla con su
/// colisión, las caras que dan a otro hielo no se dibujan y los vértices se comparten, así no quedan huecos.
/// Al colocar un hielo junto a vecinos más altos hay un 50/50: sube hasta igualarlos o baja un escalón.
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

    /// <summary>Altura que sube cada nivel del montículo.</summary>
    public const float StepHeight = 0.45f;

    /// <summary>Nivel máximo. Con 0,2 por nivel la pendiente entre celdas sigue siendo caminable.</summary>
    public const int MaxLevel = 4;

    /// <summary>Probabilidad de que un hielo junto a vecinos planos empiece un montículo.</summary>
    private const double StartChance = 0.15;

    /// <summary>Lado de una zona de hielo, en celdas. Cada zona es una malla y una colisión.</summary>
    private const int ChunkSize = 16;

    /// <summary>Cuánto puede subir o bajar la superficie por ruido, para que no sea plana.</summary>
    private const float Bumpiness = 0.03f;

    /// <summary>Profundidad extra de los costados que dan al agua: así se ven marcados.</summary>
    private const float SideDepth = 1.2f;

    private static readonly Vector2I[] Neighbors = { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly HashSet<Vector2I> _dock = new(); // celdas fijas (muelle, plataformas): no cuentan como cubos
    private readonly Dictionary<Vector2I, int> _levels = new(); // altura de cada hielo visible (0 = plano)
    private readonly HashSet<Vector2I> _dirty = new(); // zonas que hay que reconstruir en este fotograma
    private readonly Dictionary<Vector2I, StaticBody3D> _chunks = new();
    private readonly Random _rng = new(4242);
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

    /// <summary>Celdas puestas por el jugador con su altura, para guardar la partida.</summary>
    public IEnumerable<(Vector2I Cell, int Level)> PlacedLevels =>
        PlacedCells.Select(c => (c, _levels.GetValueOrDefault(c)));

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
        AddCube(cell, 0);
    }

    /// <summary>Crea un cubo de hielo en la celda. La altura depende de los vecinos. Devuelve false si no se puede.</summary>
    public bool Place(Vector2I cell)
    {
        if (!CanPlace(cell))
            return false;

        _occupied.Add(cell);
        AddCube(cell, ChooseLevel(cell));
        return true;
    }

    /// <summary>
    /// Pone un cubo de hielo sin comprobar nada (por ejemplo, el que queda debajo de una rampa).
    /// No da peces: eso lo decide quien lo llama. Va plano.
    /// </summary>
    public void PlaceForced(Vector2I cell)
    {
        _occupied.Add(cell);
        AddCube(cell, 0);
    }

    /// <summary>Restaura un hielo con su altura exacta al cargar una partida.</summary>
    public void PlaceWithLevel(Vector2I cell, int level)
    {
        _occupied.Add(cell);
        AddCube(cell, Math.Clamp(level, 0, MaxLevel));
    }

    /// <summary>
    /// Altura del nuevo hielo. Si los vecinos son más altos, 50/50: iguala al más alto o baja un escalón.
    /// Si son planos, de vez en cuando empieza un montículo.
    /// </summary>
    private int ChooseLevel(Vector2I cell)
    {
        int highest = 0;
        foreach (Vector2I d in Neighbors)
            if (_levels.TryGetValue(cell + d, out int l))
                highest = Math.Max(highest, l);

        if (highest == 0)
            return _rng.NextDouble() < StartChance ? 1 : 0;

        int level = _rng.NextDouble() < 0.5 ? highest : highest - 1;
        return Math.Min(level, MaxLevel);
    }

    /// <summary>Añade el hielo de la celda con su altura y marca las zonas alrededor para reconstruirlas.</summary>
    private void AddCube(Vector2I cell, int level)
    {
        _levels[cell] = level;

        // La altura de la celda afecta a las esquinas compartidas con las 8 celdas de alrededor.
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                _dirty.Add(ChunkOf(cell + new Vector2I(dx, dz)));
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
                if (!_levels.ContainsKey(cell))
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

        // Costados libres: bajan hasta bastante por debajo del agua, así se ven marcados.
        if (!_levels.ContainsKey(new Vector2I(x, z - 1)))
            AddQuad(st, nw, ne, Bottom(x + 1, z), Bottom(x, z), Vector3.Forward);
        if (!_levels.ContainsKey(new Vector2I(x, z + 1)))
            AddQuad(st, sw, se, Bottom(x + 1, z + 1), Bottom(x, z + 1), Vector3.Back);
        if (!_levels.ContainsKey(new Vector2I(x - 1, z)))
            AddQuad(st, nw, sw, Bottom(x, z + 1), Bottom(x, z), Vector3.Left);
        if (!_levels.ContainsKey(new Vector2I(x + 1, z)))
            AddQuad(st, ne, se, Bottom(x + 1, z + 1), Bottom(x + 1, z), Vector3.Right);
    }

    /// <summary>
    /// Altura de la esquina (x, z): media de las 4 celdas que la tocan (las vacías cuentan como 0),
    /// así todas las celdas que comparten esquina usan la misma altura y no se abren huecos.
    /// </summary>
    private Vector3 Corner(int x, int z)
    {
        int sum = LevelAt(new Vector2I(x - 1, z - 1)) + LevelAt(new Vector2I(x, z - 1))
                + LevelAt(new Vector2I(x - 1, z)) + LevelAt(new Vector2I(x, z));
        float height = SurfaceY + sum / 4f * StepHeight + Bump(x, z);
        return new Vector3(x * CellSize, height, z * CellSize);
    }

    private int LevelAt(Vector2I cell) => _levels.TryGetValue(cell, out int l) ? l : 0;

    /// <summary>
    /// Punto de la base del costado libre en la esquina (x, z). La profundidad varía por esquina
    /// (fija, la misma en todas las partidas) para que los bloques no parezcan cubos perfectos.
    /// </summary>
    private static Vector3 Bottom(int x, int z)
    {
        float variation = (CellHash(new Vector2I(x, z)) % 1000u) / 999f; // 0..1
        float depth = SideDepth * (0.6f + 0.4f * variation);
        return new Vector3(x * CellSize, SurfaceY - IceCubeSize - depth, z * CellSize);
    }

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