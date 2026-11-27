using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>
/// The message stream in simple mode: MsgView, which handles <see cref="MsgViewModel.ShowMsgInteraction"/>,
/// is not created there, and an unhandled interaction is logged with its stack trace for every
/// batch. MainWindow registers a fallback handler as well; these tests pin down that the view model
/// raises the interaction only while it is activated, which is what MsgView does.
/// </summary>
[NotInParallel(nameof(SvoRayMsgViewTests))]
public class SvoRayMsgViewTests
{
    // ReactiveUI 24 has to be built before WhenAnyValue works; the apps do it at startup.
    private static readonly Lazy<bool> ReactiveUiReady = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder().BuildApp();
        return true;
    });

    [Test]
    public async Task Messages_ShouldReachTheInteractionOnlyWhileTheViewModelIsActivated()
    {
        _ = ReactiveUiReady.Value;
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());
        var showInTaskbar = AppManager.Instance.ShowInTaskbar;
        AppManager.Instance.ShowInTaskbar = true;
        try
        {
            var vm = new MsgViewModel { AutoRefresh = true, MsgFilter = string.Empty };
            var batches = 0;
            using var handler = vm.ShowMsgInteraction.RegisterHandler(interaction =>
            {
                Interlocked.Increment(ref batches);
                interaction.SetOutput(RxVoid.Default);
            });

            AppEvents.SendMsgViewRequested.Publish("not activated");
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            batches.Should().Be(0);

            using (vm.Activator.Activate())
            {
                AppEvents.SendMsgViewRequested.Publish("activated");
                for (var i = 0; i < 50 && Volatile.Read(ref batches) == 0; i++)
                {
                    await Task.Delay(100);
                }
                batches.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            AppManager.Instance.ShowInTaskbar = showInTaskbar;
        }
    }
}
