using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Persists the fidelity of reading progression reports in a JSON file next
/// to the plugin configuration. Entries are kept in memory and rewritten
/// atomically, and the oldest entries are dropped when the file grows past
/// its cap.
/// </summary>
public sealed class ProgressionStore : IProgressionStore
{
    private const int DefaultMaxEntries = 4096;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private readonly int _maxEntries;
    private readonly ILogger<ProgressionStore> _logger;
    private Dictionary<string, ProgressionStoreEntry>? _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProgressionStore}"/> interface.</param>
    public ProgressionStore(IApplicationPaths applicationPaths, ILogger<ProgressionStore> logger)
        : this(
            Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Opds.progression.json"),
            DefaultMaxEntries,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionStore"/> class.
    /// </summary>
    /// <param name="filePath">Path of the backing JSON file.</param>
    /// <param name="maxEntries">Maximum number of entries to keep.</param>
    /// <param name="logger">Optional logger.</param>
    internal ProgressionStore(
        string filePath,
        int maxEntries = DefaultMaxEntries,
        ILogger<ProgressionStore>? logger = null)
    {
        _filePath = filePath;
        _maxEntries = maxEntries;
        _logger = logger ?? NullLogger<ProgressionStore>.Instance;
    }

    /// <inheritdoc />
    public void Save(Guid userId, Guid bookId, ProgressionStoreEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_syncRoot)
        {
            var entries = Load();
            var key = CreateKey(userId, bookId);
            if (!entries.ContainsKey(key) && entries.Count >= _maxEntries)
            {
                // Bound the file: drop the progression reported longest ago.
                EvictOldest(entries);
            }

            entries[key] = entry;
            Persist(entries);
        }
    }

    /// <inheritdoc />
    public ProgressionStoreEntry? Get(Guid userId, Guid bookId)
    {
        lock (_syncRoot)
        {
            return Load().TryGetValue(CreateKey(userId, bookId), out var entry) ? entry : null;
        }
    }

    private static string CreateKey(Guid userId, Guid bookId)
        => userId.ToString("N") + ":" + bookId.ToString("N");

    private static void EvictOldest(Dictionary<string, ProgressionStoreEntry> entries)
    {
        var oldest = entries.OrderBy(entry => entry.Value.Modified).FirstOrDefault();
        if (oldest.Key is not null)
        {
            entries.Remove(oldest.Key);
        }
    }

    private Dictionary<string, ProgressionStoreEntry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _entries = JsonSerializer.Deserialize<Dictionary<string, ProgressionStoreEntry>>(json)
                    ?? new Dictionary<string, ProgressionStoreEntry>();
                return _entries;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "The progression store file is not readable; starting empty");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "The progression store file could not be read; starting empty");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "The progression store file could not be read; starting empty");
        }

        _entries = new Dictionary<string, ProgressionStoreEntry>();
        return _entries;
    }

    private void Persist(Dictionary<string, ProgressionStoreEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temporary file first, so that a crash mid-write
            // cannot corrupt the store.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entries, SerializerOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "The progression store file could not be written");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "The progression store file could not be written");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "The progression store file could not be written");
        }
    }
}
