using Cominomi.Shared.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Cominomi.Shared.Services;

/// <summary>
///     Triggers IOptionsMonitor&lt;AppSettings&gt; reload when settings are saved.
/// </summary>
public class AppSettingsChangeNotifier : IOptionsChangeTokenSource<AppSettings>
{
    private CancellationTokenSource _cts = new();

    public IChangeToken GetChangeToken()
    {
        return new CancellationChangeToken(_cts.Token);
    }

    public string Name => Options.DefaultName;

    public void NotifyChanged()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}