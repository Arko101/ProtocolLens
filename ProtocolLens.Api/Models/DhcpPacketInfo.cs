namespace ProtocolLens.Api.Models

{
	public class DhcpPacketInfo
	{
		public string MessageType { get; set; }        // Discover / Offer / Request / ACK / NAK
		public uint TransactionId { get; set; }        // Primary correlation key
		public string SourceIp { get; set; }
		public string DestinationIp { get; set; }
		public int SourcePort { get; set; }
		public int DestinationPort { get; set; }
		public DateTime Timestamp { get; set; }
		public string ClientMac { get; set; }
	}
}
