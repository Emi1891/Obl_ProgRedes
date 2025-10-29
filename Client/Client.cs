using Cliente;
using Common;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

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
                try { clientSocket.Connect(serverEndPoint); connected = true; Console.WriteLine("\nConnected to server"); }
                catch { Console.WriteLine("\nCould not connect to server. Retrying in 3 seconds..."); Thread.Sleep(3000); }
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
                    if (!int.TryParse(option, out int n) || n < 1 || n > 3) { Console.WriteLine("\nInvalid option."); continue; }

                    Command command = (Command)(n - 1);
                    _ndh!.Send(Encoding.UTF8.GetBytes(Protocol.request));
                    _ndh.Send(Encoding.UTF8.GetBytes(((int)command).ToString("D2")));

                    switch (command)
                    {
                        case Command.Register: Register(_ndh); break;
                        case Command.Login: LogIn(_ndh); break;
                    }
                }

                while (loggedIn && running)
                {
                    PrintMenu();
                    string? option = Console.ReadLine();
                    if (!int.TryParse(option, out int n) || n < 1 || n > 7) { Console.WriteLine("\nInvalid option."); continue; }
                    CommandLogged command = (CommandLogged)(n - 1);

                    _ndh!.Send(Encoding.UTF8.GetBytes(Protocol.request));
                    _ndh.Send(Encoding.UTF8.GetBytes(((int)command).ToString("D2")));

                    switch (command)
                    {
                        case CommandLogged.CreateClass: CreateClass(_ndh); break;
                        case CommandLogged.JoinClass: JoinClass(_ndh); break;
                        case CommandLogged.ListClasses: ListClasses(_ndh); break;
                        case CommandLogged.LeaveClass: LeaveClass(_ndh); break;
                        case CommandLogged.EditClass: EditClass(_ndh); break;
                        case CommandLogged.History: ShowHistory(_ndh); break;
                        case CommandLogged.Exit: DoLogout(_ndh); break;
                    }
                }
            }
        }

        static void GracefulExit()
        {
            try
            {
                if (loggedIn && _ndh != null && _socket != null && _socket.Connected)
                {
                    _ndh.Send(Encoding.UTF8.GetBytes(Protocol.request));
                    _ndh.Send(Encoding.UTF8.GetBytes(((int)CommandLogged.Exit).ToString("D2")));
                    _ndh.Send(BitConverter.GetBytes(0));
                    try { _ndh.Receive(1); } catch { }
                }
            }
            catch { }
            try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket?.Close(); } catch { }
        }

        static void DoLogout(NetworkDataHelper ndh)
        {
            try
            {
                ndh.Send(BitConverter.GetBytes(0));
                bool ok = BitConverter.ToBoolean(ndh.Receive(1));
                if (ok) { Console.WriteLine("Logged out successfully."); loggedIn = false; userLogged = null; }
                else Console.WriteLine("Logout failed.");
            }
            catch { Console.WriteLine("Logged out (connection lost)."); loggedIn = false; userLogged = null; }
        }

        internal static bool ShowClasesofTheUser(NetworkDataHelper ndh)
        {
            byte[] nombreUsuario = Encoding.UTF8.GetBytes(userLogged ?? "");
            ndh.Send(BitConverter.GetBytes(nombreUsuario.Length));
            ndh.Send(nombreUsuario);

            int classCount = BitConverter.ToInt32(ndh.Receive(4));
            if (classCount == 0) { Console.WriteLine("\nYou are not enrolled in any classes."); return true; }

            Console.WriteLine($"\nYou are enrolled in {classCount} classes:");
            for (int i = 0; i < classCount; i++)
            {
                int len = BitConverter.ToInt32(ndh.Receive(4));
                string classData = Encoding.UTF8.GetString(ndh.Receive(len));
                string[] s = classData.Split('|');
                Console.WriteLine($"\nClass {i + 1}: {s[0]}");
                Console.WriteLine($"Description: {s[1]}");
                Console.WriteLine($"Date: {s[3]}");
                Console.WriteLine($"Time: {s[4]}");
                Console.WriteLine($"Duration: {s[5]} minutes");
                Console.WriteLine($"Taken places: {s[2]}");
                Console.WriteLine($"Creator: {s[7]}");
                Console.WriteLine($"Has cover image: {(s[6] == "No image uploaded" ? "No" : "Yes")}");
            }
            return true;
        }

        internal static bool LeaveClass(NetworkDataHelper ndh)
        {
            if (string.IsNullOrWhiteSpace(userLogged))
            {
                Console.WriteLine("\nYou must be logged in.");
                return true;
            }

            ndh.Send(BitConverter.GetBytes(0));

            byte[] userBytes = Encoding.UTF8.GetBytes(userLogged);
            ndh.Send(BitConverter.GetBytes(userBytes.Length));
            ndh.Send(userBytes);

            int count = BitConverter.ToInt32(ndh.Receive(4));
            if (count == 0)
            {
                Console.WriteLine("\nYou are not enrolled in any class.");
                return true;
            }

            var myClasses = new List<string>();
            Console.WriteLine("\nYour enrolled classes:\n");
            for (int i = 0; i < count; i++)
            {
                int len = BitConverter.ToInt32(ndh.Receive(4));
                string info = Encoding.UTF8.GetString(ndh.Receive(len));
                string name = info.Split('|')[0];
                myClasses.Add(name);
                Console.WriteLine($"{i + 1}. {name}");
            }
            Console.WriteLine($"\n{count + 1}. Go back");

            Console.Write("\nChoose a class to leave: ");
            string? optStr = Console.ReadLine();
            if (!int.TryParse(optStr, out int opt) || opt < 1 || opt > count + 1)
            {
                Console.WriteLine("\nInvalid input.");
                return true;
            }
            if (opt == count + 1) return true;

            string className = myClasses[opt - 1];

            string payload = $"{userLogged}|{className}";
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            ndh.Send(BitConverter.GetBytes(payloadBytes.Length));
            ndh.Send(payloadBytes);

            bool ok = BitConverter.ToBoolean(ndh.Receive(1));
            Console.WriteLine(ok
                ? $"\nYou left '{className}' successfully."
                : "\nCould not leave the class.");

            return true;
        }



        internal static bool EditClass(NetworkDataHelper ndh)
        {
            Console.WriteLine("\nEdit Class functionality:");
            Console.Write("\nType the name of the class you want to edit: ");
            string? className = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(className)) { Console.WriteLine("\nClass name can't be null."); return true; }

            Console.Write("\nEnter new class name: ");
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
            string cmdData = $"{className}|{newName}|{newDescription}|{newQuota}|{newDate}|{newTime}|{newDuration}|{userLogged}|{hasNewImage}";
            byte[] bytes = Encoding.UTF8.GetBytes(cmdData);
            ndh.Send(BitConverter.GetBytes(bytes.Length));
            ndh.Send(bytes);

            bool ok = BitConverter.ToBoolean(ndh.Receive(1));
            Console.WriteLine(ok ? "\nClass edited successfully." : "\nCould not edit class. You might not be the owner or the class doesn't exist.");
            return true;
        }

        internal static void ShowHistory(NetworkDataHelper ndh)
        {
            string user = userLogged ?? "";
            byte[] u = Encoding.UTF8.GetBytes(user);
            ndh.Send(BitConverter.GetBytes(u.Length));
            ndh.Send(u);

            int n = BitConverter.ToInt32(ndh.Receive(4));
            if (n == 0) { Console.WriteLine("\nNo history yet."); return; }

            Console.WriteLine($"\nHistory ({n}):");
            for (int i = 0; i < n; i++)
            {
                int len = BitConverter.ToInt32(ndh.Receive(4));
                string row = Encoding.UTF8.GetString(ndh.Receive(len));
                Console.WriteLine(row);
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
                int sortOption = int.Parse(optionSort ?? "1");
                networkDataHelper.Send(BitConverter.GetBytes(sortOption));



                int classCount = BitConverter.ToInt32(networkDataHelper.Receive(4));
                if (classCount == 0) { Console.WriteLine("\nNo classes available."); return true; }

                Console.WriteLine($"\nThere are {classCount} classes available:");
                var imgUrls = new List<string[]>();
                ShowClases(classCount, networkDataHelper, imgUrls);



                bool wantImg = false;
                Console.Write("\nTo download any cover image, choose the desired class. Otherwise, type 'q': ");
                string? opt = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(opt) || opt == "q") { networkDataHelper.Send(BitConverter.GetBytes(wantImg)); return true; }

                int chosen = int.Parse(opt);
                if (chosen > classCount || chosen < 1) { Console.WriteLine("\nInvalid input."); networkDataHelper.Send(BitConverter.GetBytes(wantImg)); return true; }

                string path = imgUrls.Find(c => int.Parse(c[0]) == chosen)![1];
                className = imgUrls.Find(c => int.Parse(c[0]) == chosen)![2];
                if (path == "No image uploaded") { Console.WriteLine("\nThe selected class has no cover image."); networkDataHelper.Send(BitConverter.GetBytes(wantImg)); return true; }

                wantImg = true;
                networkDataHelper.Send(BitConverter.GetBytes(wantImg));

                byte[] pb = Encoding.UTF8.GetBytes(path);
                networkDataHelper.Send(BitConverter.GetBytes(pb.Length));
                networkDataHelper.Send(pb);

                int fileNameLength = BitConverter.ToInt32(networkDataHelper.Receive(Protocol.FileNameLenghtSize));
                fileName = Encoding.UTF8.GetString(networkDataHelper.Receive(fileNameLength));
                long fileSize = BitConverter.ToInt64(networkDataHelper.Receive(Protocol.FileSizeLenghtSize));

                long offset = 0;
                long partCount = Protocol.CalculateFileParts(fileSize);
                long currentPart = 1;
                FileStreamHelper fs = new FileStreamHelper();

                while (offset < fileSize)
                {
                    byte[] buffer;
                    bool last = currentPart == partCount;
                    if (!last)
                    {
                        buffer = networkDataHelper.Receive(Protocol.MaxFileChunkSize);
                        offset += Protocol.MaxFileChunkSize;
                    }
                    else
                    {
                        long lastSize = fileSize - offset;
                        buffer = networkDataHelper.Receive((int)lastSize);
                        offset += lastSize;
                    }
                    Directory.CreateDirectory($"Images/{className}");
                    fs.Write($"Images/{className}/" + fileName, buffer);
                    currentPart++;
                }

                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                Console.WriteLine($"File {fileName} received successfully at: {imgPath}");
                return true;
            }
            catch
            {
                string imgPath = Directory.GetCurrentDirectory() + $"/Images/{className}/" + fileName;
                if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath)) { try { File.Delete(imgPath); } catch { } }
                Console.WriteLine("\nConnection lost to server.");
                return false;
            }
        }

        internal static bool Register(NetworkDataHelper ndh)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] data = Encoding.UTF8.GetBytes(userAndPass);
            ndh.Send(BitConverter.GetBytes(data.Length));
            ndh.Send(data);

            bool ok = BitConverter.ToBoolean(ndh.Receive(1));
            Console.WriteLine(ok ? "\nRegistered successfully." : "\nRegister failed, that username already exists.");
            return true;
        }

        internal static bool LogIn(NetworkDataHelper ndh)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] data = Encoding.UTF8.GetBytes(userAndPass);
            ndh.Send(BitConverter.GetBytes(data.Length));
            ndh.Send(data);

            bool ok = BitConverter.ToBoolean(ndh.Receive(1));
            if (ok) { Console.WriteLine("\nLogged in successfully."); loggedIn = true; userLogged = userName; }
            else { Console.WriteLine("\nLogin failed, incorrect username or password."); }
            return true;
        }

        public static bool JoinClass(NetworkDataHelper ndh)
        {
            ndh.Send(BitConverter.GetBytes(0));
            int classCounter = BitConverter.ToInt32(ndh.Receive(4));
            int classNumber = 0;
            List<string[]> listed = new List<string[]>();

            Console.WriteLine("\nAviable classes to enroll:\n");
            for (int i = 0; i < classCounter; i++)
            {
                classNumber++;
                int len = BitConverter.ToInt32(ndh.Receive(4));
                string info = Encoding.UTF8.GetString(ndh.Receive(len));
                string[] s = info.Split('|');
                string className = s[0];
                string quotaOccupied = s[1];
                string totalQuota = s[2];
                listed.Add(new[] { classNumber.ToString(), className });
                Console.WriteLine($"{classNumber}. {className} ({quotaOccupied}/{totalQuota})");
            }

            Console.WriteLine($"\n{classNumber + 1}. Go back");
            if (classCounter == 0) { Console.WriteLine("\nNo classes aviable."); return false; }

            Console.Write("\nChoose an option: ");
            try
            {
                int chosen = int.Parse(Console.ReadLine()!);
                if (chosen == classCounter + 1) return true;
                if (chosen > classCounter || chosen < 1) { Console.WriteLine("\nInvalid input."); return true; }

                string selected = listed.Find(c => int.Parse(c[0]) == chosen)![1];
                string classAndUser = selected + $"|{userLogged}";
                byte[] b = Encoding.UTF8.GetBytes(classAndUser);
                ndh.Send(BitConverter.GetBytes(b.Length));
                ndh.Send(b);

                bool joined = BitConverter.ToBoolean(ndh.Receive(1));
                Console.WriteLine(joined ? $"\nYou have successfully joined the class: {selected}"
                                         : $"\nCould not join the class: {selected}. It may be full or you are already enrolled.");
                return true;
            }
            catch (SocketException) { Console.WriteLine($"\nConnection lost to server."); return false; }
            catch (Exception e) { Console.WriteLine($"\nInvalid input, try again. ({e.Message})"); return true; }
        }

        public static bool CreateClass(NetworkDataHelper ndh)
        {
            Console.Write("\nEnter class name: ");
            string? className = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(className)) { Console.WriteLine("\nClass name can't be null."); return true; }

            Console.Write("Enter class description: ");
            string? desc = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(desc)) { Console.WriteLine("\nClass description can't be null."); return true; }

            Console.Write("Enter class maximum quotas: ");
            string? quotas = Console.ReadLine();
            if (quotas == "") { Console.WriteLine("\nClass quotas cannot be null"); return true; }

            Console.Write("Enter class start date (DD/MM/YYYY) \nIf left blank, default date (today) will be applied: ");
            string? date = Console.ReadLine();
            if (date == "") { date = DateTime.Now.ToString("dd/MM/yyyy"); Console.WriteLine("Default date applied: " + date); }

            Console.Write("Enter class start time (HH:MM): ");
            string? time = Console.ReadLine();
            if (time == "") { Console.WriteLine("\nClass start time cannot be null"); return true; }

            Console.Write("Enter class duration (in minutes): ");
            string? duration = Console.ReadLine();
            if (duration == "") { Console.WriteLine("\nClass duration cannot be null"); return true; }

            Console.Write("Would you like to upload a cover image? (y/n): ");
            string? hasImg = Console.ReadLine();
            if (hasImg == "" || (hasImg != "y" && hasImg != "n")) { Console.WriteLine("\nInvalid input. Enter 'y' or 'n'."); return true; }

            string payload = $"{className}|{desc}|{quotas}|{date}|{time}|{duration}|{hasImg}|{userLogged}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            ndh.Send(BitConverter.GetBytes(data.Length));
            ndh.Send(data);

            if (hasImg == "y")
            {
                Console.Write("Type the image path: ");
                string? filePath = Console.ReadLine();
                FileInfo fi = new FileInfo(filePath);
                string fileName = fi.Name;
                byte[] fn = Encoding.UTF8.GetBytes(fileName);
                ndh.Send(BitConverter.GetBytes(fn.Length));
                ndh.Send(fn);
                ndh.Send(BitConverter.GetBytes(fi.Length));

                long offset = 0;
                long partCount = Protocol.CalculateFileParts(fi.Length);
                long currentPart = 1;
                FileStreamHelper fsh = new FileStreamHelper();

                while (offset < fi.Length)
                {
                    byte[] buffer;
                    bool last = currentPart == partCount;
                    if (!last) { buffer = fsh.Read(filePath, offset, Protocol.MaxFileChunkSize); offset += Protocol.MaxFileChunkSize; }
                    else { long lastSize = fi.Length - offset; buffer = fsh.Read(filePath, offset, (int)lastSize); offset += lastSize; }
                    ndh.Send(buffer);
                    currentPart++;
                }
            }

            bool created = BitConverter.ToBoolean(ndh.Receive(1));
            Console.WriteLine(created ? "\nClass created successfully." : "\nClass creation failed, try again.");
            return true;
        }
    }
}
