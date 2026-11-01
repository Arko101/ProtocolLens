namespace ProtocolLens.Core.Model;

/// <summary>
/// A reconstructed DHCP exchange: one or more related observations sharing a transaction
/// and compatible identity evidence. Distinct from "flow classification" (see
/// <see cref="DhcpFlow"/>) and from "finding" (see Analysis.Finding) - this type only
/// records *what was grouped together and why*, never a correctness judgement.
/// </summary>
public sealed record Conversation
{
    /// <summary>Stable, deterministic identifier derived from transaction id + identity
    /// evidence + first frame number. Stable across repeated runs on the same input (A24).</summary>
    public required string ConversationId { get; init; }

    public required uint TransactionId { get; init; }

    /// <summary>Identity evidence shared by the observations in this conversation, or null
    /// if the conversation was formed without usable identity evidence (rare, logged).</summary>
    public string? IdentityEvidence { get; init; }

    /// <summary>Observations belonging to this conversation, in capture order.</summary>
    public required IReadOnlyList<DhcpObservation> Observations { get; init; }

    /// <summary>Explanation of why these observations were grouped (for audit/debugging).</summary>
    public string GroupingRationale { get; init; } = string.Empty;
}

/// <summary>
/// An observation that could not be confidently assigned to a conversation - e.g. conflicting
/// identity evidence, or a transaction id reused in a way that could not be disambiguated.
/// Per the evidence rules, these must never be force-assigned to the "first candidate".
/// </summary>
public sealed record UnresolvedObservation
{
    public required DhcpObservation Observation { get; init; }
    public required string Reason { get; init; }
}
