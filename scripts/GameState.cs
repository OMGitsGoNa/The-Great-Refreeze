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
    /// <summary>Hielo que hay que colocar en total para recongelar el Ártico.</summary>
    public const double GoalIce = 50_000;

    /// <summary>Hielo por segundo que produce cada congelador.</summary>
    public const double FreezerRate = 0.5;

    public double Ice { get; private set; }        // hielo que llevas encima
    public double Money { get; private set; }
    public double PlacedIce { get; private set; }  // hielo ya colocado en el Ártico (progreso)

    public Upgrade Tray { get; } = new("Cubitera grande", "+1 hielo por uso", 10, 1.6);
    public Upgrade Freezer { get; } = new("Congelador", "+0,5 hielo/s", 40, 1.35);
    public Upgrade Shovel { get; } = new("Pala de colocación", "+50% dinero por hielo", 30, 2.0);

    public IReadOnlyList<Upgrade> Upgrades { get; }

    public GameState()
    {
        Upgrades = new[] { Tray, Freezer, Shovel };
    }

    public double IcePerUse => 1 + Tray.Level;
    public double IcePerSecond => Freezer.Level * FreezerRate;
    public double MoneyPerIce => 1 + 0.5 * Shovel.Level;

    /// <summary>Progreso entre 0 y 1.</summary>
    public double Progress => Math.Min(1.0, PlacedIce / GoalIce);
    public bool Won => PlacedIce >= GoalIce;

    /// <summary>Acción manual: llenar la cubitera.</summary>
    public void UseTray() => Ice += IcePerUse;

    /// <summary>Producción automática de los congeladores.</summary>
    public void Tick(double deltaSeconds) => Ice += IcePerSecond * deltaSeconds;

    /// <summary>Coloca todo el hielo que llevas. Devuelve cuánto se ha colocado.</summary>
    public double Deposit()
    {
        double placed = Ice;
        if (placed <= 0) return 0;

        PlacedIce += placed;
        Money += placed * MoneyPerIce;
        Ice = 0;
        return placed;
    }

    /// <summary>Intenta comprar una mejora. Devuelve false si no hay dinero.</summary>
    public bool TryBuy(Upgrade upgrade)
    {
        double cost = upgrade.Cost;
        if (Money < cost) return false;

        Money -= cost;
        upgrade.Level++;
        return true;
    }
}
