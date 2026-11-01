using ProtocolLens.Core.Model;

namespace ProtocolLens.Core.Analysis;

/// <summary>
/// Evaluates a reconstructed conversation against flow-conditional expectations and produces
/// <see cref="Finding"/>s. Every rule here has a corresponding unit test asserting both that it
/// fires when applicable and that it does NOT fire for flows where it would be a false
/// accusation (section 8 / rule catalog requirement).
/// </summary>
public static class DhcpConversationAnalyzer
{
    public static IReadOnlyList<Finding> Analyze(Conversation conversation)
    {
        var flow = FlowClassifier.Classify(conversation);
        var findings = new List<Finding>();
        var obs = conversation.Observations;

        switch (flow)
        {
            case DhcpFlow.InitialAllocation:
                AnalyzeInitialAllocation(conversation, obs, findings);
                break;

            case DhcpFlow.InitReboot:
            case DhcpFlow.Renewing:
            case DhcpFlow.Rebinding:
                AnalyzeRequestOnlyFlow(conversation, obs, flow, findings);
                break;

            case DhcpFlow.Inform:
                AnalyzeInform(conversation, obs, findings);
                break;

            case DhcpFlow.Release:
            case DhcpFlow.Decline:
                // RFC 2131: RELEASE and DECLINE are client-to-server notifications with no
                // defined server response. No "missing ACK" expectation applies (A12).
                findings.Add(new Finding
                {
                    RuleCode = "DHCP-NO-RESPONSE-EXPECTED",
                    Category = FindingCategory.FlowOutcome,
                    Severity = FindingSeverity.Info,
                    ConversationId = conversation.ConversationId,
                    Flow = flow,
                    Expected = "RFC 2131: RELEASE/DECLINE are one-way client notifications; no server response is defined.",
                    ObservedFrames = obs.Select(o => o.FrameNumber).ToList(),
                    Explanation = $"Observed a {flow} message. No response is expected by protocol, so its absence is not a finding.",
                });
                break;

            case DhcpFlow.Unknown:
                findings.Add(new Finding
                {
                    RuleCode = "DHCP-FLOW-UNKNOWN",
                    Category = FindingCategory.FlowProgress,
                    Severity = FindingSeverity.Info,
                    ConversationId = conversation.ConversationId,
                    Flow = flow,
                    Expected = "N/A",
                    ObservedFrames = obs.Select(o => o.FrameNumber).ToList(),
                    Explanation = "Observed evidence did not confidently match a known DHCP flow pattern.",
                    NotEvaluatedReason = "Insufficient evidence to classify flow; no flow-specific rules were evaluated."
                });
                break;
        }

        return findings;
    }

    private static void AnalyzeInitialAllocation(Conversation c, IReadOnlyList<DhcpObservation> obs, List<Finding> findings)
    {
        var discover = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Discover);
        var offers = obs.Where(o => o.MessageType == DhcpMessageType.Offer).ToList();
        var request = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Request);
        var ack = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Ack);
        var nak = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Nak);

        if (discover is not null && offers.Count == 0)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-MISSING-OFFER",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Notice,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.InitialAllocation,
                Expected = "RFC 2131 4.3.1: a server would typically respond to DISCOVER with OFFER.",
                ObservedFrames = new[] { discover.FrameNumber },
                MissingObservation = $"No OFFER observed for transaction 0x{c.TransactionId:x8} within the analyzed capture after frame {discover.FrameNumber}.",
                Explanation = "A DISCOVER was observed with no matching OFFER in the analyzed traffic. This is not automatically a server failure: the capture may end before a response, the response may use a path/interface outside this capture, or no server may have been configured to respond.",
                Hypotheses = new[] { "No DHCP server reachable at capture point.", "Response occurred outside the captured time/interface window." },
                NextChecks = new[] { "Confirm whether the capture covers the full exchange window.", "Check for OFFER traffic on other interfaces/captures." }
            });
        }

        if (offers.Count > 1)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-MULTIPLE-OFFERS",
                Category = FindingCategory.FlowProgress,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.InitialAllocation,
                Expected = "RFC 2131 4.3.1: multiple servers may legitimately respond with OFFER; this is normal.",
                ObservedFrames = offers.Select(o => o.FrameNumber).ToList(),
                Explanation = $"{offers.Count} OFFER messages observed for this transaction. Multiple offers are a normal outcome, not a deviation."
            });
        }

        if (request is not null && ack is null && nak is null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-MISSING-ACK-OR-NAK",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Notice,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.InitialAllocation,
                Expected = "RFC 2131 4.3.2: a server would typically answer REQUEST with ACK or NAK.",
                ObservedFrames = new[] { request.FrameNumber },
                MissingObservation = $"No ACK or NAK observed for transaction 0x{c.TransactionId:x8} within the analyzed capture after frame {request.FrameNumber}.",
                Explanation = "A REQUEST was observed with no matching ACK/NAK in the analyzed traffic. This does not by itself establish that the server did not respond, only that no response was captured/decoded.",
                NextChecks = new[] { "Verify the capture extends sufficiently past this frame.", "Check for retransmitted REQUESTs indicating the client also saw no response." }
            });
        }
        else if (nak is not null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-NAK-OBSERVED",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.InitialAllocation,
                Expected = "RFC 2131 4.3.2: NAK is a valid, protocol-defined response to REQUEST; it is not itself an analyzer or protocol failure.",
                ObservedFrames = new[] { nak.FrameNumber },
                Explanation = "A NAK was observed in response to REQUEST. This reports the server's rejection as communicated; it does not by itself establish why the server rejected the request."
            });
        }
        else if (ack is not null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-ACK-OBSERVED",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.InitialAllocation,
                Expected = "RFC 2131 4.3.1: REQUEST answered with ACK completes allocation.",
                ObservedFrames = new[] { ack.FrameNumber },
                Explanation = "An ACK was observed completing the allocation exchange. This reports that an ACK was sent/seen in the capture; it does not establish that the client received it, installed the address, or achieved connectivity."
            });
        }
    }

    private static void AnalyzeRequestOnlyFlow(Conversation c, IReadOnlyList<DhcpObservation> obs, DhcpFlow flow, List<Finding> findings)
    {
        var request = obs.First(o => o.MessageType == DhcpMessageType.Request);
        var ack = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Ack);
        var nak = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Nak);

        if (ack is null && nak is null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-MISSING-ACK-OR-NAK",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Notice,
                ConversationId = c.ConversationId,
                Flow = flow,
                Expected = $"RFC 2131: a {flow} REQUEST would typically be answered with ACK or NAK.",
                ObservedFrames = new[] { request.FrameNumber },
                MissingObservation = $"No ACK or NAK observed within the analyzed capture after frame {request.FrameNumber}.",
                Explanation = $"A REQUEST classified as {flow} was observed with no matching ACK/NAK in the analyzed traffic."
            });
        }
        else if (nak is not null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-NAK-OBSERVED",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = flow,
                Expected = $"RFC 2131: NAK is a valid, protocol-defined response to a {flow} REQUEST.",
                ObservedFrames = new[] { nak.FrameNumber },
                Explanation = $"A NAK was observed in response to a {flow} REQUEST. This reports the server's rejection as communicated; it does not by itself establish why the server rejected the request."
            });
        }
        else
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-ACK-OBSERVED",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = flow,
                Expected = $"RFC 2131: {flow} completes with ACK.",
                ObservedFrames = new[] { ack!.FrameNumber },
                Explanation = $"An ACK was observed completing the {flow} exchange. This reports that an ACK was sent/seen in the capture; it does not establish that the client received it, installed the address, or achieved connectivity."
            });
        }
    }

    private static void AnalyzeInform(Conversation c, IReadOnlyList<DhcpObservation> obs, List<Finding> findings)
    {
        var inform = obs.First(o => o.MessageType == DhcpMessageType.Inform);
        var ack = obs.FirstOrDefault(o => o.MessageType == DhcpMessageType.Ack);

        if (ack is null)
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-MISSING-ACK-OR-NAK",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Notice,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.Inform,
                Expected = "RFC 2131 4.4.3: INFORM is typically answered with ACK carrying configuration options only.",
                ObservedFrames = new[] { inform.FrameNumber },
                MissingObservation = $"No ACK observed within the analyzed capture after frame {inform.FrameNumber}.",
                Explanation = "An INFORM was observed with no matching ACK in the analyzed traffic."
            });
        }
        else
        {
            findings.Add(new Finding
            {
                RuleCode = "DHCP-INFORM-ACK-OBSERVED",
                Category = FindingCategory.FlowOutcome,
                Severity = FindingSeverity.Info,
                ConversationId = c.ConversationId,
                Flow = DhcpFlow.Inform,
                Expected = "RFC 2131 4.4.3: ACK to INFORM conveys configuration only, not a new lease.",
                ObservedFrames = new[] { ack.FrameNumber },
                Explanation = "An ACK was observed in response to INFORM. This is a configuration response, not a newly allocated lease, and must not be described as one."
            });
        }
    }
}
