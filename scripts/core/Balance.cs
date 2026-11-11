using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace GreatRefreeze;

/// <summary>Precio y crecimiento de una mejora. Coste del nivel N = baseCost * growth^N.</summary>
public sealed class UpgradePrice
{
    public double BaseCost { get; set; }
    public double Growth { get; set; } = 1.0;
    public int MaxLevel { get; set; } = 1;
}

/// <summary>Todos los valores de economía que se pueden ajustar desde balance.json.</summary>
public sealed class BalanceData
{
    /// <summary>Cubos para llenar el Ártico (la meta).</summary>
    public int GoalCubes { get; set; } = 100000;

    /// <summary>Dinero por pez vendido.</summary>
    public double FishPrice { get; set; } = 0.05;

    /// <summary>Peces extra por cubo y nivel de Pala (0,5 = +50 % por nivel).</summary>
    public double ShovelFishPerLevel { get; set; } = 0.5;

    /// <summary>Peces de bono al completar un sector, multiplicados por su anillo.</summary>
    public double SectorBonus { get; set; } = 10;

    /// <summary>Precios de cada mejora, por su clave (Bucket, Shovel, ...).</summary>
    public Dictionary<string, UpgradePrice> Upgrades { get; set; } = new();
}

/// <summary>
/// Lee los valores de economía de res://balance.json (en la raíz del proyecto).
/// Si el archivo falta o tiene un error, usa los valores por defecto. Las claves que falten se rellenan por defecto.
/// </summary>
public static class Balance
{
    private const string Path = "res://balance.json";

    private static BalanceData _data;

    public static BalanceData Data => _data ??= Load();

    /// <summary>Precio de una mejora por su clave. Si no existe, devuelve una compra gratuita de una vez.</summary>
    public static UpgradePrice Price(string key) =>
        Data.Upgrades.TryGetValue(key, out UpgradePrice price) ? price : new UpgradePrice { BaseCost = 0, MaxLevel = 1 };

    private static BalanceData Load()
    {
        BalanceData data = null;
        try
        {
            if (!FileAccess.FileExists(Path))
            {
                GD.PushWarning("No se encontró res://balance.json en la carpeta del proyecto: se usan los valores por defecto");
            }
            else
            {
                // Los nombres del JSON van en camelCase (baseCost) y las propiedades en PascalCase (BaseCost):
                // sin esta opción, System.Text.Json no las empareja y se ignoraba todo el archivo.
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                data = JsonSerializer.Deserialize<BalanceData>(FileAccess.GetFileAsString(Path), options);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"balance.json no se pudo leer ({e.Message}); se usan los valores por defecto");
        }

        data ??= new BalanceData();
        data.Upgrades ??= new Dictionary<string, UpgradePrice>();

        foreach (var pair in Defaults())
            data.Upgrades.TryAdd(pair.Key, pair.Value);

        // Diagnóstico: en la consola de Godot aparece qué valor se ha leído del archivo.
        // GD.Print($"[balance] cubo baseCost={data.Upgrades["Bucket"].BaseCost}, goalCubes={data.GoalCubes}, fishPrice={data.FishPrice}");
        return data;
    }

    /// <summary>Valores por defecto: los que ya tenía el juego.</summary>
    private static Dictionary<string, UpgradePrice> Defaults() => new()
    {
        ["Bucket"] = new UpgradePrice { BaseCost = 8, Growth = 1.0, MaxLevel = 1 },
        ["BucketCapacity"] = new UpgradePrice { BaseCost = 2, Growth = 1.25, MaxLevel = 19 },
        ["TableCapacity"] = new UpgradePrice { BaseCost = 5, Growth = 1.5, MaxLevel = 8 },
        ["ToughTable"] = new UpgradePrice { BaseCost = 5, Growth = 1.5, MaxLevel = 5 },
        ["Tray"] = new UpgradePrice { BaseCost = 8, Growth = 1.3, MaxLevel = 10 },
        ["Freezer"] = new UpgradePrice { BaseCost = 12, Growth = 1.25, MaxLevel = 10 },
        ["Shovel"] = new UpgradePrice { BaseCost = 60, Growth = 1.5, MaxLevel = 6 },
        ["AgileHands"] = new UpgradePrice { BaseCost = 12, Growth = 1.0, MaxLevel = 1 },
        ["Sprint"] = new UpgradePrice { BaseCost = 10, Growth = 1.0, MaxLevel = 1 },
        ["Ramps"] = new UpgradePrice { BaseCost = 6, Growth = 1.0, MaxLevel = 1 },
        ["AutoPlacer"] = new UpgradePrice { BaseCost = 60, Growth = 1.0, MaxLevel = 1 },
        ["LauncherSpeed"] = new UpgradePrice { BaseCost = 8, Growth = 1.12, MaxLevel = 20 },
        ["Flashlight"] = new UpgradePrice { BaseCost = 4, Growth = 1.0, MaxLevel = 1 },
        ["AutoFlashlight"] = new UpgradePrice { BaseCost = 15, Growth = 1.0, MaxLevel = 1 },
        ["LongArms"] = new UpgradePrice { BaseCost = 5, Growth = 1.4, MaxLevel = 10 },
        ["FastHands"] = new UpgradePrice { BaseCost = 20, Growth = 1.5, MaxLevel = 4 },
    };
}