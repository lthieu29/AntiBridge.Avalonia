using NUnit.Framework;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class AntigravityVersionServiceTests
{
    #region CompareVersions Tests

    [Test]
    public void CompareVersions_EqualVersions_ReturnsZero()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.16.5", "1.16.5"), Is.EqualTo(0));
    }

    [Test]
    public void CompareVersions_V1GreaterMajor_ReturnsPositive()
    {
        Assert.That(AntigravityVersionService.CompareVersions("2.0.0", "1.16.5"), Is.GreaterThan(0));
    }

    [Test]
    public void CompareVersions_V1LesserMajor_ReturnsNegative()
    {
        Assert.That(AntigravityVersionService.CompareVersions("0.9.0", "1.16.5"), Is.LessThan(0));
    }

    [Test]
    public void CompareVersions_V1GreaterMinor_ReturnsPositive()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.17.0", "1.16.5"), Is.GreaterThan(0));
    }

    [Test]
    public void CompareVersions_V1LesserMinor_ReturnsNegative()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.15.9", "1.16.5"), Is.LessThan(0));
    }

    [Test]
    public void CompareVersions_V1GreaterPatch_ReturnsPositive()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.16.6", "1.16.5"), Is.GreaterThan(0));
    }

    [Test]
    public void CompareVersions_V1LesserPatch_ReturnsNegative()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.16.4", "1.16.5"), Is.LessThan(0));
    }

    [Test]
    public void CompareVersions_DifferentLengths_PadsMissingWithZero()
    {
        Assert.That(AntigravityVersionService.CompareVersions("1.16", "1.16.0"), Is.EqualTo(0));
        Assert.That(AntigravityVersionService.CompareVersions("1.16.5", "1.16"), Is.GreaterThan(0));
    }

    [Test]
    public void CompareVersions_SingleComponent_ComparesCorrectly()
    {
        Assert.That(AntigravityVersionService.CompareVersions("2", "1"), Is.GreaterThan(0));
        Assert.That(AntigravityVersionService.CompareVersions("1", "2"), Is.LessThan(0));
    }

    #endregion

    #region IsNewVersion Tests

    [Test]
    public void IsNewVersion_ExactThreshold_ReturnsTrue()
    {
        Assert.That(AntigravityVersionService.IsNewVersion("1.16.5"), Is.True);
    }

    [Test]
    public void IsNewVersion_AboveThreshold_ReturnsTrue()
    {
        Assert.That(AntigravityVersionService.IsNewVersion("1.16.6"), Is.True);
        Assert.That(AntigravityVersionService.IsNewVersion("1.17.0"), Is.True);
        Assert.That(AntigravityVersionService.IsNewVersion("2.0.0"), Is.True);
    }

    [Test]
    public void IsNewVersion_BelowThreshold_ReturnsFalse()
    {
        Assert.That(AntigravityVersionService.IsNewVersion("1.16.4"), Is.False);
        Assert.That(AntigravityVersionService.IsNewVersion("1.15.9"), Is.False);
        Assert.That(AntigravityVersionService.IsNewVersion("0.99.99"), Is.False);
    }

    #endregion

    #region NormalizeVersion Tests

    [Test]
    public void NormalizeVersion_SimpleVersion_ReturnsSame()
    {
        Assert.That(AntigravityVersionService.NormalizeVersion("1.16.5"), Is.EqualTo("1.16.5"));
    }

    [Test]
    public void NormalizeVersion_FourPartVersion_TruncatesToThree()
    {
        Assert.That(AntigravityVersionService.NormalizeVersion("1.16.5.0"), Is.EqualTo("1.16.5"));
    }

    [Test]
    public void NormalizeVersion_PrefixedVersion_ExtractsVersion()
    {
        Assert.That(AntigravityVersionService.NormalizeVersion("Antigravity 1.16.5"), Is.EqualTo("1.16.5"));
    }

    [Test]
    public void NormalizeVersion_EmptyOrNull_ReturnsNull()
    {
        Assert.That(AntigravityVersionService.NormalizeVersion(""), Is.Null);
        Assert.That(AntigravityVersionService.NormalizeVersion("  "), Is.Null);
    }

    #endregion

    #region ExtractVersionFromJson Tests

    [Test]
    public void ExtractVersionFromJson_ValidJson_ReturnsVersion()
    {
        var json = """{"name": "antigravity", "version": "1.16.5", "main": "index.js"}""";
        Assert.That(AntigravityVersionService.ExtractVersionFromJson(json), Is.EqualTo("1.16.5"));
    }

    [Test]
    public void ExtractVersionFromJson_NoVersionField_ReturnsNull()
    {
        var json = """{"name": "antigravity", "main": "index.js"}""";
        Assert.That(AntigravityVersionService.ExtractVersionFromJson(json), Is.Null);
    }

    #endregion
}
