using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
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
        btnRussiaPreset.Click += async (_, _) => await AddRussiaPresetAsync();
        txtDomain.PreviewKeyDown += (_, e) =>
        {
            // Shift+Enter is the way to break a line; a bare Enter keeps meaning "add this".
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                AddDomain();
            }
        };
        btnSave.Click += (_, _) =>
        {
            // Text still sitting in the field was typed to be added, not to be discarded.
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
        var parsed = SvoRayRoutingHandler.ParseDomains(txtDomain.Text);
        if (parsed.Count == 0)
        {
            ShowStatus("Введите домен, например example.com.", true);
            return false;
        }

        var added = Append(parsed);
        if (added == 0)
        {
            ShowStatus(parsed.Count == 1
                ? $"Домен {parsed[0]} уже в списке."
                : "Все эти домены уже в списке.", true);
            return false;
        }

        txtDomain.Clear();
        txtDomain.Focus();
        ShowStatus(DescribeAdded(added, parsed.Count - added), false);
        return true;
    }

    /// <summary>
    /// The preset only makes sense as a bypass list, so a window left in the opposite mode is
    /// switched over rather than quietly producing the reverse of what the button promises.
    /// </summary>
    /// <remarks>
    /// The built-in set is applied first and the maintained one is merged on top of it, so the
    /// button does its job without a network connection and only improves on it with one.
    /// </remarks>
    private async Task AddRussiaPresetAsync()
    {
        var switched = rdoProxyListed.IsChecked == true;
        if (switched)
        {
            rdoBypassListed.IsChecked = true;
        }

        var added = Append(SvoRayRoutingHandler.RussiaPreset);

        btnRussiaPreset.IsEnabled = false;
        ShowStatus("Проверяем актуальный список…", false);
        List<string> maintained;
        try
        {
            maintained = await SvoRayRoutingHandler.DownloadRussiaListAsync();
        }
        finally
        {
            btnRussiaPreset.IsEnabled = true;
        }

        added += Append(maintained);

        var message = added == 0 ? "Набор уже в списке." : $"Добавлено: {added}.";
        if (maintained.Count == 0)
        {
            message += " Обновить список из интернета не удалось, добавлен встроенный набор.";
        }
        if (switched)
        {
            message += " Режим переключён на «всё через VPN, кроме списка».";
        }

        ShowStatus(message, false);
    }

    private int Append(IEnumerable<string> domains)
    {
        var added = 0;
        foreach (var domain in domains)
        {
            if (_domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            _domains.Add(domain);
            added++;
        }
        return added;
    }

    private static string DescribeAdded(int added, int skipped)
    {
        var text = $"Добавлено: {added}.";
        return skipped > 0 ? $"{text} Пропущено (уже в списке): {skipped}." : text;
    }

    private void BtnRemoveDomain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string domain })
        {
            _domains.Remove(domain);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        txtStatus.Text = message;
        txtStatus.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(0xF0, 0xA9, 0xA9)
            : Color.FromRgb(0x9F, 0xD9, 0xCE));
        txtStatus.Visibility = Visibility.Visible;
    }

    private void UpdateEmptyState()
    {
        txtEmptyList.Visibility = _domains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
