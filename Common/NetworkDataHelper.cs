using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Common
{
    public class NetworkDataHelper
    {
        public readonly Socket _socket;
        private readonly NetworkStream _stream;

        public NetworkDataHelper(Socket socket)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _socket.Blocking = true;
            _stream = new NetworkStream(_socket, false);
        }
        public byte[] Receive(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            var buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                EnsureConnectedOrThrow();

                int bytesRead;
                try
                {
                    bytesRead = _socket.Receive(buffer, offset, length - offset, SocketFlags.None);
                }
                catch (SocketException) { throw; }
                catch (ObjectDisposedException) { throw; }
                catch (IOException) { throw; }

                if (bytesRead == 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                offset += bytesRead;
            }
            return buffer;
        }

        public void Send(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int length = buffer.Length;
            int offset = 0;

            while (offset < length)
            {
                EnsureConnectedOrThrow();

                int bytesSent;
                try
                {
                    bytesSent = _socket.Send(buffer, offset, length - offset, SocketFlags.None);
                }
                catch (SocketException) { throw; }
                catch (ObjectDisposedException) { throw; }
                catch (IOException) { throw; }

                if (bytesSent == 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                offset += bytesSent;
            }
        }

        public async Task<byte[]> ReceiveAsync(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            var buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                EnsureConnectedOrThrow();

                int bytesRead;
                try
                {
                    bytesRead = await _stream.ReadAsync(buffer, offset, length - offset).ConfigureAwait(false);
                }
                catch (SocketException) { throw; }
                catch (ObjectDisposedException) { throw; }
                catch (IOException) { throw; }

                if (bytesRead == 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                offset += bytesRead;
            }
            return buffer;
        }

        public async Task SendAsync(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            try
            {
                await _stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch (SocketException) { throw; }
            catch (ObjectDisposedException) { throw; }
            catch (IOException) { throw; }
        }

        public byte[] ReceiveWithTimeout(int length, int msTimeout)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (msTimeout < 0) throw new ArgumentOutOfRangeException(nameof(msTimeout));

            int prev = _socket.ReceiveTimeout;
            try
            {
                _socket.ReceiveTimeout = msTimeout;
                return Receive(length);
            }
            finally
            {
                try { _socket.ReceiveTimeout = prev; } catch {  }
            }
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    if (_socket == null) return false;
                    if (!_socket.Connected) return false;

                    bool read = _socket.Poll(0, SelectMode.SelectRead);
                    bool noData = _socket.Available == 0;
                    if (read && noData) return false;

                    return true;
                }
                catch { return false; }
            }
        }

        public void CloseGracefully()
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }

        private void EnsureConnectedOrThrow()
        {
            if (!IsConnected)
            {
                throw new SocketException((int)SocketError.NotConnected);
            }
        }
    }
}
