using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Services.CoreConfig;

namespace ServiceLib.Tests.CoreConfig.Singbox;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>SvoRay's checks on the sing-box config, beyond what upstream's tests cover.</summary>
[NotInParallel(nameof(SvoRayCoreConfigSingboxTests))]
public class SvoRayCoreConfigSingboxTests
{
    [Test]
    public void GenerateClientConfigContent_WireGuard_ShouldNameItsServerOnlyInThePeer()
    {
        // sing-box refuses a WireGuard endpoint that carries server/server_port, which the outbound
        // template brings along since upstream 7.25 (MASQUE). WARP and AmneziaWG profiles go this way.
        var node = FmtHandler.ResolveConfig(
            "wireguard://AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA%3D@example.com:51820"
            + "?publickey=ISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0%2BP0A%3D&address=172.16.0.2%2F32&mtu=1280#x",
            out var msg);
        node.Should().NotBeNull(msg);
        node!.IndexId = "wg";
        node.CoreType = ECoreType.sing_box;
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        var endpoint = JsonNode.Parse(result.Data!.ToString()!)!["endpoints"]!.AsArray()
            .Single(e => e?["type"]?.ToString() == "wireguard")!.AsObject();
        endpoint.ContainsKey("server").Should().BeFalse();
        endpoint.ContainsKey("server_port").Should().BeFalse();
        endpoint["peers"]![0]!["address"]!.ToString().Should().Be("example.com");
        endpoint["peers"]![0]!["port"]!.GetValue<int>().Should().Be(51820);
    }
}
