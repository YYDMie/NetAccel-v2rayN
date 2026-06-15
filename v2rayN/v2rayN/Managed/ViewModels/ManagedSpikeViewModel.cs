using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace v2rayN.Managed.ViewModels;

/// <summary>
/// WP-03 minimal WPF spike ViewModel.
/// Skeleton only — no real connections, no secret storage in properties.
/// </summary>
public sealed class ManagedSpikeViewModel : INotifyPropertyChanged
{
    private string _loginStatus = "Not logged in";
    private string _instanceStatus = "No instance";
    private string _policySummary = "—";
    private string _selectionSummary = "—";
    private string _configSyncStatus = "—";
    private bool _isBusy;

    public string LoginStatus
    {
        get => _loginStatus;
        set => SetProperty(ref _loginStatus, value);
    }

    public string InstanceStatus
    {
        get => _instanceStatus;
        set => SetProperty(ref _instanceStatus, value);
    }

    public string PolicySummary
    {
        get => _policySummary;
        set => SetProperty(ref _policySummary, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        set => SetProperty(ref _selectionSummary, value);
    }

    public string ConfigSyncStatus
    {
        get => _configSyncStatus;
        set => SetProperty(ref _configSyncStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    // Commands are wired in code-behind to keep the spike dependency-light.
    public ICommand? LoginCommand { get; set; }
    public ICommand? RegisterInstanceCommand { get; set; }
    public ICommand? RefreshStatusCommand { get; set; }
    public ICommand? UpdateSelectionCommand { get; set; }
    public ICommand? AckConfigCommand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
