namespace InfraLLM.Core.Enums;

public enum AuditEventType
{
    CommandExecuted,
    CommandDenied,
    HostAdded,
    HostRemoved,
    PolicyChanged,
    SessionStarted,
    SessionEnded,
    CredentialAdded,
    CredentialRemoved
}
