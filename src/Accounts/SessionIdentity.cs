using System.Text;
using System.Globalization;
using System.Text.Json;

namespace Dustweave.Accounts;

internal static class SessionIdentity
{
    internal static string? GetMemberId(SessionSlot slot)
    {
        RegistryEntrySnapshot? memberEntry = slot.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.Name,
                "neon_auth_member_h1293550423",
                StringComparison.Ordinal));
        if (memberEntry is null)
        {
            return null;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(memberEntry.DataBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            string json = Encoding.UTF8.GetString(data).TrimEnd('\0');
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("member_id", out JsonElement memberId))
            {
                return null;
            }

            string digits = memberId.ValueKind switch
            {
                JsonValueKind.Number => memberId.GetRawText(),
                JsonValueKind.String => memberId.GetString() ?? string.Empty,
                _ => string.Empty,
            };
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
                ? id.ToString(CultureInfo.InvariantCulture) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);
        }
    }

    internal static string? GetMaskedMemberId(SessionSlot slot)
    {
        string? memberId = GetMemberId(slot);
        if (string.IsNullOrEmpty(memberId))
        {
            return null;
        }

        int visibleLength = Math.Min(6, memberId.Length);
        return "成员 ID •••• " + memberId[^visibleLength..];
    }
}
