namespace ProtocolLens.Core.Model;

/// <summary>
/// DHCP message types (RFC 2131 section 3, option 53 values).
/// </summary>
public enum DhcpMessageType
{
    Unknown = 0,
    Discover = 1,
    Offer = 2,
    Request = 3,
    Decline = 4,
    Ack = 5,
    Nak = 6,
    Release = 7,
    Inform = 8,
}

/// <summary>
/// A single normalized, immutable DHCP observation extracted from one captured frame.
/// This is the "evidence" unit: everything downstream (reconstruction, analysis) must be
/// explainable by tracing back to one or more <see cref="DhcpObservation"/> instances.
///
/// Field choices follow RFC 2131 (DHCP), RFC 2132 (options), RFC 3396 (concatenation),
/// RFC 4361 (client identifier), and RFC 6842 (client identifier echoing).
/// </summary>
public sealed record DhcpObservation
{
    /// <summary>Original 1-based frame number from the whole capture (not just DHCP frames),
    /// matching common capture-tool conventions (e.g. Wireshark frame.number).</summary>
    public required long FrameNumber { get; init; }

    /// <summary>Capture timestamp (UTC). Precision/resolution depends on the source capture.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    public required DhcpMessageType MessageType { get; init; }

    public required uint TransactionId { get; init; }

    /// <summary>BOOTP chaddr. NOT assumed to be the client identity on its own - a relay's
    /// Ethernet source address is a different thing and must not be substituted here.</summary>
    public required string ClientHardwareAddress { get; init; }

    /// <summary>Option 61 (Client Identifier), if present. Opaque bytes, rendered as hex.
    /// Per RFC 4361 this need not be a MAC address at all.</summary>
    public string? ClientIdentifierHex { get; init; }

    /// <summary>Option 54 (Server Identifier), if present.</summary>
    public string? ServerIdentifier { get; init; }

    /// <summary>ciaddr - set by the client in RENEW/REBIND/RELEASE when it already has a lease.</summary>
    public string? ClientIpAddress { get; init; }

    /// <summary>yiaddr - address offered/assigned by the server.</summary>
    public string? YourIpAddress { get; init; }

    /// <summary>giaddr - relay agent address. Non-zero implies this observation passed through
    /// a relay, which changes how identity evidence should be interpreted (A14).</summary>
    public string? RelayAgentAddress { get; init; }

    /// <summary>Option 50 (Requested IP Address), if present (used in REQUEST/INIT-REBOOT).</summary>
    public string? RequestedIpAddress { get; init; }

    public string SourceIp { get; init; } = string.Empty;
    public string DestinationIp { get; init; } = string.Empty;
    public int SourcePort { get; init; }
    public int DestinationPort { get; init; }

    /// <summary>Non-fatal decode diagnostics for this observation (e.g. unknown/overloaded
    /// options, truncated option data). Never silently dropped.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    /// <summary>True when this observation arrived through a relay (giaddr != 0.0.0.0).</summary>
    public bool IsRelayed => !string.IsNullOrEmpty(RelayAgentAddress) && RelayAgentAddress != "0.0.0.0";

    /// <summary>
    /// Normalized opaque client identifier (option 61), or null when absent/empty. This is
    /// the strongest identity evidence (RFC 4361) and is compared as opaque bytes, never
    /// assumed to be a MAC address.
    /// </summary>
    public string? NormalizedClientId =>
        ClientIdentifierHex is { Length: > 0 } cid ? cid.Trim().ToLowerInvariant() : null;

    /// <summary>
    /// Normalized BOOTP chaddr (hardware address), or null when absent/unusable
    /// (e.g. all-zero). IMPORTANT: chaddr remains the client's own hardware address even
    /// when the observation was relayed (RFC 2131 - giaddr identifies the relay; chaddr is
    /// untouched by relaying). It must NOT be suppressed just because <see cref="IsRelayed"/>
    /// is true (see audit finding M2b) - only the Ethernet source address (not modeled here)
    /// would be the relay's own address.
    /// </summary>
    public string? NormalizedHardwareAddress =>
        !string.IsNullOrWhiteSpace(ClientHardwareAddress) && ClientHardwareAddress != "00:00:00:00:00:00"
            ? ClientHardwareAddress.Trim().ToLowerInvariant()
            : null;

    /// <summary>
    /// A single display string summarizing the identity evidence used for this observation,
    /// for logging/UI only. NOT used for equality/grouping decisions - see
    /// <see cref="Reconstruction.ConversationReconstructor"/> for the actual (non-transitive)
    /// compatibility-based matching that replaced simple string equality.
    /// </summary>
    public string? IdentityEvidence =>
        NormalizedClientId is { } cid ? $"cid:{cid}" : (NormalizedHardwareAddress is { } mac ? $"mac:{mac}" : null);
}
