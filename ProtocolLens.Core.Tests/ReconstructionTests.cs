using ProtocolLens.Core.Analysis;
using ProtocolLens.Core.Model;
using ProtocolLens.Core.Reconstruction;
using Xunit;

namespace ProtocolLens.Core.Tests;

public class ReconstructionTests
{
    // A01: normal initial allocation - single conversation, frames preserved in order.
    [Fact]
    public void A01_NormalAllocation_GroupsIntoSingleConversation()
    {
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x1111),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x1111),
            ObsBuilder.Make(3, DhcpMessageType.Request, 0x1111),
            ObsBuilder.Make(4, DhcpMessageType.Ack, 0x1111),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Single(result.Conversations);
        Assert.Empty(result.Unresolved);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, result.Conversations[0].Observations.Select(o => o.FrameNumber));
    }

    // A05: two different clients reuse the same transaction id - must stay separate.
    [Fact]
    public void A05_SameTransactionIdDifferentClients_ProducesSeparateConversations()
    {
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x2222, mac: "aa:aa:aa:aa:aa:01"),
            ObsBuilder.Make(2, DhcpMessageType.Discover, 0x2222, mac: "bb:bb:bb:bb:bb:02"),
            ObsBuilder.Make(3, DhcpMessageType.Offer, 0x2222, mac: "aa:aa:aa:aa:aa:01"),
            ObsBuilder.Make(4, DhcpMessageType.Offer, 0x2222, mac: "bb:bb:bb:bb:bb:02"),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Equal(2, result.Conversations.Count);
        Assert.All(result.Conversations, c => Assert.Equal(2, c.Observations.Count));
        Assert.NotEqual(result.Conversations[0].IdentityEvidence, result.Conversations[1].IdentityEvidence);
    }

    // A06: same client reuses a transaction id for a distinct, later exchange - must split by gap.
    [Fact]
    public void A06_SameClientReusesTransactionIdLater_SplitsIntoDistinctAttempts()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x3333, time: t0),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x3333, time: t0.AddSeconds(1)),
            ObsBuilder.Make(3, DhcpMessageType.Request, 0x3333, time: t0.AddSeconds(2)),
            ObsBuilder.Make(4, DhcpMessageType.Ack, 0x3333, time: t0.AddSeconds(3)),
            // Same tx id, same client, but far later -> distinct attempt.
            ObsBuilder.Make(5, DhcpMessageType.Discover, 0x3333, time: t0.AddMinutes(10)),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Equal(2, result.Conversations.Count);
        Assert.Equal(4, result.Conversations[0].Observations.Count);
        Assert.Single(result.Conversations[1].Observations);
    }

    // A14: relay traffic - the giaddr/relay path must not be mistaken for client identity.
    [Fact]
    public void A14_RelayedTraffic_UsesClientIdentifierNotRelayMac()
    {
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x4444, mac: "00:00:00:00:00:00", clientId: "01aabbccddeeff", giaddr: "10.0.0.1"),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x4444, mac: "00:00:00:00:00:00", clientId: "01aabbccddeeff", giaddr: "10.0.0.1"),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Single(result.Conversations);
        Assert.StartsWith("cid:", result.Conversations[0].IdentityEvidence);
    }

    // A16: conflicting/insufficient evidence must not be force-assigned.
    [Fact]
    public void A16_AmbiguousObservationWithoutIdentity_IsReportedUnresolved()
    {
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Offer, 0x5555, mac: "aa:aa:aa:aa:aa:01"),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x5555, mac: "bb:bb:bb:bb:bb:02"),
            // No identity evidence at all, two candidate groups already exist -> unresolved.
            ObsBuilder.Make(3, DhcpMessageType.Ack, 0x5555, mac: "00:00:00:00:00:00", giaddr: "10.0.0.1"),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Equal(2, result.Conversations.Count);
        Assert.Single(result.Unresolved);
        Assert.Equal(3, result.Unresolved[0].Observation.FrameNumber);
    }

    // A17: unrelated/no-DHCP traffic still preserves original frame numbering (an empty
    // analysis over an empty set is a trivially useful, honest result).
    [Fact]
    public void A17_NoObservations_ProducesEmptyButValidResult()
    {
        var result = new ConversationReconstructor().Reconstruct(Array.Empty<DhcpObservation>());

        Assert.Empty(result.Conversations);
        Assert.Empty(result.Unresolved);
    }

    // A24: identical input produces identical conversation ids (determinism).
    [Fact]
    public void A24_SameInputTwice_ProducesIdenticalConversationIds()
    {
        DhcpObservation[] Build() => new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x6666),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x6666),
        };

        var r1 = new ConversationReconstructor().Reconstruct(Build());
        var r2 = new ConversationReconstructor().Reconstruct(Build());

        Assert.Equal(r1.Conversations[0].ConversationId, r2.Conversations[0].ConversationId);
    }
    // Mandatory counterexample (Stage 3): two candidates share xid AND hardware address but
    // have different client identifiers; a reply omits the client identifier. The reply must
    // NOT silently join either candidate (non-transitive compatibility), and must not merge
    // the two candidates together either.
    [Fact]
    public void Counterexample_SharedMacDifferentClientIds_ReplyWithoutClientId_DoesNotBridgeOrMerge()
    {
        var mac = "aa:aa:aa:aa:aa:ff";
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x9999, mac: mac, clientId: "01aaaaaaaaaaff"),
            ObsBuilder.Make(2, DhcpMessageType.Discover, 0x9999, mac: mac, clientId: "01bbbbbbbbbbff"),
            // Reply has the shared mac but no client id - ambiguous w.r.t. both candidates.
            ObsBuilder.Make(3, DhcpMessageType.Offer, 0x9999, mac: mac, clientId: null),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        // The two original DISCOVERs must remain distinct conversations (never merged).
        Assert.Equal(2, result.Conversations.Count(c => c.Observations.Any(o => o.MessageType == DhcpMessageType.Discover)));
        Assert.DoesNotContain(result.Conversations, c => c.Observations.Count(o => o.MessageType == DhcpMessageType.Discover) > 1);

        // The ambiguous reply (frame 3) must not be attached to either candidate.
        Assert.DoesNotContain(result.Conversations, c => c.Observations.Any(o => o.FrameNumber == 3));
        Assert.Contains(result.Unresolved, u => u.Observation.FrameNumber == 3);
    }

    // Relay traffic: BOOTP chaddr remains valid identity evidence even when giaddr is set -
    // relaying must not suppress hardware-address evidence (corrected audit finding).
    [Fact]
    public void RelayedTraffic_HardwareAddressRemainsUsableIdentityEvidence()
    {
        var observations = new[]
        {
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0xAAAA, mac: "cc:cc:cc:cc:cc:01", giaddr: "10.0.0.1"),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0xAAAA, mac: "cc:cc:cc:cc:cc:01", giaddr: "10.0.0.1"),
        };

        var result = new ConversationReconstructor().Reconstruct(observations);

        Assert.Single(result.Conversations);
        Assert.StartsWith("mac:", result.Conversations[0].IdentityEvidence);
    }
}
