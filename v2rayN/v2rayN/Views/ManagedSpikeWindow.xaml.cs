using System.Windows;
using System.Net.Http;
using NetAccel.Managed.Api;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Services;
using NetAccel.Managed.Storage;

namespace v2rayN.Views;

public partial class ManagedSpikeWindow : Window
{
    private readonly IAuthService _authService;
    private readonly IInstanceService _instanceService;
    private readonly IManagedSelectionService _selectionService;
    private readonly IManagedConfigSyncService _configSyncService;
    private readonly IStartupCoordinator _coordinator;

    private ManagedStatus? _lastStatus;

    public ManagedSpikeWindow()
    {
        InitializeComponent();

        // Simple manual DI - in real app this would come from a container
        var httpClient = new HttpClient();
        var apiClient = new ManagedApiClient(httpClient, "https://47.245.52.167/api/v1");
        var vault = new InMemoryCredentialVault();
        var install = new InstallationIdentityService(vault);
        var coreVersions = new Dictionary<string, string> { ["xray"] = "unknown", ["sing_box"] = "unknown" };
        var capabilities = new List<string> { "vless-reality", "hysteria2", "system-proxy", "tun" };

        _authService = new AuthService(apiClient, vault);
        _instanceService = new InstanceService(apiClient, vault, install, "wp03-spike", coreVersions, capabilities);
        _selectionService = new ManagedSelectionService(apiClient);
        var crypto = new ManagedEnvelopeSpikeV0Crypto();
        _configSyncService = new ManagedConfigSyncService(apiClient, vault, crypto, "wp03-spike", coreVersions);
        _coordinator = new StartupCoordinator(_authService, _instanceService, _selectionService, _configSyncService, apiClient);

        rbManual.Checked += (_, _) => cmbProfiles.IsEnabled = true;
        rbManual.Unchecked += (_, _) => cmbProfiles.IsEnabled = false;
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var username = txtUsername.Text;
        var password = txtPassword.Password;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            lblLoginStatus.Text = "Please enter username and password.";
            return;
        }

        lblLoginStatus.Text = "Logging in...";
        var result = await _authService.LoginAsync(username, password);
        lblLoginStatus.Text = result ? "Login succeeded." : "Login failed.";
    }

    private async void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        await _authService.LogoutAsync();
        lblLoginStatus.Text = "Logged out.";
    }

    private async void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        lblInstanceStatus.Text = "Registering...";
        var result = await _instanceService.RegisterAsync();
        lblInstanceStatus.Text = result != null ? $"Instance: {result.Id}" : "Registration failed.";
    }

    private async void BtnRecover_Click(object sender, RoutedEventArgs e)
    {
        lblInstanceStatus.Text = "Recovering...";
        var result = await _instanceService.EnsureRegisteredAsync();
        var id = _instanceService.InstanceId;
        lblInstanceStatus.Text = result && !string.IsNullOrEmpty(id)
            ? $"Instance recovered: {id}"
            : "Recovery failed.";
    }

    private async void BtnFetchStatus_Click(object sender, RoutedEventArgs e)
    {
        lblPolicySummary.Text = "Fetching...";
        lblStatusSummary.Text = "";
        try
        {
            var policy = await _selectionService.GetPolicyAsync();
            if (policy != null)
            {
                lblPolicySummary.Text = $"Policy: allow_classic={policy.AllowClassicMode}, allow_tun={policy.AllowTun}, mandatory_update={policy.MandatoryUpdate}";
            }
            else
            {
                lblPolicySummary.Text = "Policy: null";
            }

            var status = await _selectionService.GetStatusAsync();
            _lastStatus = status;
            if (status != null)
            {
                lblStatusSummary.Text = $"Status: assignment_rev={status.AssignmentRevision}, selection_rev={status.SelectionRevision}, profiles={status.Profiles.Count}, revoked={status.InstanceRevoked}";
                lstProfiles.Items.Clear();
                cmbProfiles.Items.Clear();
                foreach (var p in status.Profiles)
                {
                    var display = $"{p.DisplayName} ({p.Region}) - available={p.Available}, maintenance={p.Maintenance}";
                    lstProfiles.Items.Add(display);
                    cmbProfiles.Items.Add(new { Id = p.Id, Name = display });
                }
            }
            else
            {
                lblStatusSummary.Text = "Status: null";
            }
        }
        catch (Exception ex)
        {
            lblStatusSummary.Text = $"Error: {ex.Message}";
        }
    }

    private async void BtnUpdateSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_lastStatus == null)
        {
            lblSelectionResult.Text = "Fetch status first.";
            return;
        }

        var mode = rbAuto.IsChecked == true ? "automatic" : "manual";
        string? profileId = null;
        if (mode == "manual" && cmbProfiles.SelectedItem != null)
        {
            dynamic selected = cmbProfiles.SelectedItem;
            profileId = selected.Id;
        }

        lblSelectionResult.Text = "Updating...";
        try
        {
            var result = await _selectionService.SetSelectionAsync(mode, profileId, _lastStatus.SelectionRevision);
            lblSelectionResult.Text = result != null
                ? $"Selection updated: mode={result.SelectionMode}, rev={result.SelectionRevision}"
                : "Update failed.";
        }
        catch (Exception ex)
        {
            lblSelectionResult.Text = $"Error: {ex.Message}";
        }
    }

    private async void BtnSyncConfig_Click(object sender, RoutedEventArgs e)
    {
        var revision = _lastStatus?.AssignmentRevision;
        lblConfigResult.Text = "Syncing...";
        try
        {
            var result = await _configSyncService.FetchConfigAsync(revision);
            if (result == null)
            {
                lblConfigResult.Text = "Config sync returned null.";
                return;
            }

            if (result.IsNotModified)
            {
                lblConfigResult.Text = "Config not modified (304).";
                return;
            }

            var envelope = result.Envelope;
            if (envelope == null)
            {
                lblConfigResult.Text = "Envelope is null.";
                return;
            }

            lblConfigResult.Text = $"Schema: {envelope.Schema}, assignment_rev: {envelope.AssignmentRevision}, payload_schema: {envelope.PayloadSchema}";

            // Decrypted payload is returned as part of the short-lived result only.
            // Do NOT log or display the full decrypted payload.
        }
        catch (Exception ex)
        {
            lblConfigResult.Text = $"Error: {ex.Message}";
        }
    }
}
