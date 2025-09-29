using Cliente;
using Common;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client;

public class Client
{
    public static string? userLogged;
    public static bool loggedIn = false;

    static readonly SettingManager settingsMgr = new SettingManager();

    static Socket? _socket;
    static NetworkDataHelper? _ndh;

    static void StartingMenu()
    {
        Console.WriteLine("\nWelcome to the Client Application");
        Console.WriteLine("1. Register");
        Console.WriteLine("2. Login");
        Console.WriteLine("3. Exit");
        Console.Write("\nChoose an option: ");
    }

    static void PrintMenu()
    {
        Console.WriteLine("\nMenu:");
        Console.WriteLine("1. Create Class");
        Console.WriteLine("2. Join Class");
        Console.WriteLine("3. List Classes");
        Console.WriteLine("4. Leave Class");
        Console.WriteLine("5. History");
        Console.WriteLine("6. Logout");
        Console.Write("\nChoose an option: ");
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Starting Client Aplicaction ... ");

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        clientSocket.ReceiveTimeout = 10000;
        clientSocket.SendTimeout = 10000;
        clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        try
        {
            string clientIpStr = settingsMgr.ReadSetting(ClientConfig.ClientIpConfigKey);
            string clientPortStr = settingsMgr.ReadSetting(ClientConfig.ClientPortConfigKey);
            if (!string.IsNullOrWhiteSpace(clientIpStr) && !string.IsNullOrWhiteSpace(clientPortStr))
            {
                IPAddress clientIp = IPAddress.Parse(clientIpStr);
                int clientPort = int.Parse(clientPortStr);
                IPEndPoint clientEndPoint = new IPEndPoint(clientIp, clientPort);
                clientSocket.Bind(clientEndPoint);
                Console.WriteLine($"Bound client socket to {clientIp}:{clientPort}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: could not bind client socket to local endpoint: {ex.Message}");
        }

        IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
        int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
        IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

        try
        {
            clientSocket.Connect(serverEndPoint);
        }
        catch (Exception)
        {
            Console.WriteLine("\nCould not connect to server: shutting down client...");
            Thread.Sleep(2000);
            return;
        }

        Console.WriteLine("\nConnected to server");

        _socket = clientSocket;
        var networkDataHelper = new NetworkDataHelper(clientSocket);
        _ndh = networkDataHelper;

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; GracefulExit(); };
        AppDomain.CurrentDomain.ProcessExit += (_, __) => { GracefulExit(); };

        bool running = true;

        while (running)
        {
            while (!loggedIn && running)
            {
                StartingMenu();
                string? option = Console.ReadLine();
                int commandNum = -1;

                try
                {
                    commandNum = int.Parse(option ?? string.Empty) - 1;
                }
                catch
                {
                    Console.WriteLine("\nInvalid option. Please try again.");
                    continue;
                }

                Command command = (Command)commandNum;
                if (command == Command.Exit)
                {
                    running = false;
                    break;
                }

                if (!clientSocket.Connected) { running = false; break; }

                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {
                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);
                }
                catch (SocketException) { running = false; break; }
                catch (ObjectDisposedException) { running = false; break; }
                catch (IOException) { running = false; break; }

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

            while (loggedIn && running)
            {
                PrintMenu();
                string? option = Console.ReadLine();
                int commandNum = -1;
                try
                {
                    commandNum = int.Parse(option ?? string.Empty) - 1;
                }
                catch
                {
                    Console.WriteLine("\nInvalid option. Please try again.");
                    continue;
                }

                CommandLogged command = (CommandLogged)commandNum;

                if (!clientSocket.Connected) { running = false; break; }

                byte[] messegeType = Encoding.UTF8.GetBytes(Protocol.request);
                string commandString = ((int)command).ToString("D2");
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandString);

                try
                {
                    networkDataHelper.Send(messegeType);
                    networkDataHelper.Send(commandBytes);
                }
                catch (SocketException) { running = false; break; }
                catch (ObjectDisposedException) { running = false; break; }
                catch (IOException) { running = false; break; }

                switch (command)
                {
                    case CommandLogged.CreateClass:
                        running = CreateClass(networkDataHelper);
                        break;
                    case CommandLogged.JoinClass:
                        running = JoinClass(networkDataHelper);
                        break;
                    case CommandLogged.ListClasses:
                        running = ListClasses(networkDataHelper);
                        break;
                    case CommandLogged.LeaveClass:
                        running = LeaveClass(networkDataHelper);
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

        Console.WriteLine("\nConection interrupted...");
        try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
        try { clientSocket.Close(); } catch { }
    }
    internal static bool ShowClasesofTheUser(NetworkDataHelper networkDataHelper)
    {
        byte[] nombreUsuario = Encoding.UTF8.GetBytes(userLogged);
        byte[] nombreUsuarioLength = BitConverter.GetBytes(nombreUsuario.Length);
        try
        {
            networkDataHelper.Send(nombreUsuarioLength);
            networkDataHelper.Send(nombreUsuario);
            byte[] classCountBytes = networkDataHelper.Receive(4);
            int classCount = BitConverter.ToInt32(classCountBytes);
            if (classCount == 0)
            {
                Console.WriteLine("\nYou are not enrolled in any classes.");
                return true;
            }
            Console.WriteLine($"\nYou are enrolled in {classCount} classes:");

            for (int i = 0; i < classCount; i++)
            {
                byte[] classDataLengthBytes = networkDataHelper.Receive(4);
                int classDataLength = BitConverter.ToInt32(classDataLengthBytes);
                byte[] classDataBytes = networkDataHelper.Receive(classDataLength);
                string classData = Encoding.UTF8.GetString(classDataBytes);
                string[] classDataSplit = classData.Split('|');
                Console.WriteLine($"\nClass {i + 1}: {classDataSplit[0]}");
                Console.WriteLine($"Description: {classDataSplit[1]}");
                Console.WriteLine($"Date: {classDataSplit[3]}");
                Console.WriteLine($"Time: {classDataSplit[4]}");
                Console.WriteLine($"Duration: {classDataSplit[5]} minutes");
                Console.WriteLine($"Taken places: {classDataSplit[2]}");
                Console.WriteLine($"Creator: {classDataSplit[7]}");

                string hasImg = "Yes";
                if (classDataSplit[6] == "No image uploaded")
                {
                    hasImg = "No";
                }

                Console.WriteLine($"Has cover image: {hasImg}");
            }

            
        }
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        return true;
    }

    internal static bool LeaveClass(NetworkDataHelper networkDataHelper)
    {
        ShowClasesofTheUser(networkDataHelper);
        Console.Write("\nType the name of the class you want to leave: ");
        string? className = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(className))
        {
            Console.WriteLine("\nClass name can't be null.");
            return true;
        }
        byte[] commandData = Encoding.UTF8.GetBytes(className);
        byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);
        try
        {
            networkDataHelper.Send(commandDataLenght);
            networkDataHelper.Send(commandData);
            byte[] result = networkDataHelper.Receive(1);
            bool ok = BitConverter.ToBoolean(result);
            if (ok)
            {
                Console.WriteLine("\nLeft class successfully.");
            }
            else
            {
                Console.WriteLine("\nCould not leave class, you might not be enrolled in it.");
            }
        }
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        return true;
    }

    static void GracefulExit()
    {
        try
        {
            if (loggedIn && _ndh != null && _socket != null && _socket.Connected)
            {
                byte[] len0 = BitConverter.GetBytes(0);
                int prev = _socket.ReceiveTimeout;
                try
                {
                    _ndh.Send(len0);
                    _socket.ReceiveTimeout = 1500;
                    try { _ndh.Receive(1); } catch { }
                }
                finally
                {
                    if (_socket != null) _socket.ReceiveTimeout = prev;
                }
            }
        }
        catch { }
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket?.Close(); } catch { }
    }

    internal static bool Logout(NetworkDataHelper networkDataHelper)
    {
        try
        {
            byte[] len0 = BitConverter.GetBytes(0);
            networkDataHelper.Send(len0);

            var s = _socket;
            if (s != null)
            {
                int prev = s.ReceiveTimeout;
                try
                {
                    s.ReceiveTimeout = 2000;
                    byte[] result = networkDataHelper.Receive(1);
                    bool ok = BitConverter.ToBoolean(result);
                    if (ok)
                    {
                        Console.WriteLine("Logged out successfully.");
                        loggedIn = false;
                        userLogged = null;
                    }
                    else
                    {
                        Console.WriteLine("Logout failed, try again.");
                    }
                }
                catch
                {
                    loggedIn = false;
                    userLogged = null;
                }
                finally
                {
                    if (s != null) s.ReceiveTimeout = prev;
                }
            }
            else
            {
                loggedIn = false;
                userLogged = null;
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
        catch (IOException)
        {
            Console.WriteLine("Connection lost to server.");
            return false;
        }
    }

    internal static bool ListClasses(NetworkDataHelper networkDataHelper)
    {
        string fileName = "";
        string className = "";

        try
        {
            byte[] len0 = BitConverter.GetBytes(0);
            networkDataHelper.Send(len0);

            byte[] classCountBytes = networkDataHelper.Receive(4);
            int classCount = BitConverter.ToInt32(classCountBytes);
            if (classCount == 0)
            {
                Console.WriteLine("\nNo classes available.");
                return true;
            }
            Console.WriteLine($"\nThere are {classCount} classes available:");

            List<string[]> imgUrls = new List<string[]>();

            for (int i = 0; i < classCount; i++)
            {
                byte[] classDataLengthBytes = networkDataHelper.Receive(4);
                int classDataLength = BitConverter.ToInt32(classDataLengthBytes);
                byte[] classDataBytes = networkDataHelper.Receive(classDataLength);
                string classData = Encoding.UTF8.GetString(classDataBytes);
                string[] classDataSplit = classData.Split('|');

                Console.WriteLine($"\nClass {i + 1}: {classDataSplit[0]}");
                Console.WriteLine($"Description: {classDataSplit[1]}");
                Console.WriteLine($"Date: {classDataSplit[3]}");
                Console.WriteLine($"Time: {classDataSplit[4]}");
                Console.WriteLine($"Duration: {classDataSplit[5]} minutes");
                Console.WriteLine($"Taken places: {classDataSplit[2]}");
                Console.WriteLine($"Creator: {classDataSplit[7]}");
                Console.WriteLine($"Link: {classDataSplit[8]}");

                string hasImg = "Yes";
                if (classDataSplit[6] == "No image uploaded")
                {
                    hasImg = "No";
                }

                Console.WriteLine($"Has cover image: {hasImg}");
                imgUrls.Add(new string[] { (i + 1).ToString(), classDataSplit[6], classDataSplit[0] });
            }

            bool wantImg = false;
            Console.Write("\nTo download any cover image, choose the desired class. Otherwise, type 'q': ");
            string? option = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(option))
            {
                Console.WriteLine("\nInvalid input.");
                networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                return true;
            }
            if (option == "q")
            {
                networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                return true;
            }

            try
            {
                Console.WriteLine($"\nTexto ingresado: {option}");

                int chosenClass = int.Parse(option);

                Console.WriteLine("\nOpcion parseada correctamente");
                Console.WriteLine($"\nNumero ingresado: {chosenClass}");

                if (chosenClass > classCount || chosenClass < 1)
                {
                    Console.WriteLine("\nInvalid input. Number out of range.");
                    networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                    return true;
                }
                string classPath = imgUrls.Find(c => int.Parse(c[0]) == chosenClass)[1];
                className = imgUrls.Find(c => int.Parse(c[0]) == chosenClass)[2];
                if (classPath == "No image uploaded")
                {
                    Console.WriteLine("\nThe selected class has no cover image.");
                    networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                    return true;
                }

                wantImg = true;

                Console.WriteLine("Envio al server que quiero la imagen");

                networkDataHelper.Send(BitConverter.GetBytes(wantImg));

                Console.WriteLine("Envio path al server");

                byte[] pathBytes = Encoding.UTF8.GetBytes(classPath);
                byte[] pathBytesLength = BitConverter.GetBytes(pathBytes.Length);
                networkDataHelper.Send(pathBytesLength);
                networkDataHelper.Send(pathBytes);

                Console.WriteLine("Path enviado");

                byte[] fileNameLengthBuffer = networkDataHelper.Receive(Protocol.FileNameLenghtSize);
                int fileNameLength = BitConverter.ToInt32(fileNameLengthBuffer);

                Console.WriteLine("Largo del nombre recibido");

                byte[] fileNameBuffer = networkDataHelper.Receive(fileNameLength);
                fileName = Encoding.UTF8.GetString(fileNameBuffer);

                Console.WriteLine("Nombre recibido");

                byte[] fileSizeBuffer = networkDataHelper.Receive(Protocol.FileSizeLenghtSize);
                long fileSize = BitConverter.ToInt64(fileSizeBuffer);

                Console.WriteLine("Tamano del archivo recivido");

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
                        Console.WriteLine($"Receiving part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                        buffer = networkDataHelper.Receive(Protocol.MaxFileChunkSize);
                        offset += Protocol.MaxFileChunkSize;
                    }
                    else
                    {
                        long lastPartSize = fileSize - offset;
                        Console.WriteLine($"Receiving part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                        buffer = networkDataHelper.Receive((int)lastPartSize);
                        offset += lastPartSize;
                    }
                    Directory.CreateDirectory($"Images/{className}");
                    fileStreamHelper.Write($"Images/{className}/" + fileName, buffer);
                    currentPart++;
                }

                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                Console.WriteLine($"File {fileName} received successfully at: {imgPath}");
            }
            catch (FormatException e)
            {
                Console.WriteLine($"\nInvalid input, try again. ({e.Message})");

                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                {
                    try
                    {
                        File.Delete(imgPath);
                        Console.WriteLine($"Incomplete file {fileName} deleted from: {imgPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not delete incomplete file {fileName} from: {imgPath}. ({ex.Message})");
                    }
                }

                return true;
            }

            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");

            string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
            if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
            {
                try
                {
                    File.Delete(imgPath);
                    Console.WriteLine($"Incomplete file {fileName} deleted from: {imgPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not delete incomplete file {fileName} from: {imgPath}. ({ex.Message})");
                }
            }

            return false;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("\nConnection lost to server.");

            string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
            if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
            {
                try
                {
                    File.Delete(imgPath);
                    Console.WriteLine($"Incomplete file {fileName} deleted from: {imgPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not delete incomplete file {fileName} from: {imgPath}. ({ex.Message})");
                }
            }

            return false;
        }
        catch (IOException)
        {
            Console.WriteLine("\nConnection lost to server.");

            string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
            if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
            {
                try
                {
                    File.Delete(imgPath);
                    Console.WriteLine($"Incomplete file {fileName} deleted from: {imgPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not delete incomplete file {fileName} from: {imgPath}. ({ex.Message})");
                }
            }

            return false;
        }
    }

    internal static bool Register(NetworkDataHelper networkDataHelpe)
    {
        Console.Write("\nEnter username: ");
        string? userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("\nUsername can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("\nPassword can't be null.");
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

            if (ok)
            {
                Console.WriteLine("\nRegistered successfully.");
            }
            else
            {
                Console.WriteLine("\nRegister failed, that username already exists.");
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (IOException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }

    internal static bool LogIn(NetworkDataHelper networkDataHelper)
    {
        Console.Write("\nEnter username: ");
        string? userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("\nUsername can't be null.");
            return true;
        }
        Console.Write("Enter password: ");
        string? password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("\nPassword can't be null.");
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
                Console.WriteLine("\nLogged in successfully.");
                loggedIn = true;
                userLogged = userName;
            }
            else
            {
                Console.WriteLine("\nLogin failed, incorrect username or password.");
                return true;
            }
            return true;
        }
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (IOException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }

    public static bool JoinClass(NetworkDataHelper networkDataHelper)
    {
        byte[] len0 = BitConverter.GetBytes(0);
        networkDataHelper.Send(len0);

        bool joined = false;
        byte[] classCounterBytes = networkDataHelper.Receive(4);

        int classCounter = BitConverter.ToInt32(classCounterBytes);
        int classNumber = 0;
        List<string[]> listedClasses = new List<string[]>();

        Console.WriteLine("\nAviable classes to enroll:\n");
        for (int i = 0; i < classCounter; i++)
        {
            classNumber++;

            byte[] classInfoLengthBytes = networkDataHelper.Receive(4);

            int classInfoLength = BitConverter.ToInt32(classInfoLengthBytes);
            
            byte[] classInfoBytes = networkDataHelper.Receive(classInfoLength);
            
            string classInfo = Encoding.UTF8.GetString(classInfoBytes);

            string[] classInfoSplit = classInfo.Split('|');
            string className = classInfoSplit[0];
            string quotaOccupied = classInfoSplit[1];
            string totalQuota = classInfoSplit[2];
            string[] aux = { classNumber.ToString(), className };
            listedClasses.Add(aux);

            Console.WriteLine($"{classNumber}. {className} ({quotaOccupied}/{totalQuota})");
        }
        
        Console.WriteLine($"\n{classNumber + 1}. Go back");

        if (classCounter == 0)
        {
            Console.WriteLine("\nNo classes aviable.");
            return false;
        }
        else
        {
            Console.Write("\nChoose an option: ");
            try
            {
                int chosenClass = int.Parse(Console.ReadLine());
                if (chosenClass == classCounter + 1)
                {
                    return true;
                }
                if (chosenClass > classCounter || chosenClass < 1)
                {
                    Console.WriteLine("\nInvalid input. Number out of range.");
                    return true;
                }

                string selectedClassName = listedClasses.Find(c => int.Parse(c[0]) == chosenClass)[1];
                string classAndUserName = selectedClassName + $"|{userLogged}";

                byte[] classAndUserNameBytes = Encoding.UTF8.GetBytes(classAndUserName);
                byte[] classAndUserNameBytesLength = BitConverter.GetBytes(classAndUserNameBytes.Length);

                networkDataHelper.Send(classAndUserNameBytesLength);
                networkDataHelper.Send(classAndUserNameBytes);

                joined = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                if (joined)
                {
                    Console.WriteLine($"\nYou have successfully joined the class: {selectedClassName}");
                } 
                else
                {
                    Console.WriteLine($"\nCould not join the class: {selectedClassName}. It may be full or you are already enrolled.");
                }
                return true;
            }
            catch (SocketException se)
            {
                Console.WriteLine($"\nConnection lost to server.");
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"\nInvalid input, try again. ({e.Message})");
                return true;
            }
        }
    }

    public static bool CreateClass(NetworkDataHelper networkDataHelper)
    {
        Console.Write("\nEnter class name: ");
        string? className = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(className))
        {
            Console.WriteLine("\nClass name can't be null.");
            return true;
        }

        Console.Write("Enter class description: ");
        string? classDescription = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(classDescription))
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

        Console.Write("Enter class start time (HH:MM): ");
        string? classStartTime = Console.ReadLine();
        if (classStartTime == "")
        {
            Console.WriteLine("\nClass start time cannot be null");
            return true;
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
        byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

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
        catch (SocketException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
        catch (IOException)
        {
            Console.WriteLine("\nConnection lost to server.");
            return false;
        }
    }
}
