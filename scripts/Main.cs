using System;
using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Escena principal del prototipo. Construye el muelle, el agua, la rejilla de hielo y los sectores,
/// la cubitera, la mesa con el montón de hielo, el cubo (mejora), la máquina de mejoras, la máquina de envíos,
/// el oso polar, el jugador y el HUD.
/// Cubitera: mantén E frente a ella 2 segundos por carga. El hielo cae en la mesa y se derrite poco a poco.
/// Mesa: E para coger 1 hielo a la mano (o, con el cubo en la mano, llenar el cubo).
/// Colocar: apunta al agua y pulsa clic izquierdo (o mantenlo con Manos ágiles).
/// Uso: escena con un Node3D como raíz y este script asignado.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Distancia máxima a la que se puede colocar hielo.</summary>
    private const float ReachDistance = 8f;

    /// <summary>Tiempo entre cubos cuando mantienes el clic (con Manos ágiles): unos 20 por segundo.</summary>
    private const float HoldInterval = 0.05f;

    /// <summary>Segundos que hay que mantener E en la cubitera para una carga.</summary>
    private const float ChargeSeconds = 2f;

    /// <summary>El muelle ocupa 16 x 16 unidades: con celdas de 0,5 son las celdas de -16 a 15.</summary>
    private const int DockMinCell = SectorMap.DockMin;
    private const int DockMaxCell = SectorMap.DockMax;

    /// <summary>Mesa del montón: altura de la superficie y medidas de la tabla.</summary>
    private const float TableHeight = 0.9f;
    private const float TableWidth = 3f;
    private const float TableDepth = 2.4f;

    /// <summary>Hielos por capa sobre la mesa: 5 columnas x 4 filas. Con 200 hielos hay 10 capas.</summary>
    private const int PerLayer = 20;

    private static readonly Vector3 PileCenter = new(-3f, 0f, -1.8f);   // delante de la cubitera
    private static readonly Vector3 PileSize = new(TableWidth, TableHeight, TableDepth);
    private static readonly Vector3 BucketSize = new(0.7f, 0.6f, 0.7f);
    private static readonly Vector3 BucketRestPosition = new(-1.2f, 0.3f, -4f);  // sobre el muelle
    private static readonly Vector3 BucketHoldPosition = new(0.35f, -0.4f, -0.7f); // delante, a la derecha de la cámara
    private static readonly Color BucketEmptyColor = new(0.95f, 0.95f, 0.97f);
    private static readonly Color BucketFullColor = new(0.2f, 0.5f, 1f);
    private static readonly Color IceColor = new(0.8f, 0.93f, 1f);

    /// <summary>Celdas que ocupan las rampas: fuera del muelle, dos celdas de largo y cuatro de ancho, una por lado.</summary>
    private static readonly CellRect[] RampAreas =
    {
        new(15, -2, 16, 1),
        new(-17, -2, -16, 1),
        new(-2, 15, 1, 16),
        new(-2, -17, 1, -16),
    };

    private sealed record UpgradeRow(Upgrade Upgrade, Label Info, Button Button);

    private readonly GameState _state = new();
    private readonly IceGrid _grid = new();
    private readonly List<UpgradeRow> _menuRows = new();
    private SectorMap _sectors;
    private CellRect _bearPlatform;
    private readonly List<CellRect> _fixedAreas = new(); // zonas que nunca cuentan como cubos: plataforma del oso y rampas
    private CollisionShape3D _pileShape;

    private Player _player;
    private MeshInstance3D _ghost;
    private StandardMaterial3D _ghostMaterial;
    private PanelContainer _menu;
    private ProgressBar _chargeBar;
    private ColorRect _fade;
    private bool _drowning;

    // HUD
    private Label _fishLabel;
    private Label _moneyLabel;
    private Label _progressLabel;
    private Label _sectorLabel;
    private Label _warnLabel;
    private Label _promptLabel;
    private Label _winLabel;
    private Label _handLabel;
    private Label _bucketLabel;
    private PanelContainer _bucketSlot;

    private Station _pile;
    private Node3D _pileRoot;
    private Label3D _pileLabel;
    private BoxMesh _pileCubeMesh;
    private StandardMaterial3D _pileMaterial;
    private int _pileShown = -1;

    private MeshInstance3D _handCube;   // hielo que llevas en la mano (sin cubo)

    private Station _bucket;
    private CollisionShape3D _bucketShape;
    private StandardMaterial3D _bucketMaterial;
    private bool _bucketCarried;

    private Station _bear;      // oso polar sentado sobre su plataforma de hielo
    private Label3D _bearLabel;
    private bool _bearRescued;

    private bool _menuOpen;
    private bool _menuJustOpened; // evita que la misma tecla E que abre el menú lo cierre

    private bool _aimHit;     // el punto que miras está a alcance
    private bool _aimValid;   // se puede colocar hielo ahí
    private Vector2I _aimCell;

    private double _placeCooldown;
    private double _chargeTime;   // segundos que llevas manteniendo E en la cubitera

    private string _message = "";
    private double _messageTimer;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildWorld();
        BuildPlayer();
        BuildGhost();
        BuildHud();
        DevMode.Apply(_state); // MODO DEV: quitar esta línea al eliminar DevMode.cs

        _state.Melted += () => ShowMessage("Un hielo del montón se ha derretido");
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
            case StationKind.Pile:
                TakeFromPile();
                break;

            case StationKind.Bucket:
                PickUpBucket();
                break;

            case StationKind.Bear:
                RescueBear();
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

    /// <summary>Coge hielo del montón: 1 a la mano, o llena el cubo si lo llevas.</summary>
    private void TakeFromPile()
    {
        if (_state.IceCubes <= 0)
        {
            ShowMessage("La mesa está vacía: haz hielo en la cubitera");
            return;
        }

        int moved = _state.TakeFromPile(_bucketCarried);
        if (moved > 0)
            ShowMessage(_bucketCarried ? $"+{moved} hielo al cubo" : "Has cogido un hielo");
        else
            ShowMessage(_bucketCarried ? "El cubo está lleno" : "Ya llevas un hielo en la mano");
    }

    private void PickUpBucket()
    {
        if (_bucketCarried || !_state.HasBucket)
            return;

        _bucketCarried = true;
        _bucketShape.Disabled = true; // en la mano no bloquea ni se mira como estación
        _state.PutHandIceInBucket();
        _bucket.Reparent(_player.Camera, false);
        _bucket.Position = BucketHoldPosition;
        ShowMessage("Has cogido el cubo");
    }

    /// <summary>Carga la cubitera mientras mantienes E. Cada carga completa hace hielo en la mesa.</summary>
    private void UpdateCharge(double delta)
    {
        bool eHeld = Input.IsPhysicalKeyPressed(Key.E);
        bool onTray = _player.LookedStation?.Kind == StationKind.Tray;

        if (eHeld && onTray)
        {
            _chargeTime += delta;
            if (_chargeTime >= ChargeSeconds)
            {
                double made = _state.ProduceFromTray();
                ShowMessage(made > 0
                    ? $"+{Fmt(made)} hielo a la mesa"
                    : $"La mesa está llena ({_state.PileMax} máximo)");
                _chargeTime = 0; // si sigues con E, empieza otra carga enseguida
            }
        }
        else
        {
            _chargeTime = 0; // soltar E cancela la carga a medias
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
        if (!_state.HasIceToPlace(_bucketCarried))
        {
            if (!quiet) ShowMessage(_bucketCarried
                ? "El cubo está vacío: llénalo desde la mesa"
                : "No llevas hielo: coge uno de la mesa con E");
            return;
        }
        if (!_grid.Place(_aimCell))
        {
            if (!quiet) ShowMessage("No puedes colocar hielo aquí");
            return;
        }

        _state.TryPlaceCube(_bucketCarried);

        // Al completar un sector: bono que crece con el anillo. Las parcelas de peces dan además 1 pez por celda.
        Sector done = _sectors.RecordPlaced(_aimCell);
        if (done != null)
        {
            double bonus = _state.SectorBonusFor(done.Ring) + (done.IsFish ? done.Free : 0);
            _state.AddFish(bonus);
            ShowMessage(done.IsFish
                ? $"¡Parcela de peces completada! +{Fmt(bonus)} peces"
                : $"¡Sector completado! +{Fmt(bonus)} peces de bono");
        }
        else if (!quiet)
        {
            ShowMessage($"+{Fmt(_state.FishPerCube)} peces salvados");
        }
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageTimer = 2.0;
    }

    // ---------------------------------------------------------------- congelarse en el agua

    /// <summary>
    /// Te has pasado 5 s bajo el agua: te congelas, pantalla a negro, pierdes el 10% de peces y dinero
    /// y apareces en la base.
    /// </summary>
    private void OnDrowned()
    {
        if (_drowning)
            return;

        _drowning = true;
        _player.Frozen = true;

        Tween tween = CreateTween();
        tween.TweenProperty(_fade, "modulate:a", 1f, 0.8f);
        tween.TweenCallback(Callable.From(() =>
        {
            _state.ApplyDrowningPenalty();
            _player.Respawn();
            ShowMessage("Te has congelado en el agua: -10% de peces y dinero");
        }));
        tween.TweenInterval(0.3f);
        tween.TweenProperty(_fade, "modulate:a", 0f, 0.8f);
        tween.TweenCallback(Callable.From(() =>
        {
            _player.Frozen = false;
            _drowning = false;
        }));
    }

    // ---------------------------------------------------------------- oso polar

    /// <summary>Rescata al oso gastando peces. No cuesta dinero.</summary>
    private void RescueBear()
    {
        if (_bearRescued)
            return;

        if (!_state.TrySpendFish(GameState.BearFishCost))
        {
            ShowMessage($"Te faltan {Fmt(GameState.BearFishCost - _state.Fish)} peces para rescatar al oso");
            return;
        }

        _bearRescued = true;
        _bear.Prompt = "Oso polar rescatado";
        _bearLabel.Text = "Oso polar (rescatado)";
        ShowMessage("¡Oso polar rescatado!");
    }

    /// <summary>
    /// Cuatro rampas pequeñas, una por lado del muelle. Bajan desde la altura del muelle hasta
    /// la superficie del hielo colocado (1 unidad de largo, 2 de ancho). Se compran en la máquina de mejoras.
    /// </summary>
    private void BuildRamps()
    {
        const float rise = 0.75f;   // del muelle (y = 0) a la superficie del hielo (y = -0.75)
        const float run = 1f;       // largo horizontal de la rampa
        const float thickness = 0.2f;
        float length = Mathf.Sqrt(run * run + rise * rise);
        float angle = Mathf.Atan(rise / run);
        float centerY = -rise / 2f - thickness / 2f;
        Color color = new(0.45f, 0.4f, 0.35f);

        AddRamp(new Vector3(7.5f + run / 2f, centerY, 0f), new Vector3(length, thickness, 2f), new Vector3(0, 0, -angle), color);
        AddRamp(new Vector3(-7.5f - run / 2f, centerY, 0f), new Vector3(length, thickness, 2f), new Vector3(0, 0, angle), color);
        AddRamp(new Vector3(0f, centerY, 7.5f + run / 2f), new Vector3(2f, thickness, length), new Vector3(angle, 0, 0), color);
        AddRamp(new Vector3(0f, centerY, -7.5f - run / 2f), new Vector3(2f, thickness, length), new Vector3(-angle, 0, 0), color);

    }

    /// <summary>Al comprar las rampas: se crean y sus celdas dejan de contar como hielo colocable.</summary>
    private void BuildRampsPurchased()
    {
        BuildRamps();
        // Las celdas de las rampas cuentan como hielo colocado de su sector. Si ya había hielo, no se cuenta dos veces.
        foreach (CellRect area in RampAreas)
            for (int x = area.X0; x <= area.X1; x++)
                for (int z = area.Z0; z <= area.Z1; z++)
                {
                    var cell = new Vector2I(x, z);
                    if (_grid.IsOccupied(cell))
                        continue;

                    _grid.PlaceForced(cell); // hielo visible debajo de la rampa, sin peces por ese cubo
                    Sector done = _sectors.RecordPlaced(cell);
                    if (done != null)
                    {
                        double bonus = _state.SectorBonusFor(done.Ring) + (done.IsFish ? done.Free : 0);
                        _state.AddFish(bonus);
                    }
                }
    }


    private void AddRamp(Vector3 center, Vector3 size, Vector3 rotation, Color color)
    {
        var ramp = new StaticBody3D { Position = center, Rotation = rotation };
        ramp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(color) });
        ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(ramp);
    }

    /// <summary>
    /// El oso polar ya existe desde el inicio: sentado y triste sobre una plataforma de 3 x 3 cubos de hielo,
    /// con el borde más cercano a 50 cubos del muelle. La plataforma es hielo fijo: no cuenta como cubo colocado.
    /// </summary>
    private void BuildBear()
    {
        const int platformSize = 3;
        const int centerZ = 0;
        int minX = DockMaxCell + GameState.BearDistanceCubes;   // primera columna: a 50 del muelle
        int centerX = minX + platformSize / 2;
        int minZ = centerZ - platformSize / 2;

        _bearPlatform = new CellRect(minX, minZ, minX + platformSize - 1, minZ + platformSize - 1);
        _fixedAreas.Add(_bearPlatform);

        for (int x = minX; x < minX + platformSize; x++)
            for (int z = minZ; z < minZ + platformSize; z++)
                _grid.PlaceFixed(new Vector2I(x, z));

        StandardMaterial3D fur = Mat(new Color(0.97f, 0.97f, 1f));
        StandardMaterial3D dark = Mat(new Color(0.08f, 0.08f, 0.1f));

        _bear = new Station
        {
            Kind = StationKind.Bear,
            Prompt = $"[E] Rescatar oso polar ({Fmt(GameState.BearFishCost)} peces)",
            Position = IceGrid.CellCenter(new Vector2I(centerX, centerZ)),
        };

        // Cuerpo sentado (esfera aplastada) y cabeza.
        _bear.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.2f, Height = 2.4f },
            MaterialOverride = fur,
            Position = new Vector3(0, 1.2f, 0),
            Scale = new Vector3(1f, 0.9f, 1f),
        });
        _bear.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.75f, Height = 1.5f },
            MaterialOverride = fur,
            Position = new Vector3(0, 2.4f, 0.2f),
        });

        // Ojos y cejas caídas por dentro (tristes).
        foreach (float sx in new[] { -1f, 1f })
        {
            _bear.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f },
                MaterialOverride = dark,
                Position = new Vector3(sx * 0.28f, 2.5f, 0.85f),
            });
            _bear.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.06f, 0.06f) },
                MaterialOverride = dark,
                Position = new Vector3(sx * 0.28f, 2.68f, 0.86f),
                Rotation = new Vector3(0f, 0f, -sx * 0.4f),
            });
        }

        // Boca hacia abajo.
        foreach (float sx in new[] { -1f, 1f })
            _bear.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.25f, 0.05f, 0.05f) },
                MaterialOverride = dark,
                Position = new Vector3(sx * 0.16f, 2.2f, 0.9f),
                Rotation = new Vector3(0f, 0f, sx * 0.5f),
            });

        _bear.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2.6f, 2.8f, 2.4f) },
            Position = new Vector3(0, 1.4f, 0),
        });

        _bearLabel = new Label3D
        {
            Text = "Oso polar",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,
            PixelSize = 0.005f,
            Position = new Vector3(0, 3.5f, 0),
        };
        _bear.AddChild(_bearLabel);
        AddChild(_bear);
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
        bool bought = _state.TryBuy(upgrade);
        if (bought && upgrade == _state.Ramps)
            BuildRampsPurchased();

        if (bought && upgrade == _state.Bucket)
        {
            // El cubo aparece en el muelle; hay que ir a cogerlo con E.
            _bucket.Visible = true;
            _bucketShape.Disabled = false;
            ShowMessage("Comprado: Cubo. Está en el muelle, cógelo con E");
        }
        else
        {
            ShowMessage(bought
                ? $"Comprado: {upgrade.Name}"
                : $"Te faltan {FormatMoney(upgrade.Cost - _state.Money)} para {upgrade.Name}");
        }
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        foreach (UpgradeRow row in _menuRows)
        {
            Upgrade u = row.Upgrade;
            bool unlock = u.MaxLevel == 1;
            bool needsBucket = u == _state.BucketCapacityUpgrade && !_state.HasBucket;

            string status = unlock
                ? (u.IsMaxed ? "desbloqueado" : "bloqueado")
                : $"nivel {u.Level}";
            if (needsBucket)
                status = "requiere el cubo";
            row.Info.Text = $"{u.Name}  ({status})\n{u.Description}";

            if (u.IsMaxed)
            {
                row.Button.Text = unlock ? "Desbloqueado" : "Máximo";
                row.Button.Disabled = true;
            }
            else
            {
                row.Button.Text = $"Comprar: {FormatMoney(u.Cost)}";
                row.Button.Disabled = _state.Money < u.Cost || needsBucket;
            }
        }
    }

    private void BuildMenu(CanvasLayer hud)
    {
        _menu = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(960, 0),
        };
        _menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _menu.GrowHorizontal = Control.GrowDirection.Both;
        _menu.GrowVertical = Control.GrowDirection.Both;
        hud.AddChild(_menu);

        var box = new VBoxContainer();
        _menu.AddChild(box);
        box.AddChild(new Label { Text = "Máquina de mejoras", HorizontalAlignment = HorizontalAlignment.Center });

        // Dos columnas: cada mejora ocupa una celda de la cuadrícula.
        var grid = new GridContainer { Columns = 2 };
        box.AddChild(grid);

        foreach (Upgrade upgrade in _state.Upgrades)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
            var info = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var button = new Button { CustomMinimumSize = new Vector2(220, 0) };
            button.Pressed += () => BuyUpgrade(upgrade);

            row.AddChild(info);
            row.AddChild(button);
            grid.AddChild(row);
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
        // Agua (solo visual). Cubre todo el mundo de sectores. El hielo flota a nivel del agua.
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1200, 1200) },
            Position = new Vector3(0, -0.8f, 0),
            MaterialOverride = Mat(new Color(0.05f, 0.2f, 0.35f)),
        });

        // Muelle: su cara de arriba queda en y = 0.
        var dock = new StaticBody3D { Position = new Vector3(0, -0.5f, 0) };
        dock.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(15, 1, 15) },
            MaterialOverride = Mat(new Color(0.45f, 0.4f, 0.35f)),
        });
        dock.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(15, 1, 15) } });
        AddChild(dock);

        // Las celdas del muelle cuentan como ocupadas: desde ellas empieza el hielo.
        for (int x = DockMinCell; x <= DockMaxCell; x++)
            for (int z = DockMinCell; z <= DockMaxCell; z++)
                _grid.MarkOccupied(new Vector2I(x, z));
        AddChild(_grid);

        BuildBear();
        _sectors = new SectorMap(new Random(), _fixedAreas); // parcelas de peces distintas en cada partida
        AddChild(new SectorLines(_sectors, () => _player.GlobalPosition));

        MakeStation(StationKind.Tray, "[mantén E] Hacer hielo (cae en la mesa)", "Cubitera",
            new Vector3(-3, 0, -4), new Vector3(1.4f, 1.0f, 0.8f), new Color(0.6f, 0.8f, 1f));

        MakeStation(StationKind.Upgrades, "[E] Máquina de mejoras", "Mejoras",
            new Vector3(4, 0, -4), new Vector3(1.6f, 1.6f, 1.6f), new Color(0.9f, 0.6f, 0.2f));

        MakeStation(StationKind.Sell, "[E] Enviar peces a empresas", "Empresas",
            new Vector3(-6, 0, 4), new Vector3(1.6f, 1.4f, 1.6f), new Color(0.2f, 0.7f, 0.6f));

        BuildPile();
        BuildBucket();
    }

    /// <summary>
    /// Mesa con el montón de hielo junto a la cubitera. Los cubos son solo visuales; el collider sirve para mirarla y pulsar E.
    /// </summary>
    private void BuildPile()
    {
        _pileMaterial = Mat(IceColor);
        _pileCubeMesh = new BoxMesh { Size = IceGrid.VisualSize };

        _pile = new Station
        {
            Kind = StationKind.Pile,
            Prompt = "[E] Coger hielo de la mesa",
            Position = new Vector3(PileCenter.X, 0f, PileCenter.Z),
        };
        // El collider crece con el montón para que puedas apuntar al hielo que se apoya en la mesa.
        _pileShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = PileSize },
            Position = new Vector3(0, PileSize.Y / 2f, 0),
        };
        _pile.AddChild(_pileShape);
        AddTable();

        _pileRoot = new Node3D();
        _pile.AddChild(_pileRoot);

        _pileLabel = new Label3D
        {
            Text = "Mesa",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.005f,
        };
        _pile.AddChild(_pileLabel);
        AddChild(_pile);
    }

    /// <summary>La mesa: tablero con 4 patas. Los hielos se apoyan encima.</summary>
    private void AddTable()
    {
        var wood = Mat(new Color(0.55f, 0.4f, 0.25f));
        const float topThickness = 0.1f;

        _pile.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(TableWidth, topThickness, TableDepth) },
            MaterialOverride = wood,
            Position = new Vector3(0, TableHeight - topThickness / 2f, 0),
        });

        float legHeight = TableHeight - topThickness;
        const float legSize = 0.1f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                _pile.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(legSize, legHeight, legSize) },
                    MaterialOverride = wood,
                    Position = new Vector3(sx * (TableWidth / 2f - legSize), legHeight / 2f,
                                           sz * (TableDepth / 2f - legSize)),
                });
    }

    /// <summary>Redibuja los hielos de la mesa solo cuando cambia su número (máximo 10, en 2 filas de 5).</summary>
    private void RefreshPile()
    {
        int count = _state.IceCubes;
        if (count != _pileShown)
        {
            _pileShown = count;

            // Ajusta el collider a la altura real del montón.
            int stackLayers = (count + PerLayer - 1) / PerLayer;
            float stackHeight = TableHeight + stackLayers * IceGrid.IceCubeSize;
            ((BoxShape3D)_pileShape.Shape).Size = new Vector3(TableWidth, stackHeight, TableDepth);
            _pileShape.Position = new Vector3(0, stackHeight / 2f, 0);

            foreach (Node child in _pileRoot.GetChildren())
                child.QueueFree();

            for (int i = 0; i < count; i++)
            {
                int layer = i / PerLayer;
                int within = i % PerLayer;
                int col = within % 5;
                int row = within / 5;
                _pileRoot.AddChild(new MeshInstance3D
                {
                    Mesh = _pileCubeMesh,
                    MaterialOverride = _pileMaterial,
                    Position = new Vector3((col - 2f) * 0.55f,
                                           TableHeight + IceGrid.IceCubeSize / 2f + layer * IceGrid.IceCubeSize,
                                           (row - 1.5f) * 0.55f),
                });
            }
        }

        // Cuenta atrás del siguiente derretimiento, solo si hay hielo.
        _pileLabel.Text = count > 0
            ? $"{count} / {_state.PileMax}  ·  {_state.SecondsUntilMelt:0} s"
            : $"0 / {_state.PileMax}";
        int layers = (count + PerLayer - 1) / PerLayer;
        _pileLabel.Position = new Vector3(0, TableHeight + 0.6f + layers * IceGrid.IceCubeSize, 0);
    }

    /// <summary>El cubo empieza oculto. Aparece en el muelle al comprarlo; al cogerlo pasa a la mano.</summary>
    private void BuildBucket()
    {
        _bucketMaterial = Mat(BucketEmptyColor);
        _bucketShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = BucketSize },
            Disabled = true,
        };

        _bucket = new Station
        {
            Kind = StationKind.Bucket,
            Prompt = "[E] Coger el cubo",
            Position = BucketRestPosition,
            Visible = false,
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
        _player.Drowned += OnDrowned;

        // Hielo en la mano, visible delante de la cámara cuando no llevas el cubo.
        _handCube = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = IceGrid.VisualSize },
            MaterialOverride = Mat(IceColor),
            Position = BucketHoldPosition,
            Visible = false,
        };
        _player.Camera.AddChild(_handCube);
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

    /// <summary>Calcula el punto de la superficie del hielo al que apunta la cámara.</summary>
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
            float t = (IceGrid.SurfaceY - origin.Y) / dir.Y;
            if (t > 0 && t <= ReachDistance)
            {
                Vector3 hit = origin + dir * t;
                _aimCell = IceGrid.WorldToCell(hit);
                _aimHit = true;

                // Orden: 1) la celda recta delante de ti; 2) la que miras; 3) la vecina más cercana al punto mirado.
                Vector2I straight = StraightAheadCell();
                if (CanPlaceAt(straight))
                {
                    _aimCell = straight;
                    _aimValid = true;
                }
                else
                {
                    _aimValid = CanPlaceAt(_aimCell) || FindPlaceableCell(hit);
                }
            }
        }

        // La sombra solo aparece donde se puede colocar hielo.
        _ghost.Visible = _aimValid;
        if (_aimValid)
        {
            _ghost.Position = IceGrid.CellCenter(_aimCell) + new Vector3(0f, -IceGrid.VisualSize.Y / 2f, 0f);
            _ghostMaterial.AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.45f);
        }
    }

    /// <summary>Celda recta delante del jugador, en horizontal, justo al lado de sus pies.</summary>
    private Vector2I StraightAheadCell()
    {
        Vector3 forward = -_player.GlobalTransform.Basis.Z;
        forward.Y = 0;
        forward = forward.Normalized();
        Vector3 ahead = _player.GlobalPosition + forward * IceGrid.CellSize * 1.5f;
        return IceGrid.WorldToCell(ahead);
    }

    /// <summary>
    /// Se puede colocar hielo en la celda: está libre, toca hielo o muelle, y no está justo bajo tus pies.
    /// </summary>
    private bool CanPlaceAt(Vector2I cell)
    {
        if (!_grid.CanPlace(cell))
            return false;

        Vector3 center = IceGrid.CellCenter(cell);
        Vector3 feet = _player.GlobalPosition;
        return new Vector2(center.X - feet.X, center.Z - feet.Z).Length() > 0.45f;
    }

    /// <summary>
    /// Busca entre las 8 celdas que rodean a la que miras la más cercana al punto mirado que se pueda colocar.
    /// Así, al correr, el hielo sale aunque el cursor caiga en una celda que no sirve.
    /// </summary>
    private bool FindPlaceableCell(Vector3 hit)
    {
        Vector2I origin = _aimCell;
        bool found = false;
        float best = float.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                var cell = new Vector2I(origin.X + dx, origin.Y + dz);
                if (!CanPlaceAt(cell))
                    continue;

                Vector3 c = IceGrid.CellCenter(cell);
                float d = new Vector2(c.X - hit.X, c.Z - hit.Z).LengthSquared();
                if (d < best)
                {
                    best = d;
                    _aimCell = cell;
                    found = true;
                }
            }

        return found;
    }

    private static StandardMaterial3D Mat(Color color) => new() { AlbedoColor = color };

    // ---------------------------------------------------------------- HUD

    private void BuildHud()
    {
        var hud = new CanvasLayer();
        AddChild(hud);

        // Arriba a la izquierda: peces (con icono) y dinero.
        var topLeft = new VBoxContainer { Position = new Vector2(16, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        hud.AddChild(topLeft);

        var fishRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        topLeft.AddChild(fishRow);
        fishRow.AddChild(new TextureRect
        {
            Texture = FishIcon(),
            CustomMinimumSize = new Vector2(40, 24),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _fishLabel = new Label();
        StyleLabel(_fishLabel, 24);
        fishRow.AddChild(_fishLabel);

        _moneyLabel = new Label();
        StyleLabel(_moneyLabel, 16);
        topLeft.AddChild(_moneyLabel);

        // Arriba a la derecha: progreso del Ártico.
        _progressLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        StyleLabel(_progressLabel, 16);
        hud.AddChild(_progressLabel);
        _progressLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _progressLabel.OffsetLeft = -360;
        _progressLabel.OffsetRight = -16;
        _progressLabel.OffsetTop = 16;

        // Abajo a la izquierda: sector actual.
        _sectorLabel = new Label();
        StyleLabel(_sectorLabel, 14);
        hud.AddChild(_sectorLabel);
        _sectorLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _sectorLabel.OffsetLeft = 16;
        _sectorLabel.OffsetTop = -40;
        _sectorLabel.OffsetBottom = -16;

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
            CustomMinimumSize = new Vector2(300, 14),
        };
        _chargeBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _chargeBar.OffsetTop = -150;
        _chargeBar.OffsetBottom = -136;
        hud.AddChild(_chargeBar);

        // Aviso de que te ahogas, bajo el centro.
        _warnLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        StyleLabel(_warnLabel, 26);
        hud.AddChild(_warnLabel);
        _warnLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _warnLabel.OffsetTop = 120;
        _warnLabel.OffsetBottom = 160;
        _warnLabel.OffsetLeft = -300;
        _warnLabel.OffsetRight = 300;

        // Inventario abajo al centro: mano y cubo (el cubo aparece al cogerlo).
        var inventory = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(inventory);
        inventory.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        inventory.GrowHorizontal = Control.GrowDirection.Both;
        inventory.OffsetLeft = -160;
        inventory.OffsetRight = 160;
        inventory.OffsetTop = -76;
        inventory.OffsetBottom = -28;

        inventory.AddChild(MakeSlot(out _handLabel));
        _bucketSlot = MakeSlot(out _bucketLabel);
        _bucketSlot.Visible = false;
        inventory.AddChild(_bucketSlot);

        // Avisos: lo que puedes hacer con lo que miras y mensajes.
        _promptLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        StyleLabel(_promptLabel, 20);
        hud.AddChild(_promptLabel);
        _promptLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _promptLabel.OffsetBottom = -100;

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

        // Fundido a negro al congelarse. Va el último para tapar todo.
        _fade = new ColorRect
        {
            Color = Colors.Black,
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(_fade);
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>Una casilla del inventario: recuadro oscuro con un texto.</summary>
    private static PanelContainer MakeSlot(out Label label)
    {
        label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        StyleLabel(label, 18);

        var slot = new PanelContainer { CustomMinimumSize = new Vector2(140, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddChild(label);
        return slot;
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
        RefreshPile();

        // El cubo se ve azul cuando tiene hielo. El hielo de la mano se ve si no llevas el cubo.
        _bucketMaterial.AlbedoColor = _state.BucketIce >= 1 ? BucketFullColor : BucketEmptyColor;
        _handCube.Visible = _state.HandIce > 0 && !_bucketCarried;

        _fishLabel.Text = Fmt(_state.Fish);
        _moneyLabel.Text = FormatMoney(_state.Money);
        _progressLabel.Text = $"Ártico {_state.Progress * 100:0.0}%";

        _handLabel.Text = $"Mano  {_state.HandIce} / {GameState.HandCapacity}";
        _bucketSlot.Visible = _bucketCarried;
        _bucketLabel.Text = $"Cubo  {_state.BucketIce} / {_state.BucketMax}";

        Sector here = _sectors.At(IceGrid.WorldToCell(_player.GlobalPosition));
        _sectorLabel.Text = here == null ? ""
            : !here.Revealed ? "Sector sin explorar"
            : $"Anillo {here.Ring}  ·  {here.Filled} / {here.Free}"
              + (here.IsFish ? "  ·  parcela de peces" : "")
              + (here.Completed ? "  ✓" : "");

        _warnLabel.Text = _player.SubmergedTime > 0
            ? $"¡Sal del agua! {Math.Max(0, Player.DrownSeconds - _player.SubmergedTime):0.0} s"
            : "";

        _messageTimer -= delta;
        var prompt = new System.Text.StringBuilder();
        if (!_menuOpen)
        {
            if (_player.LookedStation != null)
                prompt.AppendLine(_player.LookedStation.Prompt);
            else if (_aimValid && _state.HasIceToPlace(_bucketCarried))
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

    /// <summary>Icono de pez dibujado en código, sin imágenes externas.</summary>
    private static Texture2D FishIcon()
    {
        const int w = 36;
        const int h = 22;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var body = new Color(0.4f, 0.72f, 1f);
        var eye = new Color(0.05f, 0.05f, 0.1f);

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float dx = (x - 14f) / 13f;
                float dy = (y - 11f) / 8f;
                bool inBody = dx * dx + dy * dy <= 1f;
                bool inTail = x >= 26 && Mathf.Abs(y - 11f) <= (x - 25) * 0.9f;
                if (inBody || inTail)
                    image.SetPixel(x, y, body);
            }

        for (int x = 9; x <= 10; x++)
            for (int y = 9; y <= 10; y++)
                image.SetPixel(x, y, eye);

        return ImageTexture.CreateFromImage(image);
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