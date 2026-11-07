using System;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Líneas sobre el agua que marcan el contorno de cada sector: blancas los normales,
/// amarillas las parcelas de peces. Solo son visuales; la lógica está en SectorMap.
/// </summary>
public partial class SectorLines : Node3D
{
    // Justo por encima del agua (y = -0.8) para que se vean sin mezclarse con ella.
    private const float LineY = -0.78f;

    private readonly SectorMap _map;

    public SectorLines(SectorMap map)
    {
        _map = map;
    }

    public override void _Ready()
    {
        AddChild(BuildOutlines(s => !s.IsFish, new Color(0.95f, 0.98f, 1f)));
        AddChild(BuildOutlines(s => s.IsFish, new Color(1f, 0.85f, 0.1f)));
    }

    private MeshInstance3D BuildOutlines(Func<Sector, bool> pick, Color color)
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
        };

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        foreach (Sector sector in _map.Sectors)
        {
            if (!pick(sector))
                continue;

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

        return new MeshInstance3D { Mesh = mesh };
    }

    private static void AddSegment(ImmediateMesh mesh, float ax, float az, float bx, float bz)
    {
        mesh.SurfaceAddVertex(new Vector3(ax, LineY, az));
        mesh.SurfaceAddVertex(new Vector3(bx, LineY, bz));
    }
}