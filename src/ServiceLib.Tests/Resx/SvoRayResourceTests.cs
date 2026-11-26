using System.Collections;
using System.Globalization;
using ServiceLib.Resx;

namespace ServiceLib.Tests.Resx;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRayResourceTests))]
public class SvoRayResourceTests
{
    [Test]
    public void EverySvoRayResource_ShouldHaveEnglishAndRussianValues()
    {
        var neutral = ResUI.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var russian = ResUI.ResourceManager.GetResourceSet(new CultureInfo("ru"), true, false);

        neutral.Should().NotBeNull();
        russian.Should().NotBeNull();

        var keys = neutral!
            .Cast<DictionaryEntry>()
            .Select(entry => entry.Key.ToString()!)
            .Where(key => key.StartsWith("SvoRay", StringComparison.Ordinal))
            .OrderBy(key => key)
            .ToList();

        keys.Should().NotBeEmpty();
        foreach (var key in keys)
        {
            (neutral.GetObject(key) as string).Should().NotBeNullOrWhiteSpace($"neutral resource {key} must be usable");
            (russian!.GetObject(key) as string).Should().NotBeNullOrWhiteSpace($"Russian resource {key} must not fall back to English");
            typeof(ResUI).GetProperty(key).Should().NotBeNull($"{key} must be available to XAML and C#");
        }
    }
}
