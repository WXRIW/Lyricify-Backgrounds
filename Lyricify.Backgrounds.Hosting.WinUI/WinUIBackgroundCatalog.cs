using Lyricify.Backgrounds;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

namespace Lyricify.Backgrounds.Hosting.WinUI;

public interface IWinUIBackgroundFactory
{
    IBackgroundProvider Provider { get; }

    WinUIBackgroundInstance Create(IBackgroundSettings settings);
}

public sealed class WinUIBackgroundInstance : IDisposable
{
    public WinUIBackgroundInstance(UIElement view, IBackgroundSession session)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public UIElement View { get; }

    public IBackgroundSession Session { get; }

    public void Dispose() => Session.Dispose();
}

public sealed class WinUIBackgroundCatalog
{
    private readonly IReadOnlyDictionary<string, IWinUIBackgroundFactory> factories;

    public WinUIBackgroundCatalog(IEnumerable<IWinUIBackgroundFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var byId = new Dictionary<string, IWinUIBackgroundFactory>(StringComparer.Ordinal);
        var providers = new List<IBackgroundProvider>();
        foreach (IWinUIBackgroundFactory factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            IBackgroundProvider provider = factory.Provider ??
                throw new ArgumentException("A background factory returned a null provider.", nameof(factories));
            if (string.IsNullOrWhiteSpace(provider.Id))
            {
                throw new ArgumentException("A background provider has an empty ID.", nameof(factories));
            }
            if (!byId.TryAdd(provider.Id, factory))
            {
                throw new ArgumentException(
                    $"The background ID '{provider.Id}' is registered more than once.",
                    nameof(factories));
            }
            providers.Add(provider);
        }

        this.factories = new ReadOnlyDictionary<string, IWinUIBackgroundFactory>(byId);
        Backgrounds = new ReadOnlyCollection<IBackgroundProvider>(providers);
    }

    public IReadOnlyList<IBackgroundProvider> Backgrounds { get; }

    public WinUIBackgroundInstance Create(
        string id,
        IBackgroundSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A background ID is required.", nameof(id));
        }
        if (!factories.TryGetValue(id, out IWinUIBackgroundFactory? factory))
        {
            throw new KeyNotFoundException($"No WinUI background is registered for ID '{id}'.");
        }

        return factory.Create(settings ?? factory.Provider.CreateDefaultSettings());
    }
}
