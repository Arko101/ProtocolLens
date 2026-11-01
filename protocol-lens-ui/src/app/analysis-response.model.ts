// Typed mirror of ProtocolLens.Api.Models.AnalysisResponse and friends. Keep in sync with
// the backend DTOs - see ProtocolLens.Api/Models/AnalysisResponse.cs.

export interface CaptureDiagnostic {
  frameNumber: number | null;
  severity: string;
  message: string;
}

export interface DhcpObservationDto {
  frameNumber: number;
  timestamp: string;
  messageType: string;
  transactionId: string;
  clientHardwareAddress: string | null;
  clientIdentifierHex: string | null;
  serverIdentifier: string | null;
  clientIpAddress: string | null;
  yourIpAddress: string | null;
  relayAgentAddress: string | null;
  requestedIpAddress: string | null;
  sourceIp: string;
  destinationIp: string;
  sourcePort: number;
  destinationPort: number;
  diagnostics: string[];
}

export interface ConversationDto {
  conversationId: string;
  transactionId: string;
  identityEvidence: string | null;
  flow: string;
  groupingRationale: string;
  observations: DhcpObservationDto[];
}

export interface UnresolvedObservationDto {
  observation: DhcpObservationDto;
  reason: string;
}

export interface FindingDto {
  ruleCode: string;
  category: string;
  severity: string;
  conversationId: string;
  flow: string;
  expected: string;
  observedFrames: number[];
  missingObservation: string | null;
  explanation: string;
  hypotheses: string[];
  nextChecks: string[];
  notEvaluatedReason: string | null;
}

export interface AnalysisResponse {
  schemaVersion: string;
  analyzerVersion: string;
  fileName: string;
  fileSizeBytes: number;
  totalFramesRead: number;
  reachedEndOfFile: boolean;
  diagnostics: CaptureDiagnostic[];
  conversations: ConversationDto[];
  unresolvedObservations: UnresolvedObservationDto[];
  findings: FindingDto[];
}

export interface ApiProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}
