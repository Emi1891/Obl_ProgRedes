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
        Console.WriteLine("\nWelcome to the Client Application");
        Console.WriteLine("1. Register");
        Console.WriteLine("2. Login");
        Console.WriteLine("3. Exit");
        Console.Write("Choose an option: ");
    }
    static void PrintMenu()
    {
        Console.WriteLine("\nMenu:");
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
            Console.WriteLine("\nCould not connect to server: shutting down client...");
            Thread.Sleep(2000);
            return;

        }

        Console.WriteLine("\nConnected to server");

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

                    Console.WriteLine("\nInvalid option. Please try again.");
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
                        Console.WriteLine("\nInvalid option.");
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

                    Console.WriteLine("\nInvalid option. Please try again.");
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
                        //running = logout(networkDataHelper);
                        break;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }

            }
            
        }
        Console.WriteLine("\nConection interrupted...");
        clientSocket.Shutdown(SocketShutdown.Both);
        clientSocket.Close();
    }

    //internal static bool logout(NetworkDataHelper networkDataHelper)
    //{
    //    try { 
    //        if (loggedOut)
    //        {
    //            Console.WriteLine("Logged out successfully.");
    //            Client.loggedIn = false;
    //            userLogged = null;
    //        }
    //        else
    //        {
    //            Console.WriteLine("Logout failed, try again.");
    //        }
    //        return true;
    //    }
    //    catch (SocketException e)
    //    {
    //        Console.WriteLine("Connection lost to server.");
    //        return false;
    //    }
    //}

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
                Console.WriteLine("\nNo classes available.");
                return true;
            }
            string[] classes = classesString.Split('|');
            Console.WriteLine("\nAvailable classes:");
            foreach (string classInfo in classes)
            {
                string[] classDetails = classInfo.Split(',');
                Console.WriteLine($"Class Name: {classDetails[0]}, Description: {classDetails[1]}, Duration: {classDetails[2]} minutes, Quotas: {classDetails[3]}");
            }
            return true;
        }
        catch (SocketException e)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }


    internal static bool Register(NetworkDataHelper networkDataHelpe)
    {
        Console.Write("\nEnter username: ");
        string? userName = Console.ReadLine();
        if (userName == "")
        {
            Console.WriteLine("\nUsername can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (password == "")
        {
            Console.WriteLine("\nPassword can't be null.");
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
                Console.WriteLine("\nRegistered successfully.");
            }
            else
            {
                Console.WriteLine("\nRegister failed, that username already exists");
            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }

    internal static bool LogIn(NetworkDataHelper networkDataHelper)
    {
        Console.Write("\nEnter username: ");
        string? userName = Console.ReadLine();
        if (userName == null)
        {
            Console.WriteLine("\nUsername can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (password == null)
        {
            Console.WriteLine("\nPassword can't be null.");
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
                Console.WriteLine("\nLogged in successfully.");
                Client.loggedIn = true;
                userLogged = userName;
            }
            else
            {
                Console.WriteLine("\nLogin failed, incorrect username or password.");
                return true;

            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }

    }

    public static bool JoinClass(NetworkDataHelper networkDataHelper)
    {

        return true;
    }

    public static bool CreateClass(NetworkDataHelper networkDataHelper)
    {
        Console.Write("\nEnter class name: ");
        string? className = Console.ReadLine();
        if (className == "")
        {
            Console.WriteLine("\nClass name can't be null.");
            return true;
        }

        Console.Write("Enter class description: ");
        string? classDescription = Console.ReadLine();
        if (classDescription == "")
        {
            Console.WriteLine("\nClass description can't be null.");
            return true;
        }

        Console.Write("Enter class maximum quotas: ");
        string? classuQotas = Console.ReadLine();
        if (classuQotas == "")
        {
            Console.WriteLine("\nClass quotas cannot be null");
            return true;
        }

        Console.Write("Enter class start date (DD/MM/YYYY) \nIf left blank, default date (today) will be applied: ");
        string? classStartDate = Console.ReadLine();
        if (classStartDate == "")
        {
            classStartDate = DateTime.Now.ToString("dd/MM/yyyy");
            Console.WriteLine("Default date applied: " + classStartDate);
        } 
        else
        {
            // Agregar validación de formato de fecha
        }

        Console.Write("Enter class start time (HH:MM): ");
        string? classStartTime = Console.ReadLine();
        if (classStartTime == "")
        {
            Console.WriteLine("\nClass start time cannot be null");
            return true;
        }
        else
        {
            // Agregar validación de formato de hora
        }

        Console.Write("Enter class duration (in minutes): ");
        string? classDuration = Console.ReadLine();
        if (classDuration == "")
        {
            Console.WriteLine("\nClass duration cannot be null");
            return true;
        }

        Console.Write("Would you like to upload a cover image? (y/n): ");
        string? hasCoverImage = Console.ReadLine();
        if (hasCoverImage == "" || (hasCoverImage != "y" && hasCoverImage != "n"))
        {
            Console.WriteLine("\nInvalid input. Enter 'y' or 'n'.");
            return true;
        }

        string ClassData = className + "|" + classDescription + "|" + classuQotas + "|" + classStartDate + "|" + classStartTime + "|" + classDuration + "|" + hasCoverImage + "|" + userLogged;
        byte[] commandData = Encoding.UTF8.GetBytes(ClassData);
        int commandLength = commandData.Length;
        byte[] commandDataLenght = BitConverter.GetBytes(commandLength);
        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);

            if (hasCoverImage == "y")
            {
                Console.Write("Type the image path: ");
                string? filePath = Console.ReadLine();

                FileInfo fileInfo = new FileInfo(filePath);

                string fileName = fileInfo.Name;
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

                int fileNameLength = fileNameBytes.Length;
                byte[] fileNameLengthBytes = BitConverter.GetBytes(fileNameLength);

                long fileSize = fileInfo.Length;
                byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);

                networkDataHelper.Send(fileNameLengthBytes);
                networkDataHelper.Send(fileNameBytes);
                networkDataHelper.Send(fileSizeBytes);

                long offset = 0;
                long partCount = Protocol.CalculateFileParts(fileSize);
                long currentPart = 1;

                FileStreamHelper fileStreamHelper = new FileStreamHelper();

                while (offset < fileSize)
                {
                    byte[] buffer;
                    bool isLastPart = currentPart == partCount;

                    if (!isLastPart)
                    {
                        Console.WriteLine($"Sending part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                        buffer = fileStreamHelper.Read(filePath, offset, Protocol.MaxFileChunkSize);
                        offset += Protocol.MaxFileChunkSize;
                    }
                    else
                    {
                        long lastPartSize = fileSize - offset;
                        Console.WriteLine($"Sending part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                        buffer = fileStreamHelper.Read(filePath, offset, (int)lastPartSize);
                        offset += lastPartSize;
                    }
                    networkDataHelper.Send(buffer);
                    currentPart++;
                }
            }

            byte[] result = networkDataHelper.Receive(1);
            bool classCreated = BitConverter.ToBoolean(result);

            if (classCreated)
            {
                Console.WriteLine("\nClass created successfully.");
            }
            else
            {
                Console.WriteLine("\nClass creation failed, try again.");
            }
            return true;

        }
        catch (SocketException e)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }
}

