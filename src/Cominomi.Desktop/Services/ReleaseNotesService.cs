using System.Reflection;
using System.Text.Json;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Cominomi.Desktop.Services;

public class ReleaseNotesService(ILogger<ReleaseNotesService> logger) : IReleaseNotesService
{
    private IReadOnlyList<ReleaseNote>? _cached;

    public Task<IReadOnlyList<ReleaseNote>> GetReleaseNotesAsync()
    {
        if (_cached != null)
            return Task.FromResult(_cached);

        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("changelog.json");

            if (stream == null)
            {
                logger.LogWarning("changelog.json embedded resource not found");
                _cached = [];
                return Task.FromResult(_cached);
            }

            var notes = JsonSerializer.Deserialize<List<ReleaseNote>>(stream, JsonDefaults.Options);
            _cached = notes?.AsReadOnly() ?? (IReadOnlyList<ReleaseNote>)[];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load release notes");
            _cached = [];
        }

        return Task.FromResult(_cached);
    }
}