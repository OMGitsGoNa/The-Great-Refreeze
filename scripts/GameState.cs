using System;
using System.Collections.Generic;

namespace GreatRefreeze;

/// <summary>
/// Una mejora comprable, con precio en dinero. El coste sube con cada nivel: BaseCost * CostGrowth ^ Level.
/// Las desbloqueables tienen MaxLevel = 1.
/// </summary>
public sealed class Upgrade
{
    public string Name { get; }
    public string Description { get; }
    public double BaseCost { get; }
    public double CostGrowth { get; }
    public int MaxLevel { get; }
    public int Level { get; set; }

    public bool IsMaxed => Level >= MaxLevel;
    public double Cost => BaseCost * Math.Pow(CostGrowth, Level);

    public Upgrade(string name, string description, double baseCost, double costGrowth, int maxLevel = int.MaxValue)
    {
        Name = name;
        Description = description;
        BaseCost = baseCost;
        CostGrowth = costGrowth;
        MaxLevel = maxLevel;
    }
}

/// <summary>
/// Toda la lógica del juego, sin nada de Godot: así es fácil de probar y de equilibrar.
/// Flujo: colocar cubos salva peces; las empresas pagan dinero por cada pez enviado;
/// el dinero compra mejoras. Los números son provisionales.
/// </summary>
public sealed class GameState
{
    /// <summary>Cubos que hay que colocar en total para recongelar el Ártico (provisional).</summary>
    public const int GoalCubes = 1000;

    /// <summary>Hielo por segundo que produce cada congelador.</summary>
    public const double FreezerRate = 0.5;

    /// <summary>Dinero que pagan las empresas por cada pez enviado.</summary>
    public const double FishPrice = 0.1;

    /// <summary>Peces extra al completar un sector.</summary>
    public const double SectorBonus = 10;

    /// <summary>Distancia entre osos polares, en cubos colocados. Los osos aún no hacen nada.</summary>
    public const int BearSpacingCubes = 50;

    public double Ice { get; private set; }      // hielo que llevas encima (1 de hielo = 1 cubo)
    public double Fish { get; private set; }     // peces salvados, pendientes de enviar
    public double Money { get; private set; }    // dinero para comprar mejoras
    public int PlacedCubes { get; private set; }

    // Precios divididos entre 10 respecto a la versión anterior (ahora en dinero).
    public Upgrade Tray { get; } = new("Cubitera grande", "+1 hielo por carga", 1, 1.6);
    public Upgrade Freezer { get; } = new("Congelador", "+0,5 hielo/s", 4, 1.35);
    public Upgrade Shovel { get; } = new("Pala de colocación", "+50% de peces por cubo", 3, 2.0);
    public Upgrade AgileHands { get; } = new("Manos ágiles", "Mantén el clic para colocar hielo sin parar", 10, 1.0, 1);
    public Upgrade Sprint { get; } = new("Sprint", "Mantén Shift para correr", 100, 1.0, 1);

    public IReadOnlyList<Upgrade> Upgrades { get; }

    public GameState()
    {
        Upgrades = new[] { Tray, Freezer, Shovel, AgileHands, Sprint };
    }

    public bool HasAgileHands => AgileHands.Level > 0;
    public bool HasSprint => Sprint.Level > 0;

    public double IcePerUse => 1 + Tray.Level;
    public double IcePerSecond => Freezer.Level * FreezerRate;

    /// <summary>Un cubo colocado salva 1 pez, más la pala.</summary>
    public double FishPerCube => 1 + 0.5 * Shovel.Level;

    /// <summary>Cubos enteros que puedes colocar ahora mismo.</summary>
    public int IceCubes => (int)Math.Floor(Ice);

    /// <summary>Progreso entre 0 y 1.</summary>
    public double Progress => Math.Min(1.0, (double)PlacedCubes / GoalCubes);
    public bool Won => PlacedCubes >= GoalCubes;

    /// <summary>Capacidad máxima del cubo que recoge el hielo de la cubitera.</summary>
    public const int BucketCapacity = 10;

    /// <summary>Hielo que hay ahora mismo en el cubo.</summary>
    public double BucketIce { get; private set; }

    public bool IsBucketFull => BucketIce >= BucketCapacity;

    /// <summary>Acción de la cubitera al completar la carga: deja hielo en el cubo. Devuelve cuánto ha entrado.</summary>
    public double UseTray()
    {
        double added = Math.Min(IcePerUse, BucketCapacity - BucketIce);
        BucketIce += added;
        return added;
    }


    /// <summary>Producción automática de los congeladores.</summary>
    public void Tick(double deltaSeconds) => Ice += IcePerSecond * deltaSeconds;

    /// <summary>
    /// Hay hielo para colocar: el del cubo si lo llevas en la mano, o el suelto del inventario.
    /// </summary>
    public bool HasIceFor(bool bucketInHand) =>
        (bucketInHand && BucketIce >= 1) || Ice >= 1;

    /// <summary>
    /// Gasta 1 de hielo (primero el del cubo si lo llevas en la mano), cuenta un cubo y salva peces.
    /// Devuelve false si no hay hielo.
    /// </summary>
    public bool TryPlaceCube(bool bucketInHand)
    {
        if (bucketInHand && BucketIce >= 1)
            BucketIce -= 1;
        else if (Ice >= 1)
            Ice -= 1;
        else
            return false;

        PlacedCubes++;
        Fish += FishPerCube;
        return true;
    }

    /// <summary>Peces extra, por ejemplo el bono de sector.</summary>
    public void AddFish(double amount) => Fish += amount;

    /// <summary>Envía los peces enteros a las empresas. Devuelve el dinero ganado.</summary>
    public double SellFish()
    {
        int toSell = (int)Math.Floor(Fish);
        if (toSell <= 0)
            return 0;

        Fish -= toSell;
        double earned = toSell * FishPrice;
        Money += earned;
        return earned;
    }

    /// <summary>Intenta comprar una mejora. Devuelve false si ya está al máximo o no hay dinero.</summary>
    public bool TryBuy(Upgrade upgrade)
    {
        double cost = upgrade.Cost;
        if (upgrade.IsMaxed || Money < cost)
            return false;

        Money -= cost;
        upgrade.Level++;
        return true;
    }
}