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

    /// <summary>
    /// La mesa crece con cada mejora de capacidad, así los hielos se reparten en el tablero en vez de apilarse muy alto.
    /// Con 200 hielos quedan 4 capas.
    /// </summary>
    private const float TableGrowth = 0.3f;   // cuánto crece la mesa (en ancho y fondo) por cada mejora
    private const float CubeSpacing = 0.55f;  // separación entre hielos sobre la mesa
    private float TableW => TableWidth + TableGrowth * _state.TableCapacityUpgrade.Level;
    private float TableD => TableDepth + TableGrowth * _state.TableCapacityUpgrade.Level;
    private int Cols => Math.Max(1, (int)(TableW / CubeSpacing));
    private int Rows => Math.Max(1, (int)(TableD / CubeSpacing));
    private int PerLayer => Cols * Rows;

    private static readonly Vector3 PileCenter = new(-3f, 0f, -1.8f);   // delante de la cubitera
    private static readonly Vector3 PileSize = new(TableWidth, TableHeight, TableDepth);
    private static readonly Vector3 BucketSize = new(0.7f, 0.6f, 0.7f);
    private static readonly Vector3 BucketRestPosition = new(-1.2f, 0.3f, -4f);  // sobre el muelle
    private static readonly Vector3 BucketHoldPosition = new(0.35f, -0.4f, -0.7f); // delante, a la derecha de la cámara
    private const float BucketRadius = 0.33f;
    private static readonly Color BucketEmptyColor = new(0.78f, 0.8f, 0.84f); // plata
    private static readonly Color BucketFullColor = new(0.25f, 0.5f, 0.95f); // azul acero: tiene hielo
    private static readonly Color IceColor = new(0.6f, 0.82f, 0.96f); // azul hielo, algo más sólido que el agua

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
    private ColorRect _frost;   // escarcha que cubre la pantalla al estar bajo el agua
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
    private int _tableLevelShown = -1;   // nivel de mesa con el que está construida
    private Node3D _tableRoot;

    private MeshInstance3D _handCube;   // hielo que llevas en la mano (sin cubo)

    private Station _bucket;
    private CollisionShape3D _bucketShape;
    private StandardMaterial3D _bucketMaterial;
    private bool _bucketCarried;

    private Station _bear;      // oso polar sentado sobre su plataforma de hielo
    private Label3D _bearLabel;
    private bool _bearRescued;

    private const double AutosaveSeconds = 30;
    private const double AutoPlaceSeconds = 10.0;  // cada cuánto lanza la máquina un hielo de la mesa
    private const int MaxLaunchRadius = 600;       // hasta dónde busca la máquina un punto libre (en celdas)
    private static readonly Vector3 LaunchPoint = new(0, 1.3f, 0); // de dónde sale el hielo, encima de la máquina
    private StaticBody3D _launcher;
    private BoxMesh _launchMesh;
    private readonly HashSet<Vector2I> _inFlight = new(); // celdas a las que ya vuela un hielo
    private double _autoPlaceTimer = AutoPlaceSeconds;
    private int _seed;                  // semilla de las parcelas de peces: se guarda para que la partida sea la misma
    private double _autosaveTimer = AutosaveSeconds;
    private bool _resetting;            // al empezar de cero no se vuelve a guardar

    // Aspecto del Ártico: agua, cielo y sol cambian con el progreso
    private StandardMaterial3D _waterMaterial;
    private ProceduralSkyMaterial _skyMaterial;
    private DirectionalLight3D _sun;
    private double _shownProgress = -1;

    // Sonidos (generados en código, ver Sounds.cs)
    private AudioStream _crackSound;
    private AudioStreamPlayer _crackPlayer;   // un solo reproductor para el crujido: no se apilan sonidos
    private AudioStream _chimeSound;
    private AudioStream _dripSound;

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
        SaveData save = SaveGame.Read();
        _seed = save?.Seed ?? new Random().Next();
        _crackSound = Sounds.Crack();
        _crackPlayer = new AudioStreamPlayer { Stream = _crackSound };
        AddChild(_crackPlayer);
        _chimeSound = Sounds.Chime();
        _dripSound = Sounds.Drip();

        BuildEnvironment();
        BuildWorld();
        BuildPlayer();
        BuildGhost();
        BuildHud();

        if (save != null)
            LoadGame(save);
        else
            DevMode.Apply(_state); // MODO DEV: quitar esta línea al eliminar DevMode.cs (solo en partida nueva)

        _state.Melted += () =>
        {
            ShowMessage("Un hielo del montón se ha derretido");
            PlaySound(_dripSound, -6);
        };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_resetting)
            SaveGameNow();
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
            UpdateAutoPlace(delta);

            bool holding = Input.MouseMode == Input.MouseModeEnum.Captured
                           && Input.IsMouseButtonPressed(MouseButton.Left);
            if (holding && _state.HasAgileHands && _placeCooldown <= 0)
            {
                TryPlaceAtAim(quiet: true);
                _placeCooldown = HoldInterval;
            }
        }

        if (!_resetting)
        {
            _autosaveTimer -= delta;
            if (_autosaveTimer <= 0)
            {
                _autosaveTimer = AutosaveSeconds;
                SaveGameNow();
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

        CarryBucket();
        _state.PutHandIceInBucket();
        ShowMessage("Has cogido el cubo");
    }

    /// <summary>Pone el cubo en la mano del jugador (lo usan al coger el cubo y al cargar la partida).</summary>
    private void CarryBucket()
    {
        _bucketCarried = true;
        _bucketShape.Disabled = true; // en la mano no bloquea ni se mira como estación
        _bucket.Reparent(_player.Camera, false);
        _bucket.Position = BucketHoldPosition;
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
        PlaceCell(_aimCell, quiet);
    }

    /// <summary>
    /// Pone un cubo en la celda: gasta hielo, da peces, bono de sector y efectos.
    /// Lo usan el clic, Manos ágiles y el colocador automático. Devuelve false si no se pudo.
    /// </summary>
    private bool PlaceCell(Vector2I cell, bool quiet, bool fromTable = false)
    {
        if (!_grid.Place(cell))
        {
            if (!quiet) ShowMessage("No puedes colocar hielo aquí");
            return false;
        }

        // El hielo sale de tu mano o cubo, salvo el del lanzador, que ya salió de la mesa al lanzarlo.
        if (fromTable)
            _state.CountPlaced();
        else
            _state.TryPlaceCube(_bucketCarried);

        // Al completar un sector: bono que crece con el anillo. Las parcelas de peces dan además 1 pez por celda.
        Vector3 at = IceGrid.CellCenter(cell) + new Vector3(0f, 0.3f, 0f);
        Sector done = _sectors.RecordPlaced(cell);
        if (done != null)
        {
            double bonus = _state.SectorBonusFor(done.Ring) + (done.IsFish ? done.Free : 0);
            _state.AddFish(bonus);
            ShowMessage(done.IsFish
                ? $"¡Parcela de peces completada! +{Fmt(bonus)} peces"
                : $"¡Sector completado! +{Fmt(bonus)} peces de bono");
            PlaySound(_chimeSound, 0);
            SpawnBurst(at, new Color(1f, 0.85f, 0.3f), 36);
        }
        else
        {
            // Cada cubo tiene su crujido y sus partículas, también con Manos ágiles.
            // El de la máquina es más suave y con menos partículas; el mensaje solo sale al colocar a mano.
            PlayCrack(fromTable ? -18 : quiet ? -10 : -3);
            SpawnBurst(at, new Color(0.9f, 0.97f, 1f), fromTable ? 6 : 12);
            if (!quiet)
                ShowMessage($"+{Fmt(_state.FishPerCube)} peces salvados");
        }
        return true;
    }

    /// <summary>Con el colocador automático, pone un cubo cada segundo (si llevas hielo).</summary>
    private void UpdateAutoPlace(double delta)
    {
        if (!_state.HasAutoPlacer)
            return;

        _autoPlaceTimer -= delta;
        if (_autoPlaceTimer > 0)
            return;

        _autoPlaceTimer = _state.LaunchSeconds;
        AutoPlaceNearPlayer();
    }

    /// <summary>
    /// La máquina lanza un hielo al punto libre más cercano a ella (el centro del muelle).
    /// Coge el hielo directamente de la mesa; si la mesa está vacía, espera.
    /// </summary>
    private void AutoPlaceNearPlayer()
    {
        if (_launcher == null || _state.IceCubes <= 0)
            return;

        Vector2I origin = IceGrid.WorldToCell(_launcher.Position);
        if (!FindNearestFreeCell(origin, out Vector2I target))
            return;

        if (!_state.TakeOneFromPile())
            return;

        LaunchIce(target);
    }

    /// <summary>
    /// Busca la celda libre más cercana al origen, revisando anillos cuadrados de radio creciente.
    /// Se para en el primer anillo que tenga una celda libre (es casi el punto más cercano y muy rápido).
    /// </summary>
    private bool FindNearestFreeCell(Vector2I origin, out Vector2I result)
    {
        for (int r = 1; r <= MaxLaunchRadius; r++)
        {
            bool found = false;
            float best = float.MaxValue;
            result = origin;

            // Solo las celdas del borde del cuadrado de radio r.
            for (int d = -r; d <= r; d++)
            {
                TryCandidate(origin, d, -r, ref found, ref best, ref result);
                TryCandidate(origin, d, r, ref found, ref best, ref result);
                if (d > -r && d < r)
                {
                    TryCandidate(origin, -r, d, ref found, ref best, ref result);
                    TryCandidate(origin, r, d, ref found, ref best, ref result);
                }
            }

            if (found)
                return true;
        }

        result = origin;
        return false;
    }

    private void TryCandidate(Vector2I origin, int dx, int dz, ref bool found, ref float best, ref Vector2I result)
    {
        var cell = new Vector2I(origin.X + dx, origin.Y + dz);
        if (!_grid.CanPlace(cell) || _inFlight.Contains(cell))
            return;

        float dist = dx * dx + dz * dz;
        if (dist < best)
        {
            best = dist;
            result = cell;
            found = true;
        }
    }

    /// <summary>Lanza un cubo de hielo desde la máquina hasta la celda; al llegar se coloca de verdad.</summary>
    private void LaunchIce(Vector2I target)
    {
        Vector3 start = LaunchPoint;
        // El cubo visible tiene el centro medio cubo por debajo de la superficie, igual que al colocarlo.
        Vector3 end = IceGrid.CellCenter(target) + new Vector3(0f, -IceGrid.IceCubeSize / 2f, 0f);

        // Marca verde en la celda de destino hasta que el hielo aterriza.
        var marker = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(IceGrid.CellSize, 0.02f, IceGrid.CellSize) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.45f),
            },
            Position = IceGrid.CellCenter(target) + new Vector3(0f, 0.02f, 0f),
        };
        AddChild(marker);

        var shot = new MeshInstance3D
        {
            Mesh = _launchMesh,
            MaterialOverride = Mat(IceColor),
            Position = start,
        };
        AddChild(shot);
        _inFlight.Add(target);

        // Vuelo en arco: sube y baja hasta la celda en 1,8 s.
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            shot.Position = start.Lerp(end, t) + Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * 2.5f);
        }), 0f, 1f, 1.8);
        tween.TweenCallback(Callable.From(() =>
        {
            shot.QueueFree();
            marker.QueueFree();
            _inFlight.Remove(target);
            if (!PlaceCell(target, quiet: true, fromTable: true))
                _state.ReturnToPile(); // no se pudo colocar: el hielo vuelve a la mesa
        }));
    }

    /// <summary>Máquina lanzadora en el centro del muelle. Aparece al comprar el colocador automático.</summary>
    private void BuildLauncher()
    {
        if (_launcher != null)
            return;

        _launchMesh = new BoxMesh { Size = IceGrid.VisualSize };
        _launcher = new StaticBody3D { Position = Vector3.Zero };
        _launcher.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.0f, 1.2f) },
            MaterialOverride = Mat(new Color(0.35f, 0.45f, 0.55f)),
            Position = new Vector3(0, 0.5f, 0),
        });
        _launcher.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.2f, 1.0f, 1.2f) },
            Position = new Vector3(0, 0.5f, 0),
        });
        _launcher.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.8f) },
            MaterialOverride = Mat(new Color(0.2f, 0.25f, 0.3f)),
            Position = new Vector3(0, 1.15f, 0),
        });
        _launcher.AddChild(new Label3D
        {
            Text = "Lanzador de hielo",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.005f,
            Position = new Vector3(0, 2.0f, 0),
        });
        // Franja luminosa en el frente de la máquina.
        _launcher.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.05f, 0.05f) },
            MaterialOverride = Glow(new Color(0.5f, 0.9f, 1f)),
            Position = new Vector3(0, 0.55f, 0.62f),
        });
        AddChild(_launcher);
    }

    /// <summary>Crujido al colocar. Usa un solo reproductor: si ya suena, lo reinicia en vez de apilar sonidos.</summary>
    private void PlayCrack(float volumeDb)
    {
        _crackPlayer.VolumeDb = volumeDb;
        _crackPlayer.Stop();
        _crackPlayer.Play();
    }

    /// <summary>Reproduce un sonido una vez y lo borra al terminar.</summary>
    private void PlaySound(AudioStream stream, float volumeDb)
    {
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>Estallido de pequeños cubos de hielo que salen hacia arriba y caen. Se borra solo.</summary>
    private void SpawnBurst(Vector3 position, Color color, int amount)
    {
        var particles = new CpuParticles3D
        {
            Position = position,
            Emitting = false,
            OneShot = true,
            Explosiveness = 1f,
            Amount = amount,
            Lifetime = 0.7,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 1.8f,
            Gravity = new Vector3(0, -4f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1f,
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.08f), Material = Mat(color) },
        };
        AddChild(particles);
        particles.Emitting = true;
        GetTree().CreateTimer(1.5).Timeout += particles.QueueFree;
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
        _state.AddMoney(GameState.BearMoneyReward);
        ShowMessage($"¡Oso polar rescatado! +{FormatMoney(GameState.BearMoneyReward)}");

        // Celebración: el oso da dos saltos y sale un estallido de cubitos.
        Vector3 rest = _bear.Position;
        Tween hop = CreateTween();
        for (int i = 0; i < 2; i++)
        {
            hop.TweenProperty(_bear, "position", rest + Vector3.Up * 0.6f, 0.2);
            hop.TweenProperty(_bear, "position", rest, 0.2);
        }
        SpawnBurst(rest + new Vector3(0f, 2.5f, 0f), new Color(0.6f, 0.85f, 1f), 30);
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

    // ---------------------------------------------------------------- partida guardada

    /// <summary>Guarda el estado actual: economía, mejoras, hielo colocado y oso. Se hace cada 30 s y al cerrar.</summary>
    private void SaveGameNow()
    {
        var data = new SaveData
        {
            Seed = _seed,
            BucketCarried = _bucketCarried,
            BearRescued = _bearRescued,
        };
        _state.WriteTo(data);

        foreach (Vector2I cell in _grid.PlacedCells)
            data.Cells.Add(new[] { cell.X, cell.Y });

        SaveGame.Write(data);
    }

    /// <summary>
    /// Restaura una partida guardada. Los peces y el dinero ya están en el guardado, así que los sectores
    /// se recalculan sin volver a dar bonos.
    /// </summary>
    private void LoadGame(SaveData save)
    {
        _state.ReadFrom(save);

        foreach (int[] c in save.Cells)
        {
            var cell = new Vector2I(c[0], c[1]);
            _grid.PlaceForced(cell);       // hielo ya colocado, sin peces
            _sectors.RecordPlaced(cell);   // revela sectores y cuenta cubos (el bono ya estaba cobrado)
        }

        if (_state.Ramps.Level > 0)
            BuildRamps();

        if (_state.HasAutoPlacer)
            BuildLauncher();

        if (_state.HasBucket)
        {
            _bucket.Visible = true;
            _bucketShape.Disabled = false;
            if (save.BucketCarried)
                CarryBucket();
        }

        _bearRescued = save.BearRescued;
        if (_bearRescued)
        {
            _bear.Prompt = "Oso polar rescatado";
            _bearLabel.Text = "Oso polar (rescatado)";
        }

        ShowMessage("Partida cargada");
    }

    /// <summary>Borra el guardado y empieza de cero recargando la escena.</summary>
    private void StartNewGame()
    {
        _resetting = true;
        SaveGame.Delete();
        GetTree().ReloadCurrentScene();
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
        if (bought && upgrade == _state.AutoPlacer)
            BuildLauncher();

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
            bool needsLauncher = u == _state.LauncherSpeed && !_state.HasAutoPlacer;
            bool needsBucket = (u == _state.BucketCapacityUpgrade && !_state.HasBucket) || needsLauncher;

            string status = unlock
                ? (u.IsMaxed ? "desbloqueado" : "bloqueado")
                : $"nivel {u.Level}";
            if (needsBucket)
                status = needsLauncher ? "requiere el lanzador" : "requiere el cubo";
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
            var info = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, // si un texto aún es largo, salta de línea
            };
            var button = new Button { CustomMinimumSize = new Vector2(220, 0) };
            button.Pressed += () => BuyUpgrade(upgrade);

            row.AddChild(info);
            row.AddChild(button);
            grid.AddChild(row);
            _menuRows.Add(new UpgradeRow(upgrade, info, button));
        }

        var reset = new Button { Text = "Nueva partida (borra el guardado)" };
        reset.Pressed += StartNewGame;
        box.AddChild(reset);

        var close = new Button { Text = "Cerrar (E / Esc)" };
        close.Pressed += CloseMenu;
        box.AddChild(close);
    }

    // ---------------------------------------------------------------- mundo

    private void BuildEnvironment()
    {
        _sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -30, 0),
            ShadowEnabled = true,
        };
        AddChild(_sun);

        _skyMaterial = new ProceduralSkyMaterial();
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMaterial },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
        };
        AddChild(new WorldEnvironment { Environment = environment });
    }

    private void BuildWorld()
    {
        // Agua (solo visual). Cubre todo el mundo de sectores. El hielo flota a nivel del agua.
        _waterMaterial = Mat(new Color(0.05f, 0.2f, 0.35f));
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1200, 1200) },
            Position = new Vector3(0, -0.8f, 0),
            MaterialOverride = _waterMaterial,
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
        BuildDockDetails();

        // Las celdas del muelle cuentan como ocupadas: desde ellas empieza el hielo.
        for (int x = DockMinCell; x <= DockMaxCell; x++)
            for (int z = DockMinCell; z <= DockMaxCell; z++)
                _grid.MarkOccupied(new Vector2I(x, z));
        AddChild(_grid);

        BuildBear();
        _sectors = new SectorMap(new Random(_seed), _fixedAreas); // parcelas de peces distintas en cada partida, guardadas por semilla
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
    /// Detalles del muelle: tablones con separaciones y tonos distintos, bolardos en las esquinas y dos cajas con colisión.
    /// Todo queda sobre la superficie del muelle (y = 0), así que no cambia dónde se puede colocar el hielo.
    /// </summary>
    private void BuildDockDetails()
    {
        var rng = new Random(_seed); // tablones con el mismo tono en cada partida guardada
        for (int i = 0; i < 15; i++)
        {
            float shade = 1f + (float)(rng.NextDouble() * 0.16 - 0.08);
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(15f, 0.02f, 0.9f) },
                MaterialOverride = Mat(new Color(0.5f * shade, 0.42f * shade, 0.32f * shade)),
                Position = new Vector3(0, 0.01f, -7f + i),
            });
        }

        var metal = Mat(new Color(0.25f, 0.27f, 0.3f));
        foreach (float x in new[] { -7f, 7f })
            foreach (float z in new[] { -7f, 7f })
                AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.2f, Height = 0.5f },
                    MaterialOverride = metal,
                    Position = new Vector3(x, 0.25f, z),
                });

        AddCrate(new Vector3(6f, 0.3f, -6f), 0.6f);
        AddCrate(new Vector3(6.6f, 0.25f, -5.2f), 0.5f);
    }

    /// <summary>Caja de madera con colisión: se puede subir encima y bordear.</summary>
    private void AddCrate(Vector3 position, float size)
    {
        var crate = new StaticBody3D { Position = position };
        crate.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size, size, size) },
            MaterialOverride = Mat(new Color(0.6f, 0.48f, 0.3f)),
        });
        crate.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size + 0.02f, 0.05f, size + 0.02f) },
            MaterialOverride = Mat(new Color(0.35f, 0.27f, 0.18f)),
            Position = new Vector3(0, size / 2f - 0.02f, 0),
        });
        crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(size, size, size) } });
        AddChild(crate);
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

    /// <summary>
    /// La mesa: tablero con 4 patas. Se (re)construye con el tamaño de su nivel; los hielos se apoyan encima.
    /// Al crecer la mesa, el centro se desplaza hacia el fondo para no tapar la cubitera.
    /// </summary>
    private void AddTable()
    {
        if (_tableRoot != null)
            _tableRoot.QueueFree();

        float w = TableW;
        float d = TableD;
        _pile.Position = new Vector3(PileCenter.X, 0f, PileCenter.Z + (d - TableDepth) / 2f);

        _tableRoot = new Node3D();
        _pile.AddChild(_tableRoot);

        var wood = Mat(new Color(0.55f, 0.4f, 0.25f));
        const float topThickness = 0.1f;

        _tableRoot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, topThickness, d) },
            MaterialOverride = wood,
            Position = new Vector3(0, TableHeight - topThickness / 2f, 0),
        });

        float legHeight = TableHeight - topThickness;
        const float legSize = 0.1f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                _tableRoot.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(legSize, legHeight, legSize) },
                    MaterialOverride = wood,
                    Position = new Vector3(sx * (w / 2f - legSize), legHeight / 2f,
                                           sz * (d / 2f - legSize)),
                });
    }

    /// <summary>Redibuja los hielos de la mesa solo cuando cambia su número (máximo 10, en 2 filas de 5).</summary>
    private void RefreshPile()
    {
        int count = _state.IceCubes;

        // Si has mejorado la mesa, se reconstruye con su nuevo tamaño y los hielos se vuelven a colocar.
        if (_state.TableCapacityUpgrade.Level != _tableLevelShown)
        {
            _tableLevelShown = _state.TableCapacityUpgrade.Level;
            AddTable();
            _pileShown = -1;
        }

        if (count != _pileShown)
        {
            _pileShown = count;

            // Ajusta el collider a la altura real del montón.
            int perLayer = PerLayer;
            int cols = Cols;
            int rows = Rows;
            int stackLayers = (count + perLayer - 1) / perLayer;
            float stackHeight = TableHeight + stackLayers * IceGrid.IceCubeSize;
            ((BoxShape3D)_pileShape.Shape).Size = new Vector3(TableW, stackHeight, TableD);
            _pileShape.Position = new Vector3(0, stackHeight / 2f, 0);

            foreach (Node child in _pileRoot.GetChildren())
                child.QueueFree();

            for (int i = 0; i < count; i++)
            {
                int layer = i / perLayer;
                int within = i % perLayer;
                int col = within % cols;
                int row = within / cols;
                _pileRoot.AddChild(new MeshInstance3D
                {
                    Mesh = _pileCubeMesh,
                    MaterialOverride = _pileMaterial,
                    Position = new Vector3((col - (cols - 1) / 2f) * CubeSpacing,
                                           TableHeight + IceGrid.IceCubeSize / 2f + layer * IceGrid.IceCubeSize,
                                           (row - (rows - 1) / 2f) * CubeSpacing),
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
        // Cubo de metal: cilindro con aro superior y asa. El color cambia según tenga hielo (ver UpdateHud).
        _bucketMaterial = new StandardMaterial3D
        {
            AlbedoColor = BucketEmptyColor,
            Metallic = 0.85f,
            Roughness = 0.3f,
        };
        _bucketShape = new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = BucketRadius, Height = BucketSize.Y },
            Disabled = true,
        };

        _bucket = new Station
        {
            Kind = StationKind.Bucket,
            Prompt = "[E] Coger el cubo",
            Position = BucketRestPosition,
            Visible = false,
        };
        _bucket.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = BucketRadius, BottomRadius = BucketRadius * 0.85f, Height = BucketSize.Y },
            MaterialOverride = _bucketMaterial,
        });
        _bucket.AddChild(new MeshInstance3D // aro superior
        {
            Mesh = new TorusMesh { InnerRadius = BucketRadius - 0.04f, OuterRadius = BucketRadius + 0.02f },
            MaterialOverride = _bucketMaterial,
            Position = new Vector3(0, BucketSize.Y / 2f, 0),
        });
        _bucket.AddChild(new MeshInstance3D // asa, en vertical sobre el cubo
        {
            Mesh = new TorusMesh { InnerRadius = 0.025f, OuterRadius = BucketRadius * 0.85f },
            MaterialOverride = _bucketMaterial,
            Position = new Vector3(0, BucketSize.Y / 2f + 0.05f, 0),
            RotationDegrees = new Vector3(90f, 0f, 0f),
        });
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

        Decorate(station, kind, size, color);
        AddChild(station);
    }

    /// <summary>
    /// Detalles visuales de las máquinas: zócalo oscuro, borde metálico, pantalla luminosa, luces de estado
    /// y una pieza propia de cada una. Todo se dibuja con cajas, así que sigue siendo fácil de tocar.
    /// </summary>
    private void Decorate(Node3D station, StationKind kind, Vector3 size, Color color)
    {
        Color metal = new Color(0.78f, 0.82f, 0.86f);
        Color dark = new Color(0.18f, 0.2f, 0.24f);
        Color screen = kind switch
        {
            StationKind.Tray => new Color(0.5f, 0.85f, 1f),
            StationKind.Upgrades => new Color(1f, 0.7f, 0.25f),
            StationKind.Sell => new Color(0.3f, 1f, 0.6f),
            _ => Colors.White,
        };

        // Zócalo oscuro abajo, borde metálico arriba.
        AddPart(station, new Vector3(size.X + 0.1f, 0.15f, size.Z + 0.1f), new Vector3(0, -size.Y / 2f + 0.075f, 0), dark);
        AddPart(station, new Vector3(size.X + 0.04f, 0.08f, size.Z + 0.04f), new Vector3(0, size.Y / 2f + 0.04f, 0), metal);

        // Pantalla frontal y tres luces de estado.
        AddPart(station, new Vector3(size.X * 0.6f, size.Y * 0.3f, 0.04f), new Vector3(0, size.Y * 0.1f, size.Z / 2f + 0.02f), dark, screen);
        for (int i = 0; i < 3; i++)
            AddPart(station, new Vector3(0.08f, 0.08f, 0.05f), new Vector3(-size.X * 0.3f + i * 0.14f, -size.Y * 0.3f, size.Z / 2f + 0.03f), dark, screen);

        // Pieza propia de cada máquina.
        switch (kind)
        {
            case StationKind.Tray:
                AddPart(station, new Vector3(0.35f, 0.18f, 0.25f), new Vector3(0, -size.Y * 0.2f, size.Z / 2f + 0.12f), metal);           // tubo de salida
                AddPart(station, new Vector3(size.X * 0.9f, 0.04f, 0.3f), new Vector3(0, size.Y / 2f + 0.1f, 0), dark, screen);          // tapa luminosa
                break;

            case StationKind.Upgrades:
                Color[] buttons = { new(0.9f, 0.5f, 0.2f), new(0.3f, 0.7f, 0.9f), new(0.6f, 0.9f, 0.4f) };
                for (int i = 0; i < buttons.Length; i++)
                    AddPart(station, new Vector3(0.12f, 0.12f, 0.05f), new Vector3(0.1f + i * 0.2f, -size.Y * 0.3f, size.Z / 2f + 0.03f), buttons[i], buttons[i]);
                break;

            case StationKind.Sell:
                AddPart(station, new Vector3(size.X * 0.7f, 0.08f, 0.4f), new Vector3(0, -size.Y * 0.2f, size.Z / 2f + 0.16f), dark); // ranura de salida
                break;
        }
    }

    /// <summary>Añade una pieza de caja a una máquina. Si 'glow' existe, la pieza brilla de ese color.</summary>
    private void AddPart(Node3D parent, Vector3 size, Vector3 position, Color color, Color? glow = null)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = glow.HasValue ? Glow(glow.Value) : Mat(color),
            Position = position,
        });
    }

    /// <summary>Material que brilla: para pantallas, luces y franjas luminosas.</summary>
    private static StandardMaterial3D Glow(Color c) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = 1.8f,
    };

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
            // La sombra late suavemente para que se vea dónde caerá el hielo.
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 180f);
            _ghostMaterial.AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.3f + 0.2f * pulse);
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
    /// Busca en un cuadrado de 5 x 5 celdas alrededor de la que miras la más cercana al punto mirado que se pueda colocar.
    /// Así, al correr, el hielo sale aunque el cursor caiga en una celda que no sirve.
    /// </summary>
    private bool FindPlaceableCell(Vector3 hit)
    {
        Vector2I origin = _aimCell;
        bool found = false;
        float best = float.MaxValue;

        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
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

        // Escarcha: se va cubriendo la pantalla mientras estás bajo el agua. Va antes del fundido.
        _frost = new ColorRect
        {
            Color = new Color(0.75f, 0.92f, 1f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(_frost);
        _frost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

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

    /// <summary>
    /// Con más Ártico recongelado el agua se vuelve blanca-azulada, el cielo más claro y el sol más fuerte.
    /// Se usa raíz cuadrada del progreso para que se note desde el principio. Solo se recalcula si cambia el progreso.
    /// </summary>
    private void UpdateArcticLook()
    {
        double progress = _state.Progress;
        if (Math.Abs(progress - _shownProgress) < 0.001)
            return;
        _shownProgress = progress;

        float t = (float)Math.Sqrt(progress);
        _waterMaterial.AlbedoColor = new Color(0.05f, 0.2f, 0.35f).Lerp(new Color(0.75f, 0.86f, 0.94f), t);
        _skyMaterial.SkyTopColor = new Color(0.3f, 0.5f, 0.8f).Lerp(new Color(0.7f, 0.85f, 0.97f), t);
        _skyMaterial.SkyHorizonColor = new Color(0.6f, 0.75f, 0.9f).Lerp(new Color(0.95f, 0.97f, 1f), t);
        _sun.LightEnergy = 1f + 0.4f * t;
    }

    private void UpdateHud(double delta)
    {
        RefreshPile();
        UpdateArcticLook();

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

        // Congelamiento: cuanto más tiempo bajo el agua, más escarcha cubre la pantalla.
        float frost = _player.SubmergedTime > 0 ? (float)(_player.SubmergedTime / Player.DrownSeconds) : 0f;
        _frost.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(frost, 0f, 1f) * 0.65f);

        _warnLabel.Text = _player.SubmergedTime > 0
            ? $"¡Te estás congelando! Sal del agua: {Math.Max(0, Player.DrownSeconds - _player.SubmergedTime):0.0} s"
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