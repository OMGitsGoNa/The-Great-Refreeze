using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GreatRefreeze;

public partial class Main
{
    // ---------------------------------------------------------------- partida guardada

    /// <summary>Guarda el estado actual: economía, mejoras, hielo colocado y oso. Se hace cada 30 s y al cerrar.</summary>
    private void SaveGameNow()
    {
        var data = new SaveData
        {
            Seed = _seed,
            BucketCarried = _bucketCarried,
            BearRescued = _bearRescued,
        };
        _state.WriteTo(data);

        foreach (var (cell, level, shape) in _grid.PlacedLevels)
            data.Cells.Add(new[] { cell.X, cell.Y, level, shape });

        data.PlayedSeconds = _playedSeconds;
        data.Progress = _state.Progress;
        SaveGame.Write(_slot, data);
    }

    /// <summary>
    /// Restaura una partida guardada. Los peces y el dinero ya están en el guardado, así que los sectores
    /// se recalculan sin volver a dar bonos.
    /// </summary>
    private void LoadGame(SaveData save)
    {
        _state.ReadFrom(save);
        _playedSeconds = save.PlayedSeconds;

        foreach (int[] c in save.Cells)
        {
            var cell = new Vector2I(c[0], c[1]);
            _grid.PlaceWithLevel(cell, c.Length > 2 ? c[2] : 0, c.Length > 3 ? c[3] : 0); // hielo ya colocado con su altura, sin peces
            _sectors.RecordPlaced(cell);   // revela sectores y cuenta cubos (el bono ya estaba cobrado)
        }

        if (_state.Ramps.Level > 0)
            BuildRamps();

        if (_state.HasAutoPlacer)
            BuildLauncher();

        if (_state.HasBucket)
        {
            _bucket.Visible = true;
            _bucketShape.Disabled = false;
            if (save.BucketCarried)
                CarryBucket();
        }

        _bearRescued = save.BearRescued;
        if (_bearRescued)
        {
            _bear.Prompt = "Oso polar rescatado";
            _bearLabel.Text = "Oso polar (rescatado)";
        }

        ShowMessage("Partida cargada");
    }

    /// <summary>Empieza una partida nueva en la ranura (sobrescribe lo que hubiera) y recarga la escena.</summary>
    private void StartNewGameInSlot(int slot)
    {
        _slot = slot;
        _resetting = true;
        _skipTitle = true;
        SaveGame.Delete(slot);
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Carga la partida de la ranura recargando la escena, para que se aplique desde cero.</summary>
    private void LoadGameInSlot(int slot)
    {
        _slot = slot;
        _resetting = true; // no guardar la escena anterior mientras se recarga
        _skipTitle = true;
        GetTree().ReloadCurrentScene();
    }

}
