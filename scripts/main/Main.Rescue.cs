using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
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
        Color color = new(0.82f, 0.68f, 0.55f);

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
                        double bonus = _state.SectorFishFor(done);
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

}