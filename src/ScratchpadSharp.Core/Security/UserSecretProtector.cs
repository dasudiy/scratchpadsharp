using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ScratchpadSharp.Core.Configuration;

namespace ScratchpadSharp.Core.Security;

public enum UserSecretKind
{
    DatabasePassword,
    SshPassword,
    SshPassphrase
}

public sealed record UserSecretPromptRequest(
    string ModuleId,
    string ModuleDisplayName,
    UserSecretKind Kind);

public interface IUserSecretPrompt
{
    Task<string?> RequestAsync(UserSecretPromptRequest request, CancellationToken ct = default);
}

public static class UserSecretPrompt
{
    public static IUserSecretPrompt? Current { get; set; }
}

/// <summary>
/// Protects secrets for the current OS user on this machine.
/// Windows: DPAPI CurrentUser. Unix: AES-GCM keyed by a 0600 file plus machine-id and user name.
/// </summary>
public static class UserSecretProtector
{
    public const string Prefix = "enc:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ScratchpadSharp.v1");

    public static bool IsProtected(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;
        if (IsProtected(plaintext))
            return plaintext;

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var sealedBytes = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)
            : EncryptAesGcm(bytes);
        return Prefix + Convert.ToBase64String(sealedBytes);
    }

    public static bool TryUnprotect(string? value, out string plaintext)
    {
        if (string.IsNullOrEmpty(value))
        {
            plaintext = string.Empty;
            return true;
        }

        if (!IsProtected(value))
        {
            plaintext = string.Empty;
            return false;
        }

        try
        {
            var sealedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            var bytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser)
                : DecryptAesGcm(sealedBytes);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    private static byte[] EncryptAesGcm(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(GetUnixKey(), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, nonce.Length);
        ciphertext.CopyTo(output, nonce.Length + tag.Length);
        return output;
    }

    private static byte[] DecryptAesGcm(byte[] sealedBytes)
    {
        if (sealedBytes.Length < 12 + 16)
            throw new CryptographicException("Ciphertext is too short.");

        var nonce = sealedBytes.AsSpan(0, 12);
        var tag = sealedBytes.AsSpan(12, 16);
        var ciphertext = sealedBytes.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(GetUnixKey(), 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] GetUnixKey()
    {
        AppPaths.EnsureUserDataDirectory();
        var path = AppPaths.UserSecretKeyPath;
        byte[] fileKey;
        if (!File.Exists(path))
        {
            fileKey = RandomNumberGenerator.GetBytes(32);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var fs = new FileStream(path, options);
            fs.Write(fileKey, 0, fileKey.Length);
        }
        else
            fileKey = File.ReadAllBytes(path);

        var salt = Encoding.UTF8.GetBytes(ReadMachineId() + "\0" + Environment.UserName);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, 32, salt, Entropy);
    }

    private static string ReadMachineId()
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            if (File.Exists(path))
            {
                var id = File.ReadAllText(path).Trim();
                if (id.Length > 0)
                    return id;
            }
        }

        return Environment.MachineName;
    }
}
