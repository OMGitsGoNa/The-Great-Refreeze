using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- sonido ambiente (viento)

    private AudioStreamPlayer _wind;
    private float _windDb = -22f;

    /// <summary>Viento de fondo en bucle. Suena bajito de día, un poco más de noche y fuerte en las tormentas.</summary>
    private void BuildWind()
    {
        _wind = new AudioStreamPlayer { Stream = Sounds.WindLoop(), VolumeDb = _windDb };
        AddChild(_wind);
        _wind.Play();
    }

    private void UpdateWind(double delta)
    {
        float target = _storm ? -7f : Mathf.Lerp(-22f, -16f, (float)_state.Darkness);
        _windDb = Mathf.MoveToward(_windDb, target, (float)delta * 10f); // cambio suave, sin saltos
        _wind.VolumeDb = _windDb;
    }
}
