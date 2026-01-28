using Microsoft.AspNetCore.DataProtection;

namespace EmailToMarkdown.Services;

public class TokenEncryptionService
{
    private readonly IDataProtector _protector;

    public TokenEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("EmailToMarkdown.TokenProtection.v1");
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        return _protector.Protect(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception)
        {
            // Token may have been encrypted with an old key
            return string.Empty;
        }
    }
}
