using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using ProtocolLens.Api.Models;
using System;


namespace ProtocolLens.Api.Services
{
    public class PcapReader
    {
        public List<DhcpPacketInfo> ExtractDhcpPacketsFromPcapFile(string filePath)  // A stream is basically a way to read data piece by piece
        {
            var count = 0;
            Console.WriteLine($"Reading pcap file from: {filePath}");

            using var device = new CaptureFileReaderDevice(filePath);
            device.Open(); // Open the pcap file for reading

            PacketCapture capture;

            List<DhcpPacketInfo> dhcpPackets = new List<DhcpPacketInfo>(); // Or in modern way --> List<DhcpInfo> dhcpPackets = new();


            while (device.GetNextPacket(out capture) == GetPacketStatus.PacketRead)
            {
                var rawPacket = capture.GetPacket(); 
                var parsedPacket = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data); // Parse the raw packet data into a Packet object
              

                if (parsedPacket.PayloadPacket is IPv4Packet ipv4packet  && ipv4packet.PayloadPacket is UdpPacket udpPacket) // if payload packet is an ipv4 packet create a varible named ipv4packet
                {
                    //var sourceIp =  ipv4packet.SourceAddress;
                    //var sourcePort = udpPacket.SourcePort;
                    //var destinationIp = ipv4packet.DestinationAddress;
                    //var  destinationPort = udpPacket.DestinationPort;
                    ////var protocol = ipv4packet.Protocol;
                    ////var dhcptype = udpPacket.PayloadPacket.GetType();

                    if ((udpPacket.SourcePort == 67 && udpPacket.DestinationPort == 68) || (udpPacket.SourcePort == 68 && udpPacket.DestinationPort == 67) )// Check if the packet is a DHCP packet (port 67 or 68)
                    {
                        
                        if (udpPacket.PayloadPacket is DhcpV4Packet dhcpV4Packet)
                        {
                            var info = new DhcpPacketInfo();

                            //var dhcpMsgType = dhcpV4Packet;
                            //var dhcpTransactionId = dhcpV4Packet.TransactionId;
                            info.TransactionId = dhcpV4Packet.TransactionId;
                            info.MessageType = dhcpV4Packet.MessageType.ToString();
                            info.SourceIp = ipv4packet.SourceAddress.ToString();
                            info.SourcePort = udpPacket.SourcePort;
                            info.DestinationIp = ipv4packet.DestinationAddress.ToString();
                            info.DestinationPort = udpPacket.DestinationPort;
                            info.ClientMac = dhcpV4Packet.ClientHardwareAddress.ToString();
                            info.Timestamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddSeconds(rawPacket.Timeval.Seconds)
                            .AddMicroseconds(rawPacket.Timeval.MicroSeconds);

                            dhcpPackets.Add(info);


                            dhcpPackets.Count();
                        }
                        


                    }

                }
                count++;

               
                
                

            }

            return dhcpPackets; // Return the actual count of packets
        }
    }
}