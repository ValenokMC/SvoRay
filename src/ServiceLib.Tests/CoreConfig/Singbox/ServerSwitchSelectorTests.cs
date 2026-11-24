using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Manager;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig.Singbox;

public class ServerSwitchSelectorTests
{
    private const string SubId = "sub-1";

    [Fact]
    public async Task GeneratedConfig_HasManualSelectorPointingAtTheSelectedServer()
    {
        var (config, servers) = Setup();
        var context = await BuildContext(config, servers, selected: servers[1]);

        var json = Generate(context);
        var cfg = JsonUtils.Deserialize<SingboxConfig>(json)!;

        var selector = cfg.outbounds.Single(o => o.tag == Global.ProxyTag);
        selector.type.Should().Be("selector");
        selector.outbounds.Should().Equal("proxy-1", "proxy-2", "proxy-3");
        selector.@default.Should().Be("proxy-2");
        selector.interrupt_exist_connections.Should().BeTrue();
        cfg.outbounds.Should().NotContain(o => o.type == "urltest", "the core must not probe servers nobody chose");
        cfg.outbounds.Where(o => o.tag.StartsWith("proxy-")).Select(o => o.server)
            .Should().Equal("a.example.com", "b.example.com", "c.example.com");
    }

    [Fact]
    public async Task GeneratedConfig_IsTheSameWhicheverServerIsSelected()
    {
        var (config, servers) = Setup();

        var first = CoreSwitchPlanner.ReadSingboxShape(Generate(await BuildContext(config, servers, servers[0])));
        var third = CoreSwitchPlanner.ReadSingboxShape(Generate(await BuildContext(config, servers, servers[2])));

        first.Should().NotBeNull();
        third.Should().NotBeNull();
        first!.Selection.Should().Be("proxy-1");
        third!.Selection.Should().Be("proxy-3");
        third.Fingerprint.Should().Be(first.Fingerprint, "only the selector default may differ, or the switch would restart the core");
    }

    [Fact]
    public async Task GeneratedConfig_ChangesWhenTheServerSetChanges()
    {
        var (config, servers) = Setup();

        var all = CoreSwitchPlanner.ReadSingboxShape(Generate(await BuildContext(config, servers, servers[0])));
        var fewer = CoreSwitchPlanner.ReadSingboxShape(Generate(await BuildContext(config, servers[..2], servers[0])));

        fewer!.Fingerprint.Should().NotBe(all!.Fingerprint);
    }

    [Fact]
    public async Task GeneratedConfig_ResolvesEveryCandidateOutsideTheTunnel()
    {
        var (config, servers) = Setup();
        var context = await BuildContext(config, servers, servers[2]);

        context.ProtectDomainList.Should().Equal("a.example.com", "b.example.com", "c.example.com");
    }

    [Fact]
    public void SingleServer_KeepsThePlainProxyOutbound()
    {
        var (config, servers) = Setup();
        var context = ServerSwitchCandidates.WithCandidates(
            CoreConfigTestFactory.CreateContext(config, servers[0], ECoreType.sing_box), [servers[0]]);

        var cfg = JsonUtils.Deserialize<SingboxConfig>(Generate(context))!;

        cfg.outbounds.Should().Contain(o => o.tag == Global.ProxyTag && o.type == "vmess");
        CoreSwitchPlanner.ReadSingboxShape(Generate(context)).Should().BeNull();
    }

    [Fact]
    public void Select_KeepsOnlyServersTheSameCoreCanSwitchBetween()
    {
        var selected = Server("b", "b.example.com");
        var otherCore = Server("c", "c.example.com", ECoreType.Xray);
        var otherSub = Server("d", "d.example.com");
        otherSub.Subid = "sub-2";
        var xhttp = Server("e", "e.example.com");
        xhttp.Network = nameof(ETransport.xhttp);
        var wireguard = Server("f", "f.example.com");
        wireguard.ConfigType = EConfigType.WireGuard;
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.sing_box, "g", "group", ["a", "b"]);
        group.Subid = SubId;
        var a = Server("a", "a.example.com");

        var result = ServerSwitchCandidates.Select(selected, [selected, otherCore, otherSub, xhttp, wireguard, group, a],
            p => p.CoreType ?? ECoreType.Xray);

        result.Select(p => p.IndexId).Should().Equal("a", "b");
    }

    [Fact]
    public void Select_TooManyServers_FallsBackToTheSelectedOneOnly()
    {
        var servers = Enumerable.Range(0, ServerSwitchCandidates.MaxCandidates + 1)
            .Select(i => Server($"n{i:D4}", $"n{i}.example.com"))
            .ToList();

        ServerSwitchCandidates.Select(servers[0], servers, p => ECoreType.sing_box).Should().BeEmpty();
    }

    [Fact]
    public void IsSwitchable_OnlyPlainServerOnSingboxOrXrayWithoutPreCore()
    {
        var (config, servers) = Setup();
        var context = CoreConfigTestFactory.CreateContext(config, servers[0], ECoreType.sing_box);

        ServerSwitchCandidates.IsSwitchable(context, null).Should().BeTrue();
        ServerSwitchCandidates.IsSwitchable(context with { RunCoreType = ECoreType.Xray }, null).Should().BeTrue();
        ServerSwitchCandidates.IsSwitchable(context with { RunCoreType = ECoreType.mihomo }, null).Should().BeFalse();
        ServerSwitchCandidates.IsSwitchable(context, context).Should().BeFalse();

        var noSub = Server("x", "x.example.com");
        noSub.Subid = string.Empty;
        ServerSwitchCandidates.IsSwitchable(context with { Node = noSub }, null).Should().BeFalse();
    }

    private static (Config Config, ProfileItem[] Servers) Setup()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return (config, [Server("a", "a.example.com"), Server("b", "b.example.com"), Server("c", "c.example.com")]);
    }

    private static ProfileItem Server(string id, string address, ECoreType coreType = ECoreType.sing_box)
    {
        var node = CoreConfigTestFactory.CreateVmessNode(coreType, id, $"server {id}");
        node.Address = address;
        node.Subid = SubId;
        node.Password = "00000000-0000-0000-0000-000000000000";
        return node;
    }

    /// <summary>
    /// The same steps as a reload: the selected server is registered when its context is
    /// built, then the rest of the subscription is registered and attached.
    /// </summary>
    private static async Task<CoreConfigContext> BuildContext(Config config, ProfileItem[] servers, ProfileItem selected)
    {
        var context = CoreConfigTestFactory.CreateContext(config, selected, ECoreType.sing_box);
        await CoreConfigContextBuilder.ResolveNodeAsync(context, selected, false);
        var candidates = ServerSwitchCandidates.Select(selected, servers, p => ECoreType.sing_box);
        foreach (var candidate in candidates.Where(c => c.IndexId != selected.IndexId))
        {
            await CoreConfigContextBuilder.ResolveNodeAsync(context, candidate, false);
        }
        return ServerSwitchCandidates.WithCandidates(context, candidates);
    }

    private static string Generate(CoreConfigContext context)
    {
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }
}
