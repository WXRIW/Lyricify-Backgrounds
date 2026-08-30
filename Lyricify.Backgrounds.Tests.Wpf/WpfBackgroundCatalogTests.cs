using Lyricify.Backgrounds.AppleMusicInspired.Ios;
using Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf;
using Lyricify.Backgrounds.Hosting.Wpf;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Xunit;
using AppleMusicIosMesh = Lyricify.Backgrounds.AppleMusicInspired.Ios.Shared.Rendering.AppleMusicIosMesh;
using AppleMusicIosClassicMesh = Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Shared.Rendering.AppleMusicIosClassicMesh;
using AppleMusicPinchVertex = Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Shared.Rendering.AppleMusicPinchVertex;

namespace Lyricify.Backgrounds.Tests.Wpf;

public sealed class WpfBackgroundCatalogTests
{
    [Fact]
    public void AppleMusicFactoriesExposeStableIdsAndDisplayNames()
    {
        var catalog = CreateAppleMusicCatalog();

        Assert.Collection(
            catalog.Backgrounds,
            provider =>
            {
                Assert.Equal(AppleMusicIosBackgroundProvider.BackgroundId, provider.Id);
                Assert.Equal("Apple Music Inspired · iOS", provider.DisplayName);
            },
            provider =>
            {
                Assert.Equal(AppleMusicIosClassicBackgroundProvider.BackgroundId, provider.Id);
                Assert.Equal("Apple Music Inspired · iOS Classic", provider.DisplayName);
            });
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
        var factory = new FakeFactory("duplicate");

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new WpfBackgroundCatalog([factory, factory]));

        Assert.Contains("duplicate", error.Message);
    }

    [Fact]
    public void FactoryRejectsMismatchedSettingsType()
    {
        var catalog = CreateAppleMusicCatalog();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            catalog.Create(
                AppleMusicIosBackgroundProvider.BackgroundId,
                new AppleMusicIosClassicBackgroundSettings()));

        Assert.Contains(nameof(AppleMusicIosBackgroundSettings), error.Message);
    }

    [Fact]
    public void InstanceDisposesItsSession()
    {
        var session = new FakeSession();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var catalog = new WpfBackgroundCatalog([new FakeFactory("fake", session)]);
                using WpfBackgroundInstance instance = catalog.Create("fake");
                Assert.Same(session, instance.Session);
                Assert.False(session.IsDisposed);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void AppleMusicFactoriesCreateRenderersInsideTheWpfVisualTree()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using WpfBackgroundInstance iosInstance =
                    new AppleMusicIosWpfBackgroundFactory().Create(
                        new AppleMusicIosBackgroundSettings());
                var iosBackground = Assert.IsType<AppleMusicIosBackground>(iosInstance.View);
                Assert.Same(iosBackground, iosInstance.Session);
                Assert.IsType<AppleMusicIosRendererView>(Assert.Single(iosBackground.Children));

                using WpfBackgroundInstance classicInstance =
                    new AppleMusicIosClassicWpfBackgroundFactory().Create(
                        new AppleMusicIosClassicBackgroundSettings());
                var classicBackground = Assert.IsType<AppleMusicIosClassicBackground>(classicInstance.View);
                Assert.Same(classicBackground, classicInstance.Session);
                Assert.IsType<AppleMusicIosClassicRendererView>(
                    Assert.Single(classicBackground.Children));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void VariantDefaultsRemainIndependent()
    {
        var ios = new AppleMusicIosBackgroundSettings();
        var classic = new AppleMusicIosClassicBackgroundSettings();

        Assert.Equal(1d, ios.BassPulseScale);
        Assert.Equal(-1, ios.PortraitSubdivisionLevels);
        Assert.Equal(0d, classic.BassPulseScale);
        Assert.Equal(3, classic.PortraitSubdivisionLevels);
        Assert.Equal(3, classic.LandscapeSubdivisionLevels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClassicUsesSixBySixMeshWithThreeSubdivisionPasses(int presetSlot)
    {
        int preset = AppleMusicIosClassicMesh.ResolvePortraitPreset(presetSlot);
        (AppleMusicPinchVertex[] portraitVertices, ushort[] portraitIndices) =
            AppleMusicIosClassicMesh.Create(preset, true, 6, 3);
        (AppleMusicPinchVertex[] landscapeVertices, ushort[] landscapeIndices) =
            AppleMusicIosClassicMesh.Create(preset, true, 6, 3);

        Assert.Equal(41 * 41, portraitVertices.Length);
        Assert.Equal(40 * 40 * 6, portraitIndices.Length);
        Assert.Equal(portraitVertices, landscapeVertices);
        Assert.Equal(portraitIndices, landscapeIndices);
        Assert.All(portraitVertices, vertex =>
        {
            Assert.True(float.IsFinite(vertex.From.X));
            Assert.True(float.IsFinite(vertex.From.Y));
            Assert.True(float.IsFinite(vertex.To.X));
            Assert.True(float.IsFinite(vertex.To.Y));
        });
    }

    [Fact]
    public void IosShaderRetainsOnlyTheCurrentProfile()
    {
        const string resourceName =
            "Lyricify.Backgrounds.AppleMusicInspired.Ios.Shared.Resources.AppleMusicIosBackground.hlsl";
        using Stream stream = typeof(AppleMusicIosMesh).Assembly
            .GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        string shader = reader.ReadToEnd();

        Assert.DoesNotContain("ApplyClassicSaturation", shader);
        Assert.DoesNotContain("return 1.8;", shader);
        Assert.Contains("return 1.4;", shader);
        Assert.Contains("return 0.7;", shader);
        Assert.Contains("clamp(color, -0.752941, 1.25098)", shader);
        Assert.Contains("clamp(color, 0.07, 0.97)", shader);
    }

    [Fact]
    public void IosClassicShaderRetainsClassicProfile()
    {
        const string resourceName =
            "Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Shared.Resources.AppleMusicIosClassicBackground.hlsl";
        using Stream stream = typeof(AppleMusicIosClassicMesh).Assembly
            .GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        string shader = reader.ReadToEnd();

        Assert.Contains("return 1.8;", shader);
        Assert.Contains("return 90.0;", shader);
        Assert.Contains("return 70.0;", shader);
        Assert.Contains("ApplyClassicSaturation(color, 1.3)", shader);
        Assert.Contains("ApplyClassicSaturation(color, 2.0)", shader);
        Assert.Contains("float3 blackMixed = color * 0.75;", shader);
        Assert.Contains("phase * 0.18", shader);
        Assert.Contains("phase * phase * 0.82", shader);
        Assert.Contains("return float4(clamp(color, 0.0, 1.0), 1.0);", shader);
    }

    [Fact]
    public void BothVariantShadersCompileEveryRuntimeEntrypoint()
    {
        CompileAllEntrypoints(
            typeof(AppleMusicIosRendererView),
            ReadRendererShader(typeof(AppleMusicIosRendererView)));
        CompileAllEntrypoints(
            typeof(AppleMusicIosClassicRendererView),
            ReadRendererShader(typeof(AppleMusicIosClassicRendererView)));
    }

    private static string ReadRendererShader(Type rendererType)
    {
        MethodInfo read = rendererType.GetMethod(
            "ReadShaderSource",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)read.Invoke(null, null)!;
    }

    private static string ReadEmbeddedShader(Type assemblyMarker, string resourceName)
    {
        using Stream stream = assemblyMarker.Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void CompileAllEntrypoints(Type rendererType, string shader)
    {
        MethodInfo compile = rendererType.GetMethod(
            "CompileShader",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        (string EntryPoint, string Target)[] entries =
        [
            ("RotationVertex", "vs_5_0"),
            ("ArtworkFillVertex", "vs_5_0"),
            ("FullscreenVertex", "vs_5_0"),
            ("PinchVertex", "vs_5_0"),
            ("RotationPixel", "ps_5_0"),
            ("BlurHorizontalPixel", "ps_5_0"),
            ("BlurVerticalPixel", "ps_5_0"),
            ("OrdinaryMaterialPixel", "ps_5_0"),
            ("MaterialTreatedPixel", "ps_5_0"),
            ("MaterialCompositePixel", "ps_5_0"),
            ("PinchPixel", "ps_5_0"),
            ("PinchCompositePixel", "ps_5_0"),
        ];

        foreach ((string entryPoint, string target) in entries)
        {
            byte[] bytecode = (byte[])compile.Invoke(null, [shader, entryPoint, target])!;
            Assert.NotEmpty(bytecode);
        }
    }

    private static WpfBackgroundCatalog CreateAppleMusicCatalog() => new(
    [
        new AppleMusicIosWpfBackgroundFactory(),
        new AppleMusicIosClassicWpfBackgroundFactory(),
    ]);

    private sealed class FakeSettings : IBackgroundSettings
    {
    }

    private sealed class FakeProvider(string id) : IBackgroundProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public IBackgroundSettings CreateDefaultSettings() => new FakeSettings();
    }

    private sealed class FakeFactory : IWpfBackgroundFactory
    {
        private readonly FakeSession session;

        public FakeFactory(string id, FakeSession? session = null)
        {
            Provider = new FakeProvider(id);
            this.session = session ?? new FakeSession();
        }

        public IBackgroundProvider Provider { get; }

        public WpfBackgroundInstance Create(IBackgroundSettings settings) =>
            new(new Border(), session);
    }

    private sealed class FakeSession : IBackgroundSession
    {
        public event EventHandler? FirstFramePresented { add { } remove { } }
        public event EventHandler<BackgroundFaultedEventArgs>? Faulted { add { } remove { } }
        public bool IsReady => true;
        public bool IsDisposed { get; private set; }
        public void UpdateState(BackgroundState state) { }
        public Task SetArtworkAsync(BackgroundArtwork artwork, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }
}
