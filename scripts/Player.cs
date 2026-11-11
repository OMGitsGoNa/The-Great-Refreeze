using Godot;

namespace GreatRefreeze;

/// <summary>
/// Jugador en primera persona. Se construye a sí mismo en código (colisión, cabeza,
/// cámara y rayo de interacción), así que no hace falta montar nada en el editor.
/// Controles: WASD mover, ratón mirar, Espacio saltar, E interactuar, Esc soltar el ratón.
/// Si pasas más de 5 segundos bajo el agua, avisa con el evento Drowned.
///
/// Movimiento "con peso": aceleras y frenas poco a poco, cuesta más cambiar de dirección
/// en el aire, caes más rápido de lo que subes, tienes un margen corto para saltar tras
/// bajar de un borde, la cámara balancea al andar y se hunde un poco al aterrizar.
/// </summary>
public partial class Player : CharacterBody3D
{
    public event System.Action<Station> StationUsed;

    /// <summary>Se dispara cuando llevas demasiado tiempo bajo el agua.</summary>
    public event System.Action Drowned;

    /// <summary>Cámara del jugador: sirve para saber a qué punto del agua apunta.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Si está activo, el jugador no se mueve ni mira (por ejemplo, con un menú abierto).</summary>
    public bool Frozen { get; set; }

    /// <summary>Permite correr manteniendo Shift.</summary>
    public bool CanSprint { get; set; }

    /// <summary>La estación que se está mirando ahora mismo (o null).</summary>
    public Station LookedStation { get; private set; }

    /// <summary>Segundos que llevas bajo el agua seguidos.</summary>
    public double SubmergedTime { get; private set; }

    /// <summary>Segundos que puedes estar bajo el agua antes de congelarte.</summary>
    public const float DrownSeconds = 5f;

    // Velocidades en metros por segundo (1 unidad = 1 metro; el jugador mide 1,8 m).
    private const float WalkSpeed = 4.0f;
    private const float SprintSpeed = 8.3f;     // término medio entre 6,6 y 10 m/s; unas 2 veces al andar
    private const float WaterSpeed = 2.4f;
    private const float SwimSpeed = 4.0f;       // al nadar hacia arriba con Espacio

    // Aceleración en unidades por segundo al cuadrado.
    private const float GroundAccel = 24f;      // arrancar en el suelo
    private const float GroundDecel = 18f;      // frenar en el suelo
    private const float AirAccel = 5f;          // en el aire apenas puedes cambiar de rumbo
    private const float WaterAccel = 6f;

    private const float Gravity = 9.8f;
    private const float FallMultiplier = 1.6f;  // caes más rápido de lo que subes
    private const float CutGravity = 3f;        // si sueltas Espacio al subir, el salto se acorta
    private const float JumpHeight = 1.15f;     // metros
    private static readonly float JumpVelocity = Mathf.Sqrt(2f * Gravity * JumpHeight);
    private const float CoyoteSeconds = 0.1f;   // margen para saltar justo después de bajar de un borde

    private const float WaterLevel = -0.8f;     // superficie del agua
    private const float MouseSensitivity = 0.0025f;

    // Balanceo de cámara al andar.
    private const float BobHeight = 0.045f;
    private const float BobSway = 0.025f;
    private const float BobSpeedFactor = 1.9f;  // ritmo del paso según la velocidad

    private Node3D _head;
    private Camera3D _camera;
    private RayCast3D _ray;
    private Vector3 _spawn;

    private float _coyoteTimer;
    private float _peakFall;     // velocidad de caída máxima desde que dejaste el suelo
    private float _landDip;      // cuánto se hunde la cámara al aterrizar (vuelve a 0)
    private float _bobTime;
    private float _bobAmount;    // 0 quieto, 1 andando (1,4 corriendo)

    public override void _Ready()
    {
        _spawn = GlobalPosition;

        // Sin esto, el jugador se "despega" de las pendientes y de los montones de hielo.
        FloorSnapLength = 0.2f;
        FloorMaxAngle = Mathf.DegToRad(50f);

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });

        _head = new Node3D { Position = new Vector3(0, 1.6f, 0) };
        AddChild(_head);

        _camera = new Camera3D();
        _head.AddChild(_camera);
        _camera.MakeCurrent();

        _ray = new RayCast3D
        {
            TargetPosition = new Vector3(0, 0, -3f),
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        _camera.AddChild(_ray);
        _ray.AddException(this);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Frozen)
            return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            _head.RotateX(-motion.Relative.Y * MouseSensitivity);

            Vector3 rotation = _head.Rotation;
            rotation.X = Mathf.Clamp(rotation.X, -1.5f, 1.5f);
            _head.Rotation = rotation;
        }
        else if (@event is InputEventMouseButton click && click.Pressed
                 && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Un clic recupera el ratón después de soltarlo con Esc.
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Esc lo gestiona Main (menú de pausa).
            if (key.PhysicalKeycode == Key.E && LookedStation != null)
            {
                StationUsed?.Invoke(LookedStation);
            }
        }
    }

    /// <summary>Lleva al jugador de vuelta a la base y para su movimiento.</summary>
    public void Respawn()
    {
        GlobalPosition = _spawn;
        Velocity = Vector3.Zero;
        SubmergedTime = 0;
        _peakFall = 0;
        _landDip = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;
        bool inWater = GlobalPosition.Y < WaterLevel;
        bool onFloor = IsOnFloor();
        bool jumpHeld = !Frozen && Input.IsPhysicalKeyPressed(Key.Space);

        // Margen para saltar: sigue contando un instante después de dejar el borde.
        _coyoteTimer = onFloor ? CoyoteSeconds : _coyoteTimer - dt;

        // Dirección que pides con WASD, en el sentido en que miras.
        float x = (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);
        float z = (Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f);
        if (Frozen)
        {
            x = 0;
            z = 0;
        }

        Vector3 wish = Transform.Basis * new Vector3(x, 0, z);
        if (wish.LengthSquared() > 1f)
            wish = wish.Normalized();

        bool sprinting = CanSprint && Input.IsPhysicalKeyPressed(Key.Shift) && !inWater;
        float maxSpeed = inWater ? WaterSpeed : (sprinting ? SprintSpeed : WalkSpeed);
        Vector2 goal = new Vector2(wish.X, wish.Z) * maxSpeed;

        // Horizontal: acelera y frena de forma gradual, con menos control en el aire.
        float rate;
        if (inWater)
            rate = WaterAccel;
        else if (!onFloor)
            rate = AirAccel;
        else
            rate = goal.LengthSquared() > 0.01f ? GroundAccel : GroundDecel;

        Vector2 horizontal = new Vector2(velocity.X, velocity.Z).MoveToward(goal, rate * dt);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Y;

        // Vertical
        if (inWater)
        {
            // En el agua flotas: te hundes despacio y nadas hacia arriba con Espacio.
            velocity.Y = Mathf.Max(velocity.Y - Gravity * 0.2f * dt, -1.5f);
            if (jumpHeld)
                velocity.Y = SwimSpeed;
            _peakFall = 0;
        }
        else if (!onFloor)
        {
            bool falling = velocity.Y < 0;
            velocity.Y -= Gravity * (falling ? FallMultiplier : 1f) * dt;

            // Si sueltas Espacio mientras subes, el salto se corta (salto de altura variable).
            if (velocity.Y > 0 && !jumpHeld)
                velocity.Y -= Gravity * CutGravity * dt;

            if (falling)
                _peakFall = Mathf.Max(_peakFall, -velocity.Y);
        }
        else
        {
            if (velocity.Y < 0)
                velocity.Y = 0; // pegado al suelo, sin rebotes

            if (jumpHeld && _coyoteTimer > 0)
            {
                velocity.Y = JumpVelocity;
                _coyoteTimer = 0;
            }
        }

        bool landed = onFloor && !_wasOnFloorLastFrame && !inWater;
        _wasOnFloorLastFrame = onFloor;

        Velocity = velocity;
        MoveAndSlide();

        if (landed)
        {
            // Un aterrizaje fuerte hunde la cámara un poco. Cuanto más caes, más se hunde.
            _landDip = Mathf.Min(0.2f, _peakFall * 0.03f);
            _peakFall = 0;
        }

        Vector2 planar = new Vector2(Velocity.X, Velocity.Z);
        UpdateCamera(dt, planar.Length(), onFloor && !inWater, sprinting);

        // Si te caes al agua, vuelves al muelle.
        if (GlobalPosition.Y < -10f)
            Respawn();

        // Bajo el agua cuenta el tiempo; si pasa el límite, te congelas.
        if (inWater && !Frozen)
        {
            SubmergedTime += delta;
            if (SubmergedTime >= DrownSeconds)
                Drowned?.Invoke();
        }
        else if (!inWater)
        {
            SubmergedTime = 0;
        }

        LookedStation = _ray.IsColliding() ? _ray.GetCollider() as Station : null;
    }

    private bool _wasOnFloorLastFrame;

    /// <summary>Balanceo al andar y hundimiento al aterrizar, suavizados.</summary>
    private void UpdateCamera(float dt, float speed, bool grounded, bool sprinting)
    {
        bool moving = grounded && speed > 0.5f;
        float targetAmount = moving ? (sprinting ? 1.4f : 1f) : 0f;
        _bobAmount = Mathf.MoveToward(_bobAmount, targetAmount, dt * 6f);

        if (moving)
            _bobTime += dt * speed * BobSpeedFactor;

        float bob = Mathf.Abs(Mathf.Sin(_bobTime)) * BobHeight * _bobAmount;
        float sway = Mathf.Cos(_bobTime) * BobSway * _bobAmount;

        _landDip = Mathf.MoveToward(_landDip, 0f, dt * 2f);
        _camera.Position = new Vector3(sway, -bob - _landDip, 0f);
    }
}