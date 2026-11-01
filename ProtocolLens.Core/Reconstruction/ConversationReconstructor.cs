using ProtocolLens.Core.Model;

namespace ProtocolLens.Core.Reconstruction;

/// <summary>
/// Deterministically groups normalized <see cref="DhcpObservation"/>s into conversations.
///
/// Policy (documented heuristic, not a protocol guarantee - see A06/A16):
/// - Primary key: TransactionId.
/// - Within a transaction id, observations are split into separate attempt-groups using
///   pairwise identity *compatibility* (see <see cref="AreCompatible"/>), not naive string
///   equality of a single combined identity field. Compatibility is evaluated against every
///   existing member of a candidate group, so a reply lacking a client identifier cannot
///   silently bridge two groups that are otherwise incompatible (e.g. same hardware address,
///   different client identifiers) - see counterexample in Stage 3 instructions.
/// - When an observation has no usable identity evidence at all (no client id, no usable
///   hardware address), it is attached to a same-transaction-id group only if there is
///   exactly one candidate group that is compatible with it *and* within the attempt-gap
///   window; otherwise it is reported unresolved rather than guessed.
/// - The same client legitimately reusing a transaction id for a brand new exchange
///   (A06) is approximated via a bounded inactivity gap: if the time since the last
///   observation in a candidate group exceeds <see cref="AttemptGapThreshold"/>, that group
///   is not considered a valid candidate and a new attempt/conversation is started. This is
///   an explicit implementation heuristic, not a protocol fact, and is documented as such.
/// </summary>
public sealed class ConversationReconstructor
{
    /// <summary>Documented heuristic threshold, not a protocol guarantee (see RFC 2131 timing
    /// is left to implementation). An observation further apart than this from every member of
    /// an otherwise-compatible candidate group is treated as a separate attempt.</summary>
    public TimeSpan AttemptGapThreshold { get; init; } = TimeSpan.FromSeconds(30);

    public ReconstructionResult Reconstruct(IEnumerable<DhcpObservation> observations)
    {
        var ordered = observations
            .OrderBy(o => o.FrameNumber)
            .ToList();

        var byTxId = ordered.GroupBy(o => o.TransactionId);

        var conversations = new List<Conversation>();
        var unresolved = new List<UnresolvedObservation>();

        foreach (var txGroup in byTxId)
        {
            var members = txGroup.ToList();
            var withIdentity = members.Where(o => o.NormalizedClientId is not null || o.NormalizedHardwareAddress is not null).ToList();
            var withoutIdentity = members.Where(o => o.NormalizedClientId is null && o.NormalizedHardwareAddress is null).ToList();

            var groups = new List<List<DhcpObservation>>();
            var ambiguousWithIdentity = new List<DhcpObservation>();
            foreach (var obs in withIdentity)
            {
                // Find candidate groups fully compatible (every member) and within the time gap
                // of their most recent member. Never pick the "first" match arbitrarily when
                // more than one candidate qualifies - that would hide a genuine ambiguity.
                var candidates = groups
                    .Where(g => g.All(m => AreCompatible(m, obs)) && obs.Timestamp - g[^1].Timestamp <= AttemptGapThreshold)
                    .ToList();

                if (candidates.Count == 1)
                {
                    candidates[0].Add(obs);
                }
                else if (candidates.Count == 0)
                {
                    groups.Add(new List<DhcpObservation> { obs });
                }
                else
                {
                    // Multiple compatible candidates: genuinely ambiguous, must not guess.
                    ambiguousWithIdentity.Add(obs);
                }
            }

            foreach (var obs in ambiguousWithIdentity)
            {
                unresolved.Add(new UnresolvedObservation
                {
                    Observation = obs,
                    Reason = "Identity evidence is compatible with multiple candidate conversations sharing this transaction id within the attempt window; cannot disambiguate."
                });
            }

            // Observations with no identity evidence at all: attach only if exactly one
            // candidate group is both compatible (vacuously true - no evidence to conflict)
            // and within the attempt-gap window; otherwise unresolved (never guess - A16).
            foreach (var obs in withoutIdentity)
            {
                var candidates = groups.Where(g => obs.Timestamp - g[^1].Timestamp <= AttemptGapThreshold).ToList();

                if (candidates.Count == 1)
                {
                    candidates[0].Add(obs);
                }
                else
                {
                    unresolved.Add(new UnresolvedObservation
                    {
                        Observation = obs,
                        Reason = candidates.Count == 0
                            ? "No identity evidence available and no compatible, time-adjacent candidate conversation shares this transaction id."
                            : "No identity evidence available and multiple candidate conversations share this transaction id within the attempt window; cannot disambiguate."
                    });
                }
            }

            foreach (var group in groups)
            {
                conversations.Add(BuildConversation(txGroup.Key, group,
                    "Grouped by shared transaction id and mutually compatible identity evidence (client identifier and/or hardware address)."));
            }
        }

        return new ReconstructionResult
        {
            Conversations = conversations.OrderBy(c => c.Observations[0].FrameNumber).ToList(),
            Unresolved = unresolved
        };
    }

    /// <summary>
    /// Two observations are compatible identity-wise if neither available field contradicts
    /// the other. This is intentionally NOT a transitive equivalence: compatibility is always
    /// checked against every existing member of a candidate group (see caller), so a message
    /// lacking a client identifier cannot bridge two members whose own identifiers conflict.
    /// Counterexample handled: A(cid=X, mac=M) and B(cid=Y, mac=M) must never end up in the
    /// same group merely because a reply C has mac=M but no cid - C is compatible with both A
    /// and B individually, but A and B are not compatible with each other, so a group can never
    /// contain both A and B regardless of C.
    /// </summary>
    private static bool AreCompatible(DhcpObservation a, DhcpObservation b)
    {
        if (a.NormalizedClientId is not null && b.NormalizedClientId is not null)
        {
            return a.NormalizedClientId == b.NormalizedClientId;
        }

        if (a.NormalizedHardwareAddress is not null && b.NormalizedHardwareAddress is not null)
        {
            return a.NormalizedHardwareAddress == b.NormalizedHardwareAddress;
        }

        // One or both sides are missing all identity evidence: no contradiction can be proven,
        // but this alone is not sufficient to prove identity either (caller still applies the
        // attempt-gap and "exactly one candidate" ambiguity rules).
        return true;
    }

    private static Conversation BuildConversation(uint txId, List<DhcpObservation> group, string rationale)
    {
        group.Sort((a, b) => a.FrameNumber.CompareTo(b.FrameNumber));
        var identity = group[0].IdentityEvidence;
        var id = $"{txId:x8}-{identity ?? "no-identity"}-{group[0].FrameNumber}";
        return new Conversation
        {
            ConversationId = id,
            TransactionId = txId,
            IdentityEvidence = identity,
            Observations = group,
            GroupingRationale = rationale
        };
    }
}

public sealed record ReconstructionResult
{
    public required IReadOnlyList<Conversation> Conversations { get; init; }
    public required IReadOnlyList<UnresolvedObservation> Unresolved { get; init; }
}
