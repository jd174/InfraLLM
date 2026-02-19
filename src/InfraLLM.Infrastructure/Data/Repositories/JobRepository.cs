using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class JobRepository : IJobRepository
{
    private readonly ApplicationDbContext _db;

    public JobRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Job>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Jobs
            .Where(j => j.OrganizationId == organizationId)
            .OrderByDescending(j => j.UpdatedAt)
            .ToListAsync(ct);

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Jobs.Include(j => j.Runs).FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<Job?> GetByOrganizationAndNameAsync(Guid organizationId, string name, CancellationToken ct = default)
        => await _db.Jobs
            .FirstOrDefaultAsync(j => j.OrganizationId == organizationId && j.Name == name, ct);

    public async Task<List<Job>> GetEnabledCronJobsAsync(CancellationToken ct = default)
        => await _db.Jobs
            .Where(j => j.IsEnabled && j.TriggerType == InfraLLM.Core.Enums.JobTriggerType.Cron && j.CronSchedule != null)
            .OrderBy(j => j.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<JobRun>> GetRecentRunsByOrganizationAsync(Guid organizationId, int limit = 50, CancellationToken ct = default)
        => await _db.JobRuns
            .Include(r => r.Job)
            .Where(r => r.Job.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<Job> CreateAsync(Job job, CancellationToken ct = default)
    {
        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return job;
    }

    public async Task<Job> UpdateAsync(Job job, CancellationToken ct = default)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _db.Jobs.Update(job);
        await _db.SaveChangesAsync(ct);
        return job;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync([id], ct);
        if (job != null)
        {
            _db.Jobs.Remove(job);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<JobRun> AddRunAsync(JobRun run, CancellationToken ct = default)
    {
        run.Id = Guid.NewGuid();
        run.CreatedAt = DateTime.UtcNow;
        _db.JobRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<JobRun> UpdateRunAsync(JobRun run, CancellationToken ct = default)
    {
        _db.JobRuns.Update(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<List<JobRun>> GetRunsAsync(Guid jobId, CancellationToken ct = default)
        => await _db.JobRuns
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
}
