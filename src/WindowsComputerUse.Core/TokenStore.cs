using System.Security.Cryptography;

namespace WindowsComputerUse.Core;

public static class TokenStore
{
    private static string TokenPath => Path.Combine(ServiceDefaults.DataDirectory, "token.dpapi");

    public static string Load()
    {
        // DPAPI CurrentUser prevents another Windows account from using a copied token file.
        return Convert.ToBase64String(ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null,
            DataProtectionScope.CurrentUser));
    }

    public static string LoadOrCreate()
    {
        Directory.CreateDirectory(ServiceDefaults.DataDirectory);
        try { return Load(); }
        catch (FileNotFoundException)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temp = Path.Combine(ServiceDefaults.DataDirectory, $".token-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(temp, encrypted);
                File.Move(temp, TokenPath);
                return Convert.ToBase64String(bytes);
            }
            catch (IOException) when (File.Exists(TokenPath)) { return Load(); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static bool Matches(string? authorization, string token)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var supplied = authorization[7..];
        var actual = System.Text.Encoding.UTF8.GetBytes(token);
        var candidate = System.Text.Encoding.UTF8.GetBytes(supplied);
        return candidate.Length == actual.Length && CryptographicOperations.FixedTimeEquals(candidate, actual);
    }
}
