using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

/// <summary>Rectángulo de celdas. Los dos extremos están incluidos.</summary>
public readonly record struct CellRect(int X0, int Z0, int X1, int Z1)
{
    public bool Contains(Vector2I cell) => cell.X >= X0 && cell.X <= X1 && cell.Y >= Z0 && cell.Y <= Z1;

    public int Area => (X1 - X0 + 1) * (Z1 - Z0 + 1);

    public int OverlapArea(CellRect other)
    {
        int w = Math.Min(X1, other.X1) - Math.Max(X0, other.X0) + 1;
        int h = Math.Min(Z1, other.Z1) - Math.Max(Z0, other.Z0) + 1;
        return w > 0 && h > 0 ? w * h : 0;
    }
}

/// <summary>Un sector: un trozo de un anillo. Lleva la cuenta de cubos colocados.</summary>
public sealed class Sector
{
    public int Ring { get; }
    public CellRect Rect { get; }

    /// <summary>Celdas donde se puede poner hielo (sin muelle ni plataforma).</summary>
    public int Free { get; set; }

    /// <summary>Cubos colocados por el jugador dentro del sector.</summary>
    public int Filled { get; private set; }

    /// <summary>Parcela de peces: al completarla da peces extra (uno por celda).</summary>
    public bool IsFish { get; set; }

    public bool Completed => Free > 0 && Filled >= Free;

    public Sector(int ring, CellRect rect)
    {
        Ring = ring;
        Rect = rect;
    }

    public void AddFilled() => Filled++;
}

/// <summary>
/// Anillos de sectores alrededor del muelle. Cada anillo es más ancho que el anterior (5, 10, 15... celdas),
/// así que los sectores cercanos son pequeños y los lejanos grandes. Con 14 anillos, el borde exterior
/// cierra un espacio de unas 1 170 000 celdas: más que el millón de cubos de la meta.
/// En cada partida, cada anillo tiene 1 o 2 parcelas de peces elegidas al azar.
/// </summary>
public sealed class SectorMap
{
    /// <summary>Límites del muelle en celdas (16 x 16 unidades con celdas de 0,5).</summary>
    public const int DockMin = -16;
    public const int DockMax = 15;

    /// <summary>Número de anillos. Con 14 se cierra el espacio hacia 1 millón de cubos.</summary>
    public const int RingCount = 14;

    /// <summary>Cuánto se ensancha cada anillo respecto al anterior, en celdas.</summary>
    public const int RingStep = 5;

    private readonly List<Sector> _sectors = new();

    public IReadOnlyList<Sector> Sectors => _sectors;

    /// <summary>Distancia en celdas desde el muelle hasta el borde exterior del anillo k (0 para el muelle).</summary>
    public static int RingEdge(int k) => RingStep * k * (k + 1) / 2;

    public static CellRect Dock => new(DockMin, DockMin, DockMax, DockMax);

    /// <summary>Área total cerrada por el último anillo. Fuera de aquí no se puede colocar hielo.</summary>
    public static CellRect World
    {
        get
        {
            int edge = RingEdge(RingCount);
            return new CellRect(DockMin - edge, DockMin - edge, DockMax + edge, DockMax + edge);
        }
    }

    public SectorMap(Random rng, IEnumerable<CellRect> fixedAreas)
    {
        for (int k = 1; k <= RingCount; k++)
            BuildRing(k);

        // Celdas que no son hielo colocable: muelle y plataformas fijas.
        var fixedList = new List<CellRect>(fixedAreas) { Dock };
        foreach (Sector s in _sectors)
        {
            int blocked = fixedList.Sum(f => s.Rect.OverlapArea(f));
            s.Free = s.Rect.Area - blocked;
        }

        // Parcelas de peces: 1 o 2 por anillo, al azar en cada partida.
        for (int k = 1; k <= RingCount; k++)
        {
            var candidates = _sectors.Where(s => s.Ring == k && s.Free > 0).ToList();
            int count = rng.Next(1, 3);
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int pick = rng.Next(candidates.Count);
                candidates[pick].IsFish = true;
                candidates.RemoveAt(pick);
            }
        }
    }

    /// <summary>Sector que contiene la celda, o null si está en el muelle o fuera del mundo.</summary>
    public Sector At(Vector2I cell)
    {
        foreach (Sector s in _sectors)
            if (s.Rect.Contains(cell))
                return s;
        return null;
    }

    /// <summary>
    /// Anota un cubo colocado. Devuelve el sector si acaba de completarse, o null.
    /// </summary>
    public Sector RecordPlaced(Vector2I cell)
    {
        Sector s = At(cell);
        if (s == null)
            return null;

        bool wasDone = s.Completed;
        s.AddFilled();
        return !wasDone && s.Completed ? s : null;
    }

    /// <summary>
    /// Crea los sectores de un anillo. Cada banda tiene de ancho el propio anillo y se parte en trozos del mismo ancho,
    /// empezando por una esquina. Las bandas norte y sur llevan las esquinas; este y oeste van entre ellas.
    /// </summary>
    private void BuildRing(int k)
    {
        int inner = RingEdge(k - 1);
        int outer = RingEdge(k);
        int depth = RingStep * k;

        int ix0 = DockMin - inner;
        int ix1 = DockMax + inner;
        int oz0 = DockMin - outer;
        int oz1 = DockMax + outer;
        int ox0 = oz0;
        int ox1 = oz1;

        for (int x = ox0; x <= ox1; x += depth)
        {
            int x1 = Math.Min(x + depth - 1, ox1);
            _sectors.Add(new Sector(k, new CellRect(x, oz0, x1, ix0 - 1)));   // norte
            _sectors.Add(new Sector(k, new CellRect(x, ix1 + 1, x1, oz1)));   // sur
        }

        for (int z = ix0; z <= ix1; z += depth)
        {
            int z1 = Math.Min(z + depth - 1, ix1);
            _sectors.Add(new Sector(k, new CellRect(ox0, z, ix0 - 1, z1)));   // oeste
            _sectors.Add(new Sector(k, new CellRect(ix1 + 1, z, ox1, z1)));   // este
        }
    }
}