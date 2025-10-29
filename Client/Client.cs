using Cliente;
using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
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
            Console.WriteLine("5. Edit Class");
            Console.WriteLine("6. History");
            Console.WriteLine("7. Logout");
            Console.Write("\nChoose an option: ");
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting Client Application ... ");

            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.ReceiveTimeout = 10000;
            clientSocket.SendTimeout = 10000;
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
            IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

            bool connected = false;
            while (!connected)
            {
                try
                {
                    clientSocket.Connect(serverEndPoint);
                    connected = true;
                    Console.WriteLine("\nConnected to server");
                }
                catch
                {
                    Console.WriteLine("\nCould not connect to server. Retrying in 3 seconds...");
                    Thread.Sleep(3000);
                }
            }

            _socket = clientSocket;
            _ndh = new NetworkDataHelper(clientSocket);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; GracefulExit(); };
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { GracefulExit(); };

            bool running = true;

            while (running)
            {
                while (!loggedIn && running)
                {
                    StartingMenu();
                    string? option = Console.ReadLine();
                    if (option == "3") return;

                    if (!int.TryParse(option, out int opt) || opt < 1 || opt > 3)
                    {
                        Console.WriteLine("\nInvalid option. Try again.");
                        continue;
                    }

                    Command command = (Command)(opt - 1);

                    _ndh!.Send(Encoding.UTF8.GetBytes(Protocol.request));
                    _ndh.Send(Encoding.UTF8.GetBytes(((int)command).ToString("D2")));

                    switch (command)
                    {
                        case Command.Register: running = Register(_ndh); break;
                        case Command.Login: running = LogIn(_ndh); break;
                    }
                }

                while (loggedIn && running)
                {
                    PrintMenu();
                    string? option = Console.ReadLine();
                    if (!int.TryParse(option, out int opt) || opt < 1 || opt > 7)
                    {
                        Console.WriteLine("\nInvalid option. Try again.");
                        continue;
                    }

                    CommandLogged command = (CommandLogged)(opt - 1);

                    _ndh!.Send(Encoding.UTF8.GetBytes(Protocol.request));
                    _ndh.Send(Encoding.UTF8.GetBytes(((int)command).ToString("D2")));

                    switch (command)
                    {
                        case CommandLogged.CreateClass: running = CreateClass(_ndh); break;
                        case CommandLogged.JoinClass:   running = JoinClass(_ndh); break;
                        case CommandLogged.ListClasses: running = ListClasses(_ndh); break;
                        case CommandLogged.LeaveClass:  running = LeaveClass(_ndh); break;
                        case CommandLogged.EditClass:   running = EditClass(_ndh); break;
                        case CommandLogged.History:     Console.WriteLine("Not implemented yet."); break;
                        case CommandLogged.Exit:        Logout(_ndh); break;
                    }
                }
            }

            Console.WriteLine("\nConnection interrupted...");
            try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
            try { clientSocket.Close(); } catch { }
        }

        internal static void Logout(NetworkDataHelper ndh)
        {
            try
            {
                ndh.Send(BitConverter.GetBytes(0));

                byte[] result = ndh.Receive(1);
                bool ok = BitConverter.ToBoolean(result);

                if (ok)
                {
                    Console.WriteLine("Logged out successfully.");
                    loggedIn = false;
                    userLogged = null;
                }
                else
                {
                    Console.WriteLine("Logout failed.");
                }
            }
            catch
            {
                Console.WriteLine("Logged out (connection lost).");
                loggedIn = false;
                userLogged = null;
                try { _socket?.Close(); } catch { }
            }
        }

        static void GracefulExit()
        {
            try
            {
                if (loggedIn && _ndh != null && _socket != null && _socket.Connected)
                {
                    byte[] mt   = Encoding.UTF8.GetBytes(Protocol.request);
                    byte[] cmd  = Encoding.UTF8.GetBytes(((int)CommandLogged.Exit).ToString("D2"));
                    byte[] len0 = BitConverter.GetBytes(0);

                    _ndh.Send(mt);
                    _ndh.Send(cmd);
                    _ndh.Send(len0);

                    try { _ndh.Receive(1); } catch { }
                }
            }
            catch { }
            try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket?.Close(); } catch { }
        }

        internal static bool Register(NetworkDataHelper networkDataHelpe)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
            byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

            try
            {
                networkDataHelpe.Send(commandDataLenght);
                networkDataHelpe.Send(commandData);

                bool ok = BitConverter.ToBoolean(networkDataHelpe.Receive(1));
                Console.WriteLine(ok ? "\nRegistered successfully." : "\nRegister failed, that username already exists.");
                return true;
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
        }

        internal static bool LogIn(NetworkDataHelper networkDataHelper)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] commandData = Encoding.UTF8.GetBytes(userAndPass);
            byte[] commandDataLenght = BitConverter.GetBytes(commandData.Length);

            try
            {
                networkDataHelper.Send(commandDataLenght);
                networkDataHelper.Send(commandData);

                bool ok = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                if (ok)
                {
                    Console.WriteLine("\nLogged in successfully.");
                    loggedIn = true;
                    userLogged = userName;
                }
                else
                {
                    Console.WriteLine("\nLogin failed, incorrect username or password.");
                }
                return true;
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
        }
        internal static void ShowClases(int classCount, NetworkDataHelper networkDataHelper, List<string[]> imgUrls)
        {
            for (int i = 0; i < classCount; i++)
            {
                int classDataLength = BitConverter.ToInt32(networkDataHelper.Receive(4));
                string[] classDataSplit = Encoding.UTF8.GetString(networkDataHelper.Receive(classDataLength)).Split('|');

                Console.WriteLine($"\nClass {i + 1}: {classDataSplit[0]}");
                Console.WriteLine($"Description: {classDataSplit[1]}");
                Console.WriteLine($"Date: {classDataSplit[3]}");
                Console.WriteLine($"Time: {classDataSplit[4]}");
                Console.WriteLine($"Duration: {classDataSplit[5]} minutes");
                Console.WriteLine($"Taken places: {classDataSplit[2]}");
                Console.WriteLine($"Creator: {classDataSplit[7]}");
                Console.WriteLine($"Link: {classDataSplit[8]}");
                Console.WriteLine($"Has cover image: {(classDataSplit[6] == "No image uploaded" ? "No" : "Yes")}");

                imgUrls.Add(new string[] { (i + 1).ToString(), classDataSplit[6], classDataSplit[0] });
            }

        }

        internal static bool ListClasses(NetworkDataHelper networkDataHelper)
        {
            string fileName = "";
            string className = "";

            try
            {
                Console.WriteLine("\nOrder by: ");
                Console.WriteLine("\n1. Name ");
                Console.WriteLine("\n2. Date");
                Console.WriteLine("\n3. Quota");



                networkDataHelper.Send(BitConverter.GetBytes(0));
                var optionSort = Console.ReadLine();
                networkDataHelper.Send(BitConverter.GetBytes(int.Parse(optionSort ?? "1")));




                int classCount = BitConverter.ToInt32(networkDataHelper.Receive(4));
                if (classCount == 0) { Console.WriteLine("\nNo classes available."); return true; }

                Console.WriteLine($"\nThere are {classCount} classes available:");
                var imgUrls = new List<string[]>();
                ShowClases(classCount, networkDataHelper, imgUrls);



                bool wantImg = false;
                Console.Write("\nTo download any cover image, choose the desired class. Otherwise, type 'q': ");
                string? option = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(option) || option == "q")
                {
                    networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                    return true;
                }

                int chosenClass = int.Parse(option);
                if (chosenClass > classCount || chosenClass < 1)
                {
                    Console.WriteLine("\nInvalid input. Number out of range.");
                    networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                    return true;
                }

                string classPath = imgUrls.Find(c => int.Parse(c[0]) == chosenClass)![1];
                className = imgUrls.Find(c => int.Parse(c[0]) == chosenClass)![2];
                if (classPath == "No image uploaded")
                {
                    Console.WriteLine("\nThe selected class has no cover image.");
                    networkDataHelper.Send(BitConverter.GetBytes(wantImg));
                    return true;
                }

                wantImg = true;
                networkDataHelper.Send(BitConverter.GetBytes(wantImg));

                byte[] pathBytes = Encoding.UTF8.GetBytes(classPath);
                networkDataHelper.Send(BitConverter.GetBytes(pathBytes.Length));
                networkDataHelper.Send(pathBytes);

                int fileNameLength = BitConverter.ToInt32(networkDataHelper.Receive(Protocol.FileNameLenghtSize));
                string fileNameStr = Encoding.UTF8.GetString(networkDataHelper.Receive(fileNameLength));
                fileName = fileNameStr;

                long fileSize = BitConverter.ToInt64(networkDataHelper.Receive(Protocol.FileSizeLenghtSize));

                long offset = 0;
                long partCount = Protocol.CalculateFileParts(fileSize);
                long currentPart = 1;

                FileStreamHelper fsh = new FileStreamHelper();

                while (offset < fileSize)
                {
                    byte[] buffer;
                    bool isLastPart = currentPart == partCount;

                    if (!isLastPart)
                    {
                        buffer = networkDataHelper.Receive(Protocol.MaxFileChunkSize);
                        offset += Protocol.MaxFileChunkSize;
                    }
                    else
                    {
                        long lastPartSize = fileSize - offset;
                        buffer = networkDataHelper.Receive((int)lastPartSize);
                        offset += lastPartSize;
                    }
                    Directory.CreateDirectory($"Images/{className}");
                    fsh.Write($"Images/{className}/" + fileName, buffer);
                    currentPart++;
                }

                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                Console.WriteLine($"File {fileName} received successfully at: {imgPath}");
                return true;
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");

                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                {
                    try { File.Delete(imgPath); Console.WriteLine($"Incomplete file {fileName} deleted from: {imgPath}"); }
                    catch { }
                }
                return false;
            }
        }

        public static bool JoinClass(NetworkDataHelper networkDataHelper)
        {
            networkDataHelper.Send(BitConverter.GetBytes(0));

            int classCounter = BitConverter.ToInt32(networkDataHelper.Receive(4));
            int classNumber = 0;
            List<string[]> listedClasses = new List<string[]>();

            Console.WriteLine("\nAvailable classes to enroll:\n");
            for (int i = 0; i < classCounter; i++)
            {
                classNumber++;

                int classInfoLength = BitConverter.ToInt32(networkDataHelper.Receive(4));
                string[] classInfoSplit = Encoding.UTF8.GetString(networkDataHelper.Receive(classInfoLength)).Split('|');

                string className = classInfoSplit[0];
                string quotaOccupied = classInfoSplit[1];
                string totalQuota = classInfoSplit[2];
                listedClasses.Add(new[] { classNumber.ToString(), className });

                Console.WriteLine($"{classNumber}. {className} ({quotaOccupied}/{totalQuota})");
            }

            Console.WriteLine($"\n{classNumber + 1}. Go back");

            if (classCounter == 0)
            {
                Console.WriteLine("\nNo classes available.");
                return false;
            }

            Console.Write("\nChoose an option: ");
            try
            {
                int chosenClass = int.Parse(Console.ReadLine()!);
                if (chosenClass == classCounter + 1) return true;
                if (chosenClass > classCounter || chosenClass < 1)
                {
                    Console.WriteLine("\nInvalid input. Number out of range.");
                    return true;
                }

                string selectedClassName = listedClasses.Find(c => int.Parse(c[0]) == chosenClass)![1];
                string classAndUserName = selectedClassName + $"|{userLogged}";

                byte[] classAndUserNameBytes = Encoding.UTF8.GetBytes(classAndUserName);
                networkDataHelper.Send(BitConverter.GetBytes(classAndUserNameBytes.Length));
                networkDataHelper.Send(classAndUserNameBytes);

                bool joined = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                Console.WriteLine(joined
                    ? $"\nYou have successfully joined the class: {selectedClassName}"
                    : $"\nCould not join the class: {selectedClassName}. It may be full or you are already enrolled.");
                return true;
            }
            catch (SocketException)
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

        public static bool CreateClass(NetworkDataHelper networkDataHelper)
        {
            Console.Write("\nEnter class name: ");
            string? className = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(className)) { Console.WriteLine("\nClass name can't be null."); return true; }

            Console.Write("Enter class description: ");
            string? classDescription = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(classDescription)) { Console.WriteLine("\nClass description can't be null."); return true; }

            Console.Write("Enter class maximum quotas: ");
            string? classuQotas = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(classuQotas)) { Console.WriteLine("\nClass quotas cannot be null"); return true; }

            Console.Write("Enter class start date (DD/MM/YYYY) \nIf left blank, default date (today) will be applied: ");
            string? classStartDate = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(classStartDate))
            {
                classStartDate = DateTime.Now.ToString("dd/MM/yyyy");
                Console.WriteLine("Default date applied: " + classStartDate);
            }

            Console.Write("Enter class start time (HH:MM): ");
            string? classStartTime = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(classStartTime)) { Console.WriteLine("\nClass start time cannot be null"); return true; }

            Console.Write("Enter class duration (in minutes): ");
            string? classDuration = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(classDuration)) { Console.WriteLine("\nClass duration cannot be null"); return true; }

            Console.Write("Would you like to upload a cover image? (y/n): ");
            string? hasCoverImage = Console.ReadLine();
            if (hasCoverImage != "y" && hasCoverImage != "n") { Console.WriteLine("\nInvalid input. Enter 'y' or 'n'."); return true; }

            string ClassData = className + "|" + classDescription + "|" + classuQotas + "|" + classStartDate + "|" + classStartTime + "|" + classDuration + "|" + hasCoverImage + "|" + userLogged;
            byte[] commandData = Encoding.UTF8.GetBytes(ClassData);
            networkDataHelper.Send(BitConverter.GetBytes(commandData.Length));
            networkDataHelper.Send(commandData);

            try
            {
                if (hasCoverImage == "y")
                {
                    Console.Write("Type the image path: ");
                    string? filePath = Console.ReadLine();

                    FileInfo fileInfo = new FileInfo(filePath);
                    string fileName = fileInfo.Name;

                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                    networkDataHelper.Send(BitConverter.GetBytes(fileNameBytes.Length));
                    networkDataHelper.Send(fileNameBytes);

                    long fileSize = fileInfo.Length;
                    networkDataHelper.Send(BitConverter.GetBytes(fileSize));

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

                bool classCreated = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                Console.WriteLine(classCreated ? "\nClass created successfully." : "\nClass creation failed, try again.");
                return true;
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
        }

        internal static bool ShowClasesofTheUser(NetworkDataHelper networkDataHelper)
        {
            byte[] nombreUsuario = Encoding.UTF8.GetBytes(userLogged ?? "");
            networkDataHelper.Send(BitConverter.GetBytes(nombreUsuario.Length));
            networkDataHelper.Send(nombreUsuario);

            try
            {
                int classCount = BitConverter.ToInt32(networkDataHelper.Receive(4));
                if (classCount == 0)
                {
                    Console.WriteLine("\nYou are not enrolled in any classes.");
                    return true;
                }
                Console.WriteLine($"\nYou are enrolled in {classCount} classes:");

                for (int i = 0; i < classCount; i++)
                {
                    int classDataLength = BitConverter.ToInt32(networkDataHelper.Receive(4));
                    string[] classDataSplit = Encoding.UTF8.GetString(networkDataHelper.Receive(classDataLength)).Split('|');

                    Console.WriteLine($"\nClass {i + 1}: {classDataSplit[0]}");
                    Console.WriteLine($"Description: {classDataSplit[1]}");
                    Console.WriteLine($"Date: {classDataSplit[3]}");
                    Console.WriteLine($"Time: {classDataSplit[4]}");
                    Console.WriteLine($"Duration: {classDataSplit[5]} minutes");
                    Console.WriteLine($"Taken places: {classDataSplit[2]}");
                    Console.WriteLine($"Creator: {classDataSplit[7]}");
                    Console.WriteLine($"Has cover image: {(classDataSplit[6] == "No image uploaded" ? "No" : "Yes")}");
                }
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
            return true;
        }

        internal static bool LeaveClass(NetworkDataHelper networkDataHelper)
        {
            if (!ShowClasesofTheUser(networkDataHelper)) return false;

            Console.Write("\nType the name of the class you want to leave: ");
            string? className = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(className))
            {
                Console.WriteLine("\nClass name can't be null.");
                return true;
            }

            string data = $"{userLogged}|{className}";
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);

            try
            {
                networkDataHelper.Send(BitConverter.GetBytes(dataBytes.Length));
                networkDataHelper.Send(dataBytes);

                bool ok = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                Console.WriteLine(ok ? "\nLeft class successfully."
                                     : "\nCould not leave class, you might not be enrolled in it.");
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
            return true;
        }

        internal static bool EditClass(NetworkDataHelper networkDataHelper)
        {
            Console.WriteLine("\nEdit Class:");

            Console.Write("\nType the current class name: ");
            string? className = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(className)) { Console.WriteLine("\nClass name can't be null."); return true; }

            Console.Write("Enter new class name: ");
            string? newName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(newName)) { Console.WriteLine("\nNew class name can't be null."); return true; }

            Console.Write("Enter new description: ");
            string? newDescription = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(newDescription)) { Console.WriteLine("\nDescription can't be null."); return true; }

            Console.Write("Enter new quota: ");
            if (!int.TryParse(Console.ReadLine(), out int newQuota) || newQuota <= 0) { Console.WriteLine("\nInvalid quota."); return true; }

            Console.Write("Enter new date (yyyy-MM-dd): ");
            string? newDate = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(newDate)) { Console.WriteLine("\nDate can't be null."); return true; }

            Console.Write("Enter new time (HH:mm): ");
            string? newTime = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(newTime)) { Console.WriteLine("\nTime can't be null."); return true; }

            Console.Write("Enter new duration (minutes): ");
            if (!int.TryParse(Console.ReadLine(), out int newDuration) || newDuration <= 0) { Console.WriteLine("\nInvalid duration."); return true; }

            string hasNewImage = "n";

            string commandData = $"{className}|{newName}|{newDescription}|{newQuota}|{newDate}|{newTime}|{newDuration}|{userLogged}|{hasNewImage}";
            byte[] commandDataBytes = Encoding.UTF8.GetBytes(commandData);

            try
            {
                networkDataHelper.Send(BitConverter.GetBytes(commandDataBytes.Length));
                networkDataHelper.Send(commandDataBytes);

                bool ok = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                Console.WriteLine(ok ? "\nClass edited successfully."
                                     : "\nCould not edit class. You might not be the owner, the class doesn't exist, or data is invalid.");
            }
            catch
            {
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
            return true;
        }

        static bool ReconnectToServer()
        {
            try
            {
                try { _socket?.Close(); } catch { }

                var newSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                newSocket.ReceiveTimeout = 10000;
                newSocket.SendTimeout = 10000;
                newSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
                int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
                IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

                newSocket.Connect(serverEndPoint);

                _socket = newSocket;
                _ndh = new NetworkDataHelper(newSocket);

                Console.WriteLine("Reconnected to server successfully.");
                return true;
            }
            catch
            {
                Console.WriteLine("Failed to reconnect to server.");
                return false;
            }
        }
    }
}
