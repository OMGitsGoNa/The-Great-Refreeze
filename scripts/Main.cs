using System.Collections.Generic;
using System.Text;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Escena principal del prototipo. Construye el muelle, el agua, la rejilla de hielo,
/// la cubitera, el cubo, la máquina de mejoras, la máquina de envíos, los osos, el jugador y el HUD.
/// Colocar hielo: apunta al agua y pulsa clic izquierdo (o mantenlo con Manos ágiles).
/// Cubo: E sobre él para cogerlo y llevarlo en la mano. Mientras lo lleves, el hielo sale de ahí.
/// Cubitera: con el cubo en la mano, mantén E frente a ella 2 segundos por carga.
/// Uso: escena con un Node3D como raíz y este script asignado.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Distancia máxima a la que se puede colocar hielo.</summary>
    private const float ReachDistance = 8f;

    /// <summary>Tiempo entre cubos cuando mantienes el clic (con Manos ágiles).</summary>
    private const float HoldInterval = 0.12f;

    /// <summary>Segundos que hay que mantener E en la cubitera para una carga.</summary>
    private const float ChargeSeconds = 2f;

    /// <summary>El muelle ocupa las celdas de -4 a 3 en X y en Z (16 x 16 unidades).</summary>
    private const int DockMinCell = -4;
    private const int DockMaxCell = 3;

    private static readonly Vector3 BucketSize = new(0.7f, 0.6f, 0.7f);
    private static readonly Vector3 BucketRestPosition = new(-1.2f, 0.3f, -4f);  // sobre el muelle
    private static readonly Vector3 BucketHoldPosition = new(0.35f, -0.4f, -0.7f); // delante, a la derecha de la cámara
    private static readonly Color BucketEmptyColor = new(0.95f, 0.95f, 0.97f);
    private static readonly Color BucketFullColor = new(0.2f, 0.5f, 1f);

    private sealed record UpgradeRow(Upgrade Upgrade, Label Info, Button Button);

    private readonly GameState _state = new();
    private readonly IceGrid _grid = new();
    private readonly List<UpgradeRow> _menuRows = new();

    private Player _player;
    private MeshInstance3D _ghost;
    private StandardMaterial3D _ghostMaterial;
    private Label _statsLabel;
    private Label _promptLabel;
    private Label _winLabel;
    private PanelContainer _menu;
    private ProgressBar _chargeBar;

    private Station _bucket;
    private CollisionShape3D _bucketShape;
    private StandardMaterial3D _bucketMaterial;
    private bool _bucketCarried;

    private bool _menuOpen;
    private bool _menuJustOpened; // evita que la misma tecla E que abre el menú lo cierre

    private bool _aimHit;     // el punto que miras está a alcance
    private bool _aimValid;   // se puede colocar hielo ahí
    private Vector2I _aimCell;

    private double _placeCooldown;
    private double _chargeTime;   // segundos que llevas manteniendo E en la cubitera

    private int _bearCount;

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
        _menuJustOpened = false;
        _placeCooldown -= delta;
        _player.CanSprint = _state.HasSprint;

        if (_menuOpen)
        {
            _aimHit = false;
            _aimValid = false;
            _ghost.Visible = false;
        }
        else
        {
            UpdateAim();
            UpdateCharge(delta);

            bool holding = Input.MouseMode == Input.MouseModeEnum.Captured
                           && Input.IsMouseButtonPressed(MouseButton.Left);
            if (holding && _state.HasAgileHands && _placeCooldown <= 0)
            {
                TryPlaceAtAim(quiet: true);
                _placeCooldown = HoldInterval;
            }
        }

        UpdateHud(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_menuOpen)
        {
            if (!_menuJustOpened && @event is InputEventKey close && close.Pressed && !close.Echo
                && (close.PhysicalKeycode == Key.Escape || close.PhysicalKeycode == Key.E))
            {
                CloseMenu();
            }
            return;
        }

        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            TryPlaceAtAim(quiet: false);
            _placeCooldown = HoldInterval;
        }
    }

    // ---------------------------------------------------------------- interacción

    private void OnStationUsed(Station station)
    {
        switch (station.Kind)
        {
            case StationKind.Bucket:
                PickUpBucket();
                break;

            case StationKind.Upgrades:
                OpenMenu();
                break;

            case StationKind.Sell:
                double earned = _state.SellFish();
                ShowMessage(earned > 0
                    ? $"Peces enviados: +{FormatMoney(earned)}"
                    : "No tienes peces que enviar");
                break;

            // La cubitera no usa E al pulsar: se carga manteniéndolo (ver UpdateCharge).
        }
    }

    private void PickUpBucket()
    {
        if (_bucketCarried)
            return;

        _bucketCarried = true;
        _bucketShape.Disabled = true; // en la mano no bloquea ni se mira como estación
        _bucket.Reparent(_player.Camera, false);
        _bucket.Position = BucketHoldPosition;
        ShowMessage("Has cogido el cubo");
    }

    /// <summary>Carga la cubitera mientras mantienes E con el cubo en la mano.</summary>
    private void UpdateCharge(double delta)
    {
        bool eHeld = Input.IsPhysicalKeyPressed(Key.E);
        bool onTray = _player.LookedStation?.Kind == StationKind.Tray;

        if (eHeld && onTray && !_bucketCarried)
        {
            ShowMessage("Coge el cubo primero (E sobre él)");
        }
        else if (eHeld && onTray && _state.IsBucketFull)
        {
            ShowMessage("El cubo está lleno: úsalo colocando hielo");
        }

        if (eHeld && onTray && _bucketCarried && !_state.IsBucketFull)
        {
            _chargeTime += delta;
            if (_chargeTime >= ChargeSeconds)
            {
                double added = _state.UseTray();
                ShowMessage($"+{Fmt(added)} de hielo al cubo");
                _chargeTime = 0; // si sigues con E, empieza otra carga enseguida
            }
        }
        else
        {
            _chargeTime = 0; // soltar E cancela lo que llevabas cargado
        }

        _chargeBar.Visible = _chargeTime > 0;
        _chargeBar.Value = _chargeTime / ChargeSeconds;
    }

    private void TryPlaceAtAim(bool quiet)
    {
        if (!_aimHit)
        {
            if (!quiet) ShowMessage("Apunta al agua para colocar hielo");
            return;
        }
        if (!_aimValid)
        {
            if (!quiet) ShowMessage("No puedes colocar hielo aquí: pégalo a lo que ya está colocado");
            return;
        }
        if (!_state.HasIceFor(_bucketCarried))
        {
            if (!quiet) ShowMessage(_bucketCarried
                ? "El cubo está vacío: llénalo en la cubitera"
                : "No tienes hielo: coge el cubo y llénalo en la cubitera");
            return;
        }
        if (!_grid.Place(_aimCell))
        {
            if (!quiet) ShowMessage("No puedes colocar hielo aquí");
            return;
        }

        Vector2I sector = IceGrid.CellToSector(_aimCell);
        _state.TryPlaceCube(_bucketCarried);

        if (_grid.IsSectorFull(sector))
        {
            _state.AddFish(GameState.SectorBonus);
            ShowMessage($"¡Sector completado! +{Fmt(GameState.SectorBonus)} peces de bono");
        }
        else if (!quiet)
        {
            ShowMessage($"+{Fmt(_state.FishPerCube)} peces salvados");
        }

        // Cada 50 cubos colocados aparece un oso polar sobre el último cubo.
        if (_state.PlacedCubes % GameState.BearSpacingCubes == 0)
            SpawnBear(_aimCell);
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageTimer = 2.0;
    }

    // ---------------------------------------------------------------- osos polares

    /// <summary>Crea un oso sobre el cubo indicado. Por ahora solo está ahí: su función aún no está definida.</summary>
    private void SpawnBear(Vector2I cell)
    {
        _bearCount++;

        var bear = new Node3D { Position = IceGrid.CellCenter(cell) + new Vector3(0f, 0.5f, 0f) };
        bear.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.2f },
            MaterialOverride = Mat(new Color(0.95f, 0.96f, 0.98f)),
        });
        bear.AddChild(new Label3D
        {
            Text = "Oso polar",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.005f,
            Position = new Vector3(0, 1.1f, 0),
        });
        AddChild(bear);

        ShowMessage("¡Ha aparecido un oso polar!");
    }

    // ---------------------------------------------------------------- menú de mejoras

    private void OpenMenu()
    {
        _menuOpen = true;
        _menuJustOpened = true;
        _menu.Visible = true;
        _player.Frozen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshMenu();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menu.Visible = false;
        _player.Frozen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void BuyUpgrade(Upgrade upgrade)
    {
        ShowMessage(_state.TryBuy(upgrade)
            ? $"Comprado: {upgrade.Name}"
            : $"Te faltan {FormatMoney(upgrade.Cost - _state.Money)} para {upgrade.Name}");
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        foreach (UpgradeRow row in _menuRows)
        {
            Upgrade u = row.Upgrade;
            bool unlock = u.MaxLevel == 1;

            string status = unlock
                ? (u.IsMaxed ? "desbloqueado" : "bloqueado")
                : $"nivel {u.Level}";
            row.Info.Text = $"{u.Name}  ({status})\n{u.Description}";

            if (u.IsMaxed)
            {
                row.Button.Text = unlock ? "Desbloqueado" : "Máximo";
                row.Button.Disabled = true;
            }
            else
            {
                row.Button.Text = $"Comprar: {FormatMoney(u.Cost)}";
                row.Button.Disabled = _state.Money < u.Cost;
            }
        }
    }

    private void BuildMenu(CanvasLayer hud)
    {
        _menu = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(620, 0),
        };
        _menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _menu.GrowHorizontal = Control.GrowDirection.Both;
        _menu.GrowVertical = Control.GrowDirection.Both;
        hud.AddChild(_menu);

        var box = new VBoxContainer();
        _menu.AddChild(box);
        box.AddChild(new Label { Text = "Máquina de mejoras", HorizontalAlignment = HorizontalAlignment.Center });

        foreach (Upgrade upgrade in _state.Upgrades)
        {
            var row = new HBoxContainer();
            var info = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var button = new Button { CustomMinimumSize = new Vector2(220, 0) };
            button.Pressed += () => BuyUpgrade(upgrade);

            row.AddChild(info);
            row.AddChild(button);
            box.AddChild(row);
            _menuRows.Add(new UpgradeRow(upgrade, info, button));
        }

        var close = new Button { Text = "Cerrar (E / Esc)" };
        close.Pressed += CloseMenu;
        box.AddChild(close);
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

        MakeStation(StationKind.Tray, "[mantén E] Cargar hielo en el cubo", "Cubitera",
            new Vector3(-3, 0, -4), new Vector3(1.4f, 1.0f, 0.8f), new Color(0.6f, 0.8f, 1f));

        MakeStation(StationKind.Upgrades, "[E] Máquina de mejoras", "Mejoras",
            new Vector3(4, 0, -4), new Vector3(1.6f, 1.6f, 1.6f), new Color(0.9f, 0.6f, 0.2f));

        MakeStation(StationKind.Sell, "[E] Enviar peces a empresas", "Empresas",
            new Vector3(-6, 0, 4), new Vector3(1.6f, 1.4f, 1.6f), new Color(0.2f, 0.7f, 0.6f));

        BuildBucket();
    }

    /// <summary>El cubo empieza en el muelle. Al cogerlo pasa a la mano y deja de tener colisión.</summary>
    private void BuildBucket()
    {
        _bucketMaterial = Mat(BucketEmptyColor);
        _bucketShape = new CollisionShape3D { Shape = new BoxShape3D { Size = BucketSize } };

        _bucket = new Station
        {
            Kind = StationKind.Bucket,
            Prompt = "[E] Coger el cubo",
            Position = BucketRestPosition,
        };
        _bucket.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = BucketSize }, MaterialOverride = _bucketMaterial });
        _bucket.AddChild(_bucketShape);
        AddChild(_bucket);
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

    /// <summary>Sombra que muestra dónde caería el cubo. Solo aparece donde se puede colocar.</summary>
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

        // La sombra solo aparece donde se puede colocar hielo.
        _ghost.Visible = _aimValid;
        if (_aimValid)
        {
            _ghost.Position = IceGrid.CellCenter(_aimCell) + new Vector3(0f, -IceGrid.CubeHeight / 2f, 0f);
            _ghostMaterial.AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.45f);
        }
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

        // Barra de carga de la cubitera, bajo el centro de la pantalla.
        _chargeBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            Visible = false,
            CustomMinimumSize = new Vector2(300, 18),
        };
        _chargeBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _chargeBar.OffsetTop = -150;
        _chargeBar.OffsetBottom = -132;
        hud.AddChild(_chargeBar);

        // Avisos: lo que puedes hacer con lo que miras y mensajes.
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

        BuildMenu(hud);
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
        // El cubo se ve azul cuando tiene hielo.
        _bucketMaterial.AlbedoColor = _state.BucketIce >= 1 ? BucketFullColor : BucketEmptyColor;

        var text = new StringBuilder();
        text.AppendLine($"Hielo: {_state.IceCubes}     Cubo: {_state.BucketIce:0} / {GameState.BucketCapacity}"
                        + (_bucketCarried ? " (en la mano)" : " (en el muelle)"));
        text.AppendLine($"Peces salvados: {Fmt(_state.Fish)}     Dinero: {FormatMoney(_state.Money)}");
        text.AppendLine($"Ártico recongelado: {_state.Progress * 100:0.0}%  ({_state.PlacedCubes} / {GameState.GoalCubes} cubos)     Osos polares: {_bearCount}");

        // Progreso del sector donde estás.
        Vector2I sector = IceGrid.CellToSector(IceGrid.WorldToCell(_player.GlobalPosition));
        int filled = _grid.CountCubesIn(sector);
        int free = _grid.CountFreeCellsIn(sector);
        text.Append($"Sector ({sector.X}, {sector.Y}): {filled} / {free} cubos");
        if (free > 0 && filled == free)
            text.Append("  ✓");
        _statsLabel.Text = text.ToString();

        _messageTimer -= delta;
        var prompt = new StringBuilder();
        if (!_menuOpen)
        {
            if (_player.LookedStation != null)
                prompt.AppendLine(_player.LookedStation.Prompt);
            else if (_aimValid && _state.HasIceFor(_bucketCarried))
                prompt.AppendLine(_state.HasAgileHands
                    ? "[mantén clic izquierdo] Colocar hielo"
                    : "[clic izquierdo] Colocar hielo");
        }
        if (_messageTimer > 0)
            prompt.AppendLine(_message);
        _promptLabel.Text = prompt.ToString().TrimEnd();

        _winLabel.Visible = _state.Won;

        if (_menuOpen)
            RefreshMenu();
    }

    /// <summary>Formatea números grandes: 1234 -> 1234, 25000 -> 25K, 2500000 -> 2,5M.</summary>
    private static string Fmt(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000:0.##}M";
        if (value >= 10_000) return $"{value / 1_000:0.#}K";
        if (value >= 100) return $"{value:0}";
        return $"{value:0.#}";
    }

    private static string FormatMoney(double value) => $"{value:0.00} $";
}