using System;
using System.Collections.Generic;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Visuales de la base: el cubo muestra el hielo que lleva dentro (cubitos que aparecen con un rebote)
/// y las máquinas rebotan cuando se usan.
/// </summary>
public partial class Main
{
    private const int BucketSlots = 48; // cubitos que caben en el cubo visualmente (3 capas de 16)
    private readonly List<MeshInstance3D> _bucketCubes = new();
    private int _shownBucketCubes;

    /// <summary>Crea los cubitos dentro del cubo, ocultos. Se muestran según el hielo que tenga.</summary>
    private void BuildBucketIce()
    {
        var mesh = new BoxMesh { Size = new Vector3(0.13f, 0.13f, 0.13f) };
        var material = Mat(IceColor);
        for (int i = 0; i < BucketSlots; i++)
        {
            int layer = i / 16;
            int index = i % 16;
            var cube = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = material,
                Position = new Vector3(-0.195f + (index % 4) * 0.13f, -0.185f + layer * 0.13f, -0.195f + (index / 4) * 0.13f),
                RotationDegrees = new Vector3(0, i * 17f, 0), // giro distinto para que no parezcan una rejilla
                Visible = false,
            };
            _bucket.AddChild(cube);
            _bucketCubes.Add(cube);
        }
    }

    /// <summary>Cada frame: muestra tantos cubitos como hielo tenga el cubo. Los nuevos aparecen con un rebote.</summary>
    private void UpdateBucketVisuals()
    {
        if (_bucket == null || _bucketCubes.Count == 0)
            return;

        int target = Math.Clamp((int)Math.Round(_state.BucketIce / (double)_state.BucketMax * BucketSlots), 0, BucketSlots);
        if (target == _shownBucketCubes)
            return;

        for (int i = 0; i < BucketSlots; i++)
        {
            MeshInstance3D cube = _bucketCubes[i];
            if (i < target)
            {
                if (!cube.Visible)
                {
                    cube.Visible = true;
                    cube.Scale = Vector3.Zero;
                    Tween tween = CreateTween();
                    tween.TweenProperty(cube, "scale", Vector3.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                }
            }
            else
            {
                cube.Visible = false;
            }
        }

        if (target > _shownBucketCubes)
            Bounce(_bucket, new Vector3(1.08f, 0.92f, 1.08f)); // el cubo se encoge un poco al recibir hielo

        _shownBucketCubes = target;
    }

    /// <summary>Rebote de un nodo: parte de 'peak' y vuelve a su tamaño normal.</summary>
    private void Bounce(Node3D node, Vector3 peak)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
            return;

        node.Scale = peak;
        Tween tween = CreateTween();
        tween.TweenProperty(node, "scale", Vector3.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Una máquina rebota y suelta un pequeño destello cuando se usa.</summary>
    private void PlayMachineUse(Station station, Color? burst = null)
    {
        if (station == null)
            return;

        Bounce(station, new Vector3(1.08f, 0.9f, 1.08f));
        SpawnBurst(station.GlobalPosition + Vector3.Up * 1.0f, burst ?? new Color(0.8f, 0.95f, 1f), 14);
    }

    /// <summary>El lanzador rebota cada vez que lanza un hielo.</summary>
    private void PulseLauncher()
    {
        Bounce(_launcher, new Vector3(1.06f, 0.94f, 1.06f));
    }
}