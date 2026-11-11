using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
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
        _environment = environment;
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

        _trayLight = new OmniLight3D
        {
            OmniRange = 3f,
            LightColor = new Color(0.5f, 0.85f, 1f),
            LightEnergy = 0f,
            ShadowEnabled = false,
            Position = new Vector3(-3, 1.6f, -4),
        };
        AddChild(_trayLight);

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
        // Cubo cuadrado de metal mate (sin brillo). El color cambia según tenga hielo (ver UpdateHud).
        _bucketMaterial = new StandardMaterial3D
        {
            AlbedoColor = BucketEmptyColor,
            Metallic = 0f,
            Roughness = 1f,
        };
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
        _bucket.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = BucketSize },
            MaterialOverride = _bucketMaterial,
        });
        _bucket.AddChild(new MeshInstance3D // asa: barra cuadrada sobre el cubo
        {
            Mesh = new BoxMesh { Size = new Vector3(BucketSize.X * 0.8f, 0.05f, 0.05f) },
            MaterialOverride = _bucketMaterial,
            Position = new Vector3(0, BucketSize.Y / 2f + 0.08f, 0),
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
            if (t > 0 && t <= ReachDistance + _state.LongArms.Level)
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
    /// Se puede colocar hielo en la celda: está libre, toca hielo o muelle, está al alcance del brazo
    /// y no te metes dentro del hielo (justo debajo de ti solo si estás por encima de la superficie).
    /// </summary>
    private bool CanPlaceAt(Vector2I cell)
    {
        if (!_grid.CanPlace(cell))
            return false;

        // Una celda con un cubo ya volando hacia ella cuenta como ocupada: se apunta a la siguiente libre.
        if (_flyingCells.Contains(cell))
            return false;

        Vector3 center = IceGrid.CellCenter(cell);
        Vector3 feet = _player.GlobalPosition;
        float dist = new Vector2(center.X - feet.X, center.Z - feet.Z).Length();

        if (dist > _state.ArmReach) // alcance del brazo: 3 m, más 1 m por nivel de Brazos largos
            return false;

        // Justo debajo de ti: solo si tus pies están por encima de la superficie (si no, el hielo te empujaría).
        if (dist <= 0.45f && feet.Y < IceGrid.SurfaceY + 0.05f)
            return false;

        return true;
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
}