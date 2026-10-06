using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace Dustweave.Accounts;

internal static class SessionRegistry
{
    internal static SessionStatus GetStatus()
    {
        bool gameRunning = IsProcessRunning("BrownDust II");
        bool starterRunning = IsProcessRunning("Browndust2Starter");

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            SessionConstants.RegistrySubKey,
            writable: false);

        if (key is null)
        {
            return new SessionStatus(
                gameRunning,
                starterRunning,
                RegistryKeyPresent: false,
                Entries: [],
                Complete: false);
        }

        List<RegistryEntryStatus> entries = [];
        bool complete = true;
        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            object? value = key.GetValue(
                definition.RegistryName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                entries.Add(new RegistryEntryStatus(
                    definition.LogicalName,
                    Present: false,
                    Kind: null,
                    ByteLength: null,
                    Enabled: null));
                complete = false;
                continue;
            }

            RegistryValueKind kind;
            try
            {
                kind = key.GetValueKind(definition.RegistryName);
            }
            catch (IOException)
            {
                entries.Add(new RegistryEntryStatus(
                    definition.LogicalName,
                    Present: false,
                    Kind: null,
                    ByteLength: null,
                    Enabled: null));
                complete = false;
                continue;
            }

            byte[] encoded = EncodeRegistryValue(kind, value);
            bool? enabled = definition.MustBeEnabled
                ? kind == RegistryValueKind.DWord && value is int intValue && intValue == 1
                : null;

            bool entryValid = kind == definition.ExpectedKind
                && encoded.Length > 0
                && (!definition.MustBeEnabled || enabled == true);
            complete &= entryValid;
            entries.Add(new RegistryEntryStatus(
                definition.LogicalName,
                Present: true,
                Kind: kind.ToString(),
                ByteLength: encoded.Length,
                Enabled: enabled));
        }

        return new SessionStatus(
            gameRunning,
            starterRunning,
            RegistryKeyPresent: true,
            Entries: entries,
            Complete: complete);
    }

    internal static SessionSlot Capture(string alias)
    {
        SessionStatus status = GetStatus();
        if (status.GameRunning || status.StarterRunning)
        {
            throw new SessionManagerException("游戏或启动器仍在运行。请直接关闭游戏并退出启动器后再捕获。不要在游戏内注销账户。");
        }

        return ReadCurrent(alias);
    }

    internal static SessionSlot ReadCurrent(string alias)
    {
        SessionStatus status = GetStatus();

        if (!status.Complete)
        {
            throw new SessionManagerException("当前注册表会话不完整或自动登录未启用。");
        }

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            SessionConstants.RegistrySubKey,
            writable: false)
            ?? throw new SessionManagerException("未找到 BrownDust II 的当前用户会话注册表。 ");

        List<RegistryEntrySnapshot> entries = [];
        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            object value = key.GetValue(
                definition.RegistryName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?? throw new SessionManagerException($"捕获期间缺少“{definition.LogicalName}”，请重新检查游戏是否完全退出。");
            RegistryValueKind kind = key.GetValueKind(definition.RegistryName);
            byte[] encoded = EncodeRegistryValue(kind, value);
            try
            {
                entries.Add(new RegistryEntrySnapshot(
                    definition.RegistryName,
                    kind.ToString(),
                    Convert.ToBase64String(encoded)));
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded);
            }
        }

        SessionSlot slot = new(
            SessionConstants.SchemaVersion,
            alias,
            DateTimeOffset.UtcNow,
            SessionConstants.RegistrySubKey,
            entries);
        ValidateSlot(slot);
        return slot;
    }

    internal static bool HasPendingLauncherToken()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            SessionConstants.RegistrySubKey,
            writable: false);
        return key?.GetValueNames().Any(name =>
            name.StartsWith("StandaloneMemberAccessToken", StringComparison.OrdinalIgnoreCase)) == true;
    }

    internal static void WriteAndVerify(SessionSlot target)
    {
        ValidateSlot(target);
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            SessionConstants.RegistrySubKey,
            writable: true)
            ?? throw new SessionManagerException("未找到可写入的 BrownDust II 当前用户会话注册表。");

        Dictionary<string, RegistryEntrySnapshot> byName = target.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal);
        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            RegistryEntrySnapshot entry = byName[definition.RegistryName];
            RegistryValueKind kind = Enum.Parse<RegistryValueKind>(entry.Kind);
            byte[] data = Convert.FromBase64String(entry.DataBase64);
            try
            {
                key.SetValue(entry.Name, DecodeRegistryValue(kind, data), kind);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);
            }
        }

        key.Flush();
        VerifyRegistryMatches(key, target);
    }

    internal static void ClearAndVerify()
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            SessionConstants.RegistrySubKey,
            writable: true)
            ?? throw new SessionManagerException("未找到可写入的 BrownDust II 当前用户会话注册表。");

        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            key.DeleteValue(definition.RegistryName, throwOnMissingValue: false);
        }

        key.Flush();
        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            if (key.GetValue(
                    definition.RegistryName,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
            {
                throw new SessionManagerException("移除本机活动会话后的回读校验失败。");
            }
        }
    }

    internal static bool SessionsEqual(SessionSlot left, SessionSlot right)
    {
        ValidateSlot(left);
        ValidateSlot(right);
        Dictionary<string, RegistryEntrySnapshot> rightByName = right.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal);
        foreach (RegistryEntrySnapshot leftEntry in left.Entries)
        {
            RegistryEntrySnapshot rightEntry = rightByName[leftEntry.Name];
            if (!string.Equals(leftEntry.Kind, rightEntry.Kind, StringComparison.Ordinal))
            {
                return false;
            }

            byte[] leftData = Convert.FromBase64String(leftEntry.DataBase64);
            byte[] rightData = Convert.FromBase64String(rightEntry.DataBase64);
            try
            {
                if (!leftData.AsSpan().SequenceEqual(rightData))
                {
                    return false;
                }
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(leftData);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(rightData);
            }
        }

        return true;
    }

    internal static void ValidateSlot(SessionSlot slot)
    {
        if (slot.SchemaVersion != SessionConstants.SchemaVersion)
        {
            throw new SessionManagerException($"不支持的槽位格式版本：{slot.SchemaVersion}。");
        }

        if (!string.Equals(
                slot.RegistrySubKey,
                SessionConstants.RegistrySubKey,
                StringComparison.Ordinal))
        {
            throw new SessionManagerException("槽位的注册表目标与当前工具不匹配。");
        }

        Dictionary<string, RegistryEntrySnapshot> byName;
        try
        {
            byName = slot.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            throw new SessionManagerException("槽位包含重复的会话字段。");
        }

        if (byName.Count != SessionConstants.RequiredRegistryValues.Length)
        {
            throw new SessionManagerException("槽位字段数量不正确。");
        }

        foreach (SessionRegistryValue definition in SessionConstants.RequiredRegistryValues)
        {
            if (!byName.TryGetValue(definition.RegistryName, out RegistryEntrySnapshot? entry))
            {
                throw new SessionManagerException($"槽位缺少“{definition.LogicalName}”。");
            }

            if (!Enum.TryParse(entry.Kind, ignoreCase: false, out RegistryValueKind kind)
                || kind != definition.ExpectedKind)
            {
                throw new SessionManagerException($"槽位中“{definition.LogicalName}”的类型不正确。");
            }

            byte[] data;
            try
            {
                data = Convert.FromBase64String(entry.DataBase64);
            }
            catch (FormatException)
            {
                throw new SessionManagerException($"槽位中“{definition.LogicalName}”的数据已损坏。");
            }

            if (data.Length == 0)
            {
                throw new SessionManagerException($"槽位中“{definition.LogicalName}”为空。");
            }

            if (definition.MustBeEnabled
                && (data.Length != sizeof(int) || BitConverter.ToInt32(data, 0) != 1))
            {
                throw new SessionManagerException($"槽位中“{definition.LogicalName}”未启用。");
            }
        }
    }

    internal static byte[] EncodeRegistryValue(RegistryValueKind kind, object value)
    {
        return kind switch
        {
            RegistryValueKind.Binary when value is byte[] bytes => bytes.ToArray(),
            RegistryValueKind.DWord when value is int intValue => BitConverter.GetBytes(intValue),
            RegistryValueKind.QWord when value is long longValue => BitConverter.GetBytes(longValue),
            RegistryValueKind.String or RegistryValueKind.ExpandString when value is string text =>
                Encoding.UTF8.GetBytes(text),
            RegistryValueKind.MultiString when value is string[] values =>
                Encoding.UTF8.GetBytes(string.Join('\0', values)),
            _ => throw new SessionManagerException($"不支持的注册表值类型：{kind}。"),
        };
    }

    private static object DecodeRegistryValue(RegistryValueKind kind, byte[] data)
    {
        return kind switch
        {
            RegistryValueKind.Binary => data,
            RegistryValueKind.DWord when data.Length == sizeof(int) => BitConverter.ToInt32(data, 0),
            RegistryValueKind.QWord when data.Length == sizeof(long) => BitConverter.ToInt64(data, 0),
            RegistryValueKind.String or RegistryValueKind.ExpandString => Encoding.UTF8.GetString(data),
            RegistryValueKind.MultiString => Encoding.UTF8.GetString(data).Split('\0'),
            _ => throw new SessionManagerException($"无法还原注册表值类型：{kind}。"),
        };
    }

    private static void VerifyRegistryMatches(RegistryKey key, SessionSlot target)
    {
        foreach (RegistryEntrySnapshot entry in target.Entries)
        {
            object? actualValue = key.GetValue(
                entry.Name,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (actualValue is null)
            {
                throw new SessionManagerException("会话写入后的注册表回读缺少字段。");
            }

            RegistryValueKind actualKind = key.GetValueKind(entry.Name);
            if (!string.Equals(actualKind.ToString(), entry.Kind, StringComparison.Ordinal))
            {
                throw new SessionManagerException("会话写入后的注册表类型不一致。");
            }

            byte[] expectedData = Convert.FromBase64String(entry.DataBase64);
            byte[] actualData = EncodeRegistryValue(actualKind, actualValue);
            try
            {
                if (!expectedData.AsSpan().SequenceEqual(actualData))
                {
                    throw new SessionManagerException("会话写入后的注册表内容不一致。");
                }
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(expectedData);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(actualData);
            }
        }
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return true;
        }
    }
}
