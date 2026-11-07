using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Escena principal del prototipo. Construye el muelle, el agua, la rejilla de hielo,
/// la cubitera, el montón de hielo, el cubo (mejora), la máquina de mejoras, la máquina de envíos,
/// los osos, el jugador y el HUD.
/// Cubitera: mantén E frente a ella 2 segundos por carga. El hielo cae en el montón y se derrite poco a poco.
/// Montón: E para coger 1 hielo a la mano (o, con el cubo en la mano, llenar el cubo).
/// Colocar: apunta al agua y pulsa clic izquierdo (o mantenlo con Manos ágiles).
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

    /// <summary>El muelle ocupa 16 x 16 unidades: con celdas de 0,5 son las celdas de -16 a 15.</summary>
    private const int DockMinCell = -16;
    private const int DockMaxCell = 15;

    /// <summary>Mesa del montón: altura de la superficie y medidas de la tabla.</summary>
    private const float TableHeight = 0.9f;
    private const float TableWidth = 3f;
    private const float TableDepth = 1.2f;

    private static readonly Vector3 PileCenter = new(-3f, 0f, -1.8f);   // delante de la cubitera
    private static readonly Vector3 PileSize = new(TableWidth, TableHeight, TableDepth);
    private static readonly Vector3 BucketSize = new(0.7f, 0.6f, 0.7f);
    private static readonly Vector3 BucketRestPosition = new(-1.2f, 0.3f, -4f);  // sobre el muelle
    private static readonly Vector3 BucketHoldPosition = new(0.35f, -0.4f, -0.7f); // delante, a la derecha de la cámara
    private static readonly Color BucketEmptyColor = new(0.95f, 0.95f, 0.97f);
    private static readonly Color BucketFullColor = new(0.2f, 0.5f, 1f);
    private static readonly Color IceColor = new(0.8f, 0.93f, 1f);

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

    private bool _menuOpen;
    private bool _menuJustOpened; // evita que la misma tecla E que abre el menú lo cierre

    private bool _aimHit;     // el punto que miras está a alcance
    private bool _aimValid;   // se puede colocar hielo ahí
    private Vector2I _aimCell;

    private double _placeCooldown;
    private double _chargeTime;   // segundos que llevas manteniendo E en la cubitera

    private Station _bear;      // primer oso polar, sobre el hielo del cubo 50
    private Label3D _bearLabel;
    private bool _bearRescued;

    private Label _fishLabel;   // número de peces, junto al icono de pez

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
            ShowMessage("El montón está vacío: haz hielo en la cubitera");
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

    /// <summary>Carga la cubitera mientras mantienes E. Cada carga completa hace hielo en el montón.</summary>
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
                    ? $"+{Fmt(made)} hielo al montón"
                    : $"La mesa está llena ({GameState.PileCapacity} máximo)");
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
                ? "El cubo está vacío: llénalo desde el montón"
                : "No llevas hielo: coge uno del montón con E");
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

    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageTimer = 2.0;
    }

    // ---------------------------------------------------------------- osos polares

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
        BuildBear();

        MakeStation(StationKind.Tray, "[mantén E] Hacer hielo (cae en el montón)", "Cubitera",
            new Vector3(-3, 0, -4), new Vector3(1.4f, 1.0f, 0.8f), new Color(0.6f, 0.8f, 1f));

        MakeStation(StationKind.Upgrades, "[E] Máquina de mejoras", "Mejoras",
            new Vector3(4, 0, -4), new Vector3(1.6f, 1.6f, 1.6f), new Color(0.9f, 0.6f, 0.2f));

        MakeStation(StationKind.Sell, "[E] Enviar peces a empresas", "Empresas",
            new Vector3(-6, 0, 4), new Vector3(1.6f, 1.4f, 1.6f), new Color(0.2f, 0.7f, 0.6f));

        BuildPile();
        BuildBucket();
    }

    /// <summary>
    /// Montón de hielo junto a la cubitera. Los cubos son solo visuales; el collider sirve para mirarlo y pulsar E.
    /// </summary>
    private void BuildPile()
    {
        _pileMaterial = Mat(IceColor);
        _pileCubeMesh = new BoxMesh { Size = IceGrid.VisualSize };

        _pile = new Station
        {
            Kind = StationKind.Pile,
            Prompt = "[E] Coger hielo del montón",
            Position = new Vector3(PileCenter.X, 0f, PileCenter.Z),
        };
        _pile.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = PileSize },
            Position = new Vector3(0, PileSize.Y / 2f, 0),
        });
        AddTable();

        _pileRoot = new Node3D();
        _pile.AddChild(_pileRoot);

        _pileLabel = new Label3D
        {
            Text = "Montón",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.005f,
        };
        _pile.AddChild(_pileLabel);
        AddChild(_pile);
    }

    /// <summary>La mesa del montón: tablero con 4 patas. Los hielos se apoyan encima.</summary>
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

            foreach (Node child in _pileRoot.GetChildren())
                child.QueueFree();

            for (int i = 0; i < count; i++)
            {
                int col = i % 5;
                int row = i / 5;
                _pileRoot.AddChild(new MeshInstance3D
                {
                    Mesh = _pileCubeMesh,
                    MaterialOverride = _pileMaterial,
                    Position = new Vector3((col - 2f) * 0.55f,
                                           TableHeight + IceGrid.IceCubeSize / 2f,
                                           (row - 0.5f) * 0.55f),
                });
            }
        }

        _pileLabel.Text = $"Montón: {count} / {GameState.PileCapacity}";
        _pileLabel.Position = new Vector3(0, TableHeight + 0.6f, 0);
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
            _ghost.Position = IceGrid.CellCenter(_aimCell) + new Vector3(0f, -IceGrid.VisualSize.Y / 2f, 0f);
            _ghostMaterial.AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.45f);
        }
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

    private static StandardMaterial3D Mat(Color color) => new() { AlbedoColor = color };

    // ---------------------------------------------------------------- HUD

    private void BuildHud()
    {
        var hud = new CanvasLayer();
        AddChild(hud);

        // Arriba, el número de peces con su icono; debajo, el resto de estadísticas.
        var statsBox = new VBoxContainer { Position = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
        hud.AddChild(statsBox);

        var fishRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        statsBox.AddChild(fishRow);
        fishRow.AddChild(new TextureRect
        {
            Texture = FishIcon(),
            CustomMinimumSize = new Vector2(44, 28),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _fishLabel = new Label();
        StyleLabel(_fishLabel, 26);
        fishRow.AddChild(_fishLabel);

        _statsLabel = new Label();
        StyleLabel(_statsLabel, 20);
        statsBox.AddChild(_statsLabel);

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
        RefreshPile();

        // El cubo se ve azul cuando tiene hielo. El hielo de la mano se ve si no llevas el cubo.
        _bucketMaterial.AlbedoColor = _state.BucketIce >= 1 ? BucketFullColor : BucketEmptyColor;
        _handCube.Visible = _state.HandIce > 0 && !_bucketCarried;

        var text = new StringBuilder();
        if (_state.IceCubes > 0)
            text.AppendLine($"Montón: {_state.IceCubes} / {GameState.PileCapacity}     se derrite 1 en {_state.SecondsUntilMelt:0.0} s");
        else
            text.AppendLine($"Montón: 0 / {GameState.PileCapacity}");

        if (_state.HasBucket)
            text.AppendLine($"Cubo: {_state.BucketIce} / {GameState.BucketCapacity}"
                            + (_bucketCarried ? " (en la mano)" : " (en el muelle)"));
        else
            text.AppendLine($"En la mano: {_state.HandIce} / {GameState.HandCapacity}");

        _fishLabel.Text = Fmt(_state.Fish);
        text.AppendLine($"Dinero: {FormatMoney(_state.Money)}");
        text.AppendLine($"Ártico recongelado: {_state.Progress * 100:0.0}%  ({_state.PlacedCubes} / {GameState.GoalCubes} cubos)     Osos polares: {(_bearRescued ? 1 : 0)} / 1");

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