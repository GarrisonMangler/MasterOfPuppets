using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MasterOfPuppets.LuaScripting.Events;

public sealed record LuaHostEvent(
    long Sequence,
    string Name,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Data);

public readonly record struct LuaEventHubStatistics(
    int Capacity,
    long Published,
    long Consumed,
    long Dropped,
    bool Completed);

/// <summary>
/// One bounded, thread-safe stream per Lua run. Producers never invoke Lua;
/// the run consumes events sequentially on its own task.
/// </summary>
public sealed class LuaEventHub : IDisposable {
    public const int DefaultCapacity = 256;
    private readonly Channel<LuaHostEvent> _channel;
    private readonly int _capacity;
    private long _sequence;
    private long _published;
    private long _consumed;
    private long _dropped;
    private int _queued;
    private int _completed;

    public LuaEventHub(int capacity = DefaultCapacity) {
        if (capacity is <= 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _channel = Channel.CreateBounded<LuaHostEvent>(new BoundedChannelOptions(capacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    }

    public bool Publish(string name, IReadOnlyDictionary<string, string>? data = null, DateTimeOffset? timestamp = null) {
        name = NormalizeName(name);
        if (Volatile.Read(ref _completed) != 0)
            return false;
        var payload = data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : data.Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Take(64)
                .ToDictionary(pair => pair.Key.Trim(), pair => Limit(pair.Value, 2000), StringComparer.Ordinal);
        var item = new LuaHostEvent(
            Interlocked.Increment(ref _sequence),
            name,
            timestamp ?? DateTimeOffset.UtcNow,
            payload);
        if (!_channel.Writer.TryWrite(item))
            return false;
        Interlocked.Increment(ref _published);
        var queued = Interlocked.Increment(ref _queued);
        if (queued > _capacity) {
            Interlocked.Exchange(ref _queued, _capacity);
            Interlocked.Increment(ref _dropped);
        }
        return true;
    }

    public bool TryRead(string? name, out LuaHostEvent? item) {
        var filter = NormalizeFilter(name);
        while (_channel.Reader.TryRead(out var candidate)) {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _consumed);
            if (filter == null || candidate.Name.Equals(filter, StringComparison.Ordinal)) {
                item = candidate;
                return true;
            }
        }
        item = null;
        return false;
    }

    public async Task<LuaHostEvent?> ReadAsync(string? name, TimeSpan timeout, CancellationToken cancellationToken) {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var filter = NormalizeFilter(name);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try {
            while (await _channel.Reader.WaitToReadAsync(timeoutSource.Token)) {
                while (_channel.Reader.TryRead(out var candidate)) {
                    Interlocked.Decrement(ref _queued);
                    Interlocked.Increment(ref _consumed);
                    if (filter == null || candidate.Name.Equals(filter, StringComparison.Ordinal))
                        return candidate;
                }
            }
            return null;
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return null;
        }
    }

    public LuaEventHubStatistics Snapshot() => new(
        _capacity,
        Volatile.Read(ref _published),
        Volatile.Read(ref _consumed),
        Volatile.Read(ref _dropped),
        Volatile.Read(ref _completed) != 0);

    public void Dispose() {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _channel.Writer.TryComplete();
    }

    private static string NormalizeName(string? name) {
        var normalized = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > 64
            || normalized.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new ArgumentException("Lua event names must contain only letters, digits, '.', '-', or '_' and be at most 64 characters.", nameof(name));
        return normalized;
    }

    private static string? NormalizeFilter(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : NormalizeName(name);

    private static string Limit(string? value, int maximum) {
        value ??= string.Empty;
        return value.Length <= maximum ? value : value[..maximum];
    }
}
