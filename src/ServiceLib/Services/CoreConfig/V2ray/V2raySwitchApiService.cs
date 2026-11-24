namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    /// <summary>
    /// Opens Xray's handler API on loopback when the core is generated for a subscription it can
    /// switch within (see <see cref="ServerSwitchCandidates"/>): a server switch then replaces the
    /// proxy outbound in the running core instead of restarting it.
    /// </summary>
    private void GenSwitchApi()
    {
        if (context.SwitchCandidates is not { Count: > 1 })
        {
            return;
        }

        _coreConfig.api = new Api4Ray
        {
            tag = XrayApiService.ApiTag,
            listen = $"{Global.Loopback}:{AppManager.Instance.StatePort3}",
            services = [XrayApiService.HandlerService],
        };
    }
}
