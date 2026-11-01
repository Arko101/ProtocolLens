using ProtocolLens.Core.Model;

namespace ProtocolLens.Core.Analysis;

/// <summary>
/// Classifies the inferred DHCP flow of a conversation from the observed message types and
/// relevant fields. Deliberately conservative: when evidence is incomplete the classifier
/// returns <see cref="DhcpFlow.Unknown"/> rather than guessing (see RFC 2131 section 4.3/4.4).
/// </summary>
public static class FlowClassifier
{
    public static DhcpFlow Classify(Conversation conversation)
    {
        var types = conversation.Observations.Select(o => o.MessageType).ToList();

        if (types.Contains(DhcpMessageType.Release))
        {
            return DhcpFlow.Release;
        }

        if (types.Contains(DhcpMessageType.Decline))
        {
            return DhcpFlow.Decline;
        }

        if (types.Contains(DhcpMessageType.Inform))
        {
            return DhcpFlow.Inform;
        }

        if (types.Contains(DhcpMessageType.Discover))
        {
            // Classic DISCOVER/OFFER/REQUEST/ACK-or-NAK allocation, even if only partially observed.
            return DhcpFlow.InitialAllocation;
        }

        var request = conversation.Observations.FirstOrDefault(o => o.MessageType == DhcpMessageType.Request);
        if (request is not null)
        {
            var hasCiaddr = !string.IsNullOrEmpty(request.ClientIpAddress) && request.ClientIpAddress != "0.0.0.0";
            // A destination of 255.255.255.255 is observed evidence of broadcast; an empty/unknown
            // destination field is NOT evidence either way and must not be treated as broadcast.
            var isBroadcast = request.DestinationIp == "255.255.255.255";
            var destinationUnknown = string.IsNullOrEmpty(request.DestinationIp);

            if (hasCiaddr && !isBroadcast && !destinationUnknown)
            {
                return DhcpFlow.Renewing;
            }
            if (hasCiaddr && isBroadcast)
            {
                return DhcpFlow.Rebinding;
            }
            if (!hasCiaddr && !string.IsNullOrEmpty(request.RequestedIpAddress))
            {
                // Requested IP (option 50) alone cannot distinguish INIT-REBOOT from a SELECTING
                // request whose earlier DISCOVER/OFFER was not captured (both omit ciaddr and
                // carry option 50). RFC 2131 4.3.2: a SELECTING REQUEST also carries the server
                // identifier (option 54) naming the chosen server; INIT-REBOOT must NOT.
                return string.IsNullOrEmpty(request.ServerIdentifier)
                    ? DhcpFlow.InitReboot
                    : DhcpFlow.Unknown;
            }
        }

        return DhcpFlow.Unknown;
    }
}
