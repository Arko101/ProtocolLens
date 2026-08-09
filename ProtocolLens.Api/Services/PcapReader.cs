using SharpPcap;
using SharpPcap.LibPcap;


namespace ProtocolLens.Api.Services
{
    public class PcapReader
    {
        public int CountPacketsInPcap(string filePath)  // A stream is basically a way to read data piece by piece
        {
            var count = 0;
            Console.WriteLine($"Reading pcap file from: {filePath}");

            using var device = new CaptureFileReaderDevice(filePath);
            device.Open(); // Open the pcap file for reading

            PacketCapture capture;

            while (device.GetNextPacket(out capture) == GetPacketStatus.PacketRead)
            {
                count++;
            }

            return count; // Return the actual count of packets
        }
    }
}