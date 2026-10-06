using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Accounts;

internal sealed class SessionVault
{
    private readonly string _slotDirectory;

    internal SessionVault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new SessionManagerException("无法确定当前 Windows 用户的 LocalAppData 目录。");
        }

        RootDirectory = Path.Combine(localAppData, SessionConstants.VaultDirectoryName);
        _slotDirectory = Path.Combine(RootDirectory, SessionConstants.SlotDirectoryName);
    }

    internal string RootDirectory { get; }

    internal bool HasRecovery => File.Exists(Path.Combine(
        RootDirectory,
        SessionConstants.RecoveryDirectoryName,
        SessionConstants.RecoveryFileName));

    internal SlotSummary Save(SessionSlot slot, bool replace)
    {
        ValidateAlias(slot.Alias);
        SessionRegistry.ValidateSlot(slot);
        Directory.CreateDirectory(_slotDirectory);

        string path = GetSlotPath(slot.Alias);
        if (File.Exists(path) && !replace)
        {
            throw new SessionManagerException($"槽位“{slot.Alias}”已存在。若确认覆盖，请追加 --replace。");
        }

        return WriteAndVerify(slot, path);
    }

    internal SlotSummary SaveRecovery(SessionSlot current)
    {
        SessionSlot recovery = current with { Alias = "automatic-recovery" };
        SessionRegistry.ValidateSlot(recovery);
        string recoveryDirectory = Path.Combine(RootDirectory, SessionConstants.RecoveryDirectoryName);
        Directory.CreateDirectory(recoveryDirectory);
        string path = Path.Combine(recoveryDirectory, SessionConstants.RecoveryFileName);
        return WriteAndVerify(recovery, path);
    }

    internal SessionSlot LoadRecovery()
    {
        string path = Path.Combine(
            RootDirectory,
            SessionConstants.RecoveryDirectoryName,
            SessionConstants.RecoveryFileName);
        if (!File.Exists(path))
        {
            throw new SessionManagerException("尚无自动恢复副本。");
        }

        return LoadFromPath(path);
    }

    internal string Fingerprint(SessionSlot slot)
    {
        SessionRegistry.ValidateSlot(slot);
        return Summarize(slot, string.Empty).Fingerprint;
    }

    internal SlotSummary SaveFixedSlot(
        int slotNumber,
        SessionSlot session,
        string displayName,
        bool replace)
    {
        ValidateSlotNumber(slotNumber);
        ValidateAlias(displayName);
        SessionSlot namedSession = session with
        {
            Alias = displayName.Trim(),
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };
        SessionRegistry.ValidateSlot(namedSession);
        string path = GetFixedSlotPath(slotNumber);
        if (File.Exists(path) && !replace)
        {
            throw new SessionManagerException($"账户槽位 {slotNumber} 已被占用。");
        }

        return WriteAndVerify(namedSession, path);
    }

    internal SessionSlot LoadFixedSlot(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        string path = GetFixedSlotPath(slotNumber);
        if (!File.Exists(path))
        {
            throw new SessionManagerException($"账户槽位 {slotNumber} 为空。");
        }

        return LoadFromPath(path);
    }

    internal SessionSlot? TryLoadFixedSlot(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        string path = GetFixedSlotPath(slotNumber);
        return File.Exists(path) ? LoadFromPath(path) : null;
    }

    internal bool FixedSlotExists(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        return File.Exists(GetFixedSlotPath(slotNumber));
    }

    internal SlotSummary RenameFixedSlot(int slotNumber, string displayName)
    {
        SessionSlot existing = LoadFixedSlot(slotNumber);
        return SaveFixedSlot(slotNumber, existing, displayName, replace: true);
    }

    internal void DeleteFixedSlot(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        string path = GetFixedSlotPath(slotNumber);
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        if (File.Exists(path))
        {
            throw new SessionManagerException($"账户槽位 {slotNumber} 删除后的回读校验失败。");
        }
    }

    internal SlotSummary MigrateLegacySlot(
        string legacyAlias,
        int slotNumber,
        string displayName)
    {
        ValidateSlotNumber(slotNumber);
        string legacyPath = GetSlotPath(legacyAlias);
        SessionSlot legacy = Load(legacyAlias);
        SlotSummary migrated = SaveFixedSlot(
            slotNumber,
            legacy,
            displayName,
            replace: false);
        File.Delete(legacyPath);
        return migrated;
    }

    private SlotSummary WriteAndVerify(SessionSlot slot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new SessionManagerException("无法确定槽位目录。"));

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(slot, SessionJsonContext.Default.SessionSlot);
        byte[] ciphertext = DpapiProtector.Protect(plaintext);
        byte[] fileBytes = new byte[SessionConstants.FileMagic.Length + ciphertext.Length];
        SessionConstants.FileMagic.CopyTo(fileBytes, 0);
        ciphertext.CopyTo(fileBytes, SessionConstants.FileMagic.Length);

        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        string? backupPath = null;
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(fileBytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                backupPath = path + ".bak-" + Guid.NewGuid().ToString("N");
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            SessionSlot verified = LoadFromPath(path);
            byte[] verifiedPlaintext = JsonSerializer.SerializeToUtf8Bytes(
                verified,
                SessionJsonContext.Default.SessionSlot);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(plaintext),
                    SHA256.HashData(verifiedPlaintext)))
            {
                throw new SessionManagerException("槽位写入后的回读校验不一致。");
            }

            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            return Summarize(verified, path);
        }
        catch
        {
            if (backupPath is not null && File.Exists(backupPath))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Replace(backupPath, path, null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(backupPath, path);
                    }
                }
                catch
                {
                    throw new SessionManagerException(
                        "加密槽位更新失败，且旧槽位无法自动放回原路径；备份文件已保留在本机槽位目录中。");
                }
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(fileBytes);
        }
    }

    internal SessionSlot Load(string alias)
    {
        ValidateAlias(alias);
        string path = GetSlotPath(alias);
        if (!File.Exists(path))
        {
            throw new SessionManagerException($"未找到槽位“{alias}”。");
        }

        SessionSlot slot = LoadFromPath(path);
        if (!string.Equals(slot.Alias, alias, StringComparison.OrdinalIgnoreCase))
        {
            throw new SessionManagerException("槽位文件名与内部别名不匹配。");
        }

        return slot;
    }

    internal IReadOnlyList<SlotSummary> List()
    {
        if (!Directory.Exists(_slotDirectory))
        {
            return [];
        }

        List<SlotSummary> summaries = [];
        foreach (string path in Directory.EnumerateFiles(
                     _slotDirectory,
                     "*" + SessionConstants.SlotExtension,
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                SessionSlot slot = LoadFromPath(path);
                summaries.Add(Summarize(slot, path));
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.ComponentModel.Win32Exception
                                               or CryptographicException
                                               or InvalidDataException
                                               or JsonException
                                               or SessionManagerException)
            {
                summaries.Add(new SlotSummary(
                    Alias: "（无法读取）",
                    CapturedAtUtc: DateTimeOffset.MinValue,
                    Fingerprint: "-",
                    FilePath: path,
                    Valid: false,
                    Error: SanitizeError(exception)));
            }
        }

        return summaries
            .OrderBy(summary => summary.Alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal SlotSummary Validate(string alias)
    {
        SessionSlot slot = Load(alias);
        return Summarize(slot, GetSlotPath(alias));
    }

    internal static void SelfTest()
    {
        ValidateSlotNumber(1);
        ValidateSlotNumber(SessionConstants.FixedSlotCount);
        foreach (int invalid in new[] { 0, SessionConstants.FixedSlotCount + 1 })
        {
            bool rejected = false;
            try
            {
                ValidateSlotNumber(invalid);
            }
            catch (SessionManagerException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new SessionManagerException("账户槽位边界自检未能拒绝非法编号。");
            }
        }

        byte[] plaintext = RandomNumberGenerator.GetBytes(128);
        byte[] ciphertext = DpapiProtector.Protect(plaintext);
        byte[] roundTrip = DpapiProtector.Unprotect(ciphertext);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(plaintext, roundTrip))
            {
                throw new SessionManagerException("DPAPI 内存回环测试不一致。");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(roundTrip);
        }
    }

    private SessionSlot LoadFromPath(string path)
    {
        byte[] fileBytes = File.ReadAllBytes(path);
        if (fileBytes.Length <= SessionConstants.FileMagic.Length
            || !fileBytes.AsSpan(0, SessionConstants.FileMagic.Length)
                .SequenceEqual(SessionConstants.FileMagic))
        {
            throw new SessionManagerException("槽位文件头无效。");
        }

        byte[] ciphertext = fileBytes.AsSpan(SessionConstants.FileMagic.Length).ToArray();
        byte[] plaintext = DpapiProtector.Unprotect(ciphertext);
        try
        {
            SessionSlot slot = JsonSerializer.Deserialize(
                plaintext,
                SessionJsonContext.Default.SessionSlot)
                ?? throw new SessionManagerException("槽位内容为空。");
            ValidateAlias(slot.Alias);
            SessionRegistry.ValidateSlot(slot);
            return slot;
        }
        catch (JsonException)
        {
            throw new SessionManagerException("槽位 JSON 内容已损坏。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileBytes);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private SlotSummary Summarize(SessionSlot slot, string path)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RegistryEntrySnapshot entry in slot.Entries.OrderBy(
                     entry => entry.Name,
                     StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Name));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Kind));
            hash.AppendData([0]);
            byte[] data = Convert.FromBase64String(entry.DataBase64);
            try
            {
                hash.AppendData(data);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(data);
            }

            hash.AppendData([0]);
        }

        string fingerprint = Convert.ToHexString(hash.GetHashAndReset())[..12];
        return new SlotSummary(
            slot.Alias,
            slot.CapturedAtUtc,
            fingerprint,
            path,
            Valid: true,
            Error: null);
    }

    private string GetSlotPath(string alias)
    {
        string normalized = alias.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        string fileId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_slotDirectory, fileId + SessionConstants.SlotExtension);
    }

    private string GetFixedSlotPath(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        return Path.Combine(_slotDirectory, $"slot-{slotNumber}{SessionConstants.SlotExtension}");
    }

    private static void ValidateSlotNumber(int slotNumber)
    {
        if (slotNumber is < 1 or > SessionConstants.FixedSlotCount)
        {
            throw new SessionManagerException(
                $"账户槽位编号必须为 1–{SessionConstants.FixedSlotCount}。");
        }
    }

    private static void ValidateAlias(string alias)
    {
        string trimmed = alias.Trim();
        if (trimmed.Length is < 1 or > 40)
        {
            throw new SessionManagerException("槽位名称长度必须为 1–40 个字符。");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new SessionManagerException("槽位名称不能包含控制字符。");
        }
    }

    private static string SanitizeError(Exception exception)
    {
        return exception switch
        {
            SessionManagerException => exception.Message,
            UnauthorizedAccessException => "没有权限读取槽位。",
            IOException => "槽位文件读取失败。",
            System.ComponentModel.Win32Exception => "槽位无法由当前 Windows 用户解密。",
            CryptographicException => "槽位解密失败。",
            JsonException => "槽位内容无法解析。",
            _ => "槽位校验失败。",
        };
    }
}
