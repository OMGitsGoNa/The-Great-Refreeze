using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Datos de una partida guardada. Solo números y listas, así se pasan a JSON sin problemas.
/// Las mejoras se guardan por posición en GameState.Upgrades: si cambias el orden, cambia el guardado.
/// </summary>
public sealed class SaveData
{
    public int Seed { get; set; }
    public double Ice { get; set; }
    public int HandIce { get; set; }
    public int BucketIce { get; set; }
    public double Fish { get; set; }
    public double Money { get; set; }
    public int PlacedCubes { get; set; }
    public double MeltTimer { get; set; }
    public double CycleTime { get; set; }
    public bool BucketCarried { get; set; }
    public bool BearRescued { get; set; }
    public int[] UpgradeLevels { get; set; } = Array.Empty<int>();

    /// <summary>Celdas con hielo colocado por el jugador, como pares [x, z].</summary>
    public List<int[]> Cells { get; set; } = new();
}

/// <summary>Lee y escribe la partida en user://savegame.json.</summary>
public static class SaveGame
{
    private const string FilePath = "user://savegame.json";

    public static bool Exists => FileAccess.FileExists(FilePath);

    /// <summary>Devuelve la partida guardada, o null si no hay o no se puede leer.</summary>
    public static SaveData Read()
    {
        if (!Exists)
            return null;

        try
        {
            string json = FileAccess.GetFileAsString(FilePath);
            return JsonSerializer.Deserialize<SaveData>(json);
        }
        catch (Exception e)
        {
            GD.PushWarning($"No se pudo leer la partida guardada: {e.Message}");
            return null;
        }
    }

    public static void Write(SaveData data)
    {
        FileAccess file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushWarning("No se pudo escribir la partida guardada");
            return;
        }

        file.StoreString(JsonSerializer.Serialize(data));
        file.Close();
    }

    public static void Delete()
    {
        if (Exists)
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
    }
}