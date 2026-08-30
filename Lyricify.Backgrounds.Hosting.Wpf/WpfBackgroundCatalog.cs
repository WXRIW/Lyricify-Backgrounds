using Lyricify.Backgrounds;
using System.Collections.ObjectModel;
using System.Windows;

namespace Lyricify.Backgrounds.Hosting.Wpf;

public interface IWpfBackgroundFactory
{
    IBackgroundProvider Provider { get; }

    WpfBackgroundInstance Create(IBackgroundSettings settings);
}

public sealed class WpfBackgroundInstance : IDisposable
{
    public WpfBackgroundInstance(
        FrameworkElement view,
        IBackgroundSession session)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public FrameworkElement View { get; }

    public IBackgroundSession Session { get; }

    public void Dispose() => Session.Dispose();
}

public sealed class WpfBackgroundCatalog
{
    private readonly IReadOnlyDictionary<string, IWpfBackgroundFactory> factories;

    public WpfBackgroundCatalog(IEnumerable<IWpfBackgroundFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var byId = new Dictionary<string, IWpfBackgroundFactory>(StringComparer.Ordinal);
        var providers = new List<IBackgroundProvider>();
        foreach (IWpfBackgroundFactory factory in factories)
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

        this.factories = new ReadOnlyDictionary<string, IWpfBackgroundFactory>(byId);
        Backgrounds = new ReadOnlyCollection<IBackgroundProvider>(providers);
    }

    public IReadOnlyList<IBackgroundProvider> Backgrounds { get; }

    public WpfBackgroundInstance Create(
        string id,
        IBackgroundSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A background ID is required.", nameof(id));
        }
        if (!factories.TryGetValue(id, out IWpfBackgroundFactory? factory))
        {
            throw new KeyNotFoundException($"No WPF background is registered for ID '{id}'.");
        }

        return factory.Create(settings ?? factory.Provider.CreateDefaultSettings());
    }
}
