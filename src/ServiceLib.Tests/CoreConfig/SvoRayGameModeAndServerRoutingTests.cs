using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

/// <summary>
/// Game mode and the subscription's routing profile in the simple screen's rule set, and how
/// both reach the two cores.
/// </summary>
public class SvoRayGameModeAndServerRoutingTests
{
    private const string GameFolder = "C:/Program Files (x86)/Steam/";

    private static SvoRayServerRoutingProfile Profile() => new()
    {
        RouteOrder = "block-proxy-direct",
        BlockSites = ["geosite:category-ads"],
        ProxySites = ["geosite:youtube", "domain:discord.com"],
        DirectSites = ["geosite:steam", "geosite:whitelist", "domain:bank.example"],
        DirectIp = ["geoip:direct"],
        GeositeUrl = "https://cdn.example/geosite.dat",
        GeoipUrl = "https://cdn.example/geoip.dat",
    };

    private static SvoRayRoutingInputs Inputs(bool withProfile = true) => new()
    {
        ServerProfile = withProfile ? Profile() : null,
        GeoSiteTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "category-ads", "youtube", "steam" },
        GeoIpTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DIRECT" },
        GameFolders = [GameFolder],
    };

    [Fact]
    public void GameMode_ShouldSendGameProcessesDirectBeforeAnythingElse()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem { GameMode = true }, Inputs());

        var first = rules[0];
        first.OutboundTag.Should().Be(Global.DirectTag);
        first.Process.Should().Contain(GameFolder);
        first.Process.Should().Contain("steam.exe");
        first.Domain.Should().BeNullOrEmpty();
    }

    [Fact]
    public void GameModeOff_ShouldAddNoProcessRule()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem { GameMode = false }, Inputs());

        rules.Should().NotContain(r => r.Process != null && r.Process.Count > 0);
    }

    /// <summary>
    /// Bypass mode: the user's list, then the server's groups in its order with QUIC refused on
    /// the proxied group, then the usual tail. Unknown categories never reach the core.
    /// </summary>
    [Fact]
    public void ServerProfile_ShouldFollowTheUserListInTheServersOrder()
    {
        var settings = new SvoRayItem { RuleDomains = ["mine.example"] };

        var rules = SvoRayRoutingHandler.BuildRules(settings, Inputs());

        var tags = rules.Select(r => (r.OutboundTag, r.Network)).ToList();
        tags.Should().Equal(
            (Global.DirectTag, null),                // the user's list
            (Global.BlockTag, null),                 // server: block
            (Global.BlockTag, "udp"),                // server: QUIC to the proxied group
            (Global.ProxyTag, null),                 // server: proxy
            (Global.DirectTag, null),                // server: direct
            (Global.DirectTag, null),                // private networks
            (Global.DirectTag, null),                // private domains
            (Global.BlockTag, "udp"),                // QUIC on the tunnel
            (Global.ProxyTag, null));                // the rest

        rules[1].Domain.Should().Equal("ext:svoray_geosite.dat:category-ads");
        rules[3].Domain.Should().Equal("ext:svoray_geosite.dat:youtube", "domain:discord.com");
        rules[4].Domain.Should().Equal("ext:svoray_geosite.dat:steam", "domain:bank.example");
        rules[4].Ip.Should().Equal("ext:svoray_geoip.dat:direct");
        rules.SelectMany(r => r.Domain ?? []).Should().NotContain(d => d.Contains("whitelist"));
    }

    [Fact]
    public void ServerProfile_InProxyListedMode_ShouldNotBlockQuicForTheProxiedGroup()
    {
        var rules = SvoRayRoutingHandler.BuildRules(
            new SvoRayItem { RoutingMode = ESvoRayRoutingMode.ProxyListed }, Inputs());

        rules.Should().NotContain(r => r.Network == "udp");
        rules.Last().OutboundTag.Should().Be(Global.DirectTag);
    }

    [Fact]
    public void ServerRoutingSwitchedOff_ShouldIgnoreTheProfile()
    {
        var withoutProfile = SvoRayRoutingHandler.BuildRules(new SvoRayItem(), Inputs(withProfile: false));
        var switchedOff = SvoRayRoutingHandler.BuildRules(new SvoRayItem { UseServerRouting = false }, Inputs());

        JsonUtils.Serialize(switchedOff, false).Should().Be(JsonUtils.Serialize(withoutProfile, false));
        JsonUtils.Serialize(withoutProfile, false)
            .Should().Be(JsonUtils.Serialize(SvoRayRoutingHandler.BuildRules(new SvoRayItem()), false));
    }

    /// <summary>Xray gets the folder prefix and the ext: categories exactly as built.</summary>
    [Fact]
    public void XrayConfig_ShouldCarryFolderProcessesAndExtCategories()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);
        context.RoutingItem.RuleSet = JsonUtils.Serialize(
            SvoRayRoutingHandler.BuildRules(new SvoRayItem { GameMode = true }, Inputs()), false);

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var v2rayConfig = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!;
        v2rayConfig.routing.rules.Should().Contain(r => r.outboundTag == Global.DirectTag
            && r.process != null && r.process.Contains(GameFolder));
        v2rayConfig.routing.rules.Should().Contain(r => r.outboundTag == Global.ProxyTag
            && r.domain != null && r.domain.Contains("ext:svoray_geosite.dat:youtube"));
    }

    /// <summary>
    /// sing-box would read a trailing-slash folder as one exact file, so it gets a prefix regex;
    /// ext: categories it cannot read are left out while plain domains still arrive.
    /// </summary>
    [Fact]
    public void SingboxConfig_ShouldTurnFoldersIntoPrefixRegexes()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);
        context.RoutingItem.RuleSet = JsonUtils.Serialize(
            SvoRayRoutingHandler.BuildRules(new SvoRayItem { GameMode = true }, Inputs()), false);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var singboxConfig = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;
        var expected = CoreConfigSingboxService.FolderPrefixRegex(GameFolder);
        singboxConfig.route.rules.Should().Contain(r => r.outbound == Global.DirectTag
            && r.process_path_regex != null && r.process_path_regex.Contains(expected));
        singboxConfig.route.rules.Should().Contain(r => r.outbound == Global.ProxyTag
            && r.domain_suffix != null && r.domain_suffix.Contains("discord.com"));
    }

    [Theory]
    [InlineData("C:/Program Files (x86)/Steam/", @"(?i)^C:\\Program Files \(x86\)\\Steam\\")]
    [InlineData("D:/Games+More/", @"(?i)^D:\\Games\+More\\")]
    public void FolderPrefixRegex_ShouldEscapeOnlyRegexCharacters(string folder, string expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        CoreConfigSingboxService.FolderPrefixRegex(folder).Should().Be(expected);
    }
}
