using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Infrastructure.Services;

public class SshConnectionPool : ISshConnectionPool, IDisposable
{
    private readonly ConcurrentDictionary<Guid, SshClient> _connections = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SshConnectionPool> _logger;
    private readonly ICredentialEncryptionService _credentialEncryption;

    public SshConnectionPool(
        IServiceProvider serviceProvider,
        ILogger<SshConnectionPool> logger,
        ICredentialEncryptionService credentialEncryption)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _credentialEncryption = credentialEncryption;
    }

    public async Task<object> GetConnectionAsync(Guid hostId, CancellationToken ct = default)
    {
        if (_connections.TryGetValue(hostId, out var existing) && existing.IsConnected)
            return existing;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var host = await db.Hosts.Include(h => h.Credential).FirstOrDefaultAsync(h => h.Id == hostId, ct)
            ?? throw new InvalidOperationException($"Host {hostId} not found");

        var username = host.Username ?? "root";
        var authMethods = BuildAuthMethods(username, host.Credential);

        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException(
                $"No authentication method available for host {host.Name}. Please attach a credential to this host.");
        }

        var connectionInfo = new ConnectionInfo(
            host.Hostname,
            host.Port,
            username,
            authMethods.ToArray());

        var client = new SshClient(connectionInfo);
        client.Connect();

        _connections[hostId] = client;
        _logger.LogInformation("SSH connection established to {Host} as {Username}", host.Hostname, username);
        return client;
    }

    private List<AuthenticationMethod> BuildAuthMethods(string username, Core.Models.Credential? credential)
    {
        var methods = new List<AuthenticationMethod>();

        if (credential == null)
        {
            _logger.LogWarning("No credential attached to host — cannot authenticate");
            return methods;
        }

        switch (credential.CredentialType)
        {
            case CredentialType.Password:
            {
                var secret = _credentialEncryption.Decrypt(credential.EncryptedValue);
                methods.Add(new PasswordAuthenticationMethod(username, secret));
                break;
            }

            case CredentialType.SSHKey:
                try
                {
                    var keyText = _credentialEncryption.Decrypt(credential.EncryptedValue);
                    var keyBytes = Encoding.UTF8.GetBytes(keyText);
                    using var keyStream = new MemoryStream(keyBytes);
                    var privateKey = new PrivateKeyFile(keyStream);
                    methods.Add(new PrivateKeyAuthenticationMethod(username, privateKey));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse SSH private key for credential {CredentialName}", credential.Name);
                }
                break;

            case CredentialType.APIToken:
                // API tokens are used for non-SSH connections; fall back to password auth
            {
                var secret = _credentialEncryption.Decrypt(credential.EncryptedValue);
                methods.Add(new PasswordAuthenticationMethod(username, secret));
                break;
            }

            default:
                _logger.LogWarning("Unsupported credential type: {Type}", credential.CredentialType);
                break;
        }

        return methods;
    }

    public Task ReleaseConnectionAsync(Guid hostId, object client)
    {
        // Pool keeps connections alive; no-op for now
        return Task.CompletedTask;
    }

    public async Task<bool> TestConnectionAsync(Guid hostId, CancellationToken ct = default)
    {
        try
        {
            // Remove any cached (possibly stale) connection first
            if (_connections.TryRemove(hostId, out var old))
            {
                try { old.Disconnect(); old.Dispose(); } catch { }
            }

            var client = (SshClient)await GetConnectionAsync(hostId, ct);
            return client.IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH connection test failed for host {HostId}", hostId);
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var client in _connections.Values)
        {
            try { client.Disconnect(); client.Dispose(); }
            catch { /* best effort cleanup */ }
        }
        _connections.Clear();
    }

    public Task InvalidateHostAsync(Guid hostId)
    {
        if (_connections.TryRemove(hostId, out var old))
        {
            try { old.Disconnect(); old.Dispose(); } catch { }
            _logger.LogInformation("SSH connection cache invalidated for host {HostId}", hostId);
        }

        return Task.CompletedTask;
    }
}
