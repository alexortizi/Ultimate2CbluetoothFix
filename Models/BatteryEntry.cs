using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitDoFixer.Models;

/// <summary>
/// Un dispositivo BLE que reporta batería. Se keyea por BleDeviceId y no por nombre:
/// dos Ultimate 2C idénticos comparten nombre, y keyear por nombre hace que el segundo
/// sobreescriba al primero.
///
/// Deliberadamente NO se ata a un ControllerEntry: no existe join confiable entre un
/// InstanceGuid de DirectInput y un device id de BLE (ver spec §7.1).
/// </summary>
public sealed class BatteryEntry : INotifyPropertyChanged
{
    public BatteryEntry(string bleDeviceId, string name)
    {
        BleDeviceId = bleDeviceId;
        _name = name;
    }

    public string BleDeviceId { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); }
    }

    private int _level;
    public int Level
    {
        get => _level;
        set { if (_level == value) return; _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
    }

    public string LevelText => $"{Level}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
