using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// La rejilla de celdas cuadradas sobre el agua. Guarda qué celdas están ocupadas (muelle o hielo)
/// y crea los cubos de hielo, que son sólidos: el jugador puede caminar por encima.
/// La celda (x, z) cubre el mundo desde (x * CellSize, z * CellSize) hasta (x + 1, z + 1) * CellSize.
/// </summary>
public partial class IceGrid : Node3D
{
    public const float CellSize = 0.5f; // igual que el lado de un cubo de hielo
    public const float CubeHeight = 1f;

    /// <summary>Un sector son 10 x 10 celdas.</summary>
    public const int SectorCells = 10;

    /// <summary>Lado de un sector en unidades del mundo (5 con los valores actuales).</summary>
    public const float SectorUnits = SectorCells * CellSize;

    /// <summary>Lado de un cubo de hielo visible. Es el mismo tamaño que los cubos de la mesa del montón.</summary>
    public const float IceCubeSize = 0.5f;

    /// <summary>Tamaño visual de un cubo (también lo usa la sombra de previsualización).</summary>
    public static readonly Vector3 VisualSize = new(IceCubeSize, IceCubeSize, IceCubeSize);

    private static readonly Vector2I[] Neighbors = { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly HashSet<Vector2I> _dock = new(); // celdas del muelle: no se pueden convertir en hielo

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

    /// <summary>Sector al que pertenece una celda. Usa redondeo hacia abajo: funciona en negativo.</summary>
    public static Vector2I CellToSector(Vector2I cell) =>
        new(Mathf.FloorToInt((float)cell.X / SectorCells), Mathf.FloorToInt((float)cell.Y / SectorCells));

    /// <summary>Centro de la celda a la altura de la superficie (y = 0).</summary>
    public static Vector3 CellCenter(Vector2I cell) =>
        new((cell.X + 0.5f) * CellSize, 0f, (cell.Y + 0.5f) * CellSize);

    public bool IsOccupied(Vector2I cell) => _occupied.Contains(cell);

    /// <summary>Marca una celda del muelle como ocupada sin crear hielo.</summary>
    public void MarkOccupied(Vector2I cell)
    {
        _occupied.Add(cell);
        _dock.Add(cell);
    }

    /// <summary>Cubos de hielo colocados en un sector (sin contar el muelle).</summary>
    public int CountCubesIn(Vector2I sector)
    {
        int count = 0;
        for (int x = 0; x < SectorCells; x++)
            for (int z = 0; z < SectorCells; z++)
            {
                var cell = new Vector2I(sector.X * SectorCells + x, sector.Y * SectorCells + z);
                if (_occupied.Contains(cell) && !_dock.Contains(cell)) count++;
            }
        return count;
    }

    /// <summary>Celdas de un sector donde se puede poner hielo (todas menos las del muelle).</summary>
    public int CountFreeCellsIn(Vector2I sector)
    {
        int count = 0;
        for (int x = 0; x < SectorCells; x++)
            for (int z = 0; z < SectorCells; z++)
            {
                var cell = new Vector2I(sector.X * SectorCells + x, sector.Y * SectorCells + z);
                if (!_dock.Contains(cell)) count++;
            }
        return count;
    }

    /// <summary>Un sector está completo cuando tiene cubos en todas sus celdas libres.</summary>
    public bool IsSectorFull(Vector2I sector) => CountCubesIn(sector) >= CountFreeCellsIn(sector);

    /// <summary>Se puede colocar si la celda está libre y toca (en cruz) una celda ocupada.</summary>
    public bool CanPlace(Vector2I cell)
    {
        if (_occupied.Contains(cell))
            return false;

        foreach (Vector2I direction in Neighbors)
        {
            if (_occupied.Contains(cell + direction))
                return true;
        }
        return false;
    }

    /// <summary>Crea un cubo de hielo en la celda. Devuelve false si no se puede colocar ahí.</summary>
    public bool Place(Vector2I cell)
    {
        if (!CanPlace(cell))
            return false;

        _occupied.Add(cell);

        // La cara de arriba queda en y = 0, a la misma altura que el muelle.
        var cube = new StaticBody3D { Position = CellCenter(cell) + new Vector3(0f, -CubeHeight / 2f, 0f) };
        // El cubo visible queda apoyado sobre la superficie (y = 0); la colisión ocupa toda la celda.
        cube.AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            MaterialOverride = _material,
            Position = new Vector3(0f, CubeHeight / 2f - IceCubeSize / 2f, 0f),
        });
        cube.AddChild(new CollisionShape3D { Shape = _shape });
        AddChild(cube);
        return true;
    }
}