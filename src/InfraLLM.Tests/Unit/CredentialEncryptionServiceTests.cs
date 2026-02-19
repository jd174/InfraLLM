using Microsoft.Extensions.Options;
using InfraLLM.Infrastructure.Services;

namespace InfraLLM.Tests.Unit;

public class CredentialEncryptionServiceTests
{
    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var service = new CredentialEncryptionService(Options.Create(new CredentialEncryptionOptions
        {
            MasterKey = "test_master_key_that_is_long_enough"
        }));

        var plaintext = "super-secret";
        var encrypted = service.Encrypt(plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.True(service.IsEncrypted(encrypted));
        Assert.Equal(plaintext, service.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_Passthrough()
    {
        var service = new CredentialEncryptionService(Options.Create(new CredentialEncryptionOptions
        {
            MasterKey = "test_master_key_that_is_long_enough"
        }));

        Assert.Equal("plain", service.Decrypt("plain"));
    }
}
