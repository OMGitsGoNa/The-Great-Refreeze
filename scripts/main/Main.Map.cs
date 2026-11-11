using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- mapa de sectores (tecla M)

    private Control _mapPanel;
    private SectorMapView _mapView;
    private bool _mapOpen;

    /// <summary>Crea el mapa (oculto). Se llama al montar los menús.</summary>
    private void BuildMap()
    {
        _mapPanel = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _mapPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(_mapPanel);

        _mapView = new SectorMapView(_sectors, () => _player.GlobalPosition);
        _mapView.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mapPanel.AddChild(_mapView);

        var hint = new Label
        {
            Text = "Mapa de sectores  ·  Rueda: ampliar / reducir  ·  Arrastrar: mover  ·  M o Esc: cerrar",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 1f));
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        hint.OffsetTop = 14;
        hint.OffsetBottom = 40;
        _mapPanel.AddChild(hint);
    }

    /// <summary>Abre el mapa. El mundo sigue corriendo, pero el jugador no se mueve.</summary>
    private void OpenMap()
    {
        _mapOpen = true;
        _mapPanel.Visible = true;
        _mapView.FitWorld();
        _player.Frozen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseMap()
    {
        _mapOpen = false;
        _mapPanel.Visible = false;
        _player.Frozen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}