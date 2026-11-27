using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;

namespace ServiceLib.Tests.CoreConfig;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>
/// The configs SvoRay generates for every protocol it ships, with TUN off and on. Each one must
/// generate here; with SVORAY_CORE_FIXTURES=1 they are also written to core-fixtures next to the
/// test assembly (Verify-Build.ps1 picks them up from there) so the
/// core update bot can run them through a new xray.exe / sing-box.exe ("xray run -test",
/// "sing-box check") before a release is made with it. See .github/workflows/core-update.yml.
/// All addresses and keys are made up.
/// Runs on its own: it binds TUN and non-TUN configs to the shared AppManager, which would
/// change what the other core config tests generate if they ran at the same time.
/// </summary>
[NotInParallel]
public class CoreFixtureConfigTests
{
    public const string FixturesVariable = "SVORAY_CORE_FIXTURES";

    /// <summary>Folder next to the test assembly the configs go to when the variable is "1".</summary>
    public const string FixturesFolder = "core-fixtures";

    private const string Uuid = "11111111-2222-3333-4444-555555555555";
    private const string RealityKey = "iAhl0UhVWHvBUuVGyTPbsvaSuzN9cgqFKSyP8hfuZ2k";

    // name, share link, generated for Xray, generated for sing-box
    public static IEnumerable<(string Name, string Link, bool Xray, bool Singbox)> Links() =>
    [
        ("vless-reality-xhttp", $"vless://{Uuid}@example.com:443?encryption=none&security=reality&sni=www.example.com&fp=chrome&pbk={RealityKey}&sid=ab&type=xhttp&path=%2F&mode=auto&extra=%7B%22xPaddingBytes%22%3A%22100-1000%22%7D#x", true, false),
        ("vless-reality-vision", $"vless://{Uuid}@example.com:443?encryption=none&flow=xtls-rprx-vision&security=reality&sni=www.example.com&fp=firefox&pbk={RealityKey}&sid=ab&type=tcp#x", true, true),
        ("vless-tls-ws", $"vless://{Uuid}@example.com:443?encryption=none&security=tls&sni=example.com&fp=chrome&alpn=http%2F1.1&type=ws&host=example.com&path=%2Fws#x", true, true),
        ("vless-tls-grpc", $"vless://{Uuid}@example.com:443?encryption=none&security=tls&sni=example.com&type=grpc&serviceName=svc&mode=gun#x", true, true),
        ("trojan-tls", "trojan://secret@example.com:443?security=tls&sni=example.com&type=tcp#x", true, true),
        ("shadowsocks", "ss://MjAyMi1ibGFrZTMtYWVzLTEyOC1nY206WXpJd09HTXhNamd4WldReU5XRTFOZz09@example.com:8388#x", true, true),
        ("hysteria2-salamander", "hysteria2://secret@example.com:443?sni=example.com&obfs=salamander&obfs-password=pass#x", true, true),
        ("hysteria2-hop", "hysteria2://secret@example.com:443?sni=example.com&mport=20000-30000#x", true, true),
        ("tuic", $"tuic://{Uuid}:secret@example.com:443?sni=example.com&congestion_control=bbr&alpn=h3#x", false, true),
        // WireGuard with the DNS of its config, as WARP imports carry it (upstream emits it as remoteDNS).
        ("wireguard", "wireguard://AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA%3D@example.com:51820?publickey=ISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0%2BP0A%3D&address=172.16.0.2%2F32%2C%20fd00%3A%3A2%2F128&mtu=1280&dns=1.1.1.1%2C%202606%3A4700%3A4700%3A%3A1111#x", true, true),
    ];

    [Test]
    [MethodDataSource(nameof(Links))]
    public void GeneratedConfigs_ShouldBuildForEveryShippedProtocol(string name, string link, bool xray, bool singbox)
    {
        var node = FmtHandler.ResolveConfig(link, out var msg);
        node.Should().NotBeNull(msg);
        node!.IndexId = name;
        // Import does this for the QUIC protocols (ConfigHandler.AddHysteria2Server/AddTuicServer):
        // they always run over TLS, and their links do not say so.
        if (node.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC && node.StreamSecurity.IsNullOrEmpty())
        {
            node.StreamSecurity = Global.StreamSecurity;
        }

        foreach (var tun in new[] { false, true })
        {
            if (xray)
            {
                Write($"xray-{name}{(tun ? "-tun" : "")}.json", Generate(node, ECoreType.Xray, tun));
            }
            if (singbox)
            {
                Write($"singbox-{name}{(tun ? "-tun" : "")}.json", Generate(node, ECoreType.sing_box, tun));
            }
        }
    }

    private static string Generate(ProfileItem node, ECoreType core, bool tun)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0, HopInterval = 30 };
        config.TunModeItem.EnableTun = tun;
        config.TunModeItem.Mtu = 1500;
        config.TunModeItem.Stack = "gvisor";
        CoreConfigTestFactory.BindAppManagerConfig(config);
        node.CoreType = core;
        var context = CoreConfigTestFactory.CreateContext(config, node, core);

        var result = core == ECoreType.Xray
            ? new CoreConfigV2rayService(context).GenerateClientConfigContent()
            : new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        var content = result.Data?.ToString();
        content.Should().NotBeNullOrWhiteSpace();
        JsonUtils.ParseJson(content!).Should().NotBeNull();
        return content!;
    }

    private static void Write(string fileName, string content)
    {
        // The variable only switches writing on; the folder is fixed next to the test assembly,
        // so no outside value decides where files are written.
        if (Environment.GetEnvironmentVariable(FixturesVariable) != "1")
        {
            return;
        }
        var folder = Path.Combine(AppContext.BaseDirectory, FixturesFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), content);
    }
}
