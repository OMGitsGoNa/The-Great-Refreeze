using System;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Mapa 2D de los sectores. La base va en marrón, los sectores completos en verde claro
/// y los pendientes en azul; todos llevan su contorno. La rueda del ratón amplía o reduce
/// (hacia donde apuntas) y arrastrando con el botón izquierdo se mueve el mapa.
/// Solo es visual: la lógica está en SectorMap.
/// </summary>
public partial class SectorMapView : Control
{
    private readonly SectorMap _map;
    private readonly Func<Vector3> _playerPosition;

    private float _scale = 4f;        // píxeles por celda
    private Vector2 _center;          // celda del mundo que queda en el centro de la vista
    private bool _dragging;
    private Sector _hover;            // sector bajo el ratón (para la ayuda al pasar por encima)
    private Vector2 _mouse;           // posición del ratón en pantalla

    private const float MaxScale = 40f;

    private static readonly Color Background = new(0.04f, 0.07f, 0.13f, 0.94f);
    private static readonly Color FilledColor = new(0.72f, 1f, 0.72f);   // sector completo: verde claro
    private static readonly Color OpenColor = new(0.25f, 0.45f, 0.95f);  // sector por rellenar: azul
    private static readonly Color Outline = new(0.95f, 0.98f, 1f);
    private static readonly Color DockColor = new(0.55f, 0.45f, 0.32f);  // la base

    public SectorMapView(SectorMap map, Func<Vector3> playerPosition)
    {
        _map = map;
        _playerPosition = playerPosition;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Ajusta la vista para ver todo el mundo de una vez.</summary>
    public void FitWorld()
    {
        CellRect world = SectorMap.World;
        float cells = world.X1 - world.X0 + 1;
        _scale = Mathf.Min(Size.X, Size.Y) / cells;
        _center = Vector2.Zero;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw(); // el jugador se mueve y se completan sectores mientras el mapa está abierto
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
                Zoom(1.25f, button.Position);
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
                Zoom(0.8f, button.Position);
            else if (button.ButtonIndex == MouseButton.Left)
                _dragging = button.Pressed;
        }
        else if (@event is InputEventMouseMotion motion)
        {
            _mouse = motion.Position;
            if (_dragging)
                _center -= motion.Relative / _scale;

            Vector2 world = ToWorld(_mouse);
            _hover = _map.At(new Vector2I(Mathf.FloorToInt(world.X), Mathf.FloorToInt(world.Y)));
            QueueRedraw();
        }
    }

    /// <summary>Amplía o reduce manteniendo fija la celda que hay bajo el ratón.</summary>
    private void Zoom(float factor, Vector2 anchor)
    {
        Vector2 before = ToWorld(anchor);
        float fit = Mathf.Min(Size.X, Size.Y) / (SectorMap.World.X1 - SectorMap.World.X0 + 1);
        _scale = Mathf.Clamp(_scale * factor, fit * 0.9f, MaxScale);
        Vector2 after = ToWorld(anchor);
        _center += before - after;
        QueueRedraw();
    }

    /// <summary>Pasa de píxeles de pantalla a celdas del mundo.</summary>
    private Vector2 ToWorld(Vector2 screen) => _center + (screen - Size / 2f) / _scale;

    /// <summary>Pasa de celdas del mundo a píxeles de pantalla.</summary>
    private Vector2 ToScreen(Vector2 cell) => Size / 2f + (cell - _center) * _scale;

    /// <summary>Rectángulo en pantalla de un rango de celdas (los dos extremos incluidos).</summary>
    private Rect2 ScreenRect(CellRect r)
    {
        Vector2 a = ToScreen(new Vector2(r.X0, r.Z0));
        Vector2 b = ToScreen(new Vector2(r.X1 + 1, r.Z1 + 1));
        return new Rect2(a, b - a);
    }

    public override void _Draw()
    {
        var view = new Rect2(Vector2.Zero, Size);
        DrawRect(view, Background);

        foreach (Sector sector in _map.Sectors)
        {
            Rect2 rect = ScreenRect(sector.Rect);
            if (!rect.Intersects(view))
                continue;

            DrawRect(rect, sector.Completed ? FilledColor : OpenColor);
            DrawRect(rect, Outline, false, 1.5f); // contorno de cada sector
        }

        // La base, por encima de los sectores.
        Rect2 dock = ScreenRect(SectorMap.Dock);
        DrawRect(dock, DockColor);
        DrawRect(dock, Outline, false, 2f);
        DrawString(ThemeDB.FallbackFont, dock.Position + new Vector2(4, 18), "Base",
            HorizontalAlignment.Left, -1, 16, Outline);

        // Dónde estás tú.
        Vector3 p = _playerPosition();
        Vector2 cell = new Vector2(p.X / IceGrid.CellSize, p.Z / IceGrid.CellSize);
        DrawCircle(ToScreen(cell), 5f, new Color(1f, 0.85f, 0.2f));

        if (_hover != null)
            DrawHover();
    }

    /// <summary>Resalta el sector bajo el ratón y muestra un recuadro con sus datos.</summary>
    private void DrawHover()
    {
        DrawRect(ScreenRect(_hover.Rect), new Color(1f, 1f, 1f, 0.22f));
        DrawRect(ScreenRect(_hover.Rect), new Color(1f, 1f, 1f), false, 3f);

        // Solo se dice que es parcela de peces si ya la has empezado (si no, sería un spoiler).
        var lines = new System.Collections.Generic.List<string>
        {
            $"Anillo {_hover.Ring}",
            $"Rellenas {_hover.Filled} de {_hover.Free}",
        };
        if (_hover.Completed)
            lines.Add("Completo");
        if (_hover.IsFish && _hover.Revealed)
            lines.Add("Parcela de peces");

        const float lineHeight = 22f;
        Vector2 size = new(210f, 16f + lines.Count * lineHeight);
        Vector2 pos = _mouse + new Vector2(18f, 18f);
        pos.X = Mathf.Min(pos.X, Size.X - size.X - 8f);
        pos.Y = Mathf.Min(pos.Y, Size.Y - size.Y - 8f);

        DrawRect(new Rect2(pos, size), new Color(0.03f, 0.06f, 0.11f, 0.95f));
        DrawRect(new Rect2(pos, size), new Color(0.6f, 0.85f, 1f), false, 1.5f);
        for (int i = 0; i < lines.Count; i++)
            DrawString(ThemeDB.FallbackFont, pos + new Vector2(12f, 26f + i * lineHeight), lines[i],
                HorizontalAlignment.Left, -1, 16, new Color(0.92f, 0.97f, 1f));
    }
}