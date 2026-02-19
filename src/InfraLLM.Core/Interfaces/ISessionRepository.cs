namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Session>> GetByUserAsync(Guid organizationId, string userId, CancellationToken ct = default);
    Task<Session> CreateAsync(Session session, CancellationToken ct = default);
    Task<Session> UpdateAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Message> AddMessageAsync(Message message, CancellationToken ct = default);
    Task<Message> UpdateMessageAsync(Message message, CancellationToken ct = default);
    Task<List<Message>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default);
}
