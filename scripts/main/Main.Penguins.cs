using System;
using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Pingüinos ayudantes. Son cuerpos físicos (CharacterBody3D) que chocan con el suelo, el hielo, las rampas,
/// las estaciones y entre ellos. Van a la mesa, cogen un hielo, cruzan el hielo hasta quedarse a 2 m de la
/// celda libre más cercana, la colocan y luego deambulan por el muelle hasta el siguiente viaje.
/// </summary>
public partial class Main
{
    private sealed class Penguin
    {
        public CharacterBody3D Body;
        public Vector3 Spot;          // puesto de salida; a él vuelve si cae al agua
        public double Timer;          // segundos que quedan para el siguiente viaje (solo cuenta cuando no viaja)
        public bool Busy;             // hace un viaje (mesa, hielo, colocar)
        public bool Carrying;         // lleva un hielo de la mesa
        public int Gen;               // sube al empezar un viaje: los paseos antiguos se cancelan
        public Vector3? Goal;         // siguiente punto del camino
        public List<Vector3> Route = new(); // camino hasta el destino (puede pasar por la rampa)
        public Action OnArrive;       // qué hacer al llegar al destino
        public double StuckTime;      // segundos seguidos sin avanzar hacia el destino
        public Vector2I? Reserved;    // celda que tiene reservada para colocar
    }

    private const float PenguinWalkSpeed = 3.0f;   // metros por segundo
    private const float PenguinArriveDist = 0.25f; // a esta distancia se considera llegado
    private const double PenguinStuckLimit = 4.0;  // segundos sin avanzar antes de dejar el viaje
    private const double PenguinPickWait = 0.3;    // segundos cogiendo el hielo
    private const double PenguinPlaceWait = 0.3;   // segundos colocándolo
    private const float PenguinReach = 2.0f;       // se para a 2 m del hielo (la colocación llega a 3 m)
    private const float PenguinGravity = 9.8f;
    private const float PenguinFallLimit = -3f;    // por debajo de esta altura ha caído al agua
    private const float RampTopDist = 7.2f;        // dentro del muelle, junto a la rampa
    private const float RampBottomDist = 8.6f;     // al pie de la rampa, ya en el hielo
    private static readonly Vector3[] RampDirs = { new(1f, 0f, 0f), new(-1f, 0f, 0f), new(0f, 0f, 1f), new(0f, 0f, -1f) };

    // Puestos de salida en el muelle, junto al borde oeste.
    private static readonly Vector3[] PenguinSpots =
    {
        new(-7f, 0f, -6f),
        new(-7f, 0f, -3f),
        new(-7f, 0f, 0f),
        new(-7f, 0f, 2.5f),
        new(-4.5f, 0f, 6f),
    };

    private readonly List<Penguin> _penguins = new();

    /// <summary>Cada frame: ajusta cuántos pingüinos hay según la mejora, mueve a cada uno y cuenta su tiempo.</summary>
    private void UpdatePenguins(double delta)
    {
        int wanted = Mathf.Min(_state.Penguins.Level, PenguinSpots.Length);

        while (_penguins.Count < wanted)
            _penguins.Add(SpawnPenguin(_penguins.Count));

        while (_penguins.Count > wanted)
        {
            _penguins[^1].Body.QueueFree();
            _penguins.RemoveAt(_penguins.Count - 1);
        }

        foreach (Penguin penguin in _penguins)
        {
            if (!Alive(penguin))
                continue;

            Steer(penguin, delta);

            if (penguin.Busy)
                continue;

            penguin.Timer -= delta;
            if (penguin.Timer <= 0)
                StartTrip(penguin);
        }
    }

    /// <summary>Mueve al pingüino hacia su destino, con gravedad y colisiones. Llama a OnArrive al llegar.</summary>
    private void Steer(Penguin p, double delta)
    {
        // Si cae al agua, vuelve a su puesto y cancela lo que hacía.
        if (p.Body.Position.Y < PenguinFallLimit)
        {
            p.Body.Position = p.Spot;
            p.Body.Velocity = Vector3.Zero;
            Abort(p);
            return;
        }

        Vector3 vel = p.Body.Velocity;
        vel.Y = p.Body.IsOnFloor() ? 0f : vel.Y - PenguinGravity * (float)delta;

        if (p.Goal is Vector3 goal)
        {
            Vector3 to = new(goal.X - p.Body.Position.X, 0f, goal.Z - p.Body.Position.Z);
            if (to.Length() <= PenguinArriveDist)
            {
                if (p.Route.Count > 1)
                {
                    p.Route.RemoveAt(0);
                    p.Goal = p.Route[0];
                    return;
                }

                p.Route.Clear();
                p.Goal = null;
                Action arrive = p.OnArrive;
                p.OnArrive = null;
                p.Body.Velocity = new Vector3(0f, vel.Y, 0f);
                p.Body.MoveAndSlide();
                arrive?.Invoke();
                return;
            }

            Vector3 dir = to.Normalized();
            vel.X = dir.X * PenguinWalkSpeed;
            vel.Z = dir.Z * PenguinWalkSpeed;
            p.Body.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z)), 0f);
        }
        else
        {
            vel.X = 0f;
            vel.Z = 0f;
        }

        p.Body.Velocity = vel;
        p.Body.MoveAndSlide();

        // Si choca con algo y no avanza, cuenta el tiempo parado; si se queda atascado mucho rato, se cancela el viaje.
        if (p.Goal != null && p.Body.GetRealVelocity().Length() < 0.2f)
        {
            p.StuckTime += delta;
            if (p.StuckTime > PenguinStuckLimit)
                Abort(p);
        }
        else
        {
            p.StuckTime = 0;
        }
    }

    /// <summary>Empieza un viaje: a la mesa. Si no hay hielo, espera un poco y vuelve a mirar.</summary>
    private void StartTrip(Penguin penguin)
    {
        penguin.Gen++;
        if (_state.IceCubes <= 0)
        {
            penguin.Timer = 1.0;
            return;
        }

        penguin.Busy = true;
        int gen = penguin.Gen;
        Walk(penguin, gen, PickSpot(), () => PenguinPick(penguin));
    }

    /// <summary>En el borde de la mesa: coge un hielo y busca la celda libre más cercana a la mesa.</summary>
    private void PenguinPick(Penguin penguin)
    {
        penguin.Carrying = _state.TakeOneFromPile();
        if (!penguin.Carrying)
        {
            Finish(penguin);
            return;
        }

        Wait(PenguinPickWait, () =>
        {
            if (!FindNearestFreeCell(IceGrid.WorldToCell(PickSpot()), out Vector2I target))
            {
                Abort(penguin);
                return;
            }

            _inFlight.Add(target); // reserva la celda para que otro pingüino no vaya a la misma
            penguin.Reserved = target;

            Vector3 cell = IceGrid.CellCenter(target);
            Vector3 pick = PickSpot();
            Vector3 fromSide = new(pick.X - cell.X, 0f, pick.Z - cell.Z);
            if (fromSide.LengthSquared() < 0.01f)
                fromSide = Vector3.Back;
            Vector3 stand = cell + fromSide.Normalized() * PenguinReach;

            Walk(penguin, penguin.Gen, stand, () => PenguinPlace(penguin, target));
        });
    }

    /// <summary>A 2 m del hielo: lo coloca (cuenta como colocado) y vuelve a deambular.</summary>
    private void PenguinPlace(Penguin penguin, Vector2I target)
    {
        Wait(PenguinPlaceWait, () =>
        {
            _inFlight.Remove(target);
            penguin.Reserved = null;

            if (penguin.Carrying && !PlaceCell(target, quiet: true, fromTable: true))
                _state.ReturnToPile(); // no se pudo colocar: el hielo vuelve a la mesa

            penguin.Carrying = false;
            Finish(penguin);
        });
    }

    /// <summary>Cancela el viaje actual: suelta la reserva, devuelve el hielo a la mesa y vuelve a deambular.</summary>
    private void Abort(Penguin penguin)
    {
        if (penguin.Reserved is Vector2I reserved)
        {
            _inFlight.Remove(reserved);
            penguin.Reserved = null;
        }

        if (penguin.Carrying)
        {
            _state.ReturnToPile();
            penguin.Carrying = false;
        }

        penguin.Goal = null;
        penguin.Route.Clear();
        penguin.OnArrive = null;
        penguin.StuckTime = 0;
        Finish(penguin);
    }

    /// <summary>Termina el viaje: reinicia el tiempo y se pone a deambular por el muelle.</summary>
    private void Finish(Penguin penguin)
    {
        penguin.Busy = false;
        penguin.Timer = _state.PenguinCycle;
        Wander(penguin, penguin.Gen);
    }

    /// <summary>Camina a un punto al azar del muelle, se para un momento y vuelve a elegir otro, hasta el siguiente viaje.</summary>
    private void Wander(Penguin penguin, int gen)
    {
        if (!Alive(penguin) || penguin.Busy || penguin.Gen != gen)
            return;

        Walk(penguin, gen, RandomDockPoint(), () =>
            Wait(0.8 + _luck.NextDouble() * 1.7, () => Wander(penguin, gen)));
    }

    private Vector3 RandomDockPoint()
    {
        float min = IceGrid.CellCenter(new Vector2I(DockMinCell, DockMinCell)).X;
        float max = IceGrid.CellCenter(new Vector2I(DockMaxCell, DockMaxCell)).X;
        float x = min + (float)_luck.NextDouble() * (max - min);
        float z = min + (float)_luck.NextDouble() * (max - min);
        return new Vector3(x, 0f, z);
    }

    /// <summary>Punto del borde oeste de la mesa, donde se coge el hielo.</summary>
    private Vector3 PickSpot() => new(PileCenter.X - TableW / 2f - 0.6f, 0f, PileCenter.Z);

    /// <summary>Pone un destino para el pingüino. Al llegar ejecuta arrive, si el viaje no se ha cancelado.</summary>
    private void Walk(Penguin penguin, int gen, Vector3 target, Action arrive)
    {
        if (!Alive(penguin) || penguin.Gen != gen)
            return;

        penguin.Route = RouteTo(penguin.Body.Position, target);
        penguin.Goal = penguin.Route[0];
        penguin.OnArrive = arrive;
        penguin.StuckTime = 0;
    }

    /// <summary>
    /// Camino hasta el destino. Si hay que bajar del muelle o subir a él, pasa por la rampa más cercana.
    /// Sin rampas compradas no hay camino: el pingüino se cae del borde (no sabe saltar).
    /// </summary>
    private List<Vector3> RouteTo(Vector3 from, Vector3 to)
    {
        bool fromDock = IsOnDock(from);
        bool toDock = IsOnDock(to);
        var route = new List<Vector3>();

        if (fromDock == toDock || _state.Ramps.Level == 0)
        {
            route.Add(to);
            return route;
        }

        int best = 0;
        float bestCost = float.MaxValue;
        for (int i = 0; i < RampDirs.Length; i++)
        {
            Vector3 top = RampDirs[i] * RampTopDist;
            Vector3 bottom = RampDirs[i] * RampBottomDist;
            float cost = fromDock
                ? FlatDist(from, top) + FlatDist(bottom, to)
                : FlatDist(from, bottom) + FlatDist(top, to);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = i;
            }
        }

        Vector3 topPoint = RampDirs[best] * RampTopDist;
        Vector3 bottomPoint = RampDirs[best] * RampBottomDist;
        if (fromDock)
        {
            route.Add(topPoint);
            route.Add(bottomPoint);
        }
        else
        {
            route.Add(bottomPoint);
            route.Add(topPoint);
        }
        route.Add(to);
        return route;
    }

    private static bool IsOnDock(Vector3 point)
    {
        Vector2I cell = IceGrid.WorldToCell(point);
        return cell.X >= DockMinCell && cell.X <= DockMaxCell && cell.Y >= DockMinCell && cell.Y <= DockMaxCell;
    }

    private static float FlatDist(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Espera unos segundos y luego ejecuta next.</summary>
    private void Wait(double seconds, Action next)
    {
        Tween tween = CreateTween();
        tween.TweenInterval(seconds);
        tween.TweenCallback(Callable.From(next));
    }

    private static bool Alive(Penguin penguin) => penguin.Body != null && GodotObject.IsInstanceValid(penguin.Body);

    /// <summary>Crea un pingüino con cuerpo físico: cuerpo negro, barriga blanca y pico naranja.</summary>
    private Penguin SpawnPenguin(int index)
    {
        Vector3 spot = PenguinSpots[index];
        var body = new CharacterBody3D
        {
            Position = spot,
            RotationDegrees = new Vector3(0, 90, 0),
            CollisionLayer = 2,        // capa propia de los pingüinos
            CollisionMask = 1,         // solo choca con el mundo (capa 1): entre pingüinos se atraviesan
        };
        AddChild(body);

        body.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.22f, Height = 0.8f },
            Position = new Vector3(0, 0.4f, 0),
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 0.8f },
            MaterialOverride = Mat(new Color(0.12f, 0.13f, 0.16f)),
            Position = new Vector3(0, 0.4f, 0),
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.16f, Height = 0.4f },
            MaterialOverride = Mat(new Color(0.95f, 0.95f, 0.95f)),
            Position = new Vector3(0, 0.36f, 0.14f),
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.06f, 0.14f) },
            MaterialOverride = Mat(new Color(1f, 0.55f, 0.1f)),
            Position = new Vector3(0, 0.68f, 0.26f),
        });

        // Salen escalonados (1 s entre uno y otro) para que no vayan todos a la vez.
        return new Penguin { Body = body, Spot = spot, Timer = 1.0 + index };
    }
}