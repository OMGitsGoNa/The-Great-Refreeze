using Godot;

namespace GreatRefreeze;

public enum StationKind
{
    Tray,     // la cubitera: aquí se hace hielo a mano
    Upgrades, // máquina de mejoras
    Sell,     // envío de peces a empresas
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