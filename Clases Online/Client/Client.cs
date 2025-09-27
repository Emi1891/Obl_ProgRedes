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


    static readonly SettingManager settingsMgr = new SettingManager();

    static void PrintMenu()
    {
        Console.WriteLine("Menu:");
        Console.WriteLine("1. Register");
        Console.WriteLine("2. Login");
        Console.WriteLine("3. Create Class");
        Console.WriteLine("4. Exit");
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
                case Command.CreateClass:
                    running = CreateClass(networkDataHelper);
                    break;
                default:
                    Console.WriteLine("Invalid option.");
                    break;

            }
        }
        Console.WriteLine("Conection interrupted...");
        clientSocket.Shutdown(SocketShutdown.Both);
        clientSocket.Close();

    }

    internal static bool Register(NetworkDataHelper networkDataHelpe)
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

    public static bool CreateClass(NetworkDataHelper networkDataHelper)
    {
        Console.Write("Enter class name: ");
        string? className = Console.ReadLine();
        if (className == null)
        {
            Console.WriteLine("Class name can't be null.");
            return true;
        }
        Console.Write("Enter class description: ");
        string? classDescription = Console.ReadLine();
        if (classDescription == null)
        {
            Console.WriteLine("Class description can't be null.");
            return true;
        }
        Console.Write("Enter class duration (in minutes): ");
        string? classDuration = Console.ReadLine();

        if (classDuration == null)
        {
            Console.WriteLine("class duration cannot be null");
            return true;
        }
        Console.WriteLine("Class maximum quotas");
        string? classuQotas = Console.ReadLine();
        if (classuQotas == null)
        {
            Console.WriteLine("Class quotas cannot be null");
            return true;
        }
        string ClassData = className + "|" + classDescription + "|" + classDuration + "|" + classuQotas;
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

