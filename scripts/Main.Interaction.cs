using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- interacción

    private void OnStationUsed(Station station)
    {
        // Con el menú de mejoras abierto, E solo sirve para cerrarlo (lo hace Main._UnhandledInput).
        if (_menuOpen)
            return;

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
                if (!_menuOpen) // con el menú abierto, E lo cierra (lo gestiona Main), no lo reabre
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
            if (fromTable)
                PlaySound(_thudSound, -12); // la máquina: golpe suave
            else
                PlayCrack(quiet ? -10 : -3);
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

        // Si lanza, espera el cooldown completo. Si no hay hielo en la mesa, se queda listo
        // (timer a 0) y lanza en cuanto aparezca hielo, sin esperar otro ciclo entero.
        if (AutoPlaceNearPlayer())
            _autoPlaceTimer = _state.LaunchSeconds;
        else
            _autoPlaceTimer = 0;
    }

    /// <summary>
    /// La máquina lanza un hielo al punto libre más cercano a ella (el centro del muelle).
    /// Coge el hielo directamente de la mesa; si la mesa está vacía, espera.
    /// </summary>
    private bool AutoPlaceNearPlayer()
    {
        if (_launcher == null || _state.IceCubes <= 0)
            return false;

        Vector2I origin = IceGrid.WorldToCell(_launcher.Position);
        if (!FindNearestFreeCell(origin, out Vector2I target))
            return false;

        if (!_state.TakeOneFromPile())
            return false;

        LaunchIce(target);
        return true;
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
            MaterialOverride = _launcherGlow = Glow(new Color(0.5f, 0.9f, 1f)),
            Position = new Vector3(0, 0.55f, 0.62f),
        });
        AddChild(_launcher);
    }

    /// <summary>Crujido al colocar. Usa un solo reproductor: si ya suena, lo reinicia en vez de apilar sonidos.</summary>
    private void PlayCrack(float volumeDb)
    {
        // Si el crujido anterior acaba de empezar, no lo cortamos: así no se solapan al poner hielo seguido.
        ulong now = Time.GetTicksMsec();
        if (_crackPlayer.Playing && now - _lastCrackMs < 90)
            return;

        _lastCrackMs = now;
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

}