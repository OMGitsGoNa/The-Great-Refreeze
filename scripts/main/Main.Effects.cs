using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // Escarcha: el velo entra desde los bordes y sube con el tiempo bajo el agua.
    private float _frostShown;  // intensidad que se ve (sube y baja poco a poco)
    private int _frostStage;    // 0, 1 o 2: para sonar un crujido al subir de nivel

    private const string FrostShader = @"
shader_type canvas_item;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float time_s = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
    vec2 d = abs(UV - 0.5) * 2.0;
    float edge = max(d.x, d.y);
    float n = noise(UV * 12.0 + vec2(time_s * 0.02, 0.0)) * 0.6 + noise(UV * 30.0) * 0.4;
    float threshold = 1.0 - amount * 1.3;
    float mask = smoothstep(threshold - 0.06, threshold + 0.06, edge + (n - 0.5) * 0.35);
    float crystal = smoothstep(0.62, 0.8, n) * mask;
    vec3 col = mix(vec3(0.7, 0.88, 1.0), vec3(1.0), crystal);
    COLOR = vec4(col, mask * (0.3 + 0.45 * amount));
}
";

    private void UpdateFrost(double delta)
    {
        float target = _player.SubmergedTime > 0
            ? Mathf.Clamp((float)(_player.SubmergedTime / Player.DrownSeconds), 0f, 1f)
            : 0f;

        // Sube con el tiempo bajo el agua y baja más despacio al salir, para que no desaparezca de golpe.
        _frostShown = Mathf.MoveToward(_frostShown, target, (float)delta * (target > _frostShown ? 0.5f : 0.35f));

        // Crujido al pasar de 1/3 y de 2/3.
        int stage = _frostShown >= 0.66f ? 2 : _frostShown >= 0.33f ? 1 : 0;
        if (stage > _frostStage)
            PlaySound(_crackSound, -6);
        _frostStage = stage;

        _frost.Visible = _frostShown > 0.001f;
        if (_frost.Visible)
        {
            var mat = (ShaderMaterial)_frost.Material;
            mat.SetShaderParameter("amount", _frostShown);
            mat.SetShaderParameter("time_s", Time.GetTicksMsec() / 1000f);
        }

        _warnLabel.Modulate = _frostShown > 0.66f ? new Color(1f, 0.6f, 0.6f) : new Color(0.85f, 0.95f, 1f);
    }

    // Rastro de pasos: al caminar sobre el hielo quedan huellas que se derriten poco a poco.
    private Node3D _footprints;
    private float _stepDistance;
    private bool _stepLeft;

    private void UpdateFootprints(double delta)
    {
        if (_menuOpen || _titleOpen || _pauseOpen || !_player.IsOnFloor())
            return;

        Vector3 flat = new Vector3(_player.Velocity.X, 0f, _player.Velocity.Z);
        if (flat.Length() < 0.5f)
            return;

        _stepDistance += flat.Length() * (float)delta;
        if (_stepDistance < 0.85f)
            return;
        _stepDistance = 0f;

        Vector3 pos = _player.GlobalPosition;
        if (!_grid.IsOccupied(IceGrid.WorldToCell(pos)))
            return; // solo se marca el hielo

        if (_footprints == null)
        {
            _footprints = new Node3D();
            AddChild(_footprints);
        }

        // Una huella a cada lado, alternando, siguiendo la dirección del personaje.
        Vector3 right = _player.GlobalTransform.Basis.X;
        float side = _stepLeft ? -0.12f : 0.12f;
        _stepLeft = !_stepLeft;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.65f, 0.85f, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
        };
        var print = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.22f, 0.34f) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _footprints.AddChild(print);
        print.GlobalPosition = pos + right * side + Vector3.Up * 0.015f;
        print.RotationDegrees = new Vector3(-90f, _player.RotationDegrees.Y, 0f);

        // Se derrite: pierde color poco a poco y desaparece.
        Tween melt = CreateTween();
        melt.TweenMethod(Callable.From<float>(a => mat.AlbedoColor = new Color(0.45f, 0.65f, 0.85f, a)), 0.7f, 0f, 25f);
        melt.TweenCallback(Callable.From(print.QueueFree));
    }

    /// <summary>Cada mejora se ve: el cubo brilla con su capacidad, la cubitera ilumina con su mejora y el lanzador cambia de color con su velocidad.</summary>
    private void ApplyUpgradeVisuals()
    {
        float bucketLevel = _state.BucketCapacityUpgrade.Level / (float)_state.BucketCapacityUpgrade.MaxLevel;
        _bucketMaterial.EmissionEnabled = bucketLevel > 0f;
        _bucketMaterial.Emission = new Color(0.5f, 0.85f, 1f);
        _bucketMaterial.EmissionEnergyMultiplier = 0.9f * bucketLevel;

        if (_trayLight != null)
            _trayLight.LightEnergy = Mathf.Min(0.3f * _state.Tray.Level, 2.5f);

        if (_launcherGlow != null)
        {
            float speed = _state.LauncherSpeed.Level / (float)_state.LauncherSpeed.MaxLevel;
            Color c = new Color(0.5f, 0.9f, 1f).Lerp(new Color(1f, 0.85f, 0.3f), speed);
            float pulse = speed > 0f ? 1f + 0.2f * Mathf.Sin(Time.GetTicksMsec() / 1000f * 4f) : 1f;
            _launcherGlow.AlbedoColor = c;
            _launcherGlow.Emission = c;
            _launcherGlow.EmissionEnergyMultiplier = (1.8f + 1.5f * speed) * pulse;
        }
    }

}
