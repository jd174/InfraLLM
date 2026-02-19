using System.Collections.Concurrent;

namespace InfraLLM.Api.Services;

public interface IChatTaskManager
{
    CancellationToken Begin(Guid sessionId, string userId, CancellationToken requestToken);
    bool Cancel(Guid sessionId, string userId);
    void Complete(Guid sessionId, string userId);
    bool IsRunning(Guid sessionId, string userId);
}

public class ChatTaskManager : IChatTaskManager
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();

    public CancellationToken Begin(Guid sessionId, string userId, CancellationToken requestToken)
    {
        var key = BuildKey(sessionId, userId);
        if (_inflight.TryRemove(key, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        _inflight[key] = cts;
        return cts.Token;
    }

    public bool Cancel(Guid sessionId, string userId)
    {
        var key = BuildKey(sessionId, userId);
        if (!_inflight.TryRemove(key, out var cts)) return false;
        cts.Cancel();
        cts.Dispose();
        return true;
    }

    public void Complete(Guid sessionId, string userId)
    {
        var key = BuildKey(sessionId, userId);
        if (_inflight.TryRemove(key, out var cts))
        {
            cts.Dispose();
        }
    }

    public bool IsRunning(Guid sessionId, string userId)
        => _inflight.ContainsKey(BuildKey(sessionId, userId));

    private static string BuildKey(Guid sessionId, string userId)
        => $"{userId}:{sessionId}";
}