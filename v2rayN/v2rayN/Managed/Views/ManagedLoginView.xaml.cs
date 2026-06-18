using NetAccel.Managed.Presentation;
using System.Windows.Controls;

namespace v2rayN.Managed.Views;

public partial class ManagedLoginView : UserControl
{
    private ManagedLoginViewModel? _viewModel;

    public ManagedLoginView()
    {
        InitializeComponent();
        Loaded += ManagedLoginView_Loaded;
        PrimaryAction.Click += PrimaryAction_Click;
        CancelAction.Click += (_, _) => _viewModel?.Cancel();
        ReturnToLoginAction.Click += ReturnToLoginAction_Click;
        PasswordInput.KeyDown += PasswordInput_KeyDown;
    }

    public void AttachViewModel(ManagedLoginViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
    }

    private void ManagedLoginView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.IsLoginFormVisible == true)
        {
            UsernameTextBox.Focus();
        }
    }

    private async void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        if (_viewModel.IsLoginFormVisible)
        {
            var password = PasswordInput.Password;
            PasswordInput.Clear();
            await _viewModel.LoginAsync(password);
            return;
        }

        await _viewModel.RetryAsync();
    }

    private async void ReturnToLoginAction_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        PasswordInput.Clear();
        await _viewModel.ReturnToLoginAsync();
        UsernameTextBox.Focus();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && PrimaryAction.IsVisible
            && PrimaryAction.IsEnabled)
        {
            PrimaryAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }
}
