using Godot;

namespace GreatRefreeze;

/// <summary>
/// Líneas blancas sobre el agua que marcan los bordes de los sectores.
/// Solo son visuales: la lógica de sectores está en IceGrid.
/// </summary>
public partial class SectorLines : Node3D
{
    /// <summary>Cuántos sectores hay a cada lado del origen.</summary>
    private const int Range = 6;

    private const float LineWidth = 0.08f;
    private const float LineHeight = 0.02f;

    // Justo por encima del agua (y = -0.8) para que se vean sin mezclarse con ella.
    private const float LineY = -0.78f;

    public override void _Ready()
    {
        float half = Range * IceGrid.SectorUnits;
        float length = 2f * half;

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.85f, 0.95f, 1f),
        };

        // Una malla para las líneas en Z y otra para las líneas en X; se reutilizan.
        var alongZ = new BoxMesh { Size = new Vector3(LineWidth, LineHeight, length) };
        var alongX = new BoxMesh { Size = new Vector3(length, LineHeight, LineWidth) };

        for (int k = -Range; k <= Range; k++)
        {
            float boundary = k * IceGrid.SectorUnits;

            AddChild(new MeshInstance3D
            {
                Mesh = alongZ,
                MaterialOverride = material,
                Position = new Vector3(boundary, LineY, 0f),
            });

            AddChild(new MeshInstance3D
            {
                Mesh = alongX,
                MaterialOverride = material,
                Position = new Vector3(0f, LineY, boundary),
            });
        }
    }
}