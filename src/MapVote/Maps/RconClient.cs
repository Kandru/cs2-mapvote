using System.Net.Sockets;
using System.Text;

namespace MapVote.Maps
{
    /// <summary>Minimal Source RCON client for localhost map listing.</summary>
    public sealed class RconClient : IDisposable
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly string _password;
        private readonly int _timeoutMs;
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private int _requestId;

        public RconClient(string ip, int port, string password, int timeoutMs = 1000)
        {
            _ip = ip;
            _port = port;
            _password = password;
            _timeoutMs = timeoutMs;
        }

        public void Connect()
        {
            _tcp = new TcpClient
            {
                ReceiveTimeout = _timeoutMs,
                SendTimeout = _timeoutMs,
            };
            _tcp.Connect(_ip, _port);
            _stream = _tcp.GetStream();
            Authenticate();
        }

        public string SendCommand(string command)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RCON not connected.");
            }

            _requestId++;
            byte[] packet = CreatePacket(_requestId, 2, command);
            _stream.Write(packet, 0, packet.Length);
            (_, _, string body) = ReceiveResponse();
            return body;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
        }

        private void Authenticate()
        {
            _requestId++;
            byte[] auth = CreatePacket(_requestId, 3, _password);
            _stream!.Write(auth, 0, auth.Length);
            (int requestId, _, _) = ReceiveResponse();
            if (requestId == -1)
            {
                throw new InvalidOperationException("RCON authentication failed.");
            }
        }

        private static byte[] CreatePacket(int requestId, int packetType, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            int packetSize = 4 + 4 + bodyBytes.Length + 2;
            byte[] packet = new byte[4 + packetSize];
            BitConverter.GetBytes(packetSize).CopyTo(packet, 0);
            BitConverter.GetBytes(requestId).CopyTo(packet, 4);
            BitConverter.GetBytes(packetType).CopyTo(packet, 8);
            bodyBytes.CopyTo(packet, 12);
            packet[^2] = 0;
            packet[^1] = 0;
            return packet;
        }

        private (int RequestId, int ResponseType, string Body) ReceiveResponse()
        {
            byte[] lengthBytes = new byte[4];
            ReadExact(lengthBytes);
            int packetLength = BitConverter.ToInt32(lengthBytes, 0);
            byte[] response = new byte[packetLength];
            ReadExact(response);
            int requestId = BitConverter.ToInt32(response, 0);
            int responseType = BitConverter.ToInt32(response, 4);
            string body = Encoding.UTF8.GetString(response, 8, Math.Max(0, packetLength - 10));
            return (requestId, responseType, body);
        }

        private void ReadExact(byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = _stream!.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("RCON connection closed.");
                }

                offset += read;
            }
        }
    }
}
