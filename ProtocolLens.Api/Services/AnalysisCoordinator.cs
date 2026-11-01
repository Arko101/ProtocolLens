using ProtocolLens.Api.Models;
using ProtocolLens.Core.Analysis;
using ProtocolLens.Core.Model;
using ProtocolLens.Core.Reconstruction;

namespace ProtocolLens.Api.Services;

/// <summary>
/// Owns the processing sequence for one upload: capture reading -> reconstruction -> flow
/// analysis -> explicit DTO mapping. Bounded, synchronous (per Stage 4 - this is a local
/// single-user tool), and cooperative-cancellation aware. Does not leak capture-library types.
/// </summary>
public sealed class AnalysisCoordinator
{
    public const string SchemaVersion = "1.0";
    public const string AnalyzerVersionValue = "dhcpv4-1.0";

    public AnalysisResponse Analyze(string filePath, string fileName, long fileSizeBytes, long maxFramesToRead, long maxObservationsToKeep, CancellationToken cancellationToken)
    {
        var reader = new PcapReader
        {
            MaxFramesToRead = maxFramesToRead,
            MaxObservationsToKeep = maxObservationsToKeep
        };
        var captureResult = reader.ExtractDhcpObservations(filePath, cancellationToken);

        var reconstruction = new ConversationReconstructor().Reconstruct(captureResult.Observations);

        var findings = new List<FindingDto>();
        var conversationDtos = new List<ConversationDto>();

        foreach (var conversation in reconstruction.Conversations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flow = FlowClassifier.Classify(conversation);
            conversationDtos.Add(MapConversation(conversation, flow));

            foreach (var finding in DhcpConversationAnalyzer.Analyze(conversation))
            {
                findings.Add(MapFinding(finding));
            }
        }

        var unresolvedDtos = reconstruction.Unresolved
            .Select(u => new UnresolvedObservationDto
            {
                Observation = MapObservation(u.Observation),
                Reason = u.Reason
            })
            .ToList();

        var diagnosticDtos = captureResult.Diagnostics
            .Select(d => new CaptureDiagnosticDto
            {
                FrameNumber = d.FrameNumber,
                Severity = d.Severity.ToString(),
                Message = d.Message
            })
            .ToList();

        if (!captureResult.ReachedEndOfFile)
        {
            diagnosticDtos.Add(new CaptureDiagnosticDto
            {
                Severity = CaptureDiagnosticSeverity.Warning.ToString(),
                Message = "The capture was not read to a confirmed end-of-file (due to a read error or a configured limit). Findings below reflect only the portion actually analyzed."
            });
        }

        return new AnalysisResponse
        {
            SchemaVersion = SchemaVersion,
            AnalyzerVersion = AnalyzerVersionValue,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            TotalFramesRead = captureResult.TotalFramesRead,
            ReachedEndOfFile = captureResult.ReachedEndOfFile,
            Diagnostics = diagnosticDtos,
            Conversations = conversationDtos,
            UnresolvedObservations = unresolvedDtos,
            Findings = findings
        };
    }

    private static ConversationDto MapConversation(Conversation conversation, DhcpFlow flow) => new()
    {
        ConversationId = conversation.ConversationId,
        TransactionId = $"0x{conversation.TransactionId:x8}",
        IdentityEvidence = conversation.IdentityEvidence,
        Flow = flow.ToString(),
        GroupingRationale = conversation.GroupingRationale,
        Observations = conversation.Observations.Select(MapObservation).ToList()
    };

    private static DhcpObservationDto MapObservation(DhcpObservation o) => new()
    {
        FrameNumber = o.FrameNumber,
        Timestamp = o.Timestamp,
        MessageType = o.MessageType.ToString(),
        TransactionId = $"0x{o.TransactionId:x8}",
        ClientHardwareAddress = o.ClientHardwareAddress,
        ClientIdentifierHex = o.ClientIdentifierHex,
        ServerIdentifier = o.ServerIdentifier,
        ClientIpAddress = o.ClientIpAddress,
        YourIpAddress = o.YourIpAddress,
        RelayAgentAddress = o.RelayAgentAddress,
        RequestedIpAddress = o.RequestedIpAddress,
        SourceIp = o.SourceIp,
        DestinationIp = o.DestinationIp,
        SourcePort = o.SourcePort,
        DestinationPort = o.DestinationPort,
        Diagnostics = o.Diagnostics
    };

    private static FindingDto MapFinding(Finding f) => new()
    {
        RuleCode = f.RuleCode,
        Category = f.Category.ToString(),
        Severity = f.Severity.ToString(),
        ConversationId = f.ConversationId,
        Flow = f.Flow.ToString(),
        Expected = f.Expected,
        ObservedFrames = f.ObservedFrames,
        MissingObservation = f.MissingObservation,
        Explanation = f.Explanation,
        Hypotheses = f.Hypotheses,
        NextChecks = f.NextChecks,
        NotEvaluatedReason = f.NotEvaluatedReason
    };
}
