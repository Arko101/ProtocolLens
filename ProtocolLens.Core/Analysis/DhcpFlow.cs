namespace ProtocolLens.Core.Analysis;

/// <summary>
/// Inferred DHCP flow for a conversation. This is an INFERENCE over observed message types,
/// never itself an observation. A conversation whose evidence does not clearly match one
/// of the named flows is classified as <see cref="Unknown"/> - this is a legitimate,
/// expected outcome, not a defect in the analyzer.
/// </summary>
public enum DhcpFlow
{
    /// <summary>Insufficient or ambiguous evidence to classify with confidence.</summary>
    Unknown,

    /// <summary>DISCOVER -> OFFER(s) -> REQUEST -> ACK/NAK, client has no prior address.</summary>
    InitialAllocation,

    /// <summary>Client has ciaddr set, sends REQUEST directly (no DISCOVER) after reboot,
    /// per RFC 2131 4.3.2 (INIT-REBOOT).</summary>
    InitReboot,

    /// <summary>Client unicasts REQUEST to its known lease server with ciaddr set (RENEWING).</summary>
    Renewing,

    /// <summary>Client broadcasts REQUEST with ciaddr set after renewal failed (REBINDING).</summary>
    Rebinding,

    /// <summary>INFORM/ACK - client already has an address, requests only configuration.</summary>
    Inform,

    /// <summary>Client-initiated RELEASE - no response is expected by protocol.</summary>
    Release,

    /// <summary>Client-initiated DECLINE - no response is expected by protocol.</summary>
    Decline,
}
