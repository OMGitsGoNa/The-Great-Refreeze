using System;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- opciones

    private Control _optionsPanel;
    private bool _optionsFromTitle; // se abrieron desde el menú principal (y no desde la pausa)

    private void BuildOptionsPanel()
    {
        var root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        var shade = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(MenuPanelBg, MenuBorder));
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        panel.AddChild(box);

        var heading = new Label { Text = "Opciones", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(heading);

        AddSlider(box, "Sensibilidad del ratón", 0.5, 2.0, 0.05, Settings.Sensitivity,
            v => Settings.Sensitivity = (float)v, v => $"{v:0.00} ×");
        AddSlider(box, "Volumen", 0.0, 1.0, 0.05, Settings.Volume,
            v =>
            {
                Settings.Volume = (float)v;
                Settings.ApplyVolume();
            },
            v => $"{v * 100:0} %");

        var back = new Button { Text = "Volver", CustomMinimumSize = new Vector2(0, 44) };
        StyleMenuButton(back);
        back.Pressed += CloseOptions;
        box.AddChild(back);

        _optionsPanel = root;
    }

    /// <summary>Un deslizador con su título y su valor escrito encima.</summary>
    private static void AddSlider(VBoxContainer box, string title, double min, double max, double step, double value,
        Action<double> onChange, Func<double, string> format)
    {
        var label = new Label { Text = $"{title}: {format(value)}" };
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        slider.ValueChanged += v =>
        {
            onChange(v);
            label.Text = $"{title}: {format(v)}";
        };

        box.AddChild(label);
        box.AddChild(slider);
    }

    private void OpenOptions()
    {
        _optionsFromTitle = _titleOpen;
        if (_titleOpen)
            _titlePanel.Visible = false;
        else
            _pausePanel.Visible = false;

        _optionsPanel.Visible = true;
    }

    /// <summary>Cierra las opciones, guarda los ajustes y vuelve al menú de origen.</summary>
    private void CloseOptions()
    {
        if (_optionsPanel == null || !_optionsPanel.Visible)
            return;

        _optionsPanel.Visible = false;
        Settings.Save();

        if (_optionsFromTitle)
            _titlePanel.Visible = true;
        else
            _pausePanel.Visible = true;
    }
}
