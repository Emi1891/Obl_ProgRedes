using Cliente;
using Common;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
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

        static async Task Main(string[] args)
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
                try { await clientSocket.ConnectAsync(serverEndPoint).ConfigureAwait(false); connected = true; Console.WriteLine("\nConnected to server"); }
                catch { Console.WriteLine("\nCould not connect to server. Retrying in 3 seconds..."); await Task.Delay(3000).ConfigureAwait(false); }
            }

            _socket = clientSocket;
            _ndh = new NetworkDataHelper(clientSocket);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; GracefulExitAsync().GetAwaiter().GetResult(); };
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { GracefulExitAsync().GetAwaiter().GetResult(); };

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
                    await _ndh!.SendAsync(Encoding.UTF8.GetBytes(Protocol.request)).ConfigureAwait(false);
                    await _ndh.SendAsync(Encoding.UTF8.GetBytes(((int)command).ToString("D2"))).ConfigureAwait(false);

                    switch (command)
                    {
                        case Command.Register: await RegisterAsync(_ndh).ConfigureAwait(false); break;
                        case Command.Login: await LogInAsync(_ndh).ConfigureAwait(false); break;
                    }
                }

                while (loggedIn && running)
                {
                    PrintMenu();
                    string? option = Console.ReadLine();
                    if (!int.TryParse(option, out int n) || n < 1 || n > 7) { Console.WriteLine("\nInvalid option."); continue; }
                    CommandLogged command = (CommandLogged)(n - 1);

                    await _ndh!.SendAsync(Encoding.UTF8.GetBytes(Protocol.request)).ConfigureAwait(false);
                    await _ndh.SendAsync(Encoding.UTF8.GetBytes(((int)command).ToString("D2"))).ConfigureAwait(false);

                    switch (command)
                    {
                        case CommandLogged.CreateClass: await CreateClassAsync(_ndh).ConfigureAwait(false); break;
                        case CommandLogged.JoinClass: await JoinClassAsync(_ndh).ConfigureAwait(false); break;
                        case CommandLogged.ListClasses: await ListClassesAsync(_ndh).ConfigureAwait(false); break;
                        case CommandLogged.LeaveClass: await LeaveClassAsync(_ndh).ConfigureAwait(false); break;
                        case CommandLogged.EditClass: await EditClassAsync(_ndh).ConfigureAwait(false); break;
                        case CommandLogged.History: await Task.Run(() => ShowHistory(_ndh)).ConfigureAwait(false); break;
                        case CommandLogged.Exit: await DoLogoutAsync(_ndh).ConfigureAwait(false); break;
                    }
                }
            }
        }

        static async Task GracefulExitAsync()
        {
            try
            {
                if (loggedIn && _ndh != null && _socket != null && _socket.Connected)
                {
                    await _ndh.SendAsync(Encoding.UTF8.GetBytes(Protocol.request)).ConfigureAwait(false);
                    await _ndh.SendAsync(Encoding.UTF8.GetBytes(((int)CommandLogged.Exit).ToString("D2"))).ConfigureAwait(false);
                    await _ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                    try { await _ndh.ReceiveAsync(1).ConfigureAwait(false); } catch { }
                }
            }
            catch { }
            try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket?.Close(); } catch { }
        }

        static async Task DoLogoutAsync(NetworkDataHelper ndh)
        {
            try
            {
                await ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                bool ok = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
                if (ok) { Console.WriteLine("Logged out successfully."); loggedIn = false; userLogged = null; }
                else Console.WriteLine("Logout failed.");
            }
            catch { Console.WriteLine("Logged out (connection lost)." ); loggedIn = false; userLogged = null; }
        }

        internal static async Task<bool> ShowClasesofTheUserAsync(NetworkDataHelper ndh)
        {
            byte[] nombreUsuario = Encoding.UTF8.GetBytes(userLogged ?? "");
            await ndh.SendAsync(BitConverter.GetBytes(nombreUsuario.Length)).ConfigureAwait(false);
            await ndh.SendAsync(nombreUsuario).ConfigureAwait(false);

            int classCount = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
            if (classCount == 0) { Console.WriteLine("\nYou are not enrolled in any classes."); return true; }

            Console.WriteLine($"\nYou are enrolled in {classCount} classes:");
            for (int i = 0; i < classCount; i++)
            {
                int len = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
                string classData = Encoding.UTF8.GetString(await ndh.ReceiveAsync(len).ConfigureAwait(false));
                string[] s = classData.Split('|');
                Console.WriteLine($"\nClass {i + 1}: {s[0]}");
                Console.WriteLine($"Description: {s[1]}");
                Console.WriteLine($"Date: {s[3]}");
                Console.WriteLine($"Time: {s[4]}");
                Console.WriteLine($"Duration: {s[5]} minutes");
                Console.WriteLine($"Taken places: {s[2]}");
                Console.WriteLine($"Creator: {s[7]}");
                Console.WriteLine($"Has cover image: {(s[6] == "No image uploaded" ? "No" : "Yes")} ");
            }
            return true;
        }

        internal static async Task<bool> LeaveClassAsync(NetworkDataHelper ndh)
        {
            if (string.IsNullOrWhiteSpace(userLogged))
            {
                Console.WriteLine("\nYou must be logged in.");
                return true;
            }

            await ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);

            byte[] userBytes = Encoding.UTF8.GetBytes(userLogged);
            await ndh.SendAsync(BitConverter.GetBytes(userBytes.Length)).ConfigureAwait(false);
            await ndh.SendAsync(userBytes).ConfigureAwait(false);

            int count = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
            if (count == 0)
            {
                Console.WriteLine("\nYou are not enrolled in any class.");
                return true;
            }

            var myClasses = new List<string>();
            Console.WriteLine("\nYour enrolled classes:\n");
            for (int i = 0; i < count; i++)
            {
                int len = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
                string info = Encoding.UTF8.GetString(await ndh.ReceiveAsync(len).ConfigureAwait(false));
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
            await ndh.SendAsync(BitConverter.GetBytes(payloadBytes.Length)).ConfigureAwait(false);
            await ndh.SendAsync(payloadBytes).ConfigureAwait(false);

            bool ok = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
            Console.WriteLine(ok
                ? $"\nYou left '{className}' successfully."
                : "\nCould not leave the class.");

            return true;
        }

        internal static async Task<bool> EditClassAsync(NetworkDataHelper ndh)
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

            bool validOption = false;
            string input = "n";

            while (!validOption) {
                Console.Write("Would you like to change the cover image? (y/n): ");
                try
                {
                    input = Console.ReadLine().ToLower();

                    if (input != "y" && input != "n")
                    {
                        Console.WriteLine("\nInvalid input. Please enter 'y' or 'n'. NO SALTO AL CATCH");
                    }
                    else
                    {
                        validOption = true;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("\nInvalid input. Please enter 'y' or 'n'. SALTO AL CATCH");
                }
            }

            string hasNewImage = input;
            string cmdData = $"{className}|{newName}|{newDescription}|{newQuota}|{newDate}|{newTime}|{newDuration}|{userLogged}|{hasNewImage}";
            byte[] bytes = Encoding.UTF8.GetBytes(cmdData);
            await ndh.SendAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
            await ndh.SendAsync(bytes).ConfigureAwait(false);

            if (hasNewImage == "y")
            {
                Console.Write("Type the image path: ");
                string? filePath = Console.ReadLine();
                FileInfo fi = new FileInfo(filePath);
                string fileName = fi.Name;
                byte[] fn = Encoding.UTF8.GetBytes(fileName);
                await ndh.SendAsync(BitConverter.GetBytes(fn.Length)).ConfigureAwait(false);
                await ndh.SendAsync(fn).ConfigureAwait(false);
                await ndh.SendAsync(BitConverter.GetBytes(fi.Length)).ConfigureAwait(false);

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
                    await ndh.SendAsync(buffer).ConfigureAwait(false);
                    currentPart++;
                }
            }

            bool ok = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
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

        internal static async Task ShowClasesAsync(int classCount, NetworkDataHelper networkDataHelper, List<string[]> imgUrls)
        {
            for (int i = 0; i < classCount; i++)
            {
                int classDataLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(4).ConfigureAwait(false), 0);
                string[] classDataSplit = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(classDataLength).ConfigureAwait(false)).Split('|');

                Console.WriteLine($"\nClass {i + 1}: {classDataSplit[0]}");
                Console.WriteLine($"Description: {classDataSplit[1]}");
                Console.WriteLine($"Date: {classDataSplit[3]}");
                Console.WriteLine($"Time: {classDataSplit[4]}");
                Console.WriteLine($"Duration: {classDataSplit[5]} minutes");
                Console.WriteLine($"Taken places: {classDataSplit[2]}");
                Console.WriteLine($"Creator: {classDataSplit[7]}");
                Console.WriteLine($"Link: {classDataSplit[8]}");
                Console.WriteLine($"Has cover image: {(classDataSplit[6] == "No image uploaded" ? "No" : "Yes")} ");

                imgUrls.Add(new string[] { (i + 1).ToString(), classDataSplit[6], classDataSplit[0] });
            }
        }

        internal static async Task<bool> ListClassesAsync(NetworkDataHelper networkDataHelper)
        {
            string fileName = "";
            string className = "";

            try
            {
                Console.WriteLine("\nOrder by: ");
                Console.WriteLine("\n1. Name ");
                Console.WriteLine("\n2. Date");
                Console.WriteLine("\n3. Quota");

                await networkDataHelper.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                var optionSort = Console.ReadLine();
                int sortOption = int.Parse(optionSort ?? "1");
                await networkDataHelper.SendAsync(BitConverter.GetBytes(sortOption)).ConfigureAwait(false);

                int classCount = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(4).ConfigureAwait(false), 0);
                if (classCount == 0) { Console.WriteLine("\nNo classes available."); return true; }

                Console.WriteLine($"\nThere are {classCount} classes available:");
                var imgUrls = new List<string[]>();
                await ShowClasesAsync(classCount, networkDataHelper, imgUrls).ConfigureAwait(false);

                bool wantImg = false;
                Console.Write("\nTo download any cover image, choose the desired class. Otherwise, type 'q': ");
                string? opt = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(opt) || opt == "q") { await networkDataHelper.SendAsync(BitConverter.GetBytes(wantImg)).ConfigureAwait(false); return true; }

                int chosen = int.Parse(opt);
                if (chosen > classCount || chosen < 1) { Console.WriteLine("\nInvalid input."); await networkDataHelper.SendAsync(BitConverter.GetBytes(wantImg)).ConfigureAwait(false); return true; }

                string path = imgUrls.Find(c => int.Parse(c[0]) == chosen)![1];
                className = imgUrls.Find(c => int.Parse(c[0]) == chosen)![2];
                if (path == "No image uploaded") { Console.WriteLine("\nThe selected class has no cover image."); await networkDataHelper.SendAsync(BitConverter.GetBytes(wantImg)).ConfigureAwait(false); return true; }

                wantImg = true;
                await networkDataHelper.SendAsync(BitConverter.GetBytes(wantImg)).ConfigureAwait(false);

                byte[] pb = Encoding.UTF8.GetBytes(path);
                await networkDataHelper.SendAsync(BitConverter.GetBytes(pb.Length)).ConfigureAwait(false);
                await networkDataHelper.SendAsync(pb).ConfigureAwait(false);

                int fileNameLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(Protocol.FileNameLenghtSize).ConfigureAwait(false), 0);
                fileName = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(fileNameLength).ConfigureAwait(false));
                long fileSize = BitConverter.ToInt64(await networkDataHelper.ReceiveAsync(Protocol.FileSizeLenghtSize).ConfigureAwait(false), 0);

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
                        buffer = await networkDataHelper.ReceiveAsync(Protocol.MaxFileChunkSize).ConfigureAwait(false);
                        offset += Protocol.MaxFileChunkSize;
                    }
                    else
                    {
                        long lastSize = fileSize - offset;
                        buffer = await networkDataHelper.ReceiveAsync((int)lastSize).ConfigureAwait(false);
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

        internal static async Task<bool> RegisterAsync(NetworkDataHelper ndh)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] data = Encoding.UTF8.GetBytes(userAndPass);
            await ndh.SendAsync(BitConverter.GetBytes(data.Length)).ConfigureAwait(false);
            await ndh.SendAsync(data).ConfigureAwait(false);

            bool ok = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
            Console.WriteLine(ok ? "\nRegistered successfully." : "\nRegister failed, that username already exists.");
            return true;
        }

        internal static async Task<bool> LogInAsync(NetworkDataHelper ndh)
        {
            Console.Write("\nEnter username: ");
            string? userName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("\nUsername can't be null."); return true; }

            Console.Write("Enter password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("\nPassword can't be null."); return true; }

            string userAndPass = userName + "|" + password;
            byte[] data = Encoding.UTF8.GetBytes(userAndPass);
            await ndh.SendAsync(BitConverter.GetBytes(data.Length)).ConfigureAwait(false);
            await ndh.SendAsync(data).ConfigureAwait(false);

            bool ok = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
            if (ok) { Console.WriteLine("\nLogged in successfully."); loggedIn = true; userLogged = userName; }
            else { Console.WriteLine("\nLogin failed, incorrect username or password."); }
            return true;
        }

        public static async Task<bool> JoinClassAsync(NetworkDataHelper ndh)
        {
            await ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
            int classCounter = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
            int classNumber = 0;
            List<string[]> listed = new List<string[]>();

            Console.WriteLine("\nAviable classes to enroll:\n");
            for (int i = 0; i < classCounter; i++)
            {
                classNumber++;
                int len = BitConverter.ToInt32(await ndh.ReceiveAsync(4).ConfigureAwait(false), 0);
                string info = Encoding.UTF8.GetString(await ndh.ReceiveAsync(len).ConfigureAwait(false));
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
                await ndh.SendAsync(BitConverter.GetBytes(b.Length)).ConfigureAwait(false);
                await ndh.SendAsync(b).ConfigureAwait(false);

                bool joined = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
                Console.WriteLine(joined ? $"\nYou have successfully joined the class: {selected}"
                                         : $"\nCould not join the class: {selected}. It may be full or you are already enrolled.");
                return true;
            }
            catch (SocketException) { Console.WriteLine($"\nConnection lost to server."); return false; }
            catch (Exception e) { Console.WriteLine($"\nInvalid input, try again. ({e.Message})"); return true; }
        }

        public static async Task<bool> CreateClassAsync(NetworkDataHelper ndh)
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
            await ndh.SendAsync(BitConverter.GetBytes(data.Length)).ConfigureAwait(false);
            await ndh.SendAsync(data).ConfigureAwait(false);

            if (hasImg == "y")
            {
                Console.Write("Type the image path: ");
                string? filePath = Console.ReadLine();
                FileInfo fi = new FileInfo(filePath);
                string fileName = fi.Name;
                byte[] fn = Encoding.UTF8.GetBytes(fileName);
                await ndh.SendAsync(BitConverter.GetBytes(fn.Length)).ConfigureAwait(false);
                await ndh.SendAsync(fn).ConfigureAwait(false);
                await ndh.SendAsync(BitConverter.GetBytes(fi.Length)).ConfigureAwait(false);

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
                    await ndh.SendAsync(buffer).ConfigureAwait(false);
                    currentPart++;
                }
            }

            bool created = BitConverter.ToBoolean(await ndh.ReceiveAsync(1).ConfigureAwait(false), 0);
            Console.WriteLine(created ? "\nClass created successfully." : "\nClass creation failed, try again.");
            return true;
        }
    }
}
