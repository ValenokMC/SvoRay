using v2rayN.Manager;

namespace v2rayN.Views;

/// <summary>
/// Asks before SvoRay replaces itself. Only collects the answer; downloading, checking and
/// starting the installer stay with the main window.
/// </summary>
public partial class SvoRayUpdateWindow : Window
{
    public SvoRayUpdateWindow(SvoRayRelease release, Version current)
    {
        InitializeComponent();

        var version = release.Version.ToString(3);
        txtQuestion.Text = string.Format(ResUI.SvoRayUpdateQuestion, version, SvoRayAppUpdate.Normalize(current).ToString(3));
        runNotes.Text = string.Format(ResUI.SvoRayWhatsNew, version);
        // The URL comes from the release checked in SvoRayAppUpdate.Evaluate: this repository only.
        lnkNotes.Click += (_, _) => ProcUtils.ProcessStart(release.NotesUrl);

        btnInstall.Click += (_, _) => DialogResult = true;
        btnLater.Click += (_, _) => DialogResult = false;
        SourceInitialized += (_, _) => SvoRayService.ApplyWindowChrome(this);
    }
}
