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
    }
    static void PrintMenu()
    {
        Console.WriteLine("Menu:");
        Console.WriteLine("1. Create Class");
        Console.WriteLine("2. Join Class");
        Console.WriteLine("3. List Classes");
        Console.WriteLine("4. Leave Class");
        Console.WriteLine("5. History");
        Console.WriteLine("6. Exit");
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
        catch (Exception e)
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

            while (!loggedIn)
            {
                StartingMenu();
                string option = Console.ReadLine();
                int commandNum = -1;

                try
                {

                    commandNum = int.Parse(option) - 1;

                }
                catch (Exception e)
                {

                    Console.WriteLine("Invalid option. Please try again.");
                    continue;
                }

                Command command = (Command)commandNum;
                if (command == Command.Exit) break;

                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {

                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);

                }
                catch (SocketException e)
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


            while (loggedIn)
            {
                PrintMenu();
                string option = Console.ReadLine();
                int commandNum = -1;
                try
                {

                    commandNum = int.Parse(option) - 1;

                }
                catch (Exception e)
                {

                    Console.WriteLine("Invalid option. Please try again.");
                    continue;
                }

                CommandLogged command = (CommandLogged)commandNum;
                if (command == CommandLogged.Exit) break;

                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {

                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);

                }
                catch (SocketException e)
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
                        //running = JoinClass(networkDataHelper);
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.ListClasses:
                        running = ListClasses(networkDataHelper);
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.LeaveClass:
                        //running = LeaveClass(networkDataHelper);
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.History:
                        //running = History(networkDataHelper);
                        Console.WriteLine("Not implemented yet.");
                        break;
                    case CommandLogged.Exit:
                        running = logout(networkDataHelper);
                        break;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }

            }
            
        }
        Console.WriteLine("Conection interrupted...");
        clientSocket.Shutdown(SocketShutdown.Both);
        clientSocket.Close();
    }
    internal static bool logout(NetworkDataHelper networkDataHelper)
    {
        try { 
            if (loggedOut)
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
        catch (SocketException e)
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
            if (classesString == "")
            {
                Console.WriteLine("No classes available.");
                return true;
            }
            string[] classes = classesString.Split('|');
            Console.WriteLine("Available classes:");
            foreach (string classInfo in classes)
            {
                string[] classDetails = classInfo.Split(',');
                Console.WriteLine($"Class Name: {classDetails[0]}, Description: {classDetails[1]}, Duration: {classDetails[2]} minutes, Quotas: {classDetails[3]}");
            }
            return true;
        }
        catch (SocketException e)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }


    internal static bool Register(NetworkDataHelper networkDataHelpe)
    {
        Console.Write("Enter username: ");
        string? userName = Console.ReadLine();
        if (userName == "")
        {
            Console.WriteLine("Username can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (password == "")
        {
            Console.WriteLine("Password can't be null.");
            return true;
        }

        string userAndPass = userName + "|" + password;
        byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
        int commandLength = commandData.Length;
        byte[] commandDataLenght = BitConverter.GetBytes(commandLength);

        try
        {
            networkDataHelpe.Send(commandDataLenght);
            networkDataHelpe.Send(commandData);

            byte[] result = networkDataHelpe.Receive(1);
            bool loggedIn = BitConverter.ToBoolean(result);

            if (loggedIn)
            {
                Console.WriteLine("Registered successfully.");
            }
            else
            {
                Console.WriteLine("Register failed, that username already exists");
            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    internal static bool LogIn(NetworkDataHelper networkDataHelper)
    {
        Console.Write("Enter username: ");
        string? userName = Console.ReadLine();
        if (userName == null)
        {
            Console.WriteLine("Username can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (password == null)
        {
            Console.WriteLine("Password can't be null.");
            return true;
        }
        string userAndPass = userName + "|" + password;
        byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
        int commandLength = commandData.Length;
        byte[] commandDataLenght = BitConverter.GetBytes(commandLength);
        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);
            byte[] result = networkDataHelper.Receive(1);
            bool loggedIn = BitConverter.ToBoolean(result);
            if (loggedIn)
            {
                Console.WriteLine("Logged in successfully.");
                Client.loggedIn = true;
                userLogged = userName;
            }
            else
            {
                Console.WriteLine("Login failed, incorrect username or password.");
                return true;

            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }

    }

    public static bool JoinClass(NetworkDataHelper networkDataHelper)
    {

        return true;
    }

    public static bool CreateClass(NetworkDataHelper networkDataHelper)
    {
        Console.Write("Enter class name: ");
        string? className = Console.ReadLine();
        if (className == "")
        {
            Console.WriteLine("Class name can't be null.");
            return true;
        }
        Console.Write("Enter class description: ");
        string? classDescription = Console.ReadLine();
        if (classDescription == "")
        {
            Console.WriteLine("Class description can't be null.");
            return true;
        }
        Console.Write("Enter class duration (in minutes): ");
        string? classDuration = Console.ReadLine();

        if (classDuration == "")
        {
            Console.WriteLine("class duration cannot be null");
            return true;
        }
        Console.WriteLine("Enter class maximum quotas: ");
        string? classuQotas = Console.ReadLine();
        if (classuQotas == "")
        {
            Console.WriteLine("Class quotas cannot be null");
            return true;
        }
        string ClassData = className + "|" + classDescription + "|" + classDuration + "|" + classuQotas + "|" + userLogged;
        byte[] commandData = Encoding.UTF8.GetBytes(ClassData);
        int commandLength = commandData.Length;
        byte[] commandDataLenght = BitConverter.GetBytes(commandLength);
        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);
            byte[] result = networkDataHelper.Receive(1);
            bool classCreated = BitConverter.ToBoolean(result);
            if (classCreated)
            {
                Console.WriteLine("Class created successfully.");
            }
            else
            {
                Console.WriteLine("Class creation failed, try again.");
            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }
}

