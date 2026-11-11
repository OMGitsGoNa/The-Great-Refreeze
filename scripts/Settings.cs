using System;
using System.Text.Json;
using Godot;

namespace GreatRefreeze;

/// <summary>Datos de los ajustes que se guardan en disco.</summary>
public sealed class SettingsData
{
    public float Sensitivity { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
}

/// <summary>
/// Ajustes del jugador: sensibilidad del ratón y volumen general.
/// Se guardan en user://settings.json, aparte de las partidas, así que valen para todas.
/// </summary>
public static class Settings
{
    private const string Path = "user://settings.json";

    /// <summary>Multiplicador de la sensibilidad del ratón (de 0,5 a 2; 1 es lo normal).</summary>
    public static float Sensitivity { get; set; } = 1f;

    /// <summary>Volumen general, de 0 a 1.</summary>
    public static float Volume { get; set; } = 1f;

    /// <summary>Lee los ajustes guardados (si los hay) y aplica el volumen.</summary>
    public static void Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                SettingsData data = JsonSerializer.Deserialize<SettingsData>(FileAccess.GetFileAsString(Path));
                if (data != null)
                {
                    Sensitivity = Mathf.Clamp(data.Sensitivity, 0.5f, 2f);
                    Volume = Mathf.Clamp(data.Volume, 0f, 1f);
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"No se pudieron leer los ajustes: {e.Message}");
        }

        ApplyVolume();
    }

    public static void Save()
    {
        FileAccess file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushWarning("No se pudieron guardar los ajustes");
            return;
        }

        file.StoreString(JsonSerializer.Serialize(new SettingsData { Sensitivity = Sensitivity, Volume = Volume }));
        file.Close();
    }

    /// <summary>Aplica el volumen al bus principal (0 = silencio).</summary>
    public static void ApplyVolume()
    {
        float db = Volume <= 0.001f ? -80f : AudioServer.LinearToDb(Volume);
        AudioServer.SetBusVolumeDb(0, db);
    }
}
