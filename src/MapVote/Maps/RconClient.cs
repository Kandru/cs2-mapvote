using System.Net.Sockets;
using System.Text;

namespace MapVote.Maps
{
    /// <summary>Minimal Source RCON client for localhost map listing.</summary>
    public sealed class RconClient : IDisposable
    {
        internal const int ServerDataResponseValue = 0;
        internal const int ServerDataAuthResponse = 2;
        internal const int ServerDataExecCommand = 2;
        internal const int ServerDataAuth = 3;
        internal const int MaxPacketLength = 64 * 1024;
        internal const int MaxBodyLength = 1024 * 1024;

        private readonly string _ip;
        private readonly int _port;
        private readonly string _password;
        private readonly int _timeoutMs;
        private TcpClient? _tcp;
        private Stream? _stream;
        private int _requestId;
        private readonly bool _ownsStream;

        public RconClient(string ip, int port, string password, int timeoutMs = 1000)
        {
            _ip = ip;
            _port = port;
            _password = password;
            _timeoutMs = timeoutMs;
            _ownsStream = true;
        }

        /// <summary>Test hook: drive the protocol against an in-memory stream.</summary>
        internal RconClient(Stream stream, string password = "test")
        {
            _ip = "127.0.0.1";
            _port = 0;
            _password = password;
            _timeoutMs = 1000;
            _stream = stream;
            _ownsStream = false;
        }

        public void Connect()
        {
            _tcp = new TcpClient
            {
                ReceiveTimeout = _timeoutMs,
                SendTimeout = _timeoutMs,
            };
            _tcp.Connect(_ip, _port);
            NetworkStream network = _tcp.GetStream();
            network.ReadTimeout = _timeoutMs;
            network.WriteTimeout = _timeoutMs;
            _stream = network;
            Authenticate();
        }

        public string SendCommand(string command)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RCON not connected.");
            }

            _requestId++;
            int commandId = _requestId;
            byte[] packet = CreatePacket(commandId, ServerDataExecCommand, command);
            _stream.Write(packet, 0, packet.Length);

            _requestId++;
            int sentinelId = _requestId;
            byte[] sentinel = CreatePacket(sentinelId, ServerDataResponseValue, string.Empty);
            _stream.Write(sentinel, 0, sentinel.Length);

            StringBuilder body = new();
            while (true)
            {
                (int requestId, _, string chunk) = ReceiveResponse();
                if (requestId == sentinelId)
                {
                    break;
                }

                if (requestId == commandId)
                {
                    if (body.Length + chunk.Length > MaxBodyLength)
                    {
                        throw new InvalidOperationException("RCON response body too large.");
                    }

                    body.Append(chunk);
                }
            }

            return body.ToString();
        }

        public void Dispose()
        {
            if (_ownsStream)
            {
                _stream?.Dispose();
                _tcp?.Dispose();
            }
        }

        internal void Authenticate()
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RCON not connected.");
            }

            _requestId++;
            int authId = _requestId;
            byte[] auth = CreatePacket(authId, ServerDataAuth, _password);
            _stream.Write(auth, 0, auth.Length);

            while (true)
            {
                (int requestId, int responseType, _) = ReceiveResponse();
                if (requestId == -1)
                {
                    throw new InvalidOperationException("RCON authentication failed.");
                }

                if (responseType == ServerDataAuthResponse && requestId == authId)
                {
                    return;
                }
            }
        }

        internal static byte[] CreatePacket(int requestId, int packetType, string body)
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

        internal (int RequestId, int ResponseType, string Body) ReceiveResponse()
        {
            byte[] lengthBytes = new byte[4];
            ReadExact(lengthBytes);
            int packetLength = BitConverter.ToInt32(lengthBytes, 0);
            if (packetLength < 10 || packetLength > MaxPacketLength)
            {
                throw new InvalidOperationException($"RCON packet length out of range: {packetLength}.");
            }

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
