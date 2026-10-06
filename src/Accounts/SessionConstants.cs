using Microsoft.Win32;

namespace Dustweave.Accounts;

internal static class SessionConstants
{
    internal const int SchemaVersion = 1;
    internal const int FixedSlotCount = 100;
    internal const string RegistrySubKey = @"Software\Gamfs\BrownDust II";
    internal const string VaultDirectoryName = "BD2AccountSessionManager";
    internal const string SlotDirectoryName = "slots";
    internal const string RecoveryDirectoryName = "recovery";
    internal const string SlotExtension = ".bd2slot";
    internal const string RecoveryFileName = "latest.bd2recovery";

    internal static readonly byte[] FileMagic = "BD2SLOT1"u8.ToArray();
    internal static readonly byte[] DpapiEntropy =
        "BD2AccountSessionManager/v1/current-user"u8.ToArray();

    internal static readonly SessionRegistryValue[] RequiredRegistryValues =
    [
        new("会话访问凭据", "neon_access_token_h1862384816", RegistryValueKind.Binary, false),
        new("账户身份凭据", "neon_auth_member_h1293550423", RegistryValueKind.Binary, false),
        new("登录提供方", "neon_auth_provider_h1524829830", RegistryValueKind.Binary, false),
        new("自动登录", "IsAutoLogin_h601540819", RegistryValueKind.DWord, true),
        new("PC 自动登录", "StandaloneAutoLogin_h1463356620", RegistryValueKind.DWord, true),
    ];
}

internal sealed record SessionRegistryValue(
    string LogicalName,
    string RegistryName,
    RegistryValueKind ExpectedKind,
    bool MustBeEnabled);
