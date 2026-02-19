using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _db;

    public SessionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<Session>> GetByUserAsync(Guid organizationId, string userId, CancellationToken ct = default)
        => await _db.Sessions
            .Where(s => s.OrganizationId == organizationId && s.UserId == userId)
            .OrderByDescending(s => s.LastMessageAt ?? s.CreatedAt)
            .ToListAsync(ct);

    public async Task<Session> CreateAsync(Session session, CancellationToken ct = default)
    {
        session.Id = Guid.NewGuid();
        session.CreatedAt = DateTime.UtcNow;
        session.HostIds ??= [];
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<Session> UpdateAsync(Session session, CancellationToken ct = default)
    {
        _db.Sessions.Update(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FindAsync([id], ct);
        if (session != null)
        {
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<Message> AddMessageAsync(Message message, CancellationToken ct = default)
    {
        message.Id = Guid.NewGuid();
        message.CreatedAt = DateTime.UtcNow;
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }

    public async Task<Message> UpdateMessageAsync(Message message, CancellationToken ct = default)
    {
        _db.Messages.Update(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }

    public async Task<List<Message>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default)
        => await _db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
}
