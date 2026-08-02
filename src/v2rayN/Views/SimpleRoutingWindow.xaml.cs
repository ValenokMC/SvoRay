using System.Collections.ObjectModel;
using System.Windows.Controls;
using v2rayN.Manager;

namespace v2rayN.Views;

/// <summary>
/// Editor for the simple-mode domain list. It only collects the user's choices; writing them
/// to the config and rebuilding the routing profile stays with the main window, which also
/// knows whether a reconnect is needed.
/// </summary>
public partial class SimpleRoutingWindow : Window
{
    private readonly ObservableCollection<string> _domains = [];

    public SimpleRoutingWindow(ESvoRayRoutingMode mode, IEnumerable<string> domains)
    {
        InitializeComponent();

        foreach (var domain in domains)
        {
            _domains.Add(domain);
        }

        lstDomains.ItemsSource = _domains;
        _domains.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        rdoBypassListed.IsChecked = mode == ESvoRayRoutingMode.BypassListed;
        rdoProxyListed.IsChecked = mode == ESvoRayRoutingMode.ProxyListed;

        btnAddDomain.Click += (_, _) => AddDomain();
        txtDomain.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddDomain();
            }
        };
        btnSave.Click += (_, _) =>
        {
            // A domain still sitting in the field was typed to be added, not to be discarded.
            if (txtDomain.Text.TrimEx().IsNotEmpty() && !AddDomain())
            {
                return;
            }

            ResultMode = rdoProxyListed.IsChecked == true
                ? ESvoRayRoutingMode.ProxyListed
                : ESvoRayRoutingMode.BypassListed;
            ResultDomains = [.. _domains];
            DialogResult = true;
        };
        btnCancel.Click += (_, _) => DialogResult = false;

        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    public ESvoRayRoutingMode ResultMode { get; private set; }

    public List<string> ResultDomains { get; private set; } = [];

    private bool AddDomain()
    {
        var domain = SvoRayRoutingHandler.NormalizeDomain(txtDomain.Text);
        if (domain.IsNullOrEmpty())
        {
            ShowStatus("Введите домен, например example.com.");
            return false;
        }

        if (_domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            ShowStatus($"Домен {domain} уже в списке.");
            return false;
        }

        _domains.Add(domain);
        txtDomain.Clear();
        txtStatus.Visibility = Visibility.Collapsed;
        txtDomain.Focus();
        return true;
    }

    private void BtnRemoveDomain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string domain })
        {
            _domains.Remove(domain);
        }
    }

    private void ShowStatus(string message)
    {
        txtStatus.Text = message;
        txtStatus.Visibility = Visibility.Visible;
    }

    private void UpdateEmptyState()
    {
        txtEmptyList.Visibility = _domains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
