using System;
using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Líneas sobre el agua que marcan el contorno de los sectores cercanos al jugador:
/// blancas los normales, amarillas las parcelas de peces. Solo se dibujan los que están
/// dentro de un radio, para que no se sepa de antemano dónde están las parcelas buenas.
/// Solo son visuales; la lógica está en SectorMap.
/// </summary>
public partial class SectorLines : Node3D
{
    /// <summary>Radio, en unidades del mundo, dentro del cual se ven los sectores.</summary>
    private const float VisibleRadius = 12f;

    /// <summary>Cuánto tiene que moverse el jugador para volver a dibujar las líneas.</summary>
    private const float RedrawDistance = 2f;

    // Justo por encima del agua (y = -0.8) para que se vean sin mezclarse con ella.
    private const float LineY = -0.78f;

    private readonly SectorMap _map;
    private readonly Func<Vector3> _viewer;
    private readonly MeshInstance3D _lines = new();
    private readonly MeshInstance3D _fishLines = new();
    private Vector3 _drawnFrom;
    private bool _drawn;
    private int _drawnReveal;

    public SectorLines(SectorMap map, Func<Vector3> viewer)
    {
        _map = map;
        _viewer = viewer;
    }

    public override void _Ready()
    {
        AddChild(_lines);
        AddChild(_fishLines);
    }

    public override void _Process(double delta)
    {
        Vector3 p = _viewer();
        bool moved = new Vector2(p.X - _drawnFrom.X, p.Z - _drawnFrom.Z).Length() >= RedrawDistance;
        bool revealedChanged = _map.RevealedCount != _drawnReveal;
        if (_drawn && !moved && !revealedChanged)
            return;

        _drawn = true;
        _drawnFrom = p;
        _drawnReveal = _map.RevealedCount;

        // Solo se ven los sectores ya revelados (con algún cubo) y cerca del jugador.
        _lines.Mesh = BuildOutlines(s => !s.IsFish && s.Revealed && IsNear(s, p), new Color(0.95f, 0.98f, 1f));
        _fishLines.Mesh = BuildOutlines(s => s.IsFish && s.Revealed && IsNear(s, p), new Color(1f, 0.85f, 0.1f));
    }

    /// <summary>¿Está el sector dentro del radio visible? Mide hasta el borde más cercano del sector.</summary>
    private static bool IsNear(Sector sector, Vector3 p)
    {
        CellRect r = sector.Rect;
        float x0 = r.X0 * IceGrid.CellSize;
        float x1 = (r.X1 + 1) * IceGrid.CellSize;
        float z0 = r.Z0 * IceGrid.CellSize;
        float z1 = (r.Z1 + 1) * IceGrid.CellSize;

        float nx = Math.Clamp(p.X, x0, x1);
        float nz = Math.Clamp(p.Z, z0, z1);
        return new Vector2(p.X - nx, p.Z - nz).Length() <= VisibleRadius;
    }

    /// <summary>Malla con el contorno de los sectores que cumplen el filtro. Null si no hay ninguno.</summary>
    private ImmediateMesh BuildOutlines(Func<Sector, bool> pick, Color color)
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
        };

        // Si ningún sector cumple el filtro no se abre la superficie (Godot da error con superficies vacías).
        var chosen = new List<Sector>();
        foreach (Sector sector in _map.Sectors)
            if (pick(sector))
                chosen.Add(sector);

        if (chosen.Count == 0)
            return null;

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        foreach (Sector sector in chosen)
        {
            CellRect r = sector.Rect;
            float x0 = r.X0 * IceGrid.CellSize;
            float x1 = (r.X1 + 1) * IceGrid.CellSize;
            float z0 = r.Z0 * IceGrid.CellSize;
            float z1 = (r.Z1 + 1) * IceGrid.CellSize;

            AddSegment(mesh, x0, z0, x1, z0);
            AddSegment(mesh, x1, z0, x1, z1);
            AddSegment(mesh, x1, z1, x0, z1);
            AddSegment(mesh, x0, z1, x0, z0);
        }
        mesh.SurfaceEnd();

        return mesh;
    }

    private static void AddSegment(ImmediateMesh mesh, float ax, float az, float bx, float bz)
    {
        mesh.SurfaceAddVertex(new Vector3(ax, LineY, az));
        mesh.SurfaceAddVertex(new Vector3(bx, LineY, bz));
    }
}