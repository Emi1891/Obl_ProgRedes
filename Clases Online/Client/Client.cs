using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Common;

namespace Client
{
    public class Client
    {
        static string? userLogged;
        static bool loggedIn = false;

        static readonly SettingManager settingsMgr = new SettingManager();

        const string CMD_REGISTER = "01";
        const string CMD_LOGIN = "02";
        const string CMD_LOGOUT = "03";
        const string CMD_CREATECLASS = "10";
        const string CMD_LISTCLASSES = "11";

        static void StartingMenu()
        {
            Console.WriteLine();
            Console.WriteLine("Welcome to the Client Application");
            Console.WriteLine("Please choose an option:");
            Console.WriteLine("1. Register");
            Console.WriteLine("2. Login");
            Console.WriteLine("3. Exit");
            Console.Write("Choose an option: ");
        }

        static void PrintMenu()
        {
            Console.WriteLine();
            Console.WriteLine("Menu:");
            Console.WriteLine("1. Create Class");
            Console.WriteLine("2. Join Class");
            Console.WriteLine("3. List Classes");
            Console.WriteLine("4. Leave Class");
            Console.WriteLine("5. History");
            Console.WriteLine("6. Logout");
            Console.WriteLine("7. Exit");
            Console.Write("Choose an option: ");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("Starting Client Application ... ");

            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                var clientIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ClientIpConfigKey));
                var clientPort = int.Parse(settingsMgr.ReadSetting(ClientConfig.ClientPortConfigKey));
                clientSocket.Bind(new IPEndPoint(clientIp, clientPort));
            }
            catch {}

            var serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            var serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
            var serverEndPoint = new IPEndPoint(serverIp, serverPort);

            try { clientSocket.Connect(serverEndPoint); }
            catch
            {
                Console.WriteLine("Could not connect to server: shutting down client...");
                Thread.Sleep(1200);
                return;
            }

            Console.WriteLine("Connected to server");
            var nh = new NetworkDataHelper(clientSocket);
            bool running = true;

            while (running)
            {
                while (!loggedIn && running)
                {
                    StartingMenu();
                    var opt = Console.ReadLine()?.Trim();
                    switch (opt)
                    {
                        case "1": running = Register(nh); break;
                        case "2": running = Login(nh); break;
                        case "3": running = false; break;
                        default: Console.WriteLine("Invalid option."); break;
                    }
                }

                while (loggedIn && running)
                {
                    PrintMenu();
                    var opt = Console.ReadLine()?.Trim();
                    switch (opt)
                    {
                        case "1": running = CreateClass(nh); break;
                        case "2": Console.WriteLine("Join not implemented yet."); break;
                        case "3": running = ListClasses(nh); break;
                        case "4": Console.WriteLine("Leave not implemented yet."); break;
                        case "5": Console.WriteLine("History not implemented yet."); break;
                        case "6": running = Logout(nh); break;
                        case "7": running = false; break;
                        default: Console.WriteLine("Invalid option."); break;
                    }
                }
            }

            Console.WriteLine("Connection interrupted...");
            try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
            clientSocket.Close();
        }


        static bool Register(NetworkDataHelper nh)
        {
            var u = PromptUsername();
            var p = PromptPassword();

            try
            {
                SendCommand(nh, CMD_REGISTER, $"{u}|{p}");
                bool ok = ReadBool(nh);
                Console.WriteLine(ok ? "Registered successfully." : "Register failed, username already exists.");
                return true;
            }
            catch (SocketException) { Console.WriteLine("Connection lost to server."); return false; }
        }

        static bool Login(NetworkDataHelper nh)
        {
            var u = PromptUsername();
            var p = PromptPassword();

            try
            {
                SendCommand(nh, CMD_LOGIN, $"{u}|{p}");
                bool ok = ReadBool(nh);
                if (ok)
                {
                    loggedIn = true;
                    userLogged = u;
                    Console.WriteLine("Logged in successfully.");
                }
                else Console.WriteLine("Login failed, incorrect username or password.");
                return true;
            }
            catch (SocketException) { Console.WriteLine("Connection lost to server."); return false; }
        }

        static bool Logout(NetworkDataHelper nh)
        {
            try
            {
                SendCommand(nh, CMD_LOGOUT, "");
                bool ok = ReadBool(nh);
                if (ok)
                {
                    loggedIn = false;
                    userLogged = null;
                    Console.WriteLine("Logged out successfully.");
                }
                else Console.WriteLine("Logout failed.");
                return true;
            }
            catch (SocketException) { Console.WriteLine("Connection lost to server."); return false; }
        }

        static bool CreateClass(NetworkDataHelper nh)
        {
            if (!loggedIn) { Console.WriteLine("You must be logged in."); return true; }

            Console.Write("Enter class name: ");
            var name = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(name)) { Console.WriteLine("Class name can't be empty."); return true; }

            Console.Write("Enter class description: ");
            var desc = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(desc)) { Console.WriteLine("Class description can't be empty."); return true; }

            Console.Write("Enter class duration (minutes): ");
            if (!int.TryParse(Console.ReadLine(), out var dur) || dur <= 0)
            { Console.WriteLine("Duration must be a positive integer."); return true; }

            Console.Write("Enter class maximum quotas: ");
            if (!int.TryParse(Console.ReadLine(), out var quotas) || quotas <= 0)
            { Console.WriteLine("Quotas must be a positive integer."); return true; }

            var payload = $"{name}|{desc}|{quotas}|{DateTime.Now:yyyy-MM-dd HH:mm}|{dur}|{userLogged}";
            try
            {
                SendCommand(nh, CMD_CREATECLASS, payload);
                bool ok = ReadBool(nh);
                Console.WriteLine(ok ? "Class created successfully." : "Class creation failed.");
                return true;
            }
            catch (SocketException) { Console.WriteLine("Connection lost to server."); return false; }
        }

        static bool ListClasses(NetworkDataHelper nh)
        {
            try
            {
                SendCommand(nh, CMD_LISTCLASSES, "");
                var lenBytes = nh.Receive(sizeof(int));
                int len = BitConverter.ToInt32(lenBytes);
                var data = len > 0 ? nh.Receive(len) : Array.Empty<byte>();
                var text = Encoding.UTF8.GetString(data);

                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("No classes available.");
                    return true;
                }
                Console.WriteLine("Available classes:");
                foreach (var line in text.Split('\n'))
                    Console.WriteLine(line);

                return true;
            }
            catch (SocketException) { Console.WriteLine("Connection lost to server."); return false; }
        }

        static void SendCommand(NetworkDataHelper nh, string cmd2, string data)
        {
            var header = Encoding.UTF8.GetBytes("REQ");
            var cmd = Encoding.UTF8.GetBytes(cmd2);
            var body = Encoding.UTF8.GetBytes(data ?? "");
            var len = BitConverter.GetBytes(body.Length);

            nh.Send(header);
            nh.Send(cmd);
            nh.Send(len);
            if (body.Length > 0) nh.Send(body);
        }

        static bool ReadBool(NetworkDataHelper nh)
        {
            var b = nh.Receive(1);
            return BitConverter.ToBoolean(b);
        }


        static string PromptUsername()
        {
            while (true)
            {
                Console.Write("Enter username: ");
                var u = (Console.ReadLine() ?? "").Trim();
                if (ValidateUsername(u, out var err)) return u;
                Console.WriteLine($"Invalid username: {err}");
            }
        }

        static string PromptPassword()
        {
            while (true)
            {
                Console.Write("Enter password: ");
                var p = Console.ReadLine() ?? "";
                if (ValidatePassword(p, out var err)) return p;
                Console.WriteLine($"Invalid password: {err}");
            }
        }

        static bool ValidateUsername(string u, out string? err)
        {
            err = null;
            if (string.IsNullOrWhiteSpace(u)) { err = "cannot be empty."; return false; }
            if (u.Length < 3 || u.Length > 20) { err = "length must be 3–20."; return false; }
            if (u.Contains(' ')) { err = "spaces not allowed."; return false; }
            if (!Regex.IsMatch(u, @"^[a-zA-Z0-9._-]+$")) { err = "only letters/numbers/._-"; return false; }
            return true;
        }

        static bool ValidatePassword(string p, out string? err)
        {
            err = null;
            if (string.IsNullOrEmpty(p)) { err = "cannot be empty."; return false; }
            if (p.Length < 6 || p.Length > 64) { err = "length must be 6–64."; return false; }
            if (p.Any(char.IsWhiteSpace)) { err = "spaces not allowed."; return false; }
            return true;
        }
    }
}
