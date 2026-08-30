using Lyricify.Backgrounds.AppleMusicInspired.Ios;
using Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI;
using Lyricify.Backgrounds.Hosting.WinUI;
using System.Reflection;
using Xunit;

namespace Lyricify.Backgrounds.Tests.WinUI;

public sealed class WinUIBackgroundCatalogTests
{
    [Fact]
    public void RegistrationListMatchesWpfContract()
    {
        var catalog = CreateAppleMusicCatalog();

        Assert.Equal(
            [
                AppleMusicIosBackgroundProvider.BackgroundId,
                AppleMusicIosClassicBackgroundProvider.BackgroundId,
            ],
            catalog.Backgrounds.Select(provider => provider.Id));
    }

    [Fact]
    public void CreateRejectsUnknownId()
    {
        var catalog = CreateAppleMusicCatalog();

        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(
            () => catalog.Create("lyricify.backgrounds.missing"));

        Assert.Contains("lyricify.backgrounds.missing", error.Message);
    }

    [Fact]
    public void ConstructorRejectsDuplicateId()
    {
        var factory = new AppleMusicIosWinUIBackgroundFactory();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new WinUIBackgroundCatalog([factory, factory]));

        Assert.Contains(AppleMusicIosBackgroundProvider.BackgroundId, error.Message);
    }

    [Fact]
    public void FactoryRejectsMismatchedSettingsType()
    {
        var catalog = CreateAppleMusicCatalog();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            catalog.Create(
                AppleMusicIosClassicBackgroundProvider.BackgroundId,
                new AppleMusicIosBackgroundSettings()));

        Assert.Contains(nameof(AppleMusicIosClassicBackgroundSettings), error.Message);
    }

    [Theory]
    [InlineData(
        typeof(AppleMusicIosBackground),
        "Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI.AppleMusicIosRenderer",
        "ApplySaturation")]
    [InlineData(
        typeof(AppleMusicIosClassicBackground),
        "Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI.AppleMusicIosClassicRenderer",
        "ApplyClassicSaturation")]
    public void RenderersResolveTheirVariantOwnedShaderResources(
        Type assemblyMarker,
        string rendererTypeName,
        string expectedSourceMarker)
    {
        Type rendererType = assemblyMarker.Assembly.GetType(rendererTypeName)!;
        MethodInfo read = rendererType.GetMethod(
            "ReadShaderSource",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        string shader = (string)read.Invoke(null, null)!;

        Assert.Contains("Texture2D<float4> Source0", shader);
        Assert.Contains(expectedSourceMarker, shader);
    }

    private static WinUIBackgroundCatalog CreateAppleMusicCatalog() => new(
    [
        new AppleMusicIosWinUIBackgroundFactory(),
        new AppleMusicIosClassicWinUIBackgroundFactory(),
    ]);
}
