namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface IJobRepository
{
    Task<List<Job>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Job?> GetByOrganizationAndNameAsync(Guid organizationId, string name, CancellationToken ct = default);
    Task<List<Job>> GetEnabledCronJobsAsync(CancellationToken ct = default);
    Task<List<JobRun>> GetRecentRunsByOrganizationAsync(Guid organizationId, int limit = 50, CancellationToken ct = default);
    Task<Job> CreateAsync(Job job, CancellationToken ct = default);
    Task<Job> UpdateAsync(Job job, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<JobRun> AddRunAsync(JobRun run, CancellationToken ct = default);
    Task<JobRun> UpdateRunAsync(JobRun run, CancellationToken ct = default);
    Task<List<JobRun>> GetRunsAsync(Guid jobId, CancellationToken ct = default);
}
