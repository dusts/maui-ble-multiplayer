using MauiBle.Core;
using MauiBle.Sample.Bluetooth;

namespace MauiBle.Sample;

public partial class MainPage : ContentPage
{
    private readonly IBleTransport _ble;
    private readonly LobbySession _session;
    private bool _hooked;

    public MainPage(IBleTransport ble, LobbySession session)
    {
        _ble = ble;
        _session = session;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_hooked)
        {
            ShowHosts(_ble.GetDiscoveredHosts());
            return;
        }

        _hooked = true;
        _ble.StatusChanged += OnStatus;
        _ble.NearbyHostsUpdated += OnHosts;
        _session.Changed += OnSessionChanged;
        NameEntry.Text = _session.LocalName;
        ShowHosts(_ble.GetDiscoveredHosts());
        RenderSession();
    }

    private async void OnHostClicked(object? sender, EventArgs e)
    {
        try
        {
            ApplyName();
            _session.SetHostIdentity();
            await _ble.StartHostingAsync(_session.LocalName);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        try
        {
            ApplyName();
            await _ble.StartScanningAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        try
        {
            await _session.SendDisconnectAsync();
            await _ble.DisconnectAsync();
            StatusLabel.Text = "Stopped.";
            ShowHosts(Array.Empty<NearbyHost>());
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        try
        {
            ApplyName();
            await _session.SendChatAsync(ChatEntry.Text ?? "");
            ChatEntry.Text = "";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private async void OnJoinClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: NearbyHost host })
            return;

        try
        {
            ApplyName();
            await _ble.ConnectAsync(host);
            await _session.SendHelloAsync();
            StatusLabel.Text = $"Joined {host.DisplayName}. Waiting for the roster.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private void ApplyName()
    {
        var name = (NameEntry.Text ?? "").Trim();
        _session.LocalName = name.Length == 0 ? "Player" : name;
    }

    private void OnStatus(object? sender, string status) =>
        MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);

    private void OnHosts(object? sender, IReadOnlyList<NearbyHost> hosts) =>
        MainThread.BeginInvokeOnMainThread(() => ShowHosts(hosts));

    private void OnSessionChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RenderSession);

    private void ShowHosts(IReadOnlyList<NearbyHost> hosts)
    {
        HostButtons.Children.Clear();
        if (hosts.Count == 0)
        {
            HostButtons.Children.Add(new Label { Text = "None yet." });
            return;
        }

        foreach (var host in hosts)
        {
            HostButtons.Children.Add(new Button
            {
                Text = $"{host.DisplayName}  ·  {BleAdvertiseName.FormatSignal(host.SignalStrength)}",
                BindingContext = host,
            });
        }

        foreach (var child in HostButtons.Children.OfType<Button>())
            child.Clicked += OnJoinClicked;
    }

    private void RenderSession()
    {
        RosterLabel.Text = _session.RosterLine;
        LogLabel.Text = _session.LogText;
    }
}
