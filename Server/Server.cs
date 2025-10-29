using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Common;
using Dominio;
using System.IO;

namespace Server
{
    internal class Server
    {
        static readonly int maxClientsAllowed = 3;
        static readonly object clientThreadsLock = new object();
        static int currentClients = 0;

        static readonly SettingManager settingsMgr = new SettingManager();

        static readonly List<Usuario> users = new List<Usuario>();
        static readonly object usersLock = new object();

        static readonly List<Clase> classes = new List<Clase>();
        static readonly object clasesLock = new object();

        static readonly Dictionary<string, HashSet<string>> cancellations = new Dictionary<string, HashSet<string>>();
        static readonly object cancelLock = new object();

        static volatile bool running = true;

        static void Main(string[] args)
        {
            users.Add(new Usuario("1", "1"));

            var usuarioBase = users.First(u => u.nombre == "1");
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            classes.Add(new Clase("Yoga", "Clase de yoga para principiantes", 60, 20, usuarioBase, today, "09:00"));
            classes.Add(new Clase("Pilates", "Pilates nivel intermedio", 45, 15, usuarioBase, today, "10:00"));
            classes.Add(new Clase("Funcional", "Entrenamiento funcional", 50, 25, usuarioBase, today, "11:00"));
            classes.Add(new Clase("Spinning", "Clase de spinning intensa", 40, 10, usuarioBase, today, "12:00"));
            classes.Add(new Clase("Stretching", "Estiramientos y movilidad", 30, 12, usuarioBase, today, "13:00"));

            Console.WriteLine("Starting Server Application ... ");
            Console.WriteLine("\nPress 'q' and 'Enter' at any time while not handling a client to stop the server.\n");

            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
            var serverEndPoint = new IPEndPoint(serverIp, serverPort);

            serverSocket.Bind(serverEndPoint);
            serverSocket.Listen();
            Console.WriteLine($"Server waiting for connection on {serverIp}:{serverPort} ...");

            var keyListener = new Thread(() =>
            {
                while (running)
                {
                    string? input = Console.ReadLine();
                    if (input != null && input.ToLower() == "q")
                    {
                        Console.WriteLine("\nShutting server down...\n");
                        running = false;
                        try { serverSocket.Close(); } catch { }
                    }
                    Thread.Sleep(100);
                }
            })
            { IsBackground = true };
            keyListener.Start();

            try
            {
                while (running)
                {
                    Socket clientSocket = serverSocket.Accept();

                    bool admit;
                    lock (clientThreadsLock)
                    {
                        admit = currentClients < maxClientsAllowed;
                        if (admit) currentClients++;
                    }

                    if (!admit)
                    {
                        try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
                        clientSocket.Close();
                        continue;
                    }

                    int clientNum = currentClients;
                    Console.WriteLine($"Client connected. Current clients: {currentClients}");

                    var clientThread = new Thread(() => HandleClient(clientSocket, clientNum)) { IsBackground = true };
                    clientThread.Start();
                }
                serverSocket.Shutdown(SocketShutdown.Both);
                serverSocket.Close();
            }
            catch (SocketException)
            {
                Console.WriteLine("Server closed correctly.");
            }
        }

        static void HandleClient(Socket clientSocket, int clientNum)
        {
            bool connected = true;
            bool clientLogged = false;

            var networkDataHelper = new NetworkDataHelper(clientSocket);

            try
            {
                while (connected && running)
                {
                    while (!clientLogged && connected && running)
                    {
                        try
                        {
                            byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);
                            byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes);
                            Command command = (Command)int.Parse(commandString);

                            int length = BitConverter.ToInt32(networkDataHelper.Receive(sizeof(int)));
                            byte[] commandData = length > 0 ? networkDataHelper.Receive(length) : Array.Empty<byte>();

                            switch (command)
                            {
                                case Command.Register: HandleRegister(networkDataHelper, commandData); break;
                                case Command.Login: clientLogged = HandleLogin(networkDataHelper, commandData); break;
                                default: Console.WriteLine($"Unknown pre-login command from client {clientNum}: {command}"); break;
                            }
                        }
                        catch (SocketException se)
                        {
                            Console.WriteLine($"Socket error with client {clientNum}: {se.Message}");
                            connected = false;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error handling client {clientNum} (pre-login): {e.Message}");
                            connected = false;
                        }
                    }

                    while (clientLogged && connected && running)
                    {
                        try
                        {
                            byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);
                            byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes);
                            CommandLogged commandLogged = (CommandLogged)int.Parse(commandString);

                            int length = BitConverter.ToInt32(networkDataHelper.Receive(sizeof(int)));
                            byte[] commandData = length > 0 ? networkDataHelper.Receive(length) : Array.Empty<byte>();

                            switch (commandLogged)
                            {
                                case CommandLogged.CreateClass: HandleCreateClass(networkDataHelper, commandData); break;
                                case CommandLogged.JoinClass: HandleJoinClass(networkDataHelper); break;
                                case CommandLogged.ListClasses: HandleListClasses(networkDataHelper, commandData); break;
                                case CommandLogged.LeaveClass: HandleLeaveClass(networkDataHelper, commandData); break;
                                case CommandLogged.EditClass: HandleEditClass(networkDataHelper, commandData); break;
                                case CommandLogged.History: HandleHistory(networkDataHelper, commandData); break;
                                case CommandLogged.Exit: HandleLogout(networkDataHelper, commandData); clientLogged = false; break;
                                default: Console.WriteLine($"Unknown logged-in command from client {clientNum}: {commandLogged}"); break;
                            }
                        }
                        catch (SocketException se)
                        {
                            Console.WriteLine($"Socket error with client {clientNum}: {se.Message}");
                            connected = false;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error handling client {clientNum} (logged): {e.Message}");
                            connected = false;
                        }
                    }
                }
            }
            finally
            {
                try { clientSocket.Shutdown(SocketShutdown.Both); clientSocket.Close(); } catch { }
                lock (clientThreadsLock) { currentClients--; }
                Console.WriteLine($"Client {clientNum} disconnected. Current clients: {currentClients}");
            }
        }

        static void HandleLogout(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            try
            {
                networkDataHelper.Send(BitConverter.GetBytes(true));
            }
            catch (SocketException)
            {
                Console.WriteLine("Client disconnected before logout confirmation.");
            }
        }

        static void HandleRegister(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] nameAndPass = dataString.Split('|');
            string username = nameAndPass[0].Trim();
            string password = nameAndPass[1];

            bool added = false;
            lock (usersLock)
            {
                bool exists = users.Any(u => string.Equals(username, u.nombre, StringComparison.Ordinal));
                if (!exists) { users.Add(new Usuario(username, password)); added = true; }
            }

            networkDataHelper.Send(BitConverter.GetBytes(added));
        }

        static bool HandleLogin(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] nameAndPass = dataString.Split('|');
            string username = nameAndPass[0].Trim();
            string password = nameAndPass[1];

            bool validUser;
            lock (usersLock)
            {
                validUser = users.Any(u => string.Equals(username, u.nombre, StringComparison.Ordinal) && u.contrasena == password);
            }

            networkDataHelper.Send(BitConverter.GetBytes(validUser));
            if (validUser) Console.WriteLine($"User {username} logged in.");
            return validUser;
        }
        static void SendSomething(NetworkDataHelper networkDataHelper, string data)
        {
            byte[] dataInfoBytes = Encoding.UTF8.GetBytes(data);
            networkDataHelper.Send(BitConverter.GetBytes(dataInfoBytes.Length));
            networkDataHelper.Send(dataInfoBytes);
        }

        static void SendClassListNormal(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            int cantClases = data.Count;
            networkDataHelper.Send(BitConverter.GetBytes(cantClases));
            foreach (var clase in data)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                SendSomething(networkDataHelper, claseInfo);
            }

        }

        static void SendClassListByName(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            data.Sort((c1, c2) => string.Compare(c1.Nombre, c2.Nombre, StringComparison.Ordinal));
            int cantClases = data.Count;
            networkDataHelper.Send(BitConverter.GetBytes(cantClases));
            foreach (var clase in data)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                SendSomething(networkDataHelper, claseInfo);
            }

        }
        static void SendClassListByDate(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            data.Sort((c1, c2) => DateTime.Compare(c1.Fecha, c2.Fecha));
            int cantClases = data.Count;
            networkDataHelper.Send(BitConverter.GetBytes(cantClases));
            foreach (var clase in data)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                SendSomething(networkDataHelper, claseInfo);
            }
        }

        static void SendClassListByQuota(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            data.Sort((c1, c2) => c1.CuposDisponibles.CompareTo(c2.CuposDisponibles));
            int cantClases = data.Count;
            networkDataHelper.Send(BitConverter.GetBytes(cantClases));
            foreach (var clase in data)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                SendSomething(networkDataHelper, claseInfo);
            }
        }

        static void HandleListClasses(NetworkDataHelper networkDataHelper, byte[] data)
        {
            try
            {
                
                int sortOption = 1;

                if (data != null && data.Length > 0)
                {
                    string optionStr = Encoding.UTF8.GetString(data);
                    if (!int.TryParse(optionStr, out sortOption)) sortOption = 1;
                }
                else
                {
                    byte[] sortBytes = networkDataHelper.Receive(sizeof(int));
                    sortOption = BitConverter.ToInt32(sortBytes);
                }

                lock (clasesLock)
                {
                    if (sortOption == 1) { SendClassListByName(networkDataHelper, classes); }
                    else if (sortOption == 2) { SendClassListByDate(networkDataHelper, classes); }
                    else if (sortOption == 3) { SendClassListByQuota(networkDataHelper, classes); }
                    else { SendClassListNormal(networkDataHelper, classes); }
                }

                bool needToSendImg = BitConverter.ToBoolean(networkDataHelper.Receive(1));
                if (needToSendImg)
                {
                    lock (clasesLock)
                    {
                        int pathLength = BitConverter.ToInt32(networkDataHelper.Receive(4));
                        string path = Encoding.UTF8.GetString(networkDataHelper.Receive(pathLength));

                        FileInfo fileInfo = new FileInfo(path);

                        string fileName = fileInfo.Name;
                        byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

                        networkDataHelper.Send(BitConverter.GetBytes(fileNameBytes.Length));
                        networkDataHelper.Send(fileNameBytes);
                        networkDataHelper.Send(BitConverter.GetBytes(fileInfo.Length));

                        long offset = 0;
                        long partCount = Protocol.CalculateFileParts(fileInfo.Length);
                        long currentPart = 1;

                        FileStreamHelper fsh = new FileStreamHelper();

                        while (offset < fileInfo.Length)
                        {
                            byte[] buffer;
                            bool isLastPart = currentPart == partCount;

                            if (!isLastPart)
                            {
                                buffer = fsh.Read(path, offset, Protocol.MaxFileChunkSize);
                                offset += Protocol.MaxFileChunkSize;
                            }
                            else
                            {
                                long lastPartSize = fileInfo.Length - offset;
                                Console.WriteLine($"Sending part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                                buffer = fsh.Read(path, offset, (int)lastPartSize);
                                offset += lastPartSize;
                            }
                            networkDataHelper.Send(buffer);
                            currentPart++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending class list: {e.Message}");
            }
        }

        static void HandleJoinClass(NetworkDataHelper networkDataHelper)
        {
            bool joined = false;

            try
            {
                lock (clasesLock)
                {
                    int classCoutner = classes.Count(c => c.CuposDisponibles > 0);
                    networkDataHelper.Send(BitConverter.GetBytes(classCoutner));

                    foreach (var c in classes.Where(c => c.CuposDisponibles > 0))
                    {
                        string classInfo = $"{c.Nombre}|{c.CuposMaximos - c.CuposDisponibles}|{c.CuposMaximos}";
                        byte[] classInfoBytes = Encoding.UTF8.GetBytes(classInfo);
                        networkDataHelper.Send(BitConverter.GetBytes(classInfoBytes.Length));
                        networkDataHelper.Send(classInfoBytes);
                    }
                }

                int namesLength = BitConverter.ToInt32(networkDataHelper.Receive(4));
                string[] names = Encoding.UTF8.GetString(networkDataHelper.Receive(namesLength)).Split('|');

                string className = names[0];
                string userName = names[1];

                Usuario userToEnroll = users.Find(u => u.nombre == userName);
                Clase desiredClass = classes.Find(c => c.Nombre == className);

                lock (clasesLock)
                {
                    if (desiredClass != null &&
                        desiredClass.CuposDisponibles > 0 &&
                        !desiredClass.usuarios.Any(u => u.nombre == userName))
                    {
                        desiredClass.AgregarUsuario(userToEnroll);
                        joined = true;
                        Console.WriteLine($"User {userName} joined class '{className}'.");
                    }
                }

                if (joined)
                {
                    lock (cancelLock)
                    {
                        if (cancellations.ContainsKey(userName))
                        {
                            cancellations[userName].Remove(className);
                            if (cancellations[userName].Count == 0) cancellations.Remove(userName);
                        }
                    }
                }

                networkDataHelper.Send(BitConverter.GetBytes(joined));
            }
            catch (SocketException se)
            {
                Console.WriteLine($"Client disconnected due to a socket error: {se.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e.Message}");
            }
        }

        static bool HandleLeaveClass(NetworkDataHelper ndh, byte[] _)
        {
            showClasesOfTheClient(ndh);

            int len = BitConverter.ToInt32(ndh.Receive(sizeof(int)));
            string data = Encoding.UTF8.GetString(ndh.Receive(len));
            string[] parts = data.Split('|');
            if (parts.Length != 2) { ndh.Send(BitConverter.GetBytes(false)); return false; }

            string userName = parts[0];
            string className = parts[1];

            Usuario? user;
            lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
            if (user == null) { ndh.Send(BitConverter.GetBytes(false)); return false; }

            Clase? clase;
            lock (clasesLock) { clase = classes.FirstOrDefault(c => c.Nombre == className); }
            if (clase == null) { ndh.Send(BitConverter.GetBytes(false)); return false; }

            try
            {
                bool removed = false;
                lock (clasesLock)
                {
                    var found = clase.usuarios.FirstOrDefault(u => u.nombre == userName);
                    if (found != null)
                    {
                        clase.usuarios.Remove(found);
                        removed = true;
                    }
                }

                if (!removed)
                {
                    ndh.Send(BitConverter.GetBytes(false));
                    return false;
                }

                lock (cancelLock)
                {
                    if (!cancellations.TryGetValue(userName, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        cancellations[userName] = set;
                    }
                    set.Add(className);
                }

                ndh.Send(BitConverter.GetBytes(true));
                Console.WriteLine($"User {userName} left class '{className}'.");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error leaving class: {e.Message}");
                ndh.Send(BitConverter.GetBytes(false));
                return false;
            }
        }

        static void showClasesOfTheClient(NetworkDataHelper networkDataHelper)
        {
            int userNameLength = BitConverter.ToInt32(networkDataHelper.Receive(sizeof(int)));
            string userName = Encoding.UTF8.GetString(networkDataHelper.Receive(userNameLength));

            Usuario? usuarioLogeado = null;
            lock (usersLock) { usuarioLogeado = users.FirstOrDefault(u => u.nombre.Equals(userName)); }

            if (usuarioLogeado == null)
            {
                networkDataHelper.Send(BitConverter.GetBytes(0));
                return;
            }

            List<Clase> clasesUsuario;
            lock (clasesLock) { clasesUsuario = classes.Where(c => c.usuarios.Contains(usuarioLogeado)).ToList(); }

            networkDataHelper.Send(BitConverter.GetBytes(clasesUsuario.Count));
            try
            {
                foreach (var clase in clasesUsuario)
                {
                    string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}";
                    byte[] claseInfoBytes = Encoding.UTF8.GetBytes(claseInfo);
                    networkDataHelper.Send(BitConverter.GetBytes(claseInfoBytes.Length));
                    networkDataHelper.Send(claseInfoBytes);
                }
            }
            catch (Exception e) { Console.WriteLine($"Error sending class list: {e.Message}"); }
        }

        static bool HandleCreateClass(NetworkDataHelper dataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] p = dataString.Split('|');
            if (p.Length < 8) { dataHelper.Send(BitConverter.GetBytes(false)); return false; }

            string name = p[0];
            string desc = p[1];
            bool okQuota = int.TryParse(p[2], out int quota);
            string startDate = p[3];
            string startTime = p[4];
            bool okDur = int.TryParse(p[5], out int dur);
            string hasImg = p[6];
            string user = p[7];
            string imgUrl = "";

            if (!okQuota || !okDur) { dataHelper.Send(BitConverter.GetBytes(false)); return false; }

            Usuario owner;
            lock (usersLock) { owner = users.FirstOrDefault(u => u.nombre == user); }
            if (owner == null) { dataHelper.Send(BitConverter.GetBytes(false)); return false; }

            string fileName = "";
            bool created = false;

            lock (clasesLock)
            {
                bool exists = classes.Any(c => string.Equals(c.Nombre, name, StringComparison.Ordinal));
                if (!exists)
                {
                    if (hasImg == "y")
                    {
                        try
                        {
                            int fileNameLength = BitConverter.ToInt32(dataHelper.Receive(Protocol.FileNameLenghtSize));
                            string fileNameStr = Encoding.UTF8.GetString(dataHelper.Receive(fileNameLength));
                            fileName = fileNameStr;

                            long fileSize = BitConverter.ToInt64(dataHelper.Receive(Protocol.FileSizeLenghtSize));

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
                                    buffer = dataHelper.Receive(Protocol.MaxFileChunkSize);
                                    offset += Protocol.MaxFileChunkSize;
                                }
                                else
                                {
                                    long lastPartSize = fileSize - offset;
                                    buffer = dataHelper.Receive((int)lastPartSize);
                                    offset += lastPartSize;
                                }
                                Directory.CreateDirectory("Images");
                                fileStreamHelper.Write("Images/" + fileName, buffer);
                                currentPart++;
                            }

                            imgUrl = Directory.GetCurrentDirectory() + "/Images/" + fileName;
                        }
                        catch (Exception)
                        {
                            string imgPath = Directory.GetCurrentDirectory() + $"/Images/" + fileName;
                            if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                            {
                                try { File.Delete(imgPath); } catch { }
                            }
                        }
                    }
                    var newClass = new Clase(name, desc, dur, quota, owner, startDate, startTime);
                    if (imgUrl != "") newClass.UrlFoto = imgUrl;
                    classes.Add(newClass);
                    created = true;
                }
            }

            dataHelper.Send(BitConverter.GetBytes(created));
            return created;
        }

        static bool HandleEditClass(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            try
            {
                string dataString = Encoding.UTF8.GetString(commandData);
                string[] parts = dataString.Split('|');

                if (parts.Length == 3 && parts[1] == "VALIDATE")
                {
                    string validateClassName = parts[0];
                    string validateUserName = parts[2];

                    Usuario? validateUser;
                    lock (usersLock) { validateUser = users.FirstOrDefault(u => u.nombre == validateUserName); }
                    if (validateUser == null) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                    Clase? validateClassToEdit;
                    lock (clasesLock) { validateClassToEdit = classes.FirstOrDefault(c => c.Nombre == validateClassName); }
                    if (validateClassToEdit == null) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                    bool canEdit = validateClassToEdit.owner.nombre == validateUserName;
                    networkDataHelper.Send(BitConverter.GetBytes(canEdit));
                    return canEdit;
                }

                if (parts.Length < 8) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                string className = parts[0];
                string newName = parts[1];
                string newDescription = parts[2];
                string newQuotaStr = parts[3];
                string newDate = parts[4];
                string newTime = parts[5];
                string newDurationStr = parts[6];
                string userName = parts[7];

                if (!int.TryParse(newQuotaStr, out int newQuota) || !int.TryParse(newDurationStr, out int newDuration))
                { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                Usuario? user;
                lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
                if (user == null) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                Clase? classToEdit;
                lock (clasesLock) { classToEdit = classes.FirstOrDefault(c => c.Nombre == className); }
                if (classToEdit == null) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                if (classToEdit.owner.nombre != userName) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                if (newName != className)
                {
                    lock (clasesLock)
                    {
                        bool nameExists = classes.Any(c => c.Nombre == newName && c != classToEdit);
                        if (nameExists) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }
                    }
                }

                if (newQuota < classToEdit.usuarios.Count) { networkDataHelper.Send(BitConverter.GetBytes(false)); return false; }

                lock (clasesLock)
                {
                    classToEdit.Nombre = newName;
                    classToEdit.Descripcion = newDescription;
                    classToEdit.CuposMaximos = newQuota;
                    classToEdit.Fecha = DateTime.Parse(newDate);
                    classToEdit.Hora = newTime;
                    classToEdit.Duracion = newDuration;
                    classToEdit.Link = $"www.classlink.com/{newName.Replace(" ", "_").ToLower()}";
                }

                networkDataHelper.Send(BitConverter.GetBytes(true));
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error editing class: {e.Message}");
                networkDataHelper.Send(BitConverter.GetBytes(false));
                return false;
            }
        }

        static void HandleHistory(NetworkDataHelper ndh, byte[] cmdData)
        {
            try
            {
                string userName = Encoding.UTF8.GetString(cmdData);
                Usuario? user;
                lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
                if (user == null)
                {
                    ndh.Send(BitConverter.GetBytes(0));
                    return;
                }

                var results = new Dictionary<string, string>(StringComparer.Ordinal);

                lock (clasesLock)
                {
                    foreach (var c in classes.Where(c => c.usuarios.Any(u => u.nombre == userName)))
                    {
                        bool finished = IsFinished(c);
                        string status = finished ? "Finished" : "Active";
                        results[c.Nombre] = $"{c.Nombre}|{status}|{c.Fecha:yyyy-MM-dd}|{c.Hora}|{c.Duracion}";
                    }
                }

                lock (cancelLock)
                {
                    if (cancellations.TryGetValue(userName, out var set))
                    {
                        foreach (var className in set)
                        {
                            Clase? c;
                            lock (clasesLock) { c = classes.FirstOrDefault(x => x.Nombre == className); }
                            string fecha = c != null ? c.Fecha.ToString("yyyy-MM-dd") : "";
                            string hora = c != null ? c.Hora : "";
                            int dur = c != null ? c.Duracion : 0;
                            results[className] = $"{className}|Cancelled|{fecha}|{hora}|{dur}";
                        }
                    }
                }

                ndh.Send(BitConverter.GetBytes(results.Count));
                foreach (var line in results.Values)
                {
                    var bytes = Encoding.UTF8.GetBytes(line);
                    ndh.Send(BitConverter.GetBytes(bytes.Length));
                    ndh.Send(bytes);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending history: {e.Message}");
                ndh.Send(BitConverter.GetBytes(0));
            }
        }

        static bool IsFinished(Clase c)
        {
            TimeSpan.TryParse(c.Hora, out var ts);
            var end = c.Fecha.Date.Add(ts).AddMinutes(c.Duracion);
            return DateTime.Now >= end;
        }
    }
}
