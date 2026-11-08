using System.Collections.Generic;
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

    private BoxMesh _mesh;
    private BoxShape3D _shape;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        _mesh = new BoxMesh { Size = VisualSize };
        _shape = new BoxShape3D { Size = new Vector3(CellSize, CubeHeight, CellSize) };
        _material = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.93f, 1f) };
    }

    public static Vector2I WorldToCell(Vector3 point) =>
        new(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Z / CellSize));

    /// <summary>Centro de la celda a la altura de la superficie del hielo.</summary>
    public static Vector3 CellCenter(Vector2I cell) =>
        new((cell.X + 0.5f) * CellSize, SurfaceY, (cell.Y + 0.5f) * CellSize);

    public bool IsOccupied(Vector2I cell) => _occupied.Contains(cell);

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
        // El cubo visible queda con su cara de arriba en SurfaceY; la colisión ocupa toda la celda.
        var cube = new StaticBody3D { Position = CellCenter(cell) + new Vector3(0f, -CubeHeight / 2f, 0f) };
        cube.AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            MaterialOverride = _material,
            Position = new Vector3(0f, CubeHeight / 2f - IceCubeSize / 2f, 0f),
        });
        cube.AddChild(new CollisionShape3D { Shape = _shape });
        AddChild(cube);
    }
}