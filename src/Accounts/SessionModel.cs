using System.Text.Json.Serialization;

namespace BD2AccountSessionManager;

internal sealed record SessionSlot(
    int SchemaVersion,
    string Alias,
    DateTimeOffset CapturedAtUtc,
    string RegistrySubKey,
    IReadOnlyList<RegistryEntrySnapshot> Entries);

internal sealed record RegistryEntrySnapshot(
    string Name,
    string Kind,
    string DataBase64);

internal sealed record SessionStatus(
    bool GameRunning,
    bool StarterRunning,
    bool RegistryKeyPresent,
    IReadOnlyList<RegistryEntryStatus> Entries,
    bool Complete);

internal sealed record RegistryEntryStatus(
    string LogicalName,
    bool Present,
    string? Kind,
    int? ByteLength,
    bool? Enabled);

internal sealed record SlotSummary(
    string Alias,
    DateTimeOffset CapturedAtUtc,
    string Fingerprint,
    string FilePath,
    bool Valid,
    string? Error);

internal sealed record ActivationResult(
    string Alias,
    string Fingerprint,
    bool Changed,
    DateTimeOffset? RecoveryCapturedAtUtc);

internal sealed record FixedSlotState(
    int Number,
    bool Occupied,
    bool Valid,
    string? DisplayName,
    DateTimeOffset? CapturedAtUtc,
    string? Fingerprint,
    string? MaskedMemberId,
    bool IsCurrent,
    string? Error);

internal sealed record SessionDashboardState(
    SessionStatus Status,
    bool CurrentSessionComplete,
    int? CurrentSlotNumber,
    string CurrentDisplayName,
    string? CurrentMaskedMemberId,
    string? CurrentFingerprint,
    bool HasRecovery,
    IReadOnlyList<FixedSlotState> Slots);

[JsonSerializable(typeof(SessionSlot))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class SessionJsonContext : JsonSerializerContext;
