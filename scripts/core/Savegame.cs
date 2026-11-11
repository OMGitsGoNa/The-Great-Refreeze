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
    public bool RelaxedMode { get; set; }
    public bool BucketCarried { get; set; }
    public bool BearRescued { get; set; }
    public int[] UpgradeLevels { get; set; } = Array.Empty<int>();

    /// <summary>Segundos jugados en esta partida (se muestra en la lista de partidas).</summary>
    public double PlayedSeconds { get; set; }

    /// <summary>Progreso del Ártico al guardar, de 0 a 1 (se muestra en la lista de partidas).</summary>
    public double Progress { get; set; }

    /// <summary>Celdas con hielo colocado por el jugador, como [x, z, altura, forma].</summary>
    public List<int[]> Cells { get; set; } = new();
}

/// <summary>
/// Lee y escribe las partidas en user://savegame_1.json, _2 y _3.
/// Cada número es una ranura: la partida 1, 2 o 3.
/// </summary>
public static class SaveGame
{
    /// <summary>Número de ranuras de guardado.</summary>
    public const int Slots = 3;

    private static string PathFor(int slot) => $"user://savegame_{slot}.json";

    public static bool Exists(int slot) => FileAccess.FileExists(PathFor(slot));

    /// <summary>¿Hay alguna partida guardada en alguna ranura?</summary>
    public static bool AnyExists()
    {
        for (int slot = 1; slot <= Slots; slot++)
            if (Exists(slot))
                return true;
        return false;
    }

    /// <summary>Devuelve la partida de la ranura, o null si no hay o no se puede leer.</summary>
    public static SaveData Read(int slot)
    {
        if (!Exists(slot))
            return null;

        try
        {
            string json = FileAccess.GetFileAsString(PathFor(slot));
            return JsonSerializer.Deserialize<SaveData>(json);
        }
        catch (Exception e)
        {
            GD.PushWarning($"No se pudo leer la partida {slot}: {e.Message}");
            return null;
        }
    }

    public static void Write(int slot, SaveData data)
    {
        FileAccess file = FileAccess.Open(PathFor(slot), FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushWarning($"No se pudo escribir la partida {slot}");
            return;
        }

        file.StoreString(JsonSerializer.Serialize(data));
        file.Close();
    }

    public static void Delete(int slot)
    {
        if (Exists(slot))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathFor(slot)));
    }
}