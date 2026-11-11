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
        _menu.Visible = true; // el juego sigue corriendo por detrás (no se congela ni el mundo ni el jugador)
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshMenu();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menu.Visible = false;
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
                : $"Te faltan {FormatMoney(upgrade.Cost - _state.Money)} para {upgrade.Name}");
        }
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        foreach (UpgradeRow row in _menuRows)
        {
            Upgrade u = row.Upgrade;
            bool unlock = u.MaxLevel == 1;
            bool needsLauncher = u == _state.LauncherSpeed && !_state.HasAutoPlacer;
            bool needsHands = u == _state.FastHands && !_state.HasAgileHands;
            bool needsBucket = (u == _state.BucketCapacityUpgrade && !_state.HasBucket) || needsLauncher || needsHands;

            string status = unlock
                ? (u.IsMaxed ? "desbloqueado" : "bloqueado")
                : $"nivel {u.Level}";
            if (needsBucket)
                status = needsLauncher ? "requiere el lanzador"
                    : needsHands ? "requiere Manos ágiles"
                    : "requiere el cubo";
            row.Info.Text = $"{u.Name}  ({status})\n{u.Description}";

            // Debajo de la descripción, lo que tienes ahora mismo con esa mejora.
            if (u == _state.FastHands)
                row.Info.Text += $"\nAhora: una colocación cada {_state.PlaceInterval:0.00} s";
            else if (u == _state.LongArms)
                row.Info.Text += $"\nAhora: alcance de {_state.ArmReach:0} m";

            if (u.IsMaxed)
            {
                row.Button.Text = unlock ? "Desbloqueado" : "Máximo";
                row.Button.Disabled = true;
            }
            else
            {
                row.Button.Text = $"Comprar: {FormatMoney(u.Cost)}";
                row.Button.Disabled = _state.Money < u.Cost || needsBucket;
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

        var box = new VBoxContainer();
        _menu.AddChild(box);
        box.AddChild(new Label { Text = "Máquina de mejoras", HorizontalAlignment = HorizontalAlignment.Center });

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
            var button = new Button { CustomMinimumSize = new Vector2(220, 0) };
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