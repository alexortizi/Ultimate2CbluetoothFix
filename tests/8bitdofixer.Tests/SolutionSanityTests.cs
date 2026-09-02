using Xunit;

namespace BitDoFixer.Tests;

public class SolutionSanityTests
{
    [Fact]
    public void TestProjectCanSeeTheAppAssembly()
    {
        // Localization es un tipo public de la app; si esto compila y corre,
        // el ProjectReference y el TargetFramework están bien.
        Assert.NotNull(Localization.Instance);
    }

    [Fact]
    public void LocalizationDefaultsToEnglish()
    {
        Assert.True(Localization.Instance.IsEnglish);
    }
}
