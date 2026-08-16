using System.Text.Json.Serialization;

namespace ClassTranscriber.Api.Contracts;

public sealed class SystemCapabilitiesDto
{
    public required string CollectedAtUtc { get; init; }
    public required HostedProviderCapabilityDto[] HostedProviders { get; init; }
    public required ComputeBackendCapabilityDto[] ComputeBackends { get; init; }
    public required string Architecture { get; init; }
    public required int LogicalProcessorCount { get; init; }
    public required string OsDescription { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? HardwareName { get; init; }
}

public sealed record HostedProviderCapabilityDto(
    string Provider,
    bool Configured,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? Reachable,
    string Status);

public sealed record ComputeBackendCapabilityDto(
    string Backend,
    bool Available,
    string[] Devices,
    string Status);
