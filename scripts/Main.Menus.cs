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
            new MenuItem("Guardar y salir", SaveAndGoToMenu, true));
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

    // Paneles: fondo oscuro con esquinas redondeadas, borde fino y más aire por dentro.
    private static readonly Color MenuPanelBg = new(0.05f, 0.11f, 0.19f, 0.97f);
    private static readonly Color MachinePanelBg = new(0.12f, 0.09f, 0.05f, 0.97f);

    private static StyleBoxFlat PanelStyle(Color bg, Color border) => new StyleBoxFlat
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        BorderWidthTop = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 14,
        CornerRadiusTopRight = 14,
        CornerRadiusBottomLeft = 14,
        CornerRadiusBottomRight = 14,
        ContentMarginLeft = 26,
        ContentMarginRight = 26,
        ContentMarginTop = 22,
        ContentMarginBottom = 22,
        ShadowColor = new Color(0f, 0f, 0f, 0.5f),
        ShadowSize = 12,
    };

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
        panel.AddThemeStyleboxOverride("panel", PanelStyle(MenuPanelBg, MenuBorder));
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);
        var heading = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(heading);

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

    // ---------------------------------------------------------------- modo de juego (al empezar una partida)

    private Control _modePanel;  // pantalla "Modo de juego"
    private bool _modeRelaxed;   // elección temporal en esa pantalla (por defecto: no)

    /// <summary>Antes de empezar una partida nueva se elige el modo sin prisa. Por defecto, desactivado.</summary>
    private void ChooseMode(int slot)
    {
        CloseConfirm(); // por si venía de "¿sobrescribir?"
        if (_slotPanel != null)
            _slotPanel.Visible = false;
        if (_modePanel != null)
        {
            _modePanel.Visible = false;
            _modePanel.QueueFree();
        }

        _modeRelaxed = false;
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

        var heading = new Label { Text = $"Partida {slot}: modo de juego", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 32);
        box.AddChild(heading);

        var info = new Label
        {
            Text = "Sin prisa: siempre de día y el hielo del montón no se derrite. Se elige ahora y no se puede cambiar durante la partida.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        box.AddChild(info);

        var modeButton = new Button { Text = ModeText(), CustomMinimumSize = new Vector2(0, 52) };
        modeButton.AddThemeFontSizeOverride("font_size", 20);
        StyleMenuButton(modeButton);
        modeButton.Pressed += () =>
        {
            _modeRelaxed = !_modeRelaxed;
            modeButton.Text = ModeText();
        };
        box.AddChild(modeButton);

        var start = new Button { Text = "Empezar partida", CustomMinimumSize = new Vector2(0, 52) };
        start.AddThemeFontSizeOverride("font_size", 20);
        StyleMenuButton(start);
        start.Pressed += () =>
        {
            _relaxedPref = _modeRelaxed; // se aplica a la partida nueva al recargar la escena
            StartNewGameInSlot(slot);
        };
        box.AddChild(start);

        var back = new Button { Text = "Volver", CustomMinimumSize = new Vector2(0, 44) };
        StyleMenuButton(back);
        back.Pressed += CloseMode;
        box.AddChild(back);

        _modePanel = root;
    }

    private string ModeText() => "Modo sin prisa: " + (_modeRelaxed ? "sí" : "no");

    /// <summary>Vuelve de la pantalla de modo a la lista de partidas.</summary>
    private void CloseMode()
    {
        if (_modePanel != null)
        {
            _modePanel.Visible = false;
            _modePanel.QueueFree();
            _modePanel = null;
        }
        if (_slotPanel != null)
            _slotPanel.Visible = true;
    }

    /// <summary>Guarda la partida y vuelve al menú principal. Desde allí se sale del juego.</summary>
    private void SaveAndGoToMenu()
    {
        SaveGameNow();
        _resetting = true;  // se recarga la escena: no volver a guardar al cerrarla
        _skipTitle = false; // esta vez sí se muestra el menú principal
        GetTree().ReloadCurrentScene();
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
                action = () => AskOverwrite(s, () => ChooseMode(s), true);
            else
                action = () => ChooseMode(s);
            // Fila: botón de la partida + botón de papelera para borrarla.
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            box.AddChild(row);

            var slotButton = new Button
            {
                Text = SlotText(s, forNew),
                Disabled = !(forNew || exists),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 52),
            };
            slotButton.AddThemeFontSizeOverride("font_size", 20);
            StyleMenuButton(slotButton);
            slotButton.Pressed += action;
            row.AddChild(slotButton);

            var trash = new Button
            {
                CustomMinimumSize = new Vector2(52, 52),
                Disabled = !exists,
                TooltipText = $"Borrar la partida {s}",
            };
            StyleMenuButton(trash);
            var icon = new TrashIcon();
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); // ocupa todo el botón; el dibujo ya va centrado
            trash.AddChild(icon);
            trash.Pressed += () => AskDelete(s, forNew);
            row.AddChild(trash);
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
    private void AskOverwrite(int slot, Action onConfirm, bool fromSlots) =>
        AskConfirm($"¿Sobrescribir la partida {slot}?", "Se perderá el progreso que tiene guardado.", onConfirm, fromSlots);

    /// <summary>Pide confirmación para borrar una partida y, si se confirma, vuelve a dibujar la lista.</summary>
    private void AskDelete(int slot, bool forNew)
    {
        AskConfirm($"¿Borrar la partida {slot}?", "Se borrará para siempre. No se puede deshacer.", () =>
        {
            SaveGame.Delete(slot);
            CloseConfirm();
            OpenSlots(forNew);
        }, true);
    }

    /// <summary>Ventana de confirmación genérica con título, texto y botones Confirmar / Cancelar.</summary>
    private void AskConfirm(string title, string body, Action onConfirm, bool fromSlots)
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
        panel.AddThemeStyleboxOverride("panel", PanelStyle(MenuPanelBg, MenuBorder));
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);
        box.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label { Text = body, HorizontalAlignment = HorizontalAlignment.Center });

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