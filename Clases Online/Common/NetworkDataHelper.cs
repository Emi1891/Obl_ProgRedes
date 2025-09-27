using System.Net.Sockets;
namespace Common;

public class NetworkDataHelper
{
    public readonly Socket _socket;
    
    public NetworkDataHelper(Socket socket){
        _socket = socket;
    }

    public byte[] Receive(int lenght)
    {
        byte[] buffer = new byte[lenght];
        int offset = 0;
        
        while (offset < lenght)
        {
            int bytesRead = _socket.Receive(buffer, offset, lenght - offset, SocketFlags.None);
            if (bytesRead == 0)
            {
                throw new Exception("Connection closed by remote host");
            }
            offset += bytesRead;
        }

        return buffer;
    }

    public void Send(byte[] buffer)
    {
        int length = buffer.Length;
        int offset = 0;
        while(offset < length)
        {
            int bytesSent = _socket.Send(buffer, offset, length - offset, SocketFlags.None);
            if (bytesSent == 0)
            {
                throw new Exception("Connection closed by remote host");
            }
            offset += bytesSent;
        }
    }
}