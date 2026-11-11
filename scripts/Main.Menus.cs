using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- menú principal y pausa

    private void BuildOverlays()
    {
        _overlay = new CanvasLayer { Layer = 20 }; // por encima del HUD
        AddChild(_overlay);

        _titlePanel = BuildTitleScreen();

        _pausePanel = BuildMenuPanel("Pausa", new Color(0f, 0f, 0f, 0.55f),
            new MenuItem("Continuar", ContinueGame, true),
            new MenuItem("Guardar partida", SaveWithConfirm, true),
            new MenuItem("Nueva partida", () => OpenSlots(true), true),
            new MenuItem(RelaxedText(), ToggleRelaxed, true),
            new MenuItem("Salir al escritorio", QuitGame, true));
    }

    /// <summary>Menú principal: pantalla completa con el título y las opciones de inicio.</summary>
    private Control BuildTitleScreen()
    {
        var root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        var background = new ColorRect { Color = new Color(0.04f, 0.09f, 0.16f), MouseFilter = Control.MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.AddThemeConstantOverride("separation", 12);
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(box);

        var title = new Label { Text = "The Great Refreeze", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 56);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.95f, 1f));
        box.AddChild(title);

        var subtitle = new Label { Text = "Vuelve a congelar el Ártico", HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.95f));
        box.AddChild(subtitle);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28) }); // separación antes de los botones

        AddTitleButton(box, new MenuItem("Nueva partida", () => OpenSlots(true), true));
        AddTitleButton(box, new MenuItem("Cargar partida", () => OpenSlots(false), SaveGame.AnyExists()));
        AddTitleButton(box, new MenuItem("Opciones", () => { }, false)); // todavía no hace nada
        AddTitleButton(box, new MenuItem("Salir", QuitGame, true));

        return root;
    }

    // Estilos de botones. Menús: azul hielo. Máquina de mejoras: bronce y dorado, para distinguirla.
    private static readonly Color MenuBg = new(0.10f, 0.22f, 0.36f);
    private static readonly Color MenuBorder = new(0.6f, 0.85f, 1f);
    private static readonly Color MachineBg = new(0.24f, 0.18f, 0.08f);
    private static readonly Color MachineBorder = new(0.95f, 0.75f, 0.3f);

    private static StyleBoxFlat ButtonStyle(Color bg, Color border) => new StyleBoxFlat
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        BorderWidthTop = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = 14,
        ContentMarginRight = 14,
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
    };

    private static void ApplyButtonStyle(Button b, Color bg, Color border)
    {
        b.AddThemeStyleboxOverride("normal", ButtonStyle(bg, border));
        b.AddThemeStyleboxOverride("hover", ButtonStyle(bg.Lightened(0.15f), border));
        b.AddThemeStyleboxOverride("pressed", ButtonStyle(bg.Darkened(0.2f), border));
        b.AddThemeStyleboxOverride("focus", ButtonStyle(bg, border));
        b.AddThemeStyleboxOverride("disabled", ButtonStyle(bg.Darkened(0.45f), border.Darkened(0.5f)));
        b.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 1f));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.55f, 0.6f));
    }

    private static void StyleMenuButton(Button b) => ApplyButtonStyle(b, MenuBg, MenuBorder);

    private static void StyleMachineButton(Button b) => ApplyButtonStyle(b, MachineBg, MachineBorder);

    private static void AddTitleButton(VBoxContainer box, MenuItem item)
    {
        var button = new Button
        {
            Text = item.Text,
            Disabled = !item.Enabled,
            CustomMinimumSize = new Vector2(480, 52),
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        StyleMenuButton(button);
        button.Pressed += item.Action;
        box.AddChild(button);
    }

    private Control BuildMenuPanel(string title, Color backdrop, params MenuItem[] items)
    {
        var root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        var shade = new ColorRect { Color = backdrop, MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center });

        foreach (MenuItem item in items)
        {
            var button = new Button
            {
                Text = item.Text,
                Disabled = !item.Enabled,
                CustomMinimumSize = new Vector2(360, 44),
            };
            StyleMenuButton(button);
            button.Pressed += item.Action;
            if (item.Text.StartsWith("Modo sin prisa"))
                _relaxedButtons.Add(button);
            box.AddChild(button);
        }
        return root;
    }

    private void ShowTitle()
    {
        _titleOpen = true;
        _titlePanel.Visible = true;
        _player.Frozen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OpenPause()
    {
        _pauseOpen = true;
        _pausePanel.Visible = true;
        _player.Frozen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Cierra el menú principal o el de pausa y vuelve al juego.</summary>
    private void ContinueGame()
    {
        _titleOpen = false;
        _pauseOpen = false;
        _titlePanel.Visible = false;
        _pausePanel.Visible = false;
        _player.Frozen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private string RelaxedText() => "Modo sin prisa: " + (_state.RelaxedMode ? "sí" : "no");

    /// <summary>Cambia el modo sin prisa: siempre de día y sin derretimiento. Se aplica al instante.</summary>
    private void ToggleRelaxed()
    {
        _state.RelaxedMode = !_state.RelaxedMode;
        _relaxedPref = _state.RelaxedMode;
        foreach (Button b in _relaxedButtons)
            b.Text = RelaxedText();
        ShowMessage(_state.RelaxedMode ? "Modo sin prisa activado" : "Modo sin prisa desactivado");
    }

    // ---------------------------------------------------------------- ranuras de guardado

    /// <summary>Abre la lista de partidas: para empezar una nueva (forNew) o para cargar una existente.</summary>
    private void OpenSlots(bool forNew)
    {
        _slotsFromTitle = _titleOpen;
        if (_titleOpen)
            _titlePanel.Visible = false;
        else
            _pausePanel.Visible = false;

        if (_slotPanel != null)
        {
            _slotPanel.Visible = false;
            _slotPanel.QueueFree();
        }
        _slotPanel = BuildSlotPanel(forNew);
        _slotPanel.Visible = true;
    }

    private void CloseSlots()
    {
        if (_slotPanel != null)
            _slotPanel.Visible = false;

        if (_slotsFromTitle)
            _titlePanel.Visible = true;
        else
            _pausePanel.Visible = true;
    }

    private Control BuildSlotPanel(bool forNew)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        var background = new ColorRect { Color = new Color(0.04f, 0.09f, 0.16f, 0.96f), MouseFilter = Control.MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        box.AddThemeConstantOverride("separation", 12);
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(box);

        var heading = new Label { Text = forNew ? "Nueva partida" : "Cargar partida", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 36);
        box.AddChild(heading);

        for (int slot = 1; slot <= SaveGame.Slots; slot++)
        {
            int s = slot;
            bool exists = SaveGame.Exists(s);
            Action action;
            if (!forNew)
                action = () => LoadGameInSlot(s);
            else if (exists)
                action = () => AskOverwrite(s, () => StartNewGameInSlot(s), true);
            else
                action = () => StartNewGameInSlot(s);
            AddTitleButton(box, new MenuItem(SlotText(s, forNew), action, forNew || exists));
        }

        AddTitleButton(box, new MenuItem("Volver", CloseSlots, true));
        return root;
    }

    /// <summary>Texto de una ranura: vacía, o el progreso del Ártico y el tiempo jugado.</summary>
    private static string SlotText(int slot, bool forNew)
    {
        SaveData data = SaveGame.Read(slot);
        if (data == null)
            return $"Partida {slot}  ·  Vacía";

        string info = $"Partida {slot}  ·  {data.Progress * 100:0.0} % Ártico  ·  {FormatPlayTime(data.PlayedSeconds)}";
        return forNew ? info + "  (se sobrescribe)" : info;
    }

    /// <summary>Tiempo jugado con el formato "xx d xx h xx m".</summary>
    private static string FormatPlayTime(double seconds)
    {
        long total = (long)seconds;
        long days = total / 86400;
        long hours = total % 86400 / 3600;
        long minutes = total % 3600 / 60;
        return $"{days:00} d {hours:00} h {minutes:00} m";
    }

    // ---------------------------------------------------------------- confirmar sobrescritura

    private Control _confirmPanel;   // ventana "¿Sobrescribir?"
    private bool _confirmFromSlots;  // se abrió desde la lista de partidas (y hay que volver a ella al cancelar)

    /// <summary>Pregunta antes de sobrescribir una partida. Confirmar ejecuta la acción; Cancelar vuelve atrás.</summary>
    private void AskOverwrite(int slot, Action onConfirm, bool fromSlots)
    {
        CloseConfirm();
        _confirmFromSlots = fromSlots;
        if (fromSlots && _slotPanel != null)
            _slotPanel.Visible = false;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        var shade = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);
        box.AddChild(new Label { Text = $"¿Sobrescribir la partida {slot}?", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label { Text = "Se perderá el progreso que tiene guardado.", HorizontalAlignment = HorizontalAlignment.Center });

        var yes = new Button { Text = "Confirmar", CustomMinimumSize = new Vector2(0, 44) };
        yes.Pressed += () => onConfirm();
        StyleMenuButton(yes);
        box.AddChild(yes);

        var no = new Button { Text = "Cancelar", CustomMinimumSize = new Vector2(0, 44) };
        no.Pressed += CloseConfirm;
        StyleMenuButton(no);
        box.AddChild(no);

        _confirmPanel = root;
    }

    /// <summary>Cierra la ventana de confirmación; si venía de la lista de partidas, vuelve a ella.</summary>
    private void CloseConfirm()
    {
        if (_confirmPanel != null)
        {
            _confirmPanel.Visible = false;
            _confirmPanel.QueueFree();
            _confirmPanel = null;
        }

        if (_confirmFromSlots && _slotPanel != null)
            _slotPanel.Visible = true;
        _confirmFromSlots = false;
    }

    /// <summary>Guardar desde la pausa: si ya hay partida en esa ranura, pregunta antes de sobrescribirla.</summary>
    private void SaveWithConfirm()
    {
        if (SaveGame.Exists(_slot))
            AskOverwrite(_slot, () => { SaveAndNotify(); CloseConfirm(); }, false);
        else
            SaveAndNotify();
    }

    private void SaveAndNotify()
    {
        SaveGameNow();
        ShowMessage("Partida guardada");
    }

    /// <summary>Guarda la partida (si ya estás jugando) y cierra el juego.</summary>
    private void QuitGame()
    {
        if (!_titleOpen && !_resetting)
            SaveGameNow();
        GetTree().Quit();
    }

}
