using Godot;

namespace GreatRefreeze;

/// <summary>
/// Icono de papelera dibujado con código (no necesita imágenes ni fuentes especiales).
/// </summary>
public partial class TrashIcon : Control
{
    /// <summary>Color del dibujo.</summary>
    public Color Tint { get; set; } = new Color(0.92f, 0.97f, 1f);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(24, 24);
        MouseFilter = MouseFilterEnum.Ignore; // que el clic lo reciba el botón de debajo
    }

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        const float line = 2f;

        // Dibuja al 60 % del tamaño del control y centrado (un margen del 20 % a cada lado)
        DrawSetTransform(Size * 0.2f, 0f, new Vector2(0.6f, 0.6f));

        // El dibujo ocupa el centro del control (con márgenes iguales a izquierda y derecha)
        // Asa de la tapa
        DrawRect(new Rect2(w * 0.38f, h * 0.10f, w * 0.24f, h * 0.10f), Tint, false, line);
        // Tapa
        DrawRect(new Rect2(w * 0.14f, h * 0.22f, w * 0.72f, h * 0.09f), Tint);
        // Cuerpo del cubo
        DrawRect(new Rect2(w * 0.24f, h * 0.35f, w * 0.52f, h * 0.55f), Tint, false, line);
        // Rayas interiores
        for (int i = 1; i <= 2; i++)
        {
            float x = w * (0.24f + 0.52f * i / 3f);
            DrawLine(new Vector2(x, h * 0.45f), new Vector2(x, h * 0.82f), Tint, line);
        }
    }
}