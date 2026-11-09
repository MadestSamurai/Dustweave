using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Dustweave.Accounts;
using Microsoft.Win32;

namespace Dustweave;

// An explicit local acknowledgement, not a portable preference. Never include
// this registry value in account exports, cache migration or diagnostic bundles.
// It is not DRM: failure keeps the notice visible but cannot stop normal use.
public sealed class DailyFreeDistributionAcknowledgement
{
    private const string RegistryPath = @"Software\MadestSamurai\Dustweave\LocalConfirmations";
    private const string RegistryValue = "FreeDistributionV1";
    private const string Purpose = "Dustweave.free-distribution";
    private readonly Func<byte[]?> read;
    private readonly Action<byte[]> write;
    private readonly Func<string> fingerprint;
    private readonly Func<byte[], byte[]> protect, unprotect;
    private sealed record Receipt(string Purpose, int Revision, string Fingerprint, DateTimeOffset ConfirmedAt);

    public DailyFreeDistributionAcknowledgement() : this(ReadRegistry, WriteRegistry) { }

    // Isolated UI previews supply an in-memory store, never the user's registry.
    public DailyFreeDistributionAcknowledgement(Func<byte[]?> read, Action<byte[]> write)
        : this(read, write, MachineUserFingerprint, bytes => DpapiProtector.Protect(bytes), bytes => DpapiProtector.Unprotect(bytes)) { }

    internal DailyFreeDistributionAcknowledgement(Func<byte[]?> read, Action<byte[]> write,
        Func<string> fingerprint, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
    {
        this.read = read; this.write = write; this.fingerprint = fingerprint;
        this.protect = protect; this.unprotect = unprotect;
    }

    public static string Phrase(string language) => language switch {
        "en-US" => "I confirm", "zh-TW" => "我確認", _ => "我确认"
    };
    public static bool Matches(string? input, string language) => string.Equals(input?.Trim(), Phrase(language),
        language == "en-US" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public bool IsAcknowledged
    {
        get { try { return Valid(read()); } catch { return false; } }
    }

    private bool Valid(byte[]? value)
    {
        if (value is not { Length: > 0 and <= 8192 }) return false;
        var receipt = JsonSerializer.Deserialize<Receipt>(unprotect(value));
        return receipt is { Purpose: Purpose, Revision: 1 } && receipt.ConfirmedAt != default
            && !string.IsNullOrWhiteSpace(receipt.Fingerprint)
            && string.Equals(receipt.Fingerprint, fingerprint(), StringComparison.Ordinal);
    }

    public void Confirm(string? input, string language)
    {
        if (!Matches(input, language)) throw new ArgumentException("distribution.confirm_invalid");
        var identity = fingerprint();
        if (string.IsNullOrWhiteSpace(identity)) throw new IOException("distribution.save_failed");
        byte[] value = protect(JsonSerializer.SerializeToUtf8Bytes(new Receipt(Purpose, 1, identity, DateTimeOffset.UtcNow)));
        if (value.Length is 0 or > 8192) throw new IOException("distribution.save_failed");
        write(value);
        // UI may hide only after persistent storage was flushed, read again and
        // authenticated. A denied write, stale read or corrupt record is retryable.
        byte[]? persisted = read();
        if (persisted == null || !value.AsSpan().SequenceEqual(persisted) || !Valid(persisted))
            throw new IOException("distribution.save_failed");
    }

    private static byte[]? ReadRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        return key?.GetValue(RegistryValue) as byte[];
    }
    private static void WriteRegistry(byte[] value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
        key.SetValue(RegistryValue, value, RegistryValueKind.Binary);
        key.Flush();
    }
    private static string MachineUserFingerprint()
    {
        // DPAPI CurrentUser protects integrity and binds the user profile. The
        // additional installation identity rejects copying/roaming to another PC.
        using var local = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = local.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        string? machine = key?.GetValue("MachineGuid") as string;
        using var identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        if (!Guid.TryParse(machine, out var guid) || string.IsNullOrWhiteSpace(sid))
            throw new IOException("distribution.save_failed");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Purpose + "\n" + guid.ToString("D") + "\n" + sid)));
    }
}
