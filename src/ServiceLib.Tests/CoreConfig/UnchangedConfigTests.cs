using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Manager;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

/// <summary>
/// A background subscription refresh reloads with "keep if unchanged": the running core stays
/// when the regenerated config is the one it runs. That only works if generating the same
/// inputs twice gives the same config, and anything a connection depends on changes it.
/// </summary>
/// <remarks>
/// Alone, not in parallel: generation reads the config bound to <see cref="AppManager"/> (the
/// Clash API port follows its TUN flag), and other tests bind their own between two generations.
/// </remarks>
[Collection(nameof(UnchangedConfigTests))]
public class UnchangedConfigTests
{
    private const string SubId = "sub-1";

    [Theory]
    [InlineData(ECoreType.sing_box, false)]
    [InlineData(ECoreType.sing_box, true)]
    [InlineData(ECoreType.Xray, false)]
    [InlineData(ECoreType.Xray, true)]
    public async Task SameSubscriptionAgain_KeepsTheRunningCore(ECoreType core, bool tun)
    {
        var (config, servers) = Setup(core, tun);
        var running = Digest(core, await BuildContext(config, servers, servers[1], core));

        // What the refresh brings back: the same servers as new objects, the same rules.
        var (_, refreshed) = Setup(core, tun);
        var again = Digest(core, await BuildContext(config, refreshed, refreshed[1], core));

        again.Should().Be(running);
        CoreSwitchPlanner.CanKeepRunningCore(running, coreAlive: true, again).Should().BeTrue();
    }

    [Theory]
    [InlineData(ECoreType.sing_box)]
    [InlineData(ECoreType.Xray)]
    public async Task ChangedServerSetServerOrRouting_Reloads(ECoreType core)
    {
        var (config, servers) = Setup(core, tun: true);
        var running = Digest(core, await BuildContext(config, servers, servers[0], core));

        var fewer = Digest(core, await BuildContext(config, servers[..2], servers[0], core));
        var otherServer = Digest(core, await BuildContext(config, servers, servers[2], core));
        var moved = Setup(core, tun: true).Servers;
        moved[0].Address = "a2.example.com";
        var movedServer = Digest(core, await BuildContext(config, moved, moved[0], core));
        var otherRules = await BuildContext(config, servers, servers[0], core);
        otherRules.RoutingItem!.RuleSet = """[{"OutboundTag":"direct","Domain":["domain:example.ru"],"Enabled":true}]""";
        var rules = Digest(core, otherRules);

        foreach (var changed in new[] { fewer, otherServer, movedServer, rules })
        {
            changed.Should().NotBe(running);
            CoreSwitchPlanner.CanKeepRunningCore(running, coreAlive: true, changed).Should().BeFalse();
        }
    }

    private static string? Digest(ECoreType core, CoreConfigContext context)
    {
        var result = core == ECoreType.sing_box
            ? new CoreConfigSingboxService(context).GenerateClientConfigContent()
            : new CoreConfigV2rayService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var digest = CoreSwitchPlanner.ReadConfigDigest(result.Data!.ToString());
        digest.Should().NotBeNull();
        return digest;
    }

    private static (Config Config, ProfileItem[] Servers) Setup(ECoreType core, bool tun)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        config.TunModeItem.EnableTun = tun;
        // Dated log file names are in the config; they must not count as a change.
        config.CoreBasicItem.LogEnabled = true;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return (config, [Server("a", "a.example.com", core), Server("b", "b.example.com", core), Server("c", "c.example.com", core)]);
    }

    private static ProfileItem Server(string id, string address, ECoreType core)
    {
        var node = CoreConfigTestFactory.CreateVmessNode(core, id, $"server {id}");
        node.Address = address;
        node.Subid = SubId;
        node.Password = "00000000-0000-0000-0000-000000000000";
        return node;
    }

    /// <summary>The same steps as a reload, as in the server switch tests.</summary>
    private static async Task<CoreConfigContext> BuildContext(Config config, ProfileItem[] servers, ProfileItem selected, ECoreType core)
    {
        var context = CoreConfigTestFactory.CreateContext(config, selected, core);
        await CoreConfigContextBuilder.ResolveNodeAsync(context, selected, false);
        var candidates = ServerSwitchCandidates.Select(selected, servers, _ => core);
        foreach (var candidate in candidates.Where(c => c.IndexId != selected.IndexId))
        {
            await CoreConfigContextBuilder.ResolveNodeAsync(context, candidate, false);
        }
        return ServerSwitchCandidates.WithCandidates(context, candidates);
    }
}

[CollectionDefinition(nameof(UnchangedConfigTests), DisableParallelization = true)]
public class UnchangedConfigCollection;
