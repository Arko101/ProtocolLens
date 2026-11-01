namespace ProtocolLens.Api.Models;

/// <summary>Explicit typed API contract for a completed (or partially completed) analysis.
/// Deliberately does not expose any SharpPcap/PacketDotNet types.</summary>
public sealed record AnalysisResponse
{
    public required string SchemaVersion { get; init; }
    public required string AnalyzerVersion { get; init; }

    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }

    /// <summary>Total frames read from the capture (DHCP and non-DHCP).</summary>
    public required long TotalFramesRead { get; init; }

    /// <summary>True only if the reader reached a genuine end-of-file for this capture.</summary>
    public required bool ReachedEndOfFile { get; init; }

    /// <summary>Non-fatal capture/parse diagnostics. A non-empty list means some findings
    /// below may be qualified/limited - see each finding's own fields.</summary>
    public required IReadOnlyList<CaptureDiagnosticDto> Diagnostics { get; init; }

    public required IReadOnlyList<ConversationDto> Conversations { get; init; }
    public required IReadOnlyList<UnresolvedObservationDto> UnresolvedObservations { get; init; }
    public required IReadOnlyList<FindingDto> Findings { get; init; }
}

public sealed record CaptureDiagnosticDto
{
    public long? FrameNumber { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record DhcpObservationDto
{
    public required long FrameNumber { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string MessageType { get; init; }
    public required string TransactionId { get; init; }
    public string? ClientHardwareAddress { get; init; }
    public string? ClientIdentifierHex { get; init; }
    public string? ServerIdentifier { get; init; }
    public string? ClientIpAddress { get; init; }
    public string? YourIpAddress { get; init; }
    public string? RelayAgentAddress { get; init; }
    public string? RequestedIpAddress { get; init; }
    public required string SourceIp { get; init; }
    public required string DestinationIp { get; init; }
    public required int SourcePort { get; init; }
    public required int DestinationPort { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
}

public sealed record ConversationDto
{
    public required string ConversationId { get; init; }
    public required string TransactionId { get; init; }
    public string? IdentityEvidence { get; init; }
    public required string Flow { get; init; }
    public required string GroupingRationale { get; init; }
    public required IReadOnlyList<DhcpObservationDto> Observations { get; init; }
}

public sealed record UnresolvedObservationDto
{
    public required DhcpObservationDto Observation { get; init; }
    public required string Reason { get; init; }
}

public sealed record FindingDto
{
    public required string RuleCode { get; init; }
    public required string Category { get; init; }
    public required string Severity { get; init; }
    public required string ConversationId { get; init; }
    public required string Flow { get; init; }
    public required string Expected { get; init; }
    public required IReadOnlyList<long> ObservedFrames { get; init; }
    public string? MissingObservation { get; init; }
    public required string Explanation { get; init; }
    public required IReadOnlyList<string> Hypotheses { get; init; }
    public required IReadOnlyList<string> NextChecks { get; init; }
    public string? NotEvaluatedReason { get; init; }
}
