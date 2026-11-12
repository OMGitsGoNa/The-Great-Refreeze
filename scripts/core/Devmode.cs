namespace GreatRefreeze;

/// <summary>
/// MODO DEV: solo para pruebas. Código auxiliar que se eliminará en el futuro.
/// Para quitarlo: borra este archivo y la línea "DevMode.Apply(_state);" de Main.cs.
/// Pon Enabled en false para desactivarlo sin borrar nada.
/// </summary>
public static class DevMode
{
    /// <summary>true = modo dev activo; false = juego normal.</summary>
    public const bool Enabled = true;

    /// <summary>Dinero con el que empieza el modo dev.</summary>
    public const double StartMoney = 10000;

    /// <summary>Aplica las ayudas de prueba al estado del juego al empezar.</summary>
    public static void Apply(GameState state)
    {
        if (!Enabled)
            return;

        state.AddMoney(StartMoney);
    }
}