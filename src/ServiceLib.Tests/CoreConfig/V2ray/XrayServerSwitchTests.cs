using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Manager;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig.V2ray;

public class XrayServerSwitchTests
{
    private const string SubId = "sub-1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedConfig_DiffersOnlyInTheProxyOutbound(bool tun)
    {
        var (config, servers) = Setup(tun);

        var first = CoreSwitchPlanner.ReadXrayShape(Generate(await BuildContext(config, servers, servers[0])));
        var third = CoreSwitchPlanner.ReadXrayShape(Generate(await BuildContext(config, servers, servers[2])));

        first.Should().NotBeNull("a switchable Xray config has the handler API and one proxy outbound");
        third.Should().NotBeNull();
        third!.Fingerprint.Should().Be(first!.Fingerprint, "everything but the proxy outbound must match, or the switch restarts");
        first.Selection.Should().Contain("a.example.com");
        third.Selection.Should().Contain("c.example.com");
    }

    [Fact]
    public async Task GeneratedConfig_OpensTheHandlerApiOnLoopbackOnly()
    {
        var (config, servers) = Setup(tun: false);
        var cfg = JsonUtils.Deserialize<V2rayConfig>(Generate(await BuildContext(config, servers, servers[1])))!;

        cfg.api.Should().NotBeNull();
        cfg.api!.listen.Should().Be($"{Global.Loopback}:{AppManager.Instance.StatePort3}");
        cfg.api.services.Should().Equal(XrayApiService.HandlerService);
        cfg.outbounds.Should().ContainSingle(o => o.tag == Global.ProxyTag);
        cfg.outbounds.Should().NotContain(o => o.tag.StartsWith(Global.ProxyTag + "-"),
            "Xray keeps one proxy outbound; the other servers only join the DNS protect list");
    }

    [Fact]
    public void SingleServer_HasNoApiAndIsNotSwitchable()
    {
        var (config, servers) = Setup(tun: false);
        var context = ServerSwitchCandidates.WithCandidates(
            CoreConfigTestFactory.CreateContext(config, servers[0], ECoreType.Xray), [servers[0]]);

        var json = Generate(context);

        JsonUtils.Deserialize<V2rayConfig>(json)!.api.Should().BeNull();
        CoreSwitchPlanner.ReadXrayShape(json).Should().BeNull();
    }

    [Fact]
    public void Select_ForXray_DropsWhatXrayCannotRun()
    {
        var selected = Server("a", "a.example.com");
        var tuic = Server("b", "b.example.com");
        tuic.ConfigType = EConfigType.TUIC;
        var xhttp = Server("d", "d.example.com");
        xhttp.Network = nameof(ETransport.xhttp);

        var result = ServerSwitchCandidates.Select(selected, [selected, tuic, xhttp], _ => ECoreType.Xray);

        result.Select(p => p.IndexId).Should().Equal("a", "d");
    }

    private static (Config Config, ProfileItem[] Servers) Setup(bool tun)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.TunModeItem.EnableTun = tun;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return (config, [Server("a", "a.example.com"), Server("b", "b.example.com"), Server("c", "c.example.com")]);
    }

    private static ProfileItem Server(string id, string address)
    {
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, id, $"server {id}");
        node.Address = address;
        node.Subid = SubId;
        node.Password = "00000000-0000-0000-0000-000000000000";
        return node;
    }

    /// <summary>The same steps as a reload, as in the sing-box selector tests.</summary>
    private static async Task<CoreConfigContext> BuildContext(Config config, ProfileItem[] servers, ProfileItem selected)
    {
        var context = CoreConfigTestFactory.CreateContext(config, selected, ECoreType.Xray);
        await CoreConfigContextBuilder.ResolveNodeAsync(context, selected, false);
        var candidates = ServerSwitchCandidates.Select(selected, servers, _ => ECoreType.Xray);
        foreach (var candidate in candidates.Where(c => c.IndexId != selected.IndexId))
        {
            await CoreConfigContextBuilder.ResolveNodeAsync(context, candidate, false);
        }
        return ServerSwitchCandidates.WithCandidates(context, candidates);
    }

    private static string Generate(CoreConfigContext context)
    {
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }
}
