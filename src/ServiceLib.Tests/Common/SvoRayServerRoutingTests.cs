using System.Text;
using AwesomeAssertions;
using ServiceLib.Common;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRayServerRoutingTests
{
    /// <summary>The profile as a 3X-UI panel sends it: Happ's format, every scalar a string.</summary>
    private const string PanelProfileJson = """
        {"Name":"LetMeOut","GlobalProxy":"true","UseChunkFiles":"true","RemoteDns":"8.8.8.8",
         "DomesticDns":"77.88.8.8","Geoipurl":"https://cdn.example/geoip.dat",
         "Geositeurl":"https://cdn.example/geosite.dat","RouteOrder":"block-proxy-direct",
         "DirectSites":["geosite:private","geosite:category-ru","geosite:steam","domain:cdn.example"],
         "DirectIp":["geoip:private","geoip:direct"],
         "ProxySites":["geosite:youtube","domain:discord.com"],"ProxyIp":[],
         "BlockSites":["geosite:category-ads"],"BlockIp":[],"DomainStrategy":"AsIs","FakeDNS":"false"}
        """;

    private static string Header(string json, bool urlSafe = false)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        if (urlSafe)
        {
            b64 = b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
        return "happ://routing/onadd/" + b64;
    }

    [Fact]
    public void ParseHeader_ShouldReadThePanelFormat()
    {
        var profile = SvoRayServerRouting.ParseHeader(Header(PanelProfileJson));

        profile.Should().NotBeNull();
        profile!.Name.Should().Be("LetMeOut");
        profile.RouteOrder.Should().Be("block-proxy-direct");
        profile.DomainStrategy.Should().Be("AsIs");
        profile.DirectSites.Should().Contain(["geosite:steam", "domain:cdn.example"]);
        profile.DirectIp.Should().Equal("geoip:private", "geoip:direct");
        profile.ProxySites.Should().Equal("geosite:youtube", "domain:discord.com");
        profile.BlockSites.Should().Equal("geosite:category-ads");
        profile.GeositeUrl.Should().Be("https://cdn.example/geosite.dat");
        profile.GeoipUrl.Should().Be("https://cdn.example/geoip.dat");
    }

    [Fact]
    public void ParseHeader_ShouldAcceptUrlSafeBase64WithoutPadding()
    {
        SvoRayServerRouting.ParseHeader(Header(PanelProfileJson, urlSafe: true)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vless://not-a-routing-profile")]
    [InlineData("happ://routing/onadd/%%%not-base64%%%")]
    [InlineData("happ://routing/onadd/bm90IGpzb24=")] // "not json"
    public void ParseHeader_ShouldIgnoreAnythingElse(string? value)
    {
        SvoRayServerRouting.ParseHeader(value).Should().BeNull();
    }

    [Fact]
    public void FromHeaders_ShouldHonourTheServerSwitchingRoutingOff()
    {
        SvoRayServerRouting.FromHeaders(Header(PanelProfileJson), "false").Should().BeNull();
        SvoRayServerRouting.FromHeaders(Header(PanelProfileJson), "0").Should().BeNull();
        SvoRayServerRouting.FromHeaders(Header(PanelProfileJson), "true").Should().NotBeNull();
        SvoRayServerRouting.FromHeaders(Header(PanelProfileJson), null).Should().NotBeNull();
    }

    [Fact]
    public void StoredProfile_ShouldRoundTrip()
    {
        var stored = SvoRayServerRouting.FromHeaders(Header(PanelProfileJson), "true");
        var profile = SvoRayServerRouting.Parse(stored);

        profile.Should().NotBeNull();
        profile!.ProxySites.Should().Equal("geosite:youtube", "domain:discord.com");
    }

    /// <summary>
    /// Entries end up inside the core config, so anything that is not a plain matcher is dropped,
    /// and only https geo sources are accepted.
    /// </summary>
    [Fact]
    public void Parse_ShouldDropEntriesThatAreNotPlainMatchersAndInsecureSources()
    {
        var profile = SvoRayServerRouting.Parse("""
            {"DirectSites":["domain:ok.example","two words","quote\"d","comma,list","geosite:steam"],
             "Geositeurl":"http://cdn.example/geosite.dat","RouteOrder":"direct-direct-proxy"}
            """);

        profile.Should().NotBeNull();
        profile!.DirectSites.Should().Equal("domain:ok.example", "geosite:steam");
        profile.GeositeUrl.Should().BeNull();
        profile.RouteOrder.Should().Be(SvoRayServerRouting.DefaultRouteOrder);
    }

    [Fact]
    public void Parse_ShouldReturnNullForAProfileWithoutRules()
    {
        SvoRayServerRouting.Parse("""{"Name":"empty","DirectSites":[]}""").Should().BeNull();
    }

    [Fact]
    public void RewriteGeo_ShouldPointKnownCategoriesAtTheProfileFileAndDropTheRest()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "STEAM", "category-ru" };
        var dropped = new List<string>();

        var result = SvoRayServerRouting.RewriteGeo(
            ["geosite:steam", "geosite:whitelist", "domain:a.example", "ext:evil.dat:x", "geosite:category-ru@ads"],
            "geosite:", SvoRayServerRouting.GeoSiteFileName, tags, dropped);

        result.Should().Equal(
            "ext:svoray_geosite.dat:steam",
            "domain:a.example",
            "ext:svoray_geosite.dat:category-ru@ads");
        dropped.Should().Equal("geosite:whitelist", "ext:evil.dat:x");
    }

    [Fact]
    public void RewriteGeo_WithoutAProfileFile_ShouldKeepStockCategoriesAsTheyAre()
    {
        var dropped = new List<string>();
        var result = SvoRayServerRouting.RewriteGeo(["geosite:steam", "geosite:missing"], "geosite:", null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam" }, dropped);

        result.Should().Equal("geosite:steam");
        dropped.Should().Equal("geosite:missing");
    }

    [Fact]
    public void RewriteGeo_WithoutTags_ShouldDropEveryGeoEntry()
    {
        var dropped = new List<string>();
        var result = SvoRayServerRouting.RewriteGeo(["geoip:direct", "10.0.0.0/8"], "geoip:",
            SvoRayServerRouting.GeoIpFileName, null, dropped);

        result.Should().Equal("10.0.0.0/8");
        dropped.Should().Equal("geoip:direct");
    }

    [Fact]
    public void ReadGeoTags_ShouldListCategoriesOfAGeoFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. GeoEntry("STEAM", 40), .. GeoEntry("category-ru", 3)]);
            SvoRayServerRouting.ReadGeoTags(path).Should().BeEquivalentTo(["STEAM", "category-ru"]);

            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("<html>blocked</html>"));
            SvoRayServerRouting.ReadGeoTags(path).Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }

        SvoRayServerRouting.ReadGeoTags(path).Should().BeEmpty();
    }

    /// <summary>One list entry: field 1 (the category) followed by some opaque payload.</summary>
    private static byte[] GeoEntry(string tag, int payload)
    {
        var name = Encoding.UTF8.GetBytes(tag);
        var body = new List<byte> { 0x0A, (byte)name.Length };
        body.AddRange(name);
        body.Add(0x12);
        body.Add((byte)payload);
        body.AddRange(new byte[payload]);
        return [0x0A, (byte)body.Count, .. body];
    }
}
