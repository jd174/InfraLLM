namespace InfraLLM.Core.Interfaces;

public interface ICredentialEncryptionService
{
    bool IsEncrypted(string value);
    string Encrypt(string plaintext);
    string Decrypt(string ciphertextOrPlaintext);
}
