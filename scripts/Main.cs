using System.Text;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Escena principal del prototipo. Construye el muelle, el agua, la rejilla de hielo,
/// la cubitera, el jugador y el HUD.
/// Colocar hielo: apunta al agua (se ve una sombra) y pulsa clic izquierdo.
/// Uso: escena con un Node3D como raíz y este script asignado.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Distancia máxima a la que se puede colocar hielo.</summary>
    private const float ReachDistance = 8f;

    /// <summary>El muelle ocupa las celdas de -4 a 3 en X y en Z (16 x 16 unidades).</summary>
    private const int DockMinCell = -4;
    private const int DockMaxCell = 3;

    private static readonly Key[] UpgradeKeys = { Key.Key1, Key.Key2, Key.Key3 };

    private readonly GameState _state = new();
    private readonly IceGrid _grid = new();

    private Player _player;
    private MeshInstance3D _ghost;
    private StandardMaterial3D _ghostMaterial;
    private Label _statsLabel;
    private Label _promptLabel;
    private Label _winLabel;

    private bool _aimHit;     // el punto que miras está a alcance
    private bool _aimValid;   // se puede colocar hielo ahí
    private Vector2I _aimCell;

    private string _message = "";
    private double _messageTimer;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildWorld();
        BuildPlayer();
        BuildGhost();
        BuildHud();
    }

    public override void _Process(double delta)
    {
        _state.Tick(delta);
        UpdateAim();
        UpdateHud(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            TryPlaceAtAim();
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        for (int i = 0; i < UpgradeKeys.Length; i++)
        {
            if (key.PhysicalKeycode != UpgradeKeys[i]) continue;

            Upgrade upgrade = _state.Upgrades[i];
            ShowMessage(_state.TryBuy(upgrade)
                ? $"Comprado: {upgrade.Name} (nivel {upgrade.Level})"
                : $"Te faltan {Fmt(upgrade.Cost - _state.Points)} puntos para {upgrade.Name}");
        }
    }

    // ---------------------------------------------------------------- interacción

    private void OnStationUsed(Station station)
    {
        if (station.Kind == StationKind.Tray)
        {
            _state.UseTray();
            ShowMessage($"+{Fmt(_state.IcePerUse)} de hielo");
        }
    }

    private void TryPlaceAtAim()
    {
        if (!_aimHit)
        {
            ShowMessage("Apunta al agua para colocar hielo");
            return;
        }
        if (!_aimValid)
        {
            ShowMessage("No puedes colocar hielo aquí: pégalo a lo que ya está colocado");
            return;
        }
        if (_state.IceCubes < 1)
        {
            ShowMessage("No tienes hielo: usa la cubitera (E)");
            return;
        }
        if (!_grid.Place(_aimCell))
        {
            ShowMessage("No puedes colocar hielo aquí");
            return;
        }

        Vector2I sector = IceGrid.CellToSector(_aimCell);
        _state.TryPlaceCube();

        if (_grid.IsSectorFull(sector))
        {
            _state.AddBonus(GameState.SectorBonus);
            ShowMessage($"¡Sector completado! +{Fmt(GameState.SectorBonus)} puntos de bono");
        }
        else
        {
            ShowMessage($"+{Fmt(_state.PointsPerCube)} puntos");
        }
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageTimer = 2.0;
    }

    /// <summary>Calcula el punto del agua (y = 0) al que apunta la cámara.</summary>
    private void UpdateAim()
    {
        Camera3D cam = _player.Camera;
        Vector3 origin = cam.GlobalPosition;
        Vector3 dir = -cam.GlobalTransform.Basis.Z;

        _aimHit = false;
        _aimValid = false;

        // Solo hay punto en la superficie si miras hacia abajo.
        if (dir.Y < -0.01f)
        {
            float t = -origin.Y / dir.Y;
            if (t > 0 && t <= ReachDistance)
            {
                Vector3 hit = origin + dir * t;
                _aimCell = IceGrid.WorldToCell(hit);
                _aimHit = true;

                // No se puede colocar hielo donde estás de pie.
                _aimValid = _grid.CanPlace(_aimCell)
                            && _aimCell != IceGrid.WorldToCell(_player.GlobalPosition);
            }
        }

        _ghost.Visible = _aimHit;
        if (_aimHit)
        {
            _ghost.Position = IceGrid.CellCenter(_aimCell) + new Vector3(0f, -IceGrid.CubeHeight / 2f, 0f);
            _ghostMaterial.AlbedoColor = _aimValid
                ? new Color(0.3f, 1f, 0.4f, 0.45f)
                : new Color(1f, 0.3f, 0.3f, 0.45f);
        }
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
        // Agua (solo visual). El hielo que se coloque flota sobre ella.
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            Position = new Vector3(0, -0.8f, 0),
            MaterialOverride = Mat(new Color(0.05f, 0.2f, 0.35f)),
        });

        // Muelle: su cara de arriba queda en y = 0, igual que los cubos de hielo.
        var dock = new StaticBody3D { Position = new Vector3(0, -0.5f, 0) };
        dock.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(16, 1, 16) },
            MaterialOverride = Mat(new Color(0.45f, 0.4f, 0.35f)),
        });
        dock.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(16, 1, 16) } });
        AddChild(dock);

        // Las celdas del muelle cuentan como ocupadas: desde ellas empieza el hielo.
        for (int x = DockMinCell; x <= DockMaxCell; x++)
            for (int z = DockMinCell; z <= DockMaxCell; z++)
                _grid.MarkOccupied(new Vector2I(x, z));
        AddChild(_grid);
        AddChild(new SectorLines());

        MakeStation(StationKind.Tray, "[E] Hacer hielo", "Cubitera",
            new Vector3(-3, 0, -4), new Vector3(1.4f, 1.0f, 0.8f), new Color(0.6f, 0.8f, 1f));
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

    /// <summary>Sombra que muestra dónde caería el cubo: verde si se puede, roja si no.</summary>
    private void BuildGhost()
    {
        _ghostMaterial = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _ghost = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = IceGrid.VisualSize },
            MaterialOverride = _ghostMaterial,
            Visible = false,
        };
        AddChild(_ghost);
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

        // Avisos: lo que puedes hacer con lo que miras y mensajes de compra o colocación.
        _promptLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        StyleLabel(_promptLabel, 22);
        hud.AddChild(_promptLabel);
        _promptLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _promptLabel.OffsetBottom = -80;

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
        var text = new StringBuilder();
        text.AppendLine($"Hielo: {_state.IceCubes}     Puntos: {Fmt(_state.Points)}");
        text.AppendLine($"Ártico recongelado: {_state.Progress * 100:0.0}%  ({_state.PlacedCubes} / {GameState.GoalCubes} cubos)");
        text.AppendLine();

        for (int i = 0; i < _state.Upgrades.Count; i++)
        {
            Upgrade u = _state.Upgrades[i];
            text.AppendLine($"[{i + 1}] {u.Name}  nivel {u.Level}  -  {Fmt(u.Cost)} puntos   ({u.Description})");
        }
        // Progreso del sector donde estás.
        Vector2I sector = IceGrid.CellToSector(IceGrid.WorldToCell(_player.GlobalPosition));
        bool onDock = sector.X is >= -1 and <= 0 && sector.Y is >= -1 and <= 0;
        if (onDock)
        {
            text.AppendLine("Estás en el muelle");
        }
        else
        {
            int filled = _grid.CountOccupiedIn(sector);
            int total = IceGrid.SectorCells * IceGrid.SectorCells;
            text.AppendLine($"Sector ({sector.X}, {sector.Y}): {filled} / {total} cubos" + (filled == total ? "  ✓" : ""));
        }

        _statsLabel.Text = text.ToString();

        _messageTimer -= delta;
        var prompt = new StringBuilder();
        if (_player.LookedStation != null)
            prompt.AppendLine(_player.LookedStation.Prompt);
        else if (_aimValid && _state.IceCubes >= 1)
            prompt.AppendLine("[clic izquierdo] Colocar hielo");
        if (_messageTimer > 0)
            prompt.AppendLine(_message);
        _promptLabel.Text = prompt.ToString().TrimEnd();

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