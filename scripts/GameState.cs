using System;
using System.Collections.Generic;

namespace GreatRefreeze;

/// <summary>
/// Una mejora comprable. El coste sube con cada nivel: BaseCost * CostGrowth ^ Level.
/// </summary>
public sealed class Upgrade
{
    public string Name { get; }
    public string Description { get; }
    public double BaseCost { get; }
    public double CostGrowth { get; }
    public int Level { get; set; }

    public double Cost => BaseCost * Math.Pow(CostGrowth, Level);

    public Upgrade(string name, string description, double baseCost, double costGrowth)
    {
        Name = name;
        Description = description;
        BaseCost = baseCost;
        CostGrowth = costGrowth;
    }
}

/// <summary>
/// Toda la lógica del juego, sin nada de Godot: así es fácil de probar y de equilibrar.
/// Los números son provisionales; hay que ajustarlos jugando.
/// </summary>
public sealed class GameState
{
    /// <summary>Cubos que hay que colocar en total para recongelar el Ártico (provisional).</summary>
    public const int GoalCubes = 1000;

    /// <summary>Hielo por segundo que produce cada congelador.</summary>
    public const double FreezerRate = 0.5;

    public double Ice { get; private set; }     // hielo que llevas encima (1 de hielo = 1 cubo)
    public double Points { get; private set; }  // la moneda del juego
    public int PlacedCubes { get; private set; }

    public Upgrade Tray { get; } = new("Cubitera grande", "+1 hielo por uso", 10, 1.6);
    public Upgrade Freezer { get; } = new("Congelador", "+0,5 hielo/s", 40, 1.35);
    public Upgrade Shovel { get; } = new("Pala de colocación", "+50% de puntos por cubo", 30, 2.0);

    public IReadOnlyList<Upgrade> Upgrades { get; }

    public GameState()
    {
        Upgrades = new[] { Tray, Freezer, Shovel };
    }

    public double IcePerUse => 1 + Tray.Level;
    public double IcePerSecond => Freezer.Level * FreezerRate;

    /// <summary>Provisional: 1 punto por cubo. Más adelante los puntos vendrán de los sectores.</summary>
    public double PointsPerCube => 1 + 0.5 * Shovel.Level;

    /// <summary>Cubos enteros que puedes colocar ahora mismo.</summary>
    public int IceCubes => (int)Math.Floor(Ice);

    /// <summary>Progreso entre 0 y 1.</summary>
    public double Progress => Math.Min(1.0, (double)PlacedCubes / GoalCubes);
    public bool Won => PlacedCubes >= GoalCubes;

    /// <summary>Acción manual: llenar la cubitera.</summary>
    public void UseTray() => Ice += IcePerUse;

    /// <summary>Producción automática de los congeladores.</summary>
    public void Tick(double deltaSeconds) => Ice += IcePerSecond * deltaSeconds;

    /// <summary>Gasta 1 de hielo y cuenta un cubo colocado. Devuelve false si no hay hielo.</summary>
    public bool TryPlaceCube()
    {
        if (Ice < 1)
            return false;

        Ice -= 1;
        PlacedCubes++;
        Points += PointsPerCube;
        return true;
    }

    /// <summary>Puntos extra al completar un sector (provisional).</summary>
    public const double SectorBonus = 50;

    public void AddBonus(double amount) => Points += amount;

    /// <summary>Intenta comprar una mejora. Devuelve false si no hay puntos.</summary>
    public bool TryBuy(Upgrade upgrade)
    {
        double cost = upgrade.Cost;
        if (Points < cost)
            return false;

        Points -= cost;
        upgrade.Level++;
        return true;
    }
}