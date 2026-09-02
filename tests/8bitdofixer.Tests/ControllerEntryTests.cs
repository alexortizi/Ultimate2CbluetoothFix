using System.ComponentModel;
using BitDoFixer.Models;
using Xunit;

namespace BitDoFixer.Tests;

public class ControllerEntryTests
{
    private static ControllerEntry Entry() =>
        new(Guid.NewGuid(), "8BitDo Ultimate 2C", playerIndex: 1);

    [Fact]
    public void NewEntryStartsConnecting()
    {
        Assert.Equal(ControllerState.Connecting, Entry().State);
    }

    [Fact]
    public void ChangingStateRaisesPropertyChangedForStateAndStatusText()
    {
        var entry = Entry();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.State = ControllerState.Mapped;

        // StatusText es derivado: si no notifica, la UI se queda con el texto viejo.
        Assert.Contains(nameof(ControllerEntry.State), raised);
        Assert.Contains(nameof(ControllerEntry.StatusText), raised);
    }

    [Fact]
    public void SettingTheSameStateDoesNotNotify()
    {
        var entry = Entry();
        entry.State = ControllerState.Mapped;

        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        entry.State = ControllerState.Mapped;

        Assert.Empty(raised);
    }

    [Fact]
    public void ChangingRumbleSupportRaisesPropertyChanged()
    {
        var entry = Entry();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.RumbleSupported = true;

        Assert.Contains(nameof(ControllerEntry.RumbleSupported), raised);
    }

    [Fact]
    public void StatusTextIsLocalizedNotAnEnumName()
    {
        var entry = Entry();
        entry.State = ControllerState.Mapped;
        Assert.Equal(Localization.Instance.StateMapped, entry.StatusText);
    }

    [Fact]
    public void IdentityIsImmutable()
    {
        var entry = Entry();
        Assert.Equal("8BitDo Ultimate 2C", entry.Name);
        Assert.Equal(1, entry.PlayerIndex);
        Assert.NotEqual(Guid.Empty, entry.InstanceGuid);
    }

    [Fact]
    public void BatteryEntryNotifiesOnLevelChange()
    {
        var battery = new BatteryEntry("BLE#abc123", "8BitDo Ultimate 2C");
        var raised = new List<string?>();
        ((INotifyPropertyChanged)battery).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        battery.Level = 42;

        Assert.Contains(nameof(BatteryEntry.Level), raised);
        Assert.Equal(42, battery.Level);
    }
}
