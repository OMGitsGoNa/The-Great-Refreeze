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
    // Intervalo de Manos ágiles: lo calcula GameState.PlaceInterval (0,25 s base, baja con Colocación rápida).

    /// <summary>Segundos que hay que mantener E en la cubitera para una carga.</summary>
    // Segundos para hacer hielo con la cubitera: los da GameState.ChargeSeconds (baja con Cubitera rápida).

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
    private readonly Random _luck = new(); // suerte del hielo azul
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
    private StandardMaterial3D _launcherGlow; // franja del lanzador: cambia de color con su velocidad
    private OmniLight3D _trayLight;           // luz de la cubitera: crece con su mejora
    private BoxMesh _launchMesh;
    private readonly HashSet<Vector2I> _inFlight = new(); // celdas a las que ya vuela un hielo
    private double _autoPlaceTimer = AutoPlaceSeconds;
    private int _seed;                  // semilla de las parcelas de peces: se guarda para que la partida sea la misma
    private double _autosaveTimer = AutosaveSeconds;
    private bool _resetting;            // al empezar de cero no se vuelve a guardar
    private static int _slot = 1;       // ranura de guardado en uso (1 a 3); estática para sobrevivir a la recarga de escena
    private double _playedSeconds;      // tiempo jugado de esta partida (se guarda)
    private Control _slotPanel;         // lista de ranuras (nueva partida o cargar)
    private bool _slotsFromTitle;       // la lista se abrió desde el menú principal (y no desde la pausa)

    // Aspecto del Ártico: agua, cielo y sol cambian con el progreso
    private StandardMaterial3D _waterMaterial;
    private ProceduralSkyMaterial _skyMaterial;
    private DirectionalLight3D _sun;
    private Godot.Environment _environment;
    private SpotLight3D _flashlight;
    private bool _flashOn;   // linterna encendida a mano (tecla T)
    private bool _tWasDown;  // para detectar la pulsación de T una sola vez
    private double _shownProgress = -1;

    // Sonidos (generados en código, ver Sounds.cs)
    private AudioStream _crackSound;
    private AudioStreamPlayer _crackPlayer;   // un solo reproductor para el crujido: no se apilan sonidos
    private AudioStream _chimeSound;
    private AudioStream _dripSound;
    private AudioStream _thudSound;   // golpe suave de la máquina
    private ulong _lastCrackMs;       // para no solapar crujidos seguidos de la mano

    private bool _menuOpen;
    private bool _menuJustOpened; // evita que la misma tecla E que abre el menú lo cierre

    // Menú principal y pausa (Esc)
    private CanvasLayer _overlay;
    private Control _titlePanel;
    private Control _pausePanel;
    private bool _titleOpen;
    private bool _pauseOpen;
    private static bool _relaxedPref;                                  // modo sin prisa elegido (se conserva en partidas nuevas)
    private readonly System.Collections.Generic.List<Button> _relaxedButtons = new();
    private static bool _devPref;   // partida nueva en modo dev (temporal; se elige al crear la partida)
    private static bool _skipTitle; // al empezar una partida nueva desde un menú no se vuelve a mostrar el menú principal

    private readonly record struct MenuItem(string Text, Action Action, bool Enabled);

    private bool _aimHit;     // el punto que miras está a alcance
    private bool _aimValid;   // se puede colocar hielo ahí
    private Vector2I _aimCell;

    private double _placeCooldown;
    private double _chargeTime;   // segundos que llevas manteniendo E en la cubitera

    private string _message = "";
    private double _messageTimer;

    public override void _Ready()
    {
        Settings.Load(); // sensibilidad y volumen, guardados aparte de las partidas
        SaveData save = SaveGame.Read(_slot);
        _state.RelaxedMode = save == null && _relaxedPref; // partida nueva: conserva el modo elegido en el menú
        _seed = save?.Seed ?? new Random().Next();
        _crackSound = Sounds.Crack();
        _crackPlayer = new AudioStreamPlayer { Stream = _crackSound };
        AddChild(_crackPlayer);
        _chimeSound = Sounds.Chime();
        _dripSound = Sounds.Drip();
        _thudSound = Sounds.Thud();
        BuildWind();

        BuildEnvironment();
        BuildWorld();
        BuildWeather();
        BuildPlayer();
        BuildGhost();
        BuildHud();

        if (save != null)
            LoadGame(save);
        else
            if (_devPref)
                DevMode.Apply(_state); // MODO DEV (temporal): solo si lo elegiste al crear la partida

        BuildOverlays();
        bool showTitle = !_skipTitle;
        _skipTitle = false;
        if (showTitle)
            ShowTitle();

        _state.Melted += () =>
        {
            ShowMessage("Un hielo del montón se ha derretido");
            PlaySound(_dripSound, -6);
        };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_resetting && !_titleOpen)
            SaveGameNow();
    }

    public override void _Process(double delta)
    {
        // Con el menú principal o el de pausa abiertos, el mundo no avanza.
        if (_titleOpen || _pauseOpen)
            return;

        _playedSeconds += delta;
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
                _placeCooldown = _state.PlaceInterval;
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

        UpdateAutoPlace(delta); // el lanzador sigue funcionando aunque el menú esté abierto
        UpdateWind(delta);
        UpdatePenguins(delta);
        UpdateFootprints(delta);
        UpdateWeather();
        UpdateHud(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Mapa de sectores: M o Esc lo cierran; no abre nada más mientras está abierto.
        if (_mapOpen)
        {
            if (@event is InputEventKey mapKey && mapKey.Pressed && !mapKey.Echo
                && (mapKey.PhysicalKeycode == Key.M || mapKey.PhysicalKeycode == Key.Escape))
                CloseMap();
            return;
        }

        if (@event is InputEventKey mKey && mKey.Pressed && !mKey.Echo && mKey.PhysicalKeycode == Key.M
            && !_titleOpen && !_pauseOpen && !_menuOpen)
        {
            OpenMap();
            return;
        }

        if (_menuOpen)
        {
            if (!_menuJustOpened && @event is InputEventKey close && close.Pressed && !close.Echo
                && (close.PhysicalKeycode == Key.Escape || close.PhysicalKeycode == Key.E))
            {
                CloseMenu();
            }
            return;
        }

        // Esc: abre el menú de pausa o lo cierra (el menú principal no responde a Esc).
        if (@event is InputEventKey esc && esc.Pressed && !esc.Echo && esc.PhysicalKeycode == Key.Escape && !_titleOpen)
        {
            if (_confirmPanel != null)
                CloseConfirm();
            else if (_optionsPanel != null && _optionsPanel.Visible)
                CloseOptions();
            else if (_modePanel != null && _modePanel.Visible)
                CloseMode();
            else if (_slotPanel != null && _slotPanel.Visible)
                CloseSlots();
            else if (_pauseOpen)
                ContinueGame();
            else
                OpenPause();
            return;
        }

        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            TryPlaceAtAim(quiet: false);
            _placeCooldown = _state.PlaceInterval;
        }
    }

}