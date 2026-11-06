using Godot;

namespace GreatRefreeze;

/// <summary>
/// Escena principal del prototipo. Construye el mundo en código (muelle, agua, hielo,
/// estaciones, jugador y HUD) y conecta el jugador con la lógica de GameState.
/// Uso: escena con un Node3D como raíz y este script asignado.
/// </summary>
public partial class Main : Node3D
{
    private readonly GameState _state = new();

    private Player _player;
    private MeshInstance3D _iceSheet;
    private Label _statsLabel;
    private Label _promptLabel;
    private Label _winLabel;

    private string _message = "";
    private double _messageTimer;

    // Teclas 1, 2 y 3 compran las mejoras en el mismo orden que GameState.Upgrades.
    private static readonly Key[] UpgradeKeys = { Key.Key1, Key.Key2, Key.Key3 };

    public override void _Ready()
    {
        BuildEnvironment();
        BuildWorld();
        BuildPlayer();
        BuildHud();
    }

    public override void _Process(double delta)
    {
        _state.Tick(delta);
        UpdateIceSheet();
        UpdateHud(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        for (int i = 0; i < UpgradeKeys.Length; i++)
        {
            if (key.PhysicalKeycode != UpgradeKeys[i]) continue;

            Upgrade upgrade = _state.Upgrades[i];
            ShowMessage(_state.TryBuy(upgrade)
                ? $"Comprado: {upgrade.Name} (nivel {upgrade.Level})"
                : $"Te faltan {Fmt(upgrade.Cost - _state.Money)} $ para {upgrade.Name}");
        }
    }

    // ---------------------------------------------------------------- interacción

    private void OnStationUsed(Station station)
    {
        switch (station.Kind)
        {
            case StationKind.Tray:
                _state.UseTray();
                ShowMessage($"+{Fmt(_state.IcePerUse)} de hielo");
                break;

            case StationKind.Deposit:
                double placed = _state.Deposit();
                ShowMessage(placed > 0 ? $"Has colocado {Fmt(placed)} de hielo" : "No llevas hielo");
                break;
        }
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageTimer = 2.0;
    }

    // ---------------------------------------------------------------- mundo

    private void BuildEnvironment()
    {
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -30, 0),
            ShadowEnabled = true,
        });

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
        };
        AddChild(new WorldEnvironment { Environment = environment });
    }

    private void BuildWorld()
    {
        // Agua (solo visual).
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            Position = new Vector3(0, -0.8f, 0),
            MaterialOverride = Mat(new Color(0.05f, 0.2f, 0.35f)),
        });

        // Placa de hielo que crece con el progreso. Es un cilindro que se escala.
        _iceSheet = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.3f },
            Position = new Vector3(0, -0.65f, 0),
            MaterialOverride = Mat(new Color(0.85f, 0.95f, 1f)),
        };
        AddChild(_iceSheet);

        // Muelle donde está el jugador (la parte de arriba queda en y = 0).
        var dock = new StaticBody3D { Position = new Vector3(0, -0.5f, 0) };
        dock.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(16, 1, 16) },
            MaterialOverride = Mat(new Color(0.45f, 0.4f, 0.35f)),
        });
        dock.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(16, 1, 16) } });
        AddChild(dock);

        // Estaciones.
        MakeStation(StationKind.Tray, "[E] Hacer hielo", "Cubitera",
            new Vector3(-3, 0, -4), new Vector3(1.4f, 1.0f, 0.8f), new Color(0.6f, 0.8f, 1f));

        MakeStation(StationKind.Deposit, "[E] Colocar el hielo en el Ártico", "Ártico",
            new Vector3(3, 0, -4), new Vector3(1.6f, 1.2f, 1.6f), new Color(1f, 1f, 1f));
    }

    private void MakeStation(StationKind kind, string prompt, string title, Vector3 position, Vector3 size, Color color)
    {
        var station = new Station
        {
            Kind = kind,
            Prompt = prompt,
            Position = new Vector3(position.X, size.Y / 2f, position.Z), // apoyada en el muelle
        };

        station.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(color) });
        station.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        station.AddChild(new Label3D
        {
            Text = title,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,
            PixelSize = 0.005f,
            Position = new Vector3(0, size.Y / 2f + 0.6f, 0),
        });

        AddChild(station);
    }

    private void BuildPlayer()
    {
        _player = new Player { Position = new Vector3(0, 0.1f, 2) };
        AddChild(_player);
        _player.StationUsed += OnStationUsed;
    }

    private void UpdateIceSheet()
    {
        // La raíz cuadrada hace que el crecimiento se note ya al principio.
        float radius = 8.5f + 91.5f * Mathf.Sqrt((float)_state.Progress);
        _iceSheet.Scale = new Vector3(radius, 1f, radius);
    }

    private static StandardMaterial3D Mat(Color color) => new() { AlbedoColor = color };

    // ---------------------------------------------------------------- HUD

    private void BuildHud()
    {
        var hud = new CanvasLayer();
        AddChild(hud);

        _statsLabel = new Label { Position = new Vector2(16, 16) };
        StyleLabel(_statsLabel, 20);
        hud.AddChild(_statsLabel);

        // Mira en el centro de la pantalla.
        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(crosshair, 28);
        hud.AddChild(crosshair);
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Aviso al mirar una estación y mensajes de compra.
        _promptLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        StyleLabel(_promptLabel, 22);
        hud.AddChild(_promptLabel);
        _promptLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _promptLabel.OffsetBottom = -80;

        // Mensaje de victoria.
        _winLabel = new Label
        {
            Text = "¡Has recongelado el Ártico!",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        StyleLabel(_winLabel, 48);
        hud.AddChild(_winLabel);
        _winLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _winLabel.OffsetBottom = -200;
    }

    private static void StyleLabel(Label label, int fontSize)
    {
        label.MouseFilter = Control.MouseFilterEnum.Ignore; // que el HUD no se coma los clics
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
    }

    private void UpdateHud(double delta)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Hielo: {Fmt(_state.Ice)}     Dinero: {Fmt(_state.Money)} $");
        text.AppendLine($"Ártico recongelado: {_state.Progress * 100:0.00}%  ({Fmt(_state.PlacedIce)} / {Fmt(GameState.GoalIce)})");
        text.AppendLine();

        for (int i = 0; i < _state.Upgrades.Count; i++)
        {
            Upgrade u = _state.Upgrades[i];
            text.AppendLine($"[{i + 1}] {u.Name}  nivel {u.Level}  -  {Fmt(u.Cost)} $   ({u.Description})");
        }
        _statsLabel.Text = text.ToString();

        _messageTimer -= delta;
        string prompt = _player.LookedStation?.Prompt ?? "";
        if (_messageTimer > 0)
            prompt = prompt.Length > 0 ? prompt + "\n" + _message : _message;
        _promptLabel.Text = prompt;

        _winLabel.Visible = _state.Won;
    }

    /// <summary>Formatea números grandes: 1234 -> 1234, 25000 -> 25K, 2500000 -> 2,5M.</summary>
    private static string Fmt(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000:0.##}M";
        if (value >= 10_000) return $"{value / 1_000:0.#}K";
        if (value >= 100) return $"{value:0}";
        return $"{value:0.#}";
    }
}
