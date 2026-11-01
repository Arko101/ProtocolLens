namespace ProtocolLens.Core.Analysis;

public enum FindingSeverity
{
    Info,
    Notice,
    Warning
}

public enum FindingCategory
{
    FlowProgress,
    FlowOutcome,
    Correlation,
    CaptureLimitation
}

/// <summary>
/// A single structured finding. Deliberately verbose/explicit per the project's evidence
/// discipline: every field that could blur "observed" vs "inferred" vs "missing" is kept
/// separate so the UI/API cannot collapse them into one misleading status.
/// </summary>
public sealed record Finding
{
    /// <summary>Stable rule code, e.g. "DHCP-MISSING-OFFER". Documented in the rule catalog.</summary>
    public required string RuleCode { get; init; }

    public required FindingCategory Category { get; init; }
    public required FindingSeverity Severity { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>The flow this finding was evaluated against. The same rule may be inapplicable
    /// for a different flow - see <see cref="NotEvaluatedReason"/>.</summary>
    public required Analysis.DhcpFlow Flow { get; init; }

    /// <summary>Human-readable statement of what protocol behavior was expected, and why
    /// (e.g. "RFC 2131 4.3.2: a REQUEST should be answered with ACK or NAK").</summary>
    public required string Expected { get; init; }

    /// <summary>Frame numbers that constitute the observed evidence for this finding.</summary>
    public IReadOnlyList<long> ObservedFrames { get; init; } = Array.Empty<long>();

    /// <summary>Explicit statement of what was NOT observed, qualified by the analyzed
    /// interval - never phrased as a definitive network failure (see section 3 evidence rules).</summary>
    public string? MissingObservation { get; init; }

    public required string Explanation { get; init; }

    /// <summary>Hypotheses are explicitly inferences, never fact.</summary>
    public IReadOnlyList<string> Hypotheses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NextChecks { get; init; } = Array.Empty<string>();

    /// <summary>Non-null only for rules that could not be evaluated due to insufficient
    /// evidence - these must not be presented as accusations (section 8).</summary>
    public string? NotEvaluatedReason { get; init; }
}
