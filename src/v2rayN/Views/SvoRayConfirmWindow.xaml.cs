using v2rayN.Manager;

namespace v2rayN.Views;

/// <summary>
/// Asks before something on the simple screen is removed. Cancel is the default answer:
/// Enter does nothing destructive, Escape closes.
/// </summary>
public partial class SvoRayConfirmWindow : Window
{
    public SvoRayConfirmWindow(string question, string confirm)
    {
        InitializeComponent();

        txtQuestion.Text = question;
        btnYes.Content = confirm;
        btnYes.Click += (_, _) => DialogResult = true;
        btnNo.Click += (_, _) => DialogResult = false;
        Loaded += (_, _) => btnNo.Focus();
        SourceInitialized += (_, _) => SvoRayService.ApplyWindowChrome(this);
    }

    /// <summary>True only when the user pressed the confirming button.</summary>
    public static bool Ask(Window owner, string question, string confirm)
    {
        return new SvoRayConfirmWindow(question, confirm) { Owner = owner }.ShowDialog() == true;
    }
}
