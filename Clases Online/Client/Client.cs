using Cliente;
using Common;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client;

public class Client
{
    public static string? userLogged;
    public static bool loggedIn = false;

    static readonly SettingManager settingsMgr = new SettingManager();

    static void StartingMenu()
    {
        Console.WriteLine("Welcome to the Client Application");
        Console.WriteLine("Please choose an option:");
        Console.WriteLine("1. Register");
        Console.WriteLine("2. Login");
        Console.WriteLine("3. Exit");
        Console.Write("Choose an option: ");
    }
    static void PrintMenu()
    {
        Console.WriteLine("Menu:");
        Console.WriteLine("1. Create Class");
        Console.WriteLine("2. Join Class");
        Console.WriteLine("3. List Classes");
        Console.WriteLine("4. Leave Class");
        Console.WriteLine("5. History");
        Console.WriteLine("6. Logout");
        Console.Write("Choose an option: ");
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Starting Client Aplicaction ... ");

        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        IPAddress ClientIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ClientIpConfigKey));
        int ClientPort = int.Parse(settingsMgr.ReadSetting(ClientConfig.ClientPortConfigKey));
        IPEndPoint clientEndPoint = new IPEndPoint(ClientIp, ClientPort);
        clientSocket.Bind(clientEndPoint);

        IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
        int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
        IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

        try
        {
            clientSocket.Connect(serverEndPoint);
        }
        catch (Exception)
        {
            Console.WriteLine("Could not connect to server: shutting down client...");
            Thread.Sleep(2000);
            return;
        }

        Console.WriteLine("Connected to server");

        bool running = true;
        NetworkDataHelper networkDataHelper = new NetworkDataHelper(clientSocket);

        while (running)
        {
            while (!loggedIn && running)
            {
                StartingMenu();
                string option = Console.ReadLine();
                int commandNum = -1;

                try
                {
                    commandNum = int.Parse(option) - 1;
                }
                catch
                {
                    Console.WriteLine("Invalid option. Please try again.");
                    continue;
                }

                Command command = (Command)commandNum;
                if (command == Command.Exit)
                {
                    running = false;
                    break;
                }
                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {
                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);
                }
                catch (SocketException)
                {
                    running = false;
                    break;
                }

                switch (command)
                {
                    case Command.Register:
                        running = Register(networkDataHelper);
                        break;
                    case Command.Login:
                        running = LogIn(networkDataHelper);
                        break;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }
            }

            while (loggedIn && running)
            {
                PrintMenu();
                string option = Console.ReadLine();
                int commandNum = -1;
                try
                {
                    commandNum = int.Parse(option) - 1;
                }
                catch
                {
                    Console.WriteLine("Invalid option. Please try again.");
                    continue;
                }

                CommandLogged command = (CommandLogged)commandNum;
                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {
                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);
                }
                catch (SocketException)
                {
                    running = false;
                    break;
                }

                switch (command)
                {
                    case CommandLogged.CreateClass:
                        running = CreateClass(networkDataHelper);
                        break;
                    case CommandLogged.JoinClass:
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.ListClasses:
                        running = ListClasses(networkDataHelper);
                        break;
                    case CommandLogged.LeaveClass:
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.History:
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.Exit:
                        running = Logout(networkDataHelper);
                        break;

                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }
            }
        }

        Console.WriteLine("Conection interrupted...");
        try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
        clientSocket.Close();
    }
    internal static bool Logout(NetworkDataHelper networkDataHelper)
    {
        try
        {
            byte[] len0 = BitConverter.GetBytes(0);
            networkDataHelper.Send(len0);
            byte[] result = networkDataHelper.Receive(1);
            bool ok = BitConverter.ToBoolean(result);
            if (ok)
            {
                Console.WriteLine("Logged out successfully.");
                Client.loggedIn = false;
                userLogged = null;
            }
            else
            {
                Console.WriteLine("Logout failed, try again.");
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    internal static bool ListClasses(NetworkDataHelper networkDataHelper)
    {
        try
        {
            byte[] classesLengthBytes = networkDataHelper.Receive(4);
            int classesLength = BitConverter.ToInt32(classesLengthBytes);
            byte[] classesBytes = networkDataHelper.Receive(classesLength);
            string classesString = Encoding.UTF8.GetString(classesBytes);
            if (string.IsNullOrWhiteSpace(classesString))
            {
                Console.WriteLine("No classes available.");
                return true;
            }

            Console.WriteLine("Available classes:");
            foreach (var line in classesString.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine(line.Trim());
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    internal static bool Register(NetworkDataHelper networkDataHelpe)
    {
        Console.Write("Enter username: ");
        string? userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("Username can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Password can't be null.");
            return true;
        }

        string userAndPass = userName + "|" + password;
        byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
        byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

        try
        {
            networkDataHelpe.Send(commandDataLenght);
            networkDataHelpe.Send(commandData);

            byte[] result = networkDataHelpe.Receive(1);
            bool ok = BitConverter.ToBoolean(result);

            Console.WriteLine(ok ? "Registered successfully." : "Register failed, that username already exists");
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    internal static bool LogIn(NetworkDataHelper networkDataHelper)
    {
        Console.Write("Enter username: ");
        string? userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("Username can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Password can't be null.");
            return true;
        }

        string userAndPass = userName + "|" + password;
        byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
        byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);

            byte[] result = networkDataHelper.Receive(1);
            bool ok = BitConverter.ToBoolean(result);
            if (ok)
            {
                Console.WriteLine("Logged in successfully.");
                Client.loggedIn = true;
                userLogged = userName;
            }
            else
            {
                Console.WriteLine("Login failed, incorrect username or password.");
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    public static bool JoinClass(NetworkDataHelper networkDataHelper)
    {
        Console.WriteLine("Not implemented yet.");
        return true;
    }

    public static bool CreateClass(NetworkDataHelper networkDataHelper)
    {
        Console.Write("Enter class name: ");
        string? className = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(className))
        {
            Console.WriteLine("Class name can't be null.");
            return true;
        }

        Console.Write("Enter class description: ");
        string? classDescription = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(classDescription))
        {
            Console.WriteLine("Class description can't be null.");
            return true;
        }

        Console.Write("Enter class duration (in minutes): ");
        string? classDuration = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(classDuration))
        {
            Console.WriteLine("class duration cannot be null");
            return true;
        }

        Console.Write("Enter class maximum quotas: ");
        string? classuQotas = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(classuQotas))
        {
            Console.WriteLine("Class quotas cannot be null");
            return true;
        }

        string ClassData = className + "|" + classDescription + "|" + classDuration + "|" + classuQotas + "|" + userLogged;
        byte[] commandData = Encoding.UTF8.GetBytes(ClassData);
        byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);

            byte[] result = networkDataHelper.Receive(1);
            bool classCreated = BitConverter.ToBoolean(result);
            Console.WriteLine(classCreated ? "Class created successfully." : "Class creation failed, try again.");
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }
}
