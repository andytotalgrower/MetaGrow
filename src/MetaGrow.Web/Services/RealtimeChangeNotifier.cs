using System.Collections.Concurrent;
using ApiModels.Realtime;

namespace MetaGrow.Web.Services;

public interface IRealtimeChangeNotifier
{
    IDisposable Subscribe(Func<RealtimeEventEnvelope, Task> handler);
    Task PublishAsync(RealtimeEventEnvelope message);
}

public sealed class RealtimeChangeNotifier : IRealtimeChangeNotifier
{
    private readonly ConcurrentDictionary<long, Func<RealtimeEventEnvelope, Task>> _handlers = new();
    private long _nextSubscriptionId;

    public IDisposable Subscribe(Func<RealtimeEventEnvelope, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Interlocked.Increment(ref _nextSubscriptionId);
        _handlers[id] = handler;
        return new Subscription(() => _handlers.TryRemove(id, out _));
    }

    public Task PublishAsync(RealtimeEventEnvelope message)
    {
        var handlers = _handlers.Values.ToArray();
        return handlers.Length == 0
            ? Task.CompletedTask
            : Task.WhenAll(handlers.Select(handler => handler(message)));
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
