using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- menú de mejoras

    private void OpenMenu()
    {
        _menuOpen = true;
        _menuJustOpened = true;
        _menu.Visible = true; // el mundo sigue corriendo por detrás, pero el jugador no se mueve ni mira
        _player.Frozen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshMenu();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menu.Visible = false;
        _player.Frozen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private AudioStream _purchaseSound; // se crea la primera vez que compras algo

    private void BuyUpgrade(Upgrade upgrade)
    {
        bool bought = _state.TryBuy(upgrade);
        if (bought)
        {
            // Sonido de compra: un tintineo corto y alegre.
            _purchaseSound ??= Sounds.Purchase();
            PlaySound(_purchaseSound, -6);
        }

        if (bought && upgrade == _state.Ramps)
            BuildRampsPurchased();
        if (bought && upgrade == _state.AutoPlacer)
            BuildLauncher();

        if (bought && upgrade == _state.Bucket)
        {
            // El cubo aparece en el muelle; hay que ir a cogerlo con E.
            _bucket.Visible = true;
            _bucketShape.Disabled = false;
            ShowMessage("Comprado: Cubo. Está en el muelle, cógelo con E");
        }
        else
        {
            ShowMessage(bought
                ? $"Comprado: {upgrade.Name}"
                : $"Te faltan {ShortText(upgrade)} para {upgrade.Name}");
        }
        RefreshMenu();
    }

    /// <summary>Precio de la mejora en su moneda: dólares o peces.</summary>
    private string CostText(Upgrade u) => u.CostsFish ? $"{Fmt(u.Cost)} peces" : FormatMoney(u.Cost);

    /// <summary>Lo que te falta para comprar la mejora, en su moneda.</summary>
    private string ShortText(Upgrade u) => u.CostsFish
        ? $"{Fmt(u.Cost - _state.Fish)} peces"
        : FormatMoney(u.Cost - _state.Money);

    private void RefreshMenu()
    {
        foreach (UpgradeRow row in _menuRows)
        {
            Upgrade u = row.Upgrade;
            bool unlock = u.MaxLevel == 1;
            bool needsLauncher = u == _state.LauncherSpeed && !_state.HasAutoPlacer;
            bool needsHands = u == _state.FastHands && !_state.HasAgileHands;
            bool needsPenguins = u == _state.PenguinSpeed && !_state.HasPenguins;
            bool needsBucket = (u == _state.BucketCapacityUpgrade && !_state.HasBucket) || needsLauncher || needsHands || needsPenguins;

            string status = unlock
                ? (u.IsMaxed ? "desbloqueado" : "bloqueado")
                : $"nivel {u.Level}";
            if (needsBucket)
                status = needsLauncher ? "requiere el lanzador"
                    : needsHands ? "requiere Manos ágiles"
                    : needsPenguins ? "requiere pingüinos"
                    : "requiere el cubo";
            // Siempre dos líneas: nombre y estado, y una descripción corta.
            // En las mejoras de tiempo y alcance, la descripción dice lo que tienes ahora mismo.
            string desc = u.Description;
            if (u == _state.FastHands)
                desc = $"-0,05 s por nivel · ahora {_state.PlaceInterval:0.00} s";
            else if (u == _state.LongArms)
                desc = $"+1 m por nivel · ahora {_state.ArmReach:0} m";
            else if (u == _state.PenguinSpeed)
                desc = $"-15 % de tiempo por nivel · ahora {_state.PenguinCycle:0.0} s";
            row.Info.Text = $"{u.Name}  ({status})\n{desc}";

            if (u.IsMaxed)
            {
                row.Button.Text = unlock ? "Desbloqueado" : "Máximo";
                row.Button.Disabled = true;
            }
            else
            {
                row.Button.Text = $"Comprar: {CostText(u)}";
                row.Button.Disabled = !_state.CanAfford(u) || needsBucket;
            }
        }
    }

    private void BuildMenu(CanvasLayer hud)
    {
        _menu = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(960, 0),
        };
        _menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _menu.GrowHorizontal = Control.GrowDirection.Both;
        _menu.GrowVertical = Control.GrowDirection.Both;
        hud.AddChild(_menu);
        _menu.AddThemeStyleboxOverride("panel", PanelStyle(MachinePanelBg, MachineBorder));

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        _menu.AddChild(box);
        var machineTitle = new Label { Text = "Máquina de mejoras", HorizontalAlignment = HorizontalAlignment.Center };
        machineTitle.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(machineTitle);

        // Dos columnas: cada mejora ocupa una celda de la cuadrícula.
        var grid = new GridContainer { Columns = 2 };
        box.AddChild(grid);

        foreach (Upgrade upgrade in _state.Upgrades)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
            var info = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, // si un texto aún es largo, salta de línea
            };
            var button = new Button { CustomMinimumSize = new Vector2(190, 0) };
            StyleMachineButton(button);
            button.Pressed += () => BuyUpgrade(upgrade);

            row.AddChild(info);
            row.AddChild(button);
            grid.AddChild(row);
            _menuRows.Add(new UpgradeRow(upgrade, info, button));
        }

        var close = new Button { Text = "Cerrar (E / Esc)" };
        StyleMachineButton(close);
        close.Pressed += CloseMenu;
        box.AddChild(close);
    }
}