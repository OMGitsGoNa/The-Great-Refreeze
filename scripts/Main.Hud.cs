using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- HUD

    private void BuildHud()
    {
        var hud = new CanvasLayer();
        AddChild(hud);

        // Arriba a la izquierda: peces (con icono) y dinero.
        var topLeft = new VBoxContainer { Position = new Vector2(16, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        hud.AddChild(topLeft);

        var fishRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        topLeft.AddChild(fishRow);
        fishRow.AddChild(new TextureRect
        {
            Texture = FishIcon(),
            CustomMinimumSize = new Vector2(40, 24),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _fishLabel = new Label();
        StyleLabel(_fishLabel, 24);
        fishRow.AddChild(_fishLabel);

        _moneyLabel = new Label();
        StyleLabel(_moneyLabel, 16);
        topLeft.AddChild(_moneyLabel);

        // Arriba a la derecha: progreso del Ártico.
        _progressLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        StyleLabel(_progressLabel, 16);
        hud.AddChild(_progressLabel);
        _progressLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _progressLabel.OffsetLeft = -360;
        _progressLabel.OffsetRight = -16;
        _progressLabel.OffsetTop = 16;

        // Abajo a la izquierda: sector actual.
        _sectorLabel = new Label();
        StyleLabel(_sectorLabel, 14);
        hud.AddChild(_sectorLabel);
        _sectorLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _sectorLabel.OffsetLeft = 16;
        _sectorLabel.OffsetTop = -40;
        _sectorLabel.OffsetBottom = -16;

        // Mira en el centro de la pantalla.
        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(crosshair, 28);
        hud.AddChild(crosshair);
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Barra de carga de la cubitera, bajo el centro de la pantalla.
        _chargeBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            Visible = false,
            CustomMinimumSize = new Vector2(300, 14),
        };
        _chargeBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _chargeBar.OffsetTop = -150;
        _chargeBar.OffsetBottom = -136;
        hud.AddChild(_chargeBar);

        // Aviso de que te ahogas, bajo el centro.
        _warnLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        StyleLabel(_warnLabel, 26);
        hud.AddChild(_warnLabel);
        _warnLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _warnLabel.OffsetTop = 120;
        _warnLabel.OffsetBottom = 160;
        _warnLabel.OffsetLeft = -300;
        _warnLabel.OffsetRight = 300;

        // Inventario abajo al centro: mano y cubo (el cubo aparece al cogerlo).
        var inventory = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(inventory);
        inventory.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        inventory.GrowHorizontal = Control.GrowDirection.Both;
        inventory.OffsetLeft = -160;
        inventory.OffsetRight = 160;
        inventory.OffsetTop = -76;
        inventory.OffsetBottom = -28;

        inventory.AddChild(MakeSlot(out _handLabel));
        _bucketSlot = MakeSlot(out _bucketLabel);
        _bucketSlot.Visible = false;
        inventory.AddChild(_bucketSlot);

        // Avisos: lo que puedes hacer con lo que miras y mensajes.
        _promptLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        StyleLabel(_promptLabel, 20);
        hud.AddChild(_promptLabel);
        _promptLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _promptLabel.OffsetBottom = -100;

        _winLabel = new Label
        {
            Text = "¡Has recongelado el Ártico!",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        StyleLabel(_winLabel, 48);
        hud.AddChild(_winLabel);
        _winLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _winLabel.OffsetBottom = -200;

        BuildMenu(hud);

        // Escarcha: un velo que entra desde los bordes mientras estás bajo el agua (shader). Va antes del fundido.
        _frost = new ColorRect
        {
            Color = new Color(1f, 1f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            Material = new ShaderMaterial { Shader = new Shader { Code = FrostShader } },
        };
        hud.AddChild(_frost);
        _frost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Fundido a negro al congelarse. Va el último para tapar todo.
        _fade = new ColorRect
        {
            Color = Colors.Black,
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(_fade);
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>Una casilla del inventario: recuadro oscuro con un texto.</summary>
    private static PanelContainer MakeSlot(out Label label)
    {
        label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        StyleLabel(label, 18);

        var slot = new PanelContainer { CustomMinimumSize = new Vector2(140, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddChild(label);
        return slot;
    }

    private static void StyleLabel(Label label, int fontSize)
    {
        label.MouseFilter = Control.MouseFilterEnum.Ignore; // que el HUD no se coma los clics
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
    }

    private void UpdateHud(double delta)
    {
        RefreshPile();
        UpdateArcticLook();
        HandleFlashlightKey();
        UpdateDayNight();
        UpdateStorm(delta);

        // El cubo se ve azul cuando tiene hielo. El hielo de la mano se ve si no llevas el cubo.
        _bucketMaterial.AlbedoColor = _state.BucketIce >= 1 ? BucketFullColor : BucketEmptyColor;
        ApplyUpgradeVisuals();
        _handCube.Visible = _state.HandIce > 0 && !_bucketCarried;

        _fishLabel.Text = Fmt(_state.Fish);
        _moneyLabel.Text = FormatMoney(_state.Money);
        _progressLabel.Text = $"Ártico {_state.Progress * 100:0.0}%  ·  {(_state.IsNight ? "Noche" : "Día")}"
            + (_storm ? "  ·  Tormenta" : "");

        _handLabel.Text = $"Mano  {_state.HandIce} / {GameState.HandCapacity}";
        _bucketSlot.Visible = _bucketCarried;
        _bucketLabel.Text = $"Cubo  {_state.BucketIce} / {_state.BucketMax}";

        Sector here = _sectors.At(IceGrid.WorldToCell(_player.GlobalPosition));
        _sectorLabel.Text = here == null ? ""
            : !here.Revealed ? "Sector sin explorar"
            : $"Anillo {here.Ring}  ·  {here.Filled} / {here.Free}"
              + (here.IsFish ? "  ·  parcela de peces" : "")
              + (here.Completed ? "  ✓" : "");

        // Congelamiento: cuanto más tiempo bajo el agua, más escarcha cubre la pantalla.
        UpdateFrost(delta);

        _warnLabel.Text = _player.SubmergedTime > 0
            ? $"¡Te estás congelando! Sal del agua: {Math.Max(0, Player.DrownSeconds - _player.SubmergedTime):0.0} s"
            : "";

        _messageTimer -= delta;
        var prompt = new System.Text.StringBuilder();
        if (!_menuOpen)
        {
            if (_player.LookedStation != null)
                prompt.AppendLine(_player.LookedStation.Prompt);
            else if (_aimValid && _state.HasIceToPlace(_bucketCarried))
                prompt.AppendLine(_state.HasAgileHands
                    ? "[mantén clic izquierdo] Colocar hielo"
                    : "[clic izquierdo] Colocar hielo");
        }
        if (_messageTimer > 0)
            prompt.AppendLine(_message);
        _promptLabel.Text = prompt.ToString().TrimEnd();

        _winLabel.Visible = _state.Won;

        if (_menuOpen)
            RefreshMenu();
    }

    /// <summary>Icono de pez dibujado en código, sin imágenes externas.</summary>
    private static Texture2D FishIcon()
    {
        const int w = 36;
        const int h = 22;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var body = new Color(0.4f, 0.72f, 1f);
        var eye = new Color(0.05f, 0.05f, 0.1f);

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float dx = (x - 14f) / 13f;
                float dy = (y - 11f) / 8f;
                bool inBody = dx * dx + dy * dy <= 1f;
                bool inTail = x >= 26 && Mathf.Abs(y - 11f) <= (x - 25) * 0.9f;
                if (inBody || inTail)
                    image.SetPixel(x, y, body);
            }

        for (int x = 9; x <= 10; x++)
            for (int y = 9; y <= 10; y++)
                image.SetPixel(x, y, eye);

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Formatea números grandes: 1234 -> 1234, 25000 -> 25K, 2500000 -> 2,5M.</summary>
    private static string Fmt(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000:0.##}M";
        if (value >= 10_000) return $"{value / 1_000:0.#}K";
        if (value >= 100) return $"{value:0}";
        return $"{value:0.#}";
    }

    private static string FormatMoney(double value) => $"{value:0.00} $";
}
