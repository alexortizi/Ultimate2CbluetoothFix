using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitDoFixer.Models;

/// <summary>
/// Un mando, observado por la UI. La identidad es inmutable; el estado cambia.
/// StatusText y PlayerLabel son derivados y notifican junto con su fuente, para que
/// el XAML no tenga que hacer switch sobre el enum.
/// </summary>
public sealed class ControllerEntry : INotifyPropertyChanged
{
    public ControllerEntry(Guid instanceGuid, string name, int playerIndex)
    {
        InstanceGuid = instanceGuid;
        Name = name;
        PlayerIndex = playerIndex;
    }

    public Guid InstanceGuid { get; }
    public string Name { get; }

    /// <summary>Slot XInput 1..4, o 0 si no se pudo asignar ninguno.</summary>
    public int PlayerIndex { get; }

    private ControllerState _state = ControllerState.Connecting;
    public ControllerState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private bool _rumbleSupported;
    public bool RumbleSupported
    {
        get => _rumbleSupported;
        set
        {
            if (_rumbleSupported == value) return;
            _rumbleSupported = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RumbleText));
        }
    }

    public string StatusText => State switch
    {
        ControllerState.Connecting => Localization.Instance.StateConnecting,
        ControllerState.Mapped => Localization.Instance.StateMapped,
        ControllerState.Lost => Localization.Instance.StateLost,
        _ => Localization.Instance.StateConnecting
    };

    public string PlayerLabel => Localization.Instance.PlayerLabel(PlayerIndex);

    public string RumbleText => RumbleSupported
        ? Localization.Instance.RumbleOn
        : Localization.Instance.RumbleOff;

    /// <summary>Refresca todo el texto derivado tras un cambio de idioma.</summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PlayerLabel));
        OnPropertyChanged(nameof(RumbleText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
