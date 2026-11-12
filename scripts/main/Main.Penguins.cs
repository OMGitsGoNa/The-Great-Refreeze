using System;
using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Pingüinos ayudantes. Cada uno vive en su puesto del muelle. Cuando le toca, camina hasta el borde de la mesa,
/// coge un hielo, vuelve a su puesto y lo lanza a la celda libre más cercana (como el lanzador).
/// </summary>
public partial class Main
{
    private sealed class Penguin
    {
        public Node3D Root;
        public Vector3 Home;
        public double Timer;
        public bool Busy;      // está en un viaje (camina o coge hielo)
        public bool Carrying;  // lleva un hielo cogido de la mesa
    }

    private const float PenguinWalkSpeed = 1.8f; // metros por segundo
    private const double PenguinPickWait = 0.4;  // segundos cogiendo el hielo

    // Puestos en el muelle, junto al borde oeste (lejos de las estaciones). Ajustables si no encajan.
    private static readonly Vector3[] PenguinSpots =
    {
        new(-7f, 0f, -6f),
        new(-7f, 0f, -3f),
        new(-7f, 0f, 0f),
        new(-7f, 0f, 2.5f),
        new(-4.5f, 0f, 6f),
    };

    private readonly List<Penguin> _penguins = new();

    /// <summary>Cada frame: ajusta cuántos pingüinos hay según la mejora y cuenta el tiempo de cada uno.</summary>
    private void UpdatePenguins(double delta)
    {
        int wanted = Mathf.Min(_state.Penguins.Level, PenguinSpots.Length);

        while (_penguins.Count < wanted)
            _penguins.Add(SpawnPenguin(_penguins.Count));

        while (_penguins.Count > wanted)
        {
            _penguins[^1].Root.QueueFree();
            _penguins.RemoveAt(_penguins.Count - 1);
        }

        foreach (Penguin penguin in _penguins)
        {
            if (penguin.Busy)
                continue;

            penguin.Timer -= delta;
            if (penguin.Timer <= 0)
                StartTrip(penguin);
        }
    }

    /// <summary>Empieza un viaje: ir a la mesa. Si no hay hielo, espera un poco y lo vuelve a intentar.</summary>
    private void StartTrip(Penguin penguin)
    {
        if (_state.IceCubes <= 0)
        {
            penguin.Timer = 1.0;
            return;
        }

        penguin.Busy = true;
        Walk(penguin, PickSpot(), () => PenguinPick(penguin));
    }

    /// <summary>En el borde de la mesa: coge un hielo y espera un momento antes de volver.</summary>
    private void PenguinPick(Penguin penguin)
    {
        if (Alive(penguin))
            penguin.Carrying = _state.TakeOneFromPile();

        Wait(PenguinPickWait, () => Walk(penguin, penguin.Home, () => PenguinDrop(penguin)));
    }

    /// <summary>De vuelta en su puesto: lanza el hielo a la celda libre más cercana y reinicia el tiempo.</summary>
    private void PenguinDrop(Penguin penguin)
    {
        if (!Alive(penguin))
        {
            if (penguin.Carrying)
                _state.ReturnToPile(); // el pingüino desapareció con el hielo: vuelve a la mesa
            return;
        }

        if (penguin.Carrying)
        {
            Vector2I origin = IceGrid.WorldToCell(penguin.Home);
            if (FindNearestFreeCell(origin, out Vector2I target))
                LaunchIce(target, penguin.Home + new Vector3(0f, 0.9f, 0f));
            else
                _state.ReturnToPile(); // no hay celda libre: el hielo vuelve a la mesa
        }

        penguin.Carrying = false;
        penguin.Busy = false;
        penguin.Timer = _state.PenguinCycle;
    }

    /// <summary>Punto del borde oeste de la mesa, donde se coge el hielo.</summary>
    private Vector3 PickSpot() => new(PileCenter.X - TableW / 2f - 0.6f, 0f, PileCenter.Z);

    /// <summary>Camina en línea recta hasta el destino y mira hacia allí. Llama a arrive al llegar.</summary>
    private void Walk(Penguin penguin, Vector3 target, Action arrive)
    {
        if (!Alive(penguin))
        {
            arrive();
            return;
        }

        Vector3 start = penguin.Root.Position;
        float distance = start.DistanceTo(target);
        if (distance > 0.01f)
            penguin.Root.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(target.X - start.X, target.Z - start.Z)), 0f);

        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            if (Alive(penguin))
                penguin.Root.Position = start.Lerp(target, t);
        }), 0f, 1f, Math.Max(0.05, distance / PenguinWalkSpeed));
        tween.TweenCallback(Callable.From(arrive));
    }

    /// <summary>Espera unos segundos y luego ejecuta next.</summary>
    private void Wait(double seconds, Action next)
    {
        Tween tween = CreateTween();
        tween.TweenInterval(seconds);
        tween.TweenCallback(Callable.From(next));
    }

    private static bool Alive(Penguin penguin) => penguin.Root != null && GodotObject.IsInstanceValid(penguin.Root);

    /// <summary>Crea un pingüino sencillo: cuerpo negro, barriga blanca y pico naranja.</summary>
    private Penguin SpawnPenguin(int index)
    {
        Vector3 home = PenguinSpots[index];
        var root = new Node3D { Position = home, RotationDegrees = new Vector3(0, 90, 0) };
        AddChild(root);

        root.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 0.8f },
            MaterialOverride = Mat(new Color(0.12f, 0.13f, 0.16f)),
            Position = new Vector3(0, 0.4f, 0),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.16f, Height = 0.4f },
            MaterialOverride = Mat(new Color(0.95f, 0.95f, 0.95f)),
            Position = new Vector3(0, 0.36f, 0.14f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.06f, 0.14f) },
            MaterialOverride = Mat(new Color(1f, 0.55f, 0.1f)),
            Position = new Vector3(0, 0.68f, 0.26f),
        });

        // Los primeros empiezan escalonados para que no vayan todos a la vez.
        return new Penguin { Root = root, Home = home, Timer = _state.PenguinCycle * (0.5 + 0.1 * index) };
    }
}