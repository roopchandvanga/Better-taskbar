using Facet.Core.Apps;

namespace Facet.Core.Tests;

public class PackagedAppTests
{
    [Fact]
    public void ParsesFamilyAndRelativeExeFromAnInstallPath()
    {
        Assert.True(PackagedApp.TryParsePath(@"C:\Program Files\WindowsApps\Claude_5.11492.5.0_x64__pzs8sxrjxfjjc\app\claude.exe",
            out var family, out var relative));
        Assert.Equal("Claude_pzs8sxrjxfjjc", family);
        Assert.Equal(@"app\claude.exe", relative);
    }

    [Fact]
    public void ParsesPackagesWithDotsInTheName()
    {
        Assert.True(PackagedApp.TryParsePath(@"C:\Program Files\WindowsApps\Microsoft.OutlookForWindows_1.2026.1103.200_x64__8wekyb3d8bbwe\olk.exe",
            out var family, out var relative));
        Assert.Equal("Microsoft.OutlookForWindows_8wekyb3d8bbwe", family);
        Assert.Equal("olk.exe", relative);
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\")]
    [InlineData(@"C:\Program Files\WindowsApps\NotAPackageFolder\app.exe")]
    public void RejectsPathsOutsideAPackage(string path) =>
        Assert.False(PackagedApp.TryParsePath(path, out _, out _));

    private const string Manifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Applications>
            <Application Id="Helper" Executable="app\helper.exe" EntryPoint="Windows.FullTrustApplication" />
            <Application Id="Claude" Executable="app/Claude.exe" EntryPoint="Windows.FullTrustApplication" />
          </Applications>
        </Package>
        """;

    [Fact]
    public void FindsTheApplicationWhoseExecutableMatches() =>
        Assert.Equal("Claude", PackagedApp.AppIdFromManifest(Manifest, @"app\claude.exe")); // slashes and case don't matter

    [Fact]
    public void FallsBackToTheOnlyApplication()
    {
        const string single = """<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications><Application Id="App" Executable="other.exe" /></Applications></Package>""";
        Assert.Equal("App", PackagedApp.AppIdFromManifest(single, "olk.exe"));
    }

    [Fact]
    public void NoMatchAmongSeveralApplicationsIsNull() =>
        Assert.Null(PackagedApp.AppIdFromManifest(Manifest, @"app\unknown.exe"));

    [Fact]
    public void BadManifestIsNull() => Assert.Null(PackagedApp.AppIdFromManifest("not xml", "a.exe"));
}
