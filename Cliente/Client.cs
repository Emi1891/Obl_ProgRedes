
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Cliente;hjhj

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
        int ClientPort = int.Parse(settingsMgr.ReadSetting(ClientConfig.ClientPortConfigKey));
        IPEndPoint clientEndPoint = new IPEndPoint(ClientIp, ClientPort);
        clientSocket.Bind(clientEndPoint);
        
        IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ServerIpConfigKey));
        int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
        IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);
        
        try{
            clientSocket.Connect(serverEndPoint);
        }
        catch(Exception e){
            Console.WriteLine("Could not connect to server: shutting down client...");
            Thread.Sleep(2000);
            return;
        
        }
        
        Console.WriteLine("Connected to server");
        
        bool running = true;
        NetworkDataHelper dataHelper = new NetworkDataHelper(clientSocket);
        
        while(running){
            PrintMenu();
            string option = Console.ReadLine();
            switch(option){
                case "1":
                    Console.Write("Enter username: ");
                    string regUsername = Console.ReadLine();
                    Console.Write("Enter password: ");
                    string regPassword = Console.ReadLine();
                    string regMessage = $"REGISTER|{regUsername}|{regPassword}";
                    dataHelper.SendData(regMessage);
                    string regResponse = dataHelper.ReceiveData();
                    Console.WriteLine($"Server response: {regResponse}");
                    break;
                case "2":
                    Console.Write("Enter username: ");
                    string loginUsername = Console.ReadLine();
                    Console.Write("Enter password: ");
                    string loginPassword = Console.ReadLine();
                    string loginMessage = $"LOGIN|{loginUsername}|{loginPassword}";
                    dataHelper.SendData(loginMessage);
                    string loginResponse = dataHelper.ReceiveData();
                    Console.WriteLine($"Server response: {loginResponse}");
                    break;
                case "3":
                    Console.Write("Enter class name: ");
                    string className = Console.ReadLine();
                    string createClassMessage = $"CREATE_CLASS|{className}";
                    dataHelper.SendData(createClassMessage);
                    string createClassResponse = dataHelper.ReceiveData();
                    Console.WriteLine($"Server response: {createClassResponse}");
                    break;
                case "4":
                    running = false;
                    break;
                default:
                    Console.WriteLine("Invalid option. Please try again.");
                    break;
            }
        }
}
