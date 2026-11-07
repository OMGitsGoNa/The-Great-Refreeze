using Godot;

namespace GreatRefreeze;

public enum StationKind
{
    Tray,     // la cubitera: mantén E para hacer hielo, que cae en el montón
    Pile,     // el montón de hielo junto a la cubitera: E para coger hielo
    Upgrades, // máquina de mejoras
    Sell,     // envío de peces a empresas
    Bucket,   // cubo (mejora) que está en el muelle hasta que lo coges
}

/// <summary>
/// Un objeto con el que el jugador puede interactuar pulsando E.
/// </summary>
public partial class Station : StaticBody3D
{
    public StationKind Kind { get; set; }

    /// <summary>Texto que se muestra al mirarlo.</summary>
    public string Prompt { get; set; } = "";
}