using Godot;

namespace GreatRefreeze;

/// <summary>
/// Jugador en primera persona. Se construye a sí mismo en código (colisión, cabeza,
/// cámara y rayo de interacción), así que no hace falta montar nada en el editor.
/// Controles: WASD mover, ratón mirar, Espacio saltar, E interactuar, Esc soltar el ratón.
/// </summary>
public partial class Player : CharacterBody3D
{
    public event System.Action<Station> StationUsed;

    /// <summary>La estación que se está mirando ahora mismo (o null).</summary>
    public Station LookedStation { get; private set; }

    private const float Speed = 5.0f;
    private const float JumpVelocity = 4.5f;
    private const float Gravity = 9.8f;
    private const float MouseSensitivity = 0.0025f;

    private Node3D _head;
    private Camera3D _camera;
    private RayCast3D _ray;
    private Vector3 _spawn;

    public override void _Ready()
    {
        _spawn = GlobalPosition;

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
            if (key.PhysicalKeycode == Key.Escape)
            {
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
            }
            else if (key.PhysicalKeycode == Key.E && LookedStation != null)
            {
                StationUsed?.Invoke(LookedStation);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y -= Gravity * (float)delta;
        else if (Input.IsPhysicalKeyPressed(Key.Space))
            velocity.Y = JumpVelocity;

        float x = (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);
        float z = (Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f);

        Vector3 direction = (Transform.Basis * new Vector3(x, 0, z)).Normalized();
        velocity.X = direction.X * Speed;
        velocity.Z = direction.Z * Speed;

        Velocity = velocity;
        MoveAndSlide();

        // Si te caes al agua, vuelves al muelle.
        if (GlobalPosition.Y < -10f)
        {
            GlobalPosition = _spawn;
            Velocity = Vector3.Zero;
        }

        LookedStation = _ray.IsColliding() ? _ray.GetCollider() as Station : null;
    }
}
