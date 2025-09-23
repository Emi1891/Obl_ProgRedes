public class Client
{
    static void PrintMenu()
    {
        Console.WriteLine("Menu:");
        Console.WriteLine("1. Register");
        Console.WriteLine("2. Login");
        Console.WriteLine("3. Create Class");
        Console.WriteLine("4. Exit");
        Console.Write("Choose an option: ");
    }
    
    static void Main(){
        Console.WriteLine("Starting Client Aplicaction ... ");
        
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        IPAddress ClientIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ClientIpConfigKey));
        }
}
