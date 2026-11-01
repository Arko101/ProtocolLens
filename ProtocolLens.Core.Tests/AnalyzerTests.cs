using ProtocolLens.Core.Analysis;
using ProtocolLens.Core.Model;
using ProtocolLens.Core.Reconstruction;
using Xunit;

namespace ProtocolLens.Core.Tests;

public class AnalyzerTests
{
    private static Conversation Reconstruct(params DhcpObservation[] obs)
        => new ConversationReconstructor().Reconstruct(obs).Conversations.Single();

    // A01: full allocation with ACK must not claim connectivity beyond what was observed.
    [Fact]
    public void A01_FullAllocation_ProducesAckObservedFindingWithoutConnectivityClaim()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x10),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x10),
            ObsBuilder.Make(3, DhcpMessageType.Request, 0x10),
            ObsBuilder.Make(4, DhcpMessageType.Ack, 0x10));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var ackFinding = Assert.Single(findings.Where(f => f.RuleCode == "DHCP-ACK-OBSERVED"));
        Assert.Contains("does not establish", ackFinding.Explanation);
        Assert.Contains("achieved connectivity", ackFinding.Explanation);
    }

    // A02: DISCOVER with no OFFER -> qualified missing-observation finding, not a hard failure.
    [Fact]
    public void A02_DiscoverWithNoOffer_ProducesQualifiedMissingObservation()
    {
        var c = Reconstruct(ObsBuilder.Make(1, DhcpMessageType.Discover, 0x20));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings, f => f.RuleCode == "DHCP-MISSING-OFFER");
        Assert.Equal(FindingSeverity.Notice, finding.Severity);
        Assert.NotNull(finding.MissingObservation);
        Assert.Contains("analyzed capture", finding.MissingObservation!);
    }

    // A03: REQUEST with no ACK/NAK -> conditional expectation, capture-limitation wording.
    [Fact]
    public void A03_RequestWithNoAckOrNak_ProducesQualifiedMissingObservation()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x30),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x30),
            ObsBuilder.Make(3, DhcpMessageType.Request, 0x30));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings, f => f.RuleCode == "DHCP-MISSING-ACK-OR-NAK");
        Assert.Contains("does not by itself establish", finding.Explanation);
    }

    // A04: a NAK is a valid response, not a malformed-sequence/failure finding.
    [Fact]
    public void A04_NakResponse_IsReportedAsValidNotAsFailure()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Request, 0x40, requestedIp: "10.0.0.5"),
            ObsBuilder.Make(2, DhcpMessageType.Nak, 0x40));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings, f => f.RuleCode == "DHCP-NAK-OBSERVED");
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.DoesNotContain(findings, f => f.Category == FindingCategory.CaptureLimitation && f.Severity == FindingSeverity.Warning);
    }

    // A08: multiple offers in one exchange is normal, not a deviation.
    [Fact]
    public void A08_MultipleOffers_IsReportedAsNormal()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Discover, 0x50),
            ObsBuilder.Make(2, DhcpMessageType.Offer, 0x50),
            ObsBuilder.Make(3, DhcpMessageType.Offer, 0x50),
            ObsBuilder.Make(4, DhcpMessageType.Request, 0x50),
            ObsBuilder.Make(5, DhcpMessageType.Ack, 0x50));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings, f => f.RuleCode == "DHCP-MULTIPLE-OFFERS");
        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    // A09: partial capture starting mid-exchange (REQUEST/ACK only) must not claim full DORA is required.
    [Fact]
    public void A09_RequestAckOnlyPartialCapture_DoesNotDemandDiscoverOffer()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Request, 0x60, ciaddr: "10.0.0.9", destIp: "10.0.0.1"),
            ObsBuilder.Make(2, DhcpMessageType.Ack, 0x60));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        Assert.DoesNotContain(findings, f => f.RuleCode == "DHCP-MISSING-OFFER");
        Assert.Contains(findings, f => f.RuleCode == "DHCP-ACK-OBSERVED");
    }

    // A11: INFORM/ACK must be distinguished from a newly allocated lease.
    [Fact]
    public void A11_InformAck_IsDistinguishedFromNewLease()
    {
        var c = Reconstruct(
            ObsBuilder.Make(1, DhcpMessageType.Inform, 0x70),
            ObsBuilder.Make(2, DhcpMessageType.Ack, 0x70));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings, f => f.RuleCode == "DHCP-INFORM-ACK-OBSERVED");
        Assert.Contains("not a newly allocated lease", finding.Explanation);
    }

    // A12: RELEASE/DECLINE must not generate a fabricated "missing ACK" expectation.
    [Theory]
    [InlineData(DhcpMessageType.Release)]
    [InlineData(DhcpMessageType.Decline)]
    public void A12_ReleaseOrDecline_DoesNotExpectAck(DhcpMessageType type)
    {
        var c = Reconstruct(ObsBuilder.Make(1, type, 0x80));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        Assert.DoesNotContain(findings, f => f.RuleCode == "DHCP-MISSING-ACK-OR-NAK");
        Assert.Contains(findings, f => f.RuleCode == "DHCP-NO-RESPONSE-EXPECTED");
    }

    // Counterexample: an unknown/ambiguous flow must return not-evaluated, not an accusation.
    [Fact]
    public void UnknownFlow_ReturnsNotEvaluatedRatherThanAnAccusation()
    {
        var c = Reconstruct(ObsBuilder.Make(1, DhcpMessageType.Unknown, 0x90));

        var findings = DhcpConversationAnalyzer.Analyze(c);

        var finding = Assert.Single(findings);
        Assert.Equal("DHCP-FLOW-UNKNOWN", finding.RuleCode);
        Assert.NotNull(finding.NotEvaluatedReason);
    }
}
