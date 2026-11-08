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
/// Flujo: la cubitera hace hielo que va al montón de la mesa; el montón se derrite poco a poco;
/// con hielo en la mano (o en el cubo) se coloca y salva peces; las empresas pagan dinero
/// por cada pez enviado; el dinero compra mejoras. Los números son provisionales.
/// </summary>
public sealed class GameState
{
    /// <summary>Cubos que hay que colocar en total para recongelar el Ártico.</summary>
    public const int GoalCubes = 1_000_000;

    /// <summary>Hielo por segundo que produce cada congelador (va al montón).</summary>
    public const double FreezerRate = 0.5;

    /// <summary>Dinero que pagan las empresas por cada pez enviado (1 pez = 1 cubo = 0,05 $).</summary>
    public const double FishPrice = 0.05;

    /// <summary>Peces base al completar un sector. Se multiplica por el anillo.</summary>
    public const double SectorBonus = 10;

    /// <summary>Distancia, en cubos, desde el muelle a la que está el oso polar.</summary>
    public const int BearDistanceCubes = 50;

    /// <summary>Peces que cuesta rescatar al oso polar (se pagan con peces, no con dinero).</summary>
    public const double BearFishCost = 100;

    /// <summary>Hielos que caben sobre la mesa sin mejorar, y cuántos añade cada mejora (10, 29... hasta 200).</summary>
    public static readonly int[] PileSizes = { 10, 20, 40, 80, 160, 200 };

    /// <summary>Segundos base entre derretimientos de un hielo del montón, y cuántos añade cada mejora de mesa resistente.</summary>
    public const double MeltBaseSeconds = 15;
    public const double MeltStepSeconds = 5;

    /// <summary>Hielo que se puede llevar en la mano sin cubo.</summary>
    public const int HandCapacity = 1;

    /// <summary>Capacidad del cubo sin mejorar, y cuánto añade cada mejora.</summary>
    public const int BucketBase = 10;
    public const int BucketStep = 10;

    /// <summary>Fracción de peces y dinero que se pierde al congelarse en el agua.</summary>
    public const double DrowningPenalty = 0.10;

    /// <summary>Montón de hielo sobre la mesa (puede tener fracción por los congeladores).</summary>
    public double Ice { get; private set; }

    /// <summary>Hielo en la mano sin cubo (0 o 1).</summary>
    public int HandIce { get; private set; }

    /// <summary>Hielo dentro del cubo (solo si tienes la mejora Cubo).</summary>
    public int BucketIce { get; private set; }

    public double Fish { get; private set; }     // peces salvados, pendientes de enviar
    public double Money { get; private set; }    // dinero para comprar mejoras
    public int PlacedCubes { get; private set; }

    /// <summary>Se dispara cada vez que un hielo del montón se derrite.</summary>
    public event Action Melted;

    private double _meltTimer;

    // Precios: el cubo cuesta 5 $; su primera mejora de capacidad, 1 $.
    public Upgrade Bucket { get; } = new("Cubo", "Lleva el hielo en la mano (no se derrite)", 5, 1.0, 1);
    public Upgrade BucketCapacityUpgrade { get; } = new("Cubo más grande", "+10 de capacidad (máximo 50)", 1, 1.6, 4);
    public Upgrade Tray { get; } = new("Cubitera grande", "+1 hielo por carga", 1, 1.6);
    public Upgrade Freezer { get; } = new("Congelador", "+0,5 hielo/s al montón", 4, 1.35);
    public Upgrade Shovel { get; } = new("Pala de colocación", "+50% de peces por cubo", 5, 1.8);
    public Upgrade AgileHands { get; } = new("Manos ágiles", "Mantén el clic para colocar hielo sin parar", 8, 1.0, 1);
    public Upgrade Sprint { get; } = new("Sprint", "Mantén Shift para correr", 25, 1.0, 1);
    public Upgrade Ramps { get; } = new("Rampas", "Bajadas del muelle al hielo: sin saltar", 5, 1.0, 1);

    // Mesa: ambas empiezan en 2 $ y suben progresivamente.
    public Upgrade TableCapacityUpgrade { get; } = new("Mesa más grande", "Capacidad x2 (10 → 200)", 2, 1.5, PileSizes.Length - 1);
    public Upgrade ToughTableUpgrade { get; } = new("Mesa resistente", "El hielo tarda 5 s más en derretirse", 2, 1.5, 5);

    public IReadOnlyList<Upgrade> Upgrades { get; }

    public GameState()
    {
        Upgrades = new[]
        {
            Bucket, BucketCapacityUpgrade, TableCapacityUpgrade, ToughTableUpgrade,
            Tray, Freezer, Shovel, AgileHands, Sprint, Ramps,
        };
    }

    public bool HasBucket => Bucket.Level > 0;
    public bool HasAgileHands => AgileHands.Level > 0;
    public bool HasSprint => Sprint.Level > 0;

    /// <summary>Capacidad actual del cubo: 10, 20, 30, 40 o 50.</summary>
    public int BucketMax => BucketBase + BucketStep * BucketCapacityUpgrade.Level;

    /// <summary>Capacidad actual de la mesa: 10, 20, 40, 80, 160 o 200 (cada mejora la duplica).</summary>
    public int PileMax => PileSizes[TableCapacityUpgrade.Level];

    /// <summary>Segundos que tarda en derretirse un hielo de la mesa.</summary>
    public double MeltTime => MeltBaseSeconds + MeltStepSeconds * ToughTableUpgrade.Level;

    public double IcePerUse => 1 + Tray.Level;
    public double IcePerSecond => Freezer.Level * FreezerRate;

    /// <summary>Un cubo colocado salva 1 pez, más la pala.</summary>
    public double FishPerCube => 1 + 0.5 * Shovel.Level;

    /// <summary>Cubos enteros en el montón.</summary>
    public int IceCubes => (int)Math.Floor(Ice);

    /// <summary>Segundos que faltan para que se derrita el siguiente hielo del montón.</summary>
    public double SecondsUntilMelt => MeltTime - _meltTimer;

    /// <summary>Progreso entre 0 y 1.</summary>
    public double Progress => Math.Min(1.0, (double)PlacedCubes / GoalCubes);
    public bool Won => PlacedCubes >= GoalCubes;

    public bool IsBucketFull => BucketIce >= BucketMax;

    /// <summary>Producción de los congeladores y derretimiento del montón.</summary>
    public void Tick(double deltaSeconds)
    {
        Ice = Math.Min(PileMax, Ice + IcePerSecond * deltaSeconds);

        if (IceCubes <= 0)
        {
            _meltTimer = 0;
            return;
        }

        _meltTimer += deltaSeconds;
        if (_meltTimer >= MeltTime)
        {
            _meltTimer = 0;
            Ice -= 1;
            Melted?.Invoke();
        }
    }

    /// <summary>La cubitera hace hielo y lo deja en el montón. Devuelve cuánto ha hecho.</summary>
    public double ProduceFromTray()
    {
        double added = Math.Min(IcePerUse, PileMax - Ice);
        if (added <= 0)
            return 0;

        Ice += added;
        return added;
    }

    /// <summary>
    /// Coge hielo del montón. Sin cubo: 1 a la mano (si la mano está libre).
    /// Con el cubo en la mano: todo lo que quepa en el cubo. Devuelve cuántos cubos ha cogido.
    /// </summary>
    public int TakeFromPile(bool bucketInHand)
    {
        if (IceCubes <= 0)
            return 0;

        if (bucketInHand)
        {
            int moved = Math.Min(IceCubes, BucketMax - BucketIce);
            if (moved <= 0)
                return 0;

            BucketIce += moved;
            Ice -= moved;
            return moved;
        }

        if (HandIce >= HandCapacity)
            return 0;

        HandIce = HandCapacity;
        Ice -= 1;
        return 1;
    }

    /// <summary>Al coger el cubo, el hielo de la mano pasa a él (si cabe).</summary>
    public void PutHandIceInBucket()
    {
        int moved = Math.Min(HandIce, BucketMax - BucketIce);
        if (moved <= 0)
            return;

        BucketIce += moved;
        HandIce -= moved;
    }

    /// <summary>
    /// Hay hielo para colocar: el del cubo si lo llevas en la mano, o el de la mano si no.
    /// </summary>
    public bool HasIceToPlace(bool bucketInHand) =>
        bucketInHand ? BucketIce >= 1 : HandIce >= 1;

    /// <summary>Gasta 1 de hielo (del cubo o de la mano), cuenta un cubo y salva peces.</summary>
    public bool TryPlaceCube(bool bucketInHand)
    {
        if (!HasIceToPlace(bucketInHand))
            return false;

        if (bucketInHand)
            BucketIce -= 1;
        else
            HandIce -= 1;

        PlacedCubes++;
        Fish += FishPerCube;
        return true;
    }

    /// <summary>Añade dinero directamente (lo usa el modo dev y no el juego normal).</summary>
    public void AddMoney(double amount) => Money += amount;

    /// <summary>Peces que da completar un sector del anillo indicado: el bono base multiplicado por el anillo.</summary>
    public double SectorBonusFor(int ring) => SectorBonus * ring;

    /// <summary>Gasta peces si hay suficientes. Devuelve false si no alcanzan.</summary>
    public bool TrySpendFish(double amount)
    {
        if (Fish < amount)
            return false;

        Fish -= amount;
        return true;
    }

    /// <summary>Peces extra, por ejemplo el bono de sector.</summary>
    public void AddFish(double amount) => Fish += amount;

    /// <summary>Al congelarse en el agua pierdes un 10% de los peces y del dinero.</summary>
    public void ApplyDrowningPenalty()
    {
        Fish *= 1 - DrowningPenalty;
        Money *= 1 - DrowningPenalty;
    }

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

    /// <summary>
    /// Intenta comprar una mejora. Devuelve false si ya está al máximo, no hay dinero
    /// o la mejora del cubo requiere tener el cubo.
    /// </summary>
    public bool TryBuy(Upgrade upgrade)
    {
        double cost = upgrade.Cost;
        if (upgrade.IsMaxed || Money < cost)
            return false;

        if (upgrade == BucketCapacityUpgrade && !HasBucket)
            return false;

        Money -= cost;
        upgrade.Level++;
        return true;
    }
}