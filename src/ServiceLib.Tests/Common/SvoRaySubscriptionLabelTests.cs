using System.Text;
using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRaySubscriptionLabelTests
{
    [Fact]
    public void ParseTitle_ShouldReadPlainAndBase64Titles()
    {
        var encoded = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Корвелин VPN"));

        SvoRaySubscriptionLabel.ParseTitle("Korveline").Should().Be("Korveline");
        SvoRaySubscriptionLabel.ParseTitle(encoded).Should().Be("Корвелин VPN");
        SvoRaySubscriptionLabel.ParseTitle("base64:%%%").Should().BeNull();
        SvoRaySubscriptionLabel.ParseTitle("  ").Should().BeNull();
        SvoRaySubscriptionLabel.ParseTitle("Line\r\nbreak").Should().Be("Linebreak");
        SvoRaySubscriptionLabel.ParseTitle(new string('x', 60))!.Length.Should().Be(32);
    }

    [Fact]
    public void For_ShouldPreferTheUsersNameThenTheProviderTitleThenTheHost()
    {
        var sub = new SubItem { Remarks = "import_sub", Url = "https://cdn.example.com/sub/secret-token" };
        SvoRaySubscriptionLabel.For(sub).Should().Be("cdn.example.com", "the private path must never be shown");

        sub.ProfileTitle = "Example VPN";
        SvoRaySubscriptionLabel.For(sub).Should().Be("Example VPN");

        sub.Remarks = "Work";
        SvoRaySubscriptionLabel.For(sub).Should().Be("Work");
    }

    [Fact]
    public void Compose_ShouldNameTheSourceOnlyWhenThereAreSeveral()
    {
        SvoRaySubscriptionLabel.Compose("Example VPN", "1 · Main", severalSources: true).Should().Be("Example VPN · 1 · Main");
        SvoRaySubscriptionLabel.Compose("Example VPN", "1 · Main", severalSources: false).Should().Be("1 · Main");
        SvoRaySubscriptionLabel.Compose(null, "My server", severalSources: true).Should().Be("My server");
    }

    [Fact]
    public void FindDuplicates_ShouldMatchTheSameServerWhateverItsName()
    {
        var existing = Vless("a", "server.example.com", 443, "Main");
        var same = Vless("b", "server.example.com", 443, "renamed");
        var other = Vless("c", "server.example.com", 8443, "Main");

        var duplicates = ConfigHandler.FindDuplicates([existing], [same, other]);

        duplicates.Should().ContainSingle();
        duplicates[0].Added.IndexId.Should().Be("b");
        duplicates[0].Existing.IndexId.Should().Be("a");
    }

    private static ProfileItem Vless(string id, string address, int port, string remarks) => new()
    {
        IndexId = id,
        ConfigType = EConfigType.VLESS,
        Address = address,
        Port = port,
        Password = "6f1b2c3d-1111-2222-3333-444455556666",
        Remarks = remarks,
        Network = "tcp",
        StreamSecurity = "reality",
    };
}
