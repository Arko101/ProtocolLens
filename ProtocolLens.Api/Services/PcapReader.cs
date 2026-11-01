using System.Net;
using PacketDotNet;
using PacketDotNet.DhcpV4;
using ProtocolLens.Core.Model;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ProtocolLens.Api.Services;

public enum CaptureDiagnosticSeverity
{
    Info,
    Warning
}

public sealed record CaptureDiagnostic
{
    public long? FrameNumber { get; init; }
    public required CaptureDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record CaptureReadResult
{
    public required IReadOnlyList<DhcpObservation> Observations { get; init; }
    public required long TotalFramesRead { get; init; }
    public required bool ReachedEndOfFile { get; init; }
    public required IReadOnlyList<CaptureDiagnostic> Diagnostics { get; init; }
}

public sealed class PcapReader
{
    public long MaxFramesToRead { get; init; } = 100_000;
    public long MaxObservationsToKeep { get; init; } = 10_000;

    public CaptureReadResult ExtractDhcpObservations(string filePath, CancellationToken cancellationToken = default)
    {
        var observations = new List<DhcpObservation>();
        var diagnostics = new List<CaptureDiagnostic>();
        long frameNumber = 0;
        bool reachedEof = false;

        ICaptureDevice? device;
        try
        {
            device = new CaptureFileReaderDevice(filePath);
            device.Open();
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CaptureDiagnostic
            {
                Severity = CaptureDiagnosticSeverity.Warning,
                Message = $"Unable to open capture container: {Describe(ex)}"
            });
            return new CaptureReadResult
            {
                Observations = observations,
                TotalFramesRead = 0,
                ReachedEndOfFile = false,
                Diagnostics = diagnostics
            };
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (frameNumber >= MaxFramesToRead)
                {
                    diagnostics.Add(new CaptureDiagnostic
                    {
                        Severity = CaptureDiagnosticSeverity.Warning,
                        Message = $"Stopped after reaching the configured frame limit ({MaxFramesToRead}); remaining frames were not analyzed."
                    });
                    break;
                }

                GetPacketStatus status;
                PacketCapture capture;
                try
                {
                    status = device.GetNextPacket(out capture);
                }
                catch (Exception ex)
                {
                    frameNumber++;
                    diagnostics.Add(new CaptureDiagnostic
                    {
                        FrameNumber = frameNumber,
                        Severity = CaptureDiagnosticSeverity.Warning,
                        Message = $"Read error: {Describe(ex)}"
                    });
                    break;
                }

                if (status == GetPacketStatus.NoRemainingPackets)
                {
                    reachedEof = true;
                    break;
                }

                frameNumber++;

                if (status != GetPacketStatus.PacketRead)
                {
                    diagnostics.Add(new CaptureDiagnostic
                    {
                        FrameNumber = frameNumber,
                        Severity = CaptureDiagnosticSeverity.Warning,
                        Message = $"Capture reader returned status '{status}' for this frame; it was not analyzed."
                    });
                    continue;
                }

                try
                {
                    var raw = capture.GetPacket();
                    var observation = TryDecodeDhcp(raw, frameNumber, capture.Header.Timeval, diagnostics);
                    if (observation is not null)
                    {
                        if (observations.Count >= MaxObservationsToKeep)
                        {
                            diagnostics.Add(new CaptureDiagnostic
                            {
                                Severity = CaptureDiagnosticSeverity.Warning,
                                Message = $"Stopped retaining DHCP observations after reaching the configured limit ({MaxObservationsToKeep}); later DHCP frames were read but not retained."
                            });
                        }
                        else
                        {
                            observations.Add(observation);
                        }
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new CaptureDiagnostic
                    {
                        FrameNumber = frameNumber,
                        Severity = CaptureDiagnosticSeverity.Warning,
                        Message = $"Malformed packet, skipped: {Describe(ex)}"
                    });
                }
            }
        }
        finally
        {
            device.Close();
        }

        return new CaptureReadResult
        {
            Observations = observations,
            TotalFramesRead = frameNumber,
            ReachedEndOfFile = reachedEof,
            Diagnostics = diagnostics
        };
    }

    private static DhcpObservation? TryDecodeDhcp(RawCapture raw, long frameNumber, PosixTimeval timeval, List<CaptureDiagnostic> diagnostics)
    {
        Packet packet;
        try
        {
            packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CaptureDiagnostic
            {
                FrameNumber = frameNumber,
                Severity = CaptureDiagnosticSeverity.Warning,
                Message = $"Unsupported link type or malformed frame, skipped: {Describe(ex)}"
            });
            return null;
        }

        var ipv4 = packet.Extract<IPv4Packet>();
        if (ipv4 is null)
        {
            return null;
        }

        var udp = ipv4.PayloadPacket as UdpPacket ?? ipv4.Extract<UdpPacket>();
        if (udp is null || !ReferenceEquals(udp.ParentPacket, ipv4))
        {
            return null;
        }

        var srcPort = udp.SourcePort;
        var dstPort = udp.DestinationPort;

        var isCandidate =
            (srcPort == 68 && dstPort == 67) ||
            (srcPort == 67 && dstPort == 68) ||
            (srcPort == 67 && dstPort == 67);

        if (!isCandidate)
        {
            return null;
        }

        DhcpV4Packet dhcp;
        try
        {
            dhcp = new DhcpV4Packet(udp.PayloadDataSegment, udp);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CaptureDiagnostic
            {
                FrameNumber = frameNumber,
                Severity = CaptureDiagnosticSeverity.Warning,
                Message = $"UDP payload on a DHCP-candidate port pair could not be decoded as DHCPv4: {Describe(ex)}"
            });
            return null;
        }

        var frameDiagnostics = new List<string>();
        var messageType = MapMessageType(SafeGetOptionMessageType(dhcp, frameDiagnostics));

        var timestamp = new DateTimeOffset(timeval.Date, TimeSpan.Zero);

        string? clientIdHex = null;
        string? serverIdentifier = null;
        string? requestedIp = null;

        List<DhcpV4Option> options;
        try
        {
            options = dhcp.GetOptions();
        }
        catch (Exception ex)
        {
            options = new List<DhcpV4Option>();
            frameDiagnostics.Add($"Failed to parse DHCP options: {Describe(ex)}");
        }

        foreach (var option in options)
        {
            try
            {
                switch ((int)option.OptionType)
                {
                    case 61:
                        clientIdHex = Convert.ToHexString(option.Data).ToLowerInvariant();
                        break;
                    case 54:
                        if (option.Data.Length == 4)
                        {
                            serverIdentifier = new IPAddress(option.Data).ToString();
                        }
                        else
                        {
                            frameDiagnostics.Add("Server Identifier (option 54) had an unexpected length and was ignored.");
                        }
                        break;
                    case 50:
                        if (option.Data.Length == 4)
                        {
                            requestedIp = new IPAddress(option.Data).ToString();
                        }
                        else
                        {
                            frameDiagnostics.Add("Requested IP Address (option 50) had an unexpected length and was ignored.");
                        }
                        break;
                    case 255:
                    case 0:
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                frameDiagnostics.Add($"Malformed value for option {(int)option.OptionType}: {Describe(ex)}");
            }
        }

        string chaddr;
        try
        {
            var physical = dhcp.ClientHardwareAddress;
            var bytes = physical?.GetAddressBytes() ?? Array.Empty<byte>();
            chaddr = bytes.Length == 0
                ? "00:00:00:00:00:00"
                : string.Join(':', bytes.Select(b => b.ToString("x2")));
        }
        catch (Exception ex)
        {
            chaddr = "00:00:00:00:00:00";
            frameDiagnostics.Add($"Could not decode client hardware address: {Describe(ex)}");
        }

        string? ciaddr = SafeAddress(() => dhcp.ClientAddress, frameDiagnostics, "ciaddr");
        string? yiaddr = SafeAddress(() => dhcp.YourAddress, frameDiagnostics, "yiaddr");
        string? giaddr = SafeAddress(() => dhcp.GatewayAddress, frameDiagnostics, "giaddr");

        uint xid;
        try
        {
            xid = dhcp.Xid;
        }
        catch (Exception ex)
        {
            frameDiagnostics.Add($"Could not decode transaction id: {Describe(ex)}");
            xid = 0;
        }

        foreach (var d in frameDiagnostics)
        {
            diagnostics.Add(new CaptureDiagnostic { FrameNumber = frameNumber, Severity = CaptureDiagnosticSeverity.Info, Message = d });
        }

        return new DhcpObservation
        {
            FrameNumber = frameNumber,
            Timestamp = timestamp,
            MessageType = messageType,
            TransactionId = xid,
            ClientHardwareAddress = chaddr,
            ClientIdentifierHex = clientIdHex,
            ServerIdentifier = serverIdentifier,
            ClientIpAddress = ciaddr,
            YourIpAddress = yiaddr,
            RelayAgentAddress = giaddr,
            RequestedIpAddress = requestedIp,
            SourceIp = ipv4.SourceAddress.ToString(),
            DestinationIp = ipv4.DestinationAddress.ToString(),
            SourcePort = srcPort,
            DestinationPort = dstPort,
            Diagnostics = frameDiagnostics
        };
    }

    private static string? SafeAddress(Func<IPAddress> getter, List<string> diagnostics, string fieldName)
    {
        try
        {
            return getter().ToString();
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Could not decode {fieldName}: {Describe(ex)}");
            return null;
        }
    }

    private static DhcpV4MessageType? SafeGetOptionMessageType(DhcpV4Packet dhcp, List<string> diagnostics)
    {
        try
        {
            return dhcp.MessageType;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Could not decode DHCP message type (option 53): {Describe(ex)}");
            return null;
        }
    }

    private static DhcpMessageType MapMessageType(DhcpV4MessageType? type) => type switch
    {
        DhcpV4MessageType.Discover => DhcpMessageType.Discover,
        DhcpV4MessageType.Offer => DhcpMessageType.Offer,
        DhcpV4MessageType.Request => DhcpMessageType.Request,
        DhcpV4MessageType.Decline => DhcpMessageType.Decline,
        DhcpV4MessageType.Ack => DhcpMessageType.Ack,
        DhcpV4MessageType.Nak => DhcpMessageType.Nak,
        DhcpV4MessageType.Release => DhcpMessageType.Release,
        DhcpV4MessageType.Inform => DhcpMessageType.Inform,
        _ => DhcpMessageType.Unknown
    };

    private static string Describe(Exception ex) => ex.GetType().Name;
}
