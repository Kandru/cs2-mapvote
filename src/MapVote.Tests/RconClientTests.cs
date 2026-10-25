using MapVote.Maps;

namespace MapVote.Tests;

public class RconClientTests
{
    [Fact]
    public void Authenticate_SkipsValuePacket_AcceptsAuthResponse()
    {
        using SplitStream pipe = new();
        // Server often replies with an empty RESPONSE_VALUE before AUTH_RESPONSE.
        pipe.EnqueueResponse(RconClient.CreatePacket(1, RconClient.ServerDataResponseValue, ""));
        pipe.EnqueueResponse(RconClient.CreatePacket(1, RconClient.ServerDataAuthResponse, ""));

        using RconClient client = new(pipe);
        client.Authenticate();
    }

    [Fact]
    public void Authenticate_RejectsIdMinusOne()
    {
        using SplitStream pipe = new();
        pipe.EnqueueResponse(RconClient.CreatePacket(-1, RconClient.ServerDataAuthResponse, ""));

        using RconClient client = new(pipe);
        Assert.Throws<InvalidOperationException>(client.Authenticate);
    }

    [Fact]
    public void SendCommand_ReadsMultiPacketUntilSentinel()
    {
        using SplitStream pipe = new();
        pipe.EnqueueResponse(RconClient.CreatePacket(1, RconClient.ServerDataResponseValue, "part-a"));
        pipe.EnqueueResponse(RconClient.CreatePacket(1, RconClient.ServerDataResponseValue, "part-b"));
        pipe.EnqueueResponse(RconClient.CreatePacket(2, RconClient.ServerDataResponseValue, ""));

        using RconClient client = new(pipe);
        string body = client.SendCommand("maps *");
        Assert.Equal("part-apart-b", body);
    }

    [Fact]
    public void ReceiveResponse_RejectsOversizedPacket()
    {
        using SplitStream pipe = new();
        byte[] length = BitConverter.GetBytes(RconClient.MaxPacketLength + 1);
        pipe.EnqueueResponse(length);

        using RconClient client = new(pipe);
        Assert.Throws<InvalidOperationException>(() => client.ReceiveResponse());
    }

    /// <summary>Reads come from a queued response buffer; writes are discarded.</summary>
    private sealed class SplitStream : Stream
    {
        private readonly Queue<byte> _reads = new();

        public void EnqueueResponse(byte[] packet)
        {
            foreach (byte b in packet)
            {
                _reads.Enqueue(b);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = 0;
            while (n < count && _reads.Count > 0)
            {
                buffer[offset + n] = _reads.Dequeue();
                n++;
            }

            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
