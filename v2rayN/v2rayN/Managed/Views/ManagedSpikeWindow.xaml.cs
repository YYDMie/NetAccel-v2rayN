using System.Windows;
using System.Windows.Input;
using v2rayN.Managed.ViewModels;

namespace v2rayN.Managed.Views;

/// <summary>
/// WP-03 minimal WPF spike window.
/// Open via: var w = new ManagedSpikeWindow(); w.Show();
///
/// This is a skeleton only:
/// - No real API connections.
/// - No system proxy or TUN manipulation.
/// - No secrets (tokens, credentials, envelope plaintext) are stored in ViewModel properties.
/// - PasswordBox keeps the password in WPF's secure visual tree only.
/// </summary>
public partial class ManagedSpikeWindow : Window
{
    private readonly ManagedSpikeViewModel _vm;

    public ManagedSpikeWindow()
    {
        InitializeComponent();
        _vm = new ManagedSpikeViewModel();
        DataContext = _vm;

        WireCommands();
    }

    private void WireCommands()
    {
        _vm.LoginCommand = new RelayCommand(
            async _ =>
            {
                _vm.IsBusy = true;
                // Skeleton: credentials are read from UI controls and passed to
                // NetAccel.Managed.Auth.IAuthService.LoginAsync(...).
                // Never store plaintext password in ViewModel properties.
                _vm.LoginStatus = $"Login requested for {txtUsername.Text}";
                await Task.Delay(200);
                _vm.IsBusy = false;
            },
            _ => !_vm.IsBusy);

        _vm.RegisterInstanceCommand = new RelayCommand(
            async _ =>
            {
                _vm.IsBusy = true;
                // Skeleton: calls NetAccel.Managed.Instance.IInstanceService.RegisterOrRecoverAsync(...).
                _vm.InstanceStatus = "Register requested";
                await Task.Delay(200);
                _vm.IsBusy = false;
            },
            _ => !_vm.IsBusy);

        _vm.RefreshStatusCommand = new RelayCommand(
            async _ =>
            {
                _vm.IsBusy = true;
                // Skeleton: calls NetAccel.Managed.Selection.IManagedSelectionService.GetStatusAsync(...).
                _vm.PolicySummary = "Status refresh requested";
                await Task.Delay(200);
                _vm.IsBusy = false;
            },
            _ => !_vm.IsBusy);

        _vm.UpdateSelectionCommand = new RelayCommand(
            async _ =>
            {
                _vm.IsBusy = true;
                // Skeleton: calls NetAccel.Managed.Selection.IManagedSelectionService.UpdateSelectionAsync(...).
                _vm.SelectionSummary = $"Selection update: mode={txtSelectionMode.Text}, profile={txtProfileId.Text}";
                await Task.Delay(200);
                _vm.IsBusy = false;
            },
            _ => !_vm.IsBusy);

        _vm.AckConfigCommand = new RelayCommand(
            async _ =>
            {
                _vm.IsBusy = true;
                // Skeleton: ack current config revision to the server.
                _vm.ConfigSyncStatus = "Config ack requested";
                await Task.Delay(200);
                _vm.IsBusy = false;
            },
            _ => !_vm.IsBusy);

        btnLogin.Command = _vm.LoginCommand;
        btnRegister.Command = _vm.RegisterInstanceCommand;
        btnRefreshStatus.Command = _vm.RefreshStatusCommand;
        btnUpdateSelection.Command = _vm.UpdateSelectionCommand;
        btnAckConfig.Command = _vm.AckConfigCommand;
    }

    /// <summary>
/// Minimal ICommand implementation for the spike.
    /// </summary>
    private sealed class RelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (_ => true);
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);

        public async void Execute(object? parameter) => await _execute(parameter);
    }
}
