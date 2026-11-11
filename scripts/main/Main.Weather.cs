using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- clima

    private CpuParticles3D _snow;

    /// <summary>Nieve y viento. Al principio es una tormenta; según avanza el Ártico, se queda en copos suaves y acaba despejado.</summary>
    private void BuildWeather()
    {
        var flake = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            VertexColorUseAsAlbedo = true, // para que el color (y su transparencia) de cada partícula se vea
        };
        _snow = new CpuParticles3D
        {
            Amount = 900,
            Lifetime = 9.0,
            LocalCoords = false, // las partículas se quedan en el mundo mientras el emisor te sigue
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(22f, 1f, 22f),
            Direction = new Vector3(0f, -1f, 0f),
            Spread = 8f,
            InitialVelocityMin = 0.3f,
            InitialVelocityMax = 0.6f,
            ScaleAmountMin = 0.8f,
            ScaleAmountMax = 1.4f,
            Mesh = new QuadMesh { Size = new Vector2(0.06f, 0.06f), Material = flake },
        };
        AddChild(_snow);
    }

    private void UpdateWeather()
    {
        if (_snow == null)
            return;

        float intensity = _storm ? 1f : 1f - (float)_state.Progress; // 1 al principio, 0 con el Ártico recongelado
        _snow.GlobalPosition = _player.GlobalPosition + new Vector3(0f, 10f, 0f);
        // Tormenta: viento fuerte, copos grandes y rápidos. Normal: viento suave.
        float wind = _storm ? 12f : 3.5f * intensity;
        _snow.Gravity = new Vector3(wind, _storm ? -2.5f : -1.2f, _storm ? 4f : intensity);
        _snow.InitialVelocityMin = _storm ? 2f : 0.3f;
        _snow.InitialVelocityMax = _storm ? 4f : 0.6f;
        _snow.ScaleAmountMin = _storm ? 1.5f : 0.8f;
        _snow.ScaleAmountMax = _storm ? 2.5f : 1.4f;
        _snow.Color = new Color(1f, 1f, 1f, 0.25f + 0.6f * intensity);
        _snow.Emitting = intensity > 0.05f;
    }
    /// <summary>
    /// Con más Ártico recongelado el agua se vuelve blanca-azulada, el cielo más claro y el sol más fuerte.
    /// Se usa raíz cuadrada del progreso para que se note desde el principio. Solo se recalcula si cambia el progreso.
    /// </summary>
    private void UpdateArcticLook()
    {
        // Se recalcula cada fotograma porque la luz del día cambia con el ciclo.
        float t = (float)Math.Sqrt(_state.Progress);
        float day = (float)_state.Daylight; // 0 de noche, 1 a pleno día

        Color waterDay = new Color(0.05f, 0.2f, 0.35f).Lerp(new Color(0.75f, 0.86f, 0.94f), t);
        Color skyTopDay = new Color(0.3f, 0.5f, 0.8f).Lerp(new Color(0.7f, 0.85f, 0.97f), t);
        Color skyHorizonDay = new Color(0.6f, 0.75f, 0.9f).Lerp(new Color(0.95f, 0.97f, 1f), t);
        Color nightTop = new Color(0.02f, 0.03f, 0.07f);
        Color nightHorizon = new Color(0.05f, 0.06f, 0.1f);

        _waterMaterial.AlbedoColor = waterDay * (0.25f + 0.75f * day);
        _skyMaterial.SkyTopColor = nightTop.Lerp(skyTopDay, day);
        _skyMaterial.SkyHorizonColor = nightHorizon.Lerp(skyHorizonDay, day);
        _sun.LightEnergy = (1f + 0.4f * t) * day;

        // De noche la luz ambiente baja y la niebla recorta la vista: solo se ve cerca.
        _environment.AmbientLightEnergy = 0.15f + 0.85f * day;
        _environment.FogEnabled = day < 0.99f;
        _environment.FogDensity = 0.12f * (1f - day);
        _environment.FogLightColor = nightHorizon;
    }

    /// <summary>Tecla T: enciende y apaga la linterna (solo si la has comprado y no hay menú abierto).</summary>
    private void HandleFlashlightKey()
    {
        bool down = Input.IsPhysicalKeyPressed(Key.T);
        if (down && !_tWasDown && _state.HasFlashlight && !_menuOpen)
            _flashOn = !_flashOn;
        _tWasDown = down;
    }

    /// <summary>Día y noche: el sol recorre el cielo y se apaga de noche. Controla también la linterna.</summary>
    private void UpdateDayNight()
    {
        if (!_state.IsNight)
        {
            double dayProgress = _state.CycleTime / GameState.DayLength;
            _sun.RotationDegrees = new Vector3((float)(-10 - 160 * dayProgress), -30f, 0f);
        }

        EnsureFlashlight();
        if (_flashlight != null)
            _flashlight.Visible = _state.HasFlashlight && (_flashOn || (_state.HasAutoFlashlight && _state.Darkness > 0.35))
                && _flickerOn; // la tormenta hace parpadear la linterna fuera de la base
    }

    /// <summary>La linterna es un foco que sale de la cámara. Se crea la primera vez que hace falta.</summary>
    private void EnsureFlashlight()
    {
        if (_flashlight != null || _player?.Camera == null)
            return;

        _flashlight = new SpotLight3D
        {
            SpotRange = 30f,
            SpotAngle = 32f,
            LightEnergy = 4f,
            LightColor = new Color(1f, 0.95f, 0.8f),
            ShadowEnabled = false,
            Visible = false,
        };
        _player.Camera.AddChild(_flashlight);
    }

    // Tormenta de noche: a veces, al anochecer, hay una tormenta de 30 s. Fuera de la base no se ve la linterna.
    private bool _storm;
    private float _stormTimer;
    private bool _nightRolled;
    private readonly Random _stormRng = new(2024);
    private bool _flickerOn = true; // parpadeo irregular de la linterna durante la tormenta
    private float _flickerTimer;

    /// <summary>La base: la zona de muelle y mesa, a menos de 8 m del centro.</summary>
    private bool InRefuge()
    {
        Vector3 p = _player.GlobalPosition;
        return new Vector2(p.X, p.Z).Length() < 8f;
    }

    private void UpdateStorm(double delta)
    {
        if (!_state.IsNight)
        {
            _nightRolled = false; // al volver la noche se vuelve a tirar
            _storm = false;
        }
        else if (!_nightRolled)
        {
            _nightRolled = true;
            if (_stormRng.NextDouble() < 0.5)
            {
                _storm = true;
                _stormTimer = 60f;
                ShowMessage("¡Tormenta! Busca refugio en la base o espera a que pase");
            }
        }

        if (_storm)
        {
            _stormTimer -= (float)delta;
            if (_stormTimer <= 0f)
                _storm = false;
        }

        // Parpadeo irregular de la linterna fuera de la base: se apaga y se enciende a tirones, pero casi siempre hay luz.
        if (_storm && !InRefuge())
        {
            _flickerTimer -= (float)delta;
            if (_flickerTimer <= 0f)
            {
                _flickerOn = _stormRng.NextDouble() < 0.7;
                _flickerTimer = 0.05f + (float)_stormRng.NextDouble() * 0.35f;
            }
        }
        else
        {
            _flickerOn = true;
        }

        // Niebla extra durante la tormenta, salvo dentro de la base. Nunca se pierde la vista del todo.
        if (_storm && !InRefuge())
            _environment.FogDensity += 0.06f;
    }

}
