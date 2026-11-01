using ProtocolLens.Core.Model;

namespace ProtocolLens.Core.Tests;

internal static class ObsBuilder
{
    public static DhcpObservation Make(
        long frame,
        DhcpMessageType type,
        uint txId,
        string mac = "aa:bb:cc:dd:ee:01",
        string? clientId = null,
        string? ciaddr = null,
        string? yiaddr = null,
        string? giaddr = null,
        string? requestedIp = null,
        string destIp = "255.255.255.255",
        DateTimeOffset? time = null)
    {
        return new DhcpObservation
        {
            FrameNumber = frame,
            Timestamp = time ?? DateTimeOffset.UnixEpoch.AddSeconds(frame),
            MessageType = type,
            TransactionId = txId,
            ClientHardwareAddress = mac,
            ClientIdentifierHex = clientId,
            ClientIpAddress = ciaddr,
            YourIpAddress = yiaddr,
            RelayAgentAddress = giaddr,
            RequestedIpAddress = requestedIp,
            SourceIp = "0.0.0.0",
            DestinationIp = destIp,
            SourcePort = 68,
            DestinationPort = 67,
        };
    }
}
