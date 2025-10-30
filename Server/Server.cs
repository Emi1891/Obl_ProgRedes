using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Common;
using Dominio;
using System.IO;
using System.Globalization;

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

        static async Task Main(string[] args)
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

            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
            var serverEndPoint = new IPEndPoint(serverIp, serverPort);

            var listener = new TcpListener(serverEndPoint);
            listener.Start();
            Console.WriteLine($"Server waiting for connection on {serverIp}:{serverPort} ...");

            // Console key listener runs on a background task so we can stop the listener with 'q'
            _ = Task.Run(() =>
            {
                while (running)
                {
                    string? input = Console.ReadLine();
                    if (input != null && input.ToLower() == "q")
                    {
                        Console.WriteLine("\nShutting server down...\n");
                        running = false;
                        try { listener.Stop(); } catch { }
                    }
                }
            });

            try
            {
                while (running)
                {
                    TcpClient tcpClient;
                    try
                    {
                        tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break; // listener stopped
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    var clientSocket = tcpClient.Client;

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

                    // Fire-and-forget client handler (exceptions handled inside)
                    _ = Task.Run(() => HandleClientAsync(clientSocket, clientNum));
                }
            }
            catch (SocketException)
            {
                Console.WriteLine("Server closed correctly.");
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        static async Task HandleClientAsync(Socket clientSocket, int clientNum)
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
                            byte[] messageType = await networkDataHelper.ReceiveAsync(Protocol.MessageTypeLength).ConfigureAwait(false);
                            byte[] commandStringBytes = await networkDataHelper.ReceiveAsync(Protocol.CommandStringLength).ConfigureAwait(false);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes).Trim('\0').Trim();

                            if (!int.TryParse(commandString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cmdInt) || !Enum.IsDefined(typeof(Command), cmdInt))
                            {
                                Console.WriteLine($"Invalid pre-login command from client {clientNum}: '{commandString}'");
                                connected = false;
                                break;
                            }

                            Command command = (Command)cmdInt;

                            byte[] lengthBytes = await networkDataHelper.ReceiveAsync(sizeof(int)).ConfigureAwait(false);
                            int length = BitConverter.ToInt32(lengthBytes, 0);
                            if (length < 0)
                            {
                                Console.WriteLine($"Invalid length received from client {clientNum}: {length}");
                                connected = false;
                                break;
                            }

                            byte[] commandData = length > 0 ? await networkDataHelper.ReceiveAsync(length).ConfigureAwait(false) : Array.Empty<byte>();

                            switch (command)
                            {
                                case Command.Register: await HandleRegisterAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case Command.Login: clientLogged = await HandleLoginAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
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
                            byte[] messageType = await networkDataHelper.ReceiveAsync(Protocol.MessageTypeLength).ConfigureAwait(false);
                            byte[] commandStringBytes = await networkDataHelper.ReceiveAsync(Protocol.CommandStringLength).ConfigureAwait(false);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes).Trim('\0').Trim();

                            if (!int.TryParse(commandString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cmdInt) || !Enum.IsDefined(typeof(CommandLogged), cmdInt))
                            {
                                Console.WriteLine($"Invalid logged-in command from client {clientNum}: '{commandString}'");
                                connected = false;
                                break;
                            }

                            CommandLogged commandLogged = (CommandLogged)cmdInt;

                            byte[] lengthBytes = await networkDataHelper.ReceiveAsync(sizeof(int)).ConfigureAwait(false);
                            int length = BitConverter.ToInt32(lengthBytes, 0);
                            if (length < 0)
                            {
                                Console.WriteLine($"Invalid length received from client {clientNum}: {length}");
                                connected = false;
                                break;
                            }

                            byte[] commandData = length > 0 ? await networkDataHelper.ReceiveAsync(length).ConfigureAwait(false) : Array.Empty<byte>();

                            switch (commandLogged)
                            {
                                case CommandLogged.CreateClass: await HandleCreateClassAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case CommandLogged.JoinClass: await HandleJoinClassAsync(networkDataHelper).ConfigureAwait(false); break;
                                case CommandLogged.ListClasses: await HandleListClassesAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case CommandLogged.LeaveClass: await HandleLeaveClassAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case CommandLogged.EditClass: await HandleEditClassAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case CommandLogged.History: await HandleHistoryAsync(networkDataHelper, commandData).ConfigureAwait(false); break;
                                case CommandLogged.Exit: await HandleLogoutAsync(networkDataHelper, commandData).ConfigureAwait(false); clientLogged = false; break;
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

        static async Task HandleLogoutAsync(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            try
            {
                await networkDataHelper.SendAsync(BitConverter.GetBytes(true)).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                Console.WriteLine("Client disconnected before logout confirmation.");
            }
        }

        static async Task HandleRegisterAsync(NetworkDataHelper networkDataHelper, byte[] commandData)
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

            await networkDataHelper.SendAsync(BitConverter.GetBytes(added)).ConfigureAwait(false);
        }

        static async Task<bool> HandleLoginAsync(NetworkDataHelper networkDataHelper, byte[] commandData)
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

            await networkDataHelper.SendAsync(BitConverter.GetBytes(validUser)).ConfigureAwait(false);
            if (validUser) Console.WriteLine($"User {username} logged in.");
            return validUser;
        }

        static async Task SendSomethingAsync(NetworkDataHelper networkDataHelper, string data)
        {
            byte[] dataInfoBytes = Encoding.UTF8.GetBytes(data);
            await networkDataHelper.SendAsync(BitConverter.GetBytes(dataInfoBytes.Length)).ConfigureAwait(false);
            await networkDataHelper.SendAsync(dataInfoBytes).ConfigureAwait(false);
        }

        static async Task SendClassListNormalAsync(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            int cantClases = data.Count;
            await networkDataHelper.SendAsync(BitConverter.GetBytes(cantClases)).ConfigureAwait(false);
            foreach (var clase in data)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                await SendSomethingAsync(networkDataHelper, claseInfo).ConfigureAwait(false);
            }
        }

        static async Task SendClassListByNameAsync(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            var ordered = data.OrderBy(c => c.Nombre, StringComparer.Ordinal).ToList();
            int cantClases = ordered.Count;
            await networkDataHelper.SendAsync(BitConverter.GetBytes(cantClases)).ConfigureAwait(false);
            foreach (var clase in ordered)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                await SendSomethingAsync(networkDataHelper, claseInfo).ConfigureAwait(false);
            }
        }

        static async Task SendClassListByDateAsync(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            var ordered = data.OrderBy(c => c.Fecha).ToList();
            int cantClases = ordered.Count;
            await networkDataHelper.SendAsync(BitConverter.GetBytes(cantClases)).ConfigureAwait(false);
            foreach (var clase in ordered)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                await SendSomethingAsync(networkDataHelper, claseInfo).ConfigureAwait(false);
            }
        }

        static async Task SendClassListByQuotaAsync(NetworkDataHelper networkDataHelper, List<Clase> data)
        {
            var ordered = data.OrderBy(c => c.CuposDisponibles).ToList();
            int cantClases = ordered.Count;
            await networkDataHelper.SendAsync(BitConverter.GetBytes(cantClases)).ConfigureAwait(false);
            foreach (var clase in ordered)
            {
                string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                await SendSomethingAsync(networkDataHelper, claseInfo).ConfigureAwait(false);
            }
        }

        static async Task HandleListClassesAsync(NetworkDataHelper networkDataHelper, byte[] data)
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
                    byte[] sortBytes = await networkDataHelper.ReceiveAsync(sizeof(int)).ConfigureAwait(false);
                    sortOption = BitConverter.ToInt32(sortBytes, 0);
                }

                List<Clase> snapshot;
                lock (clasesLock) { snapshot = classes.ToList(); }

                if (sortOption == 1) await SendClassListByNameAsync(networkDataHelper, snapshot).ConfigureAwait(false);
                else if (sortOption == 2) await SendClassListByDateAsync(networkDataHelper, snapshot).ConfigureAwait(false);
                else if (sortOption == 3) await SendClassListByQuotaAsync(networkDataHelper, snapshot).ConfigureAwait(false);
                else await SendClassListNormalAsync(networkDataHelper, snapshot).ConfigureAwait(false);

                byte[] needImgBytes = await networkDataHelper.ReceiveAsync(1).ConfigureAwait(false);
                bool needToSendImg = BitConverter.ToBoolean(needImgBytes, 0);
                if (needToSendImg)
                {
                    int pathLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(4).ConfigureAwait(false), 0);
                    string path = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(pathLength).ConfigureAwait(false));

                    FileInfo fileInfo = new FileInfo(path);

                    string fileName = fileInfo.Name;
                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

                    await networkDataHelper.SendAsync(BitConverter.GetBytes(fileNameBytes.Length)).ConfigureAwait(false);
                    await networkDataHelper.SendAsync(fileNameBytes).ConfigureAwait(false);
                    await networkDataHelper.SendAsync(BitConverter.GetBytes(fileInfo.Length)).ConfigureAwait(false);

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
                            buffer = fsh.Read(path, offset, (int)lastPartSize);
                            offset += lastPartSize;
                        }
                        await networkDataHelper.SendAsync(buffer).ConfigureAwait(false);
                        currentPart++;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending class list: {e.Message}");
            }
        }

        static async Task HandleJoinClassAsync(NetworkDataHelper networkDataHelper)
        {
            bool joined = false;

            try
            {
                List<(string Name, int Occupied, int Max)> available;
                lock (clasesLock)
                {
                    available = classes.Where(c => c.CuposDisponibles > 0)
                        .Select(c => (c.Nombre, c.CuposMaximos - c.CuposDisponibles, c.CuposMaximos)).ToList();
                }

                await networkDataHelper.SendAsync(BitConverter.GetBytes(available.Count)).ConfigureAwait(false);

                foreach (var c in available)
                {
                    string classInfo = $"{c.Name}|{c.Occupied}|{c.Max}";
                    byte[] classInfoBytes = Encoding.UTF8.GetBytes(classInfo);
                    await networkDataHelper.SendAsync(BitConverter.GetBytes(classInfoBytes.Length)).ConfigureAwait(false);
                    await networkDataHelper.SendAsync(classInfoBytes).ConfigureAwait(false);
                }

                int namesLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(4).ConfigureAwait(false), 0);
                string[] names = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(namesLength).ConfigureAwait(false)).Split('|');

                string className = names[0];
                string userName = names[1];

                Usuario? userToEnroll;
                lock (usersLock) { userToEnroll = users.FirstOrDefault(u => u.nombre == userName); }

                bool didJoin = false;
                lock (clasesLock)
                {
                    var desiredClass = classes.FirstOrDefault(c => c.Nombre == className);
                    if (desiredClass != null && desiredClass.CuposDisponibles > 0 && !desiredClass.usuarios.Any(u => u.nombre == userName))
                    {
                        desiredClass.AgregarUsuario(userToEnroll);
                        didJoin = true;
                        Console.WriteLine($"User {userName} joined class '{className}'.");
                    }
                }

                if (didJoin)
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

                await networkDataHelper.SendAsync(BitConverter.GetBytes(didJoin)).ConfigureAwait(false);
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

        static async Task<bool> HandleLeaveClassAsync(NetworkDataHelper ndh, byte[] _)
        {
            await showClasesOfTheClientAsync(ndh).ConfigureAwait(false);

            int len = BitConverter.ToInt32(await ndh.ReceiveAsync(sizeof(int)).ConfigureAwait(false), 0);
            string data = Encoding.UTF8.GetString(await ndh.ReceiveAsync(len).ConfigureAwait(false));
            string[] parts = data.Split('|');
            if (parts.Length != 2) { await ndh.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

            string userName = parts[0];
            string className = parts[1];

            Usuario? user;
            lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
            if (user == null) { await ndh.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

            Clase? clase;
            lock (clasesLock) { clase = classes.FirstOrDefault(c => c.Nombre == className); }
            if (clase == null) { await ndh.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

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
                    await ndh.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false);
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

                await ndh.SendAsync(BitConverter.GetBytes(true)).ConfigureAwait(false);
                Console.WriteLine($"User {userName} left class '{className}'.");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error leaving class: {e.Message}");
                await ndh.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false);
                return false;
            }
        }

        static async Task showClasesOfTheClientAsync(NetworkDataHelper networkDataHelper)
        {
            int userNameLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(sizeof(int)).ConfigureAwait(false), 0);
            string userName = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(userNameLength).ConfigureAwait(false));

            Usuario? usuarioLogeado = null;
            lock (usersLock) { usuarioLogeado = users.FirstOrDefault(u => u.nombre.Equals(userName)); }

            if (usuarioLogeado == null)
            {
                await networkDataHelper.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                return;
            }

            List<string> claseInfos;
            lock (clasesLock)
            {
                claseInfos = classes.Where(c => c.usuarios.Contains(usuarioLogeado))
                    .Select(c => $"{c.Nombre}|{c.Descripcion}|{c.CuposMaximos - c.CuposDisponibles}/{c.CuposMaximos}|{c.Fecha:yyyy-MM-dd}|{c.Hora}|{c.Duracion}|{c.UrlFoto}|{c.owner.nombre}")
                    .ToList();
            }

            await networkDataHelper.SendAsync(BitConverter.GetBytes(claseInfos.Count)).ConfigureAwait(false);
            try
            {
                foreach (var claseInfo in claseInfos)
                {
                    byte[] claseInfoBytes = Encoding.UTF8.GetBytes(claseInfo);
                    await networkDataHelper.SendAsync(BitConverter.GetBytes(claseInfoBytes.Length)).ConfigureAwait(false);
                    await networkDataHelper.SendAsync(claseInfoBytes).ConfigureAwait(false);
                }
            }
            catch (Exception e) { Console.WriteLine($"Error sending class list: {e.Message}"); }
        }

        static async Task<bool> HandleCreateClassAsync(NetworkDataHelper dataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] p = dataString.Split('|');
            if (p.Length < 8) { await dataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

            string name = p[0];
            string desc = p[1];
            bool okQuota = int.TryParse(p[2], out int quota);
            string startDate = p[3];
            string startTime = p[4];
            bool okDur = int.TryParse(p[5], out int dur);
            string hasImg = p[6];
            string user = p[7];
            string imgUrl = "";

            if (!okQuota || !okDur) { await dataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

            Usuario owner;
            lock (usersLock) { owner = users.FirstOrDefault(u => u.nombre == user); }
            if (owner == null) { await dataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

            string fileName = "";
            bool created = false;

            bool exists;
            lock (clasesLock)
            {
                exists = classes.Any(c => string.Equals(c.Nombre, name, StringComparison.Ordinal));
            }

            if (exists)
            {
                await dataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false);
                return false;
            }

            if (hasImg == "y")
            {
                try
                {
                    int fileNameLength = BitConverter.ToInt32(await dataHelper.ReceiveAsync(Protocol.FileNameLenghtSize).ConfigureAwait(false), 0);
                    string fileNameStr = Encoding.UTF8.GetString(await dataHelper.ReceiveAsync(fileNameLength).ConfigureAwait(false));
                    fileName = fileNameStr;

                    long fileSize = BitConverter.ToInt64(await dataHelper.ReceiveAsync(Protocol.FileSizeLenghtSize).ConfigureAwait(false), 0);

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
                            buffer = await dataHelper.ReceiveAsync(Protocol.MaxFileChunkSize).ConfigureAwait(false);
                            offset += Protocol.MaxFileChunkSize;
                        }
                        else
                        {
                            long lastPartSize = fileSize - offset;
                            buffer = await dataHelper.ReceiveAsync((int)lastPartSize).ConfigureAwait(false);
                            offset += lastPartSize;
                        }
                        Directory.CreateDirectory($"Images/{name}");
                        fileStreamHelper.Write($"Images/{name}/" + fileName, buffer);
                        currentPart++;
                    }

                    imgUrl = Directory.GetCurrentDirectory() + $"/Images/{name}/" + fileName;
                }
                catch (Exception)
                {
                    string imgPath = Directory.GetCurrentDirectory() + $"/Images/{name}/" + fileName;
                    if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                    {
                        try { File.Delete(imgPath); } catch { }
                    }
                }
            }

            var newClass = new Clase(name, desc, dur, quota, owner, startDate, startTime);
            if (imgUrl != "") newClass.UrlFoto = imgUrl;
            lock (clasesLock) { classes.Add(newClass); }
            created = true;

            await dataHelper.SendAsync(BitConverter.GetBytes(created)).ConfigureAwait(false);
            return created;
        }

        static async Task<bool> HandleEditClassAsync(NetworkDataHelper networkDataHelper, byte[] commandData)
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
                    if (validateUser == null) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                    Clase? validateClassToEdit;
                    lock (clasesLock) { validateClassToEdit = classes.FirstOrDefault(c => c.Nombre == validateClassName); }
                    if (validateClassToEdit == null) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                    bool canEdit = validateClassToEdit.owner.nombre == validateUserName;
                    await networkDataHelper.SendAsync(BitConverter.GetBytes(canEdit)).ConfigureAwait(false);
                    return canEdit;
                }

                if (parts.Length < 8) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                string className = parts[0];
                string newName = parts[1];
                string newDescription = parts[2];
                string newQuotaStr = parts[3];
                string newDate = parts[4];
                string newTime = parts[5];
                string newDurationStr = parts[6];
                string userName = parts[7];
                string hasImg = parts[8];

                if (!int.TryParse(newQuotaStr, out int newQuota) || !int.TryParse(newDurationStr, out int newDuration))
                { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                Usuario? user;
                lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
                if (user == null) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                Clase? classToEdit;
                lock (clasesLock) { classToEdit = classes.FirstOrDefault(c => c.Nombre == className); }
                if (classToEdit == null) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                if (classToEdit.owner.nombre != userName) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                string finalClassName = className;
                bool deleteOldFolder = false;

                if (newName != className)
                {
                    bool nameExists;
                    lock (clasesLock)
                    {
                        nameExists = classes.Any(c => c.Nombre == newName && c != classToEdit);
                    }
                    if (nameExists) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }
                    finalClassName = newName;
                    deleteOldFolder = true;
                }

                if (newQuota < classToEdit.usuarios.Count) { await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false); return false; }

                if (!DateTime.TryParseExact(newDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false);
                    return false;
                }

                string newImgUrl = "No image uploaded";

                if (hasImg == "y")
                {
                    string fileName = "";
                    string imgUrl = "";
                    bool oldHasImg = classToEdit.UrlFoto != "No image uploaded";
                    string oldFileName = "";
                    string auxPath = "";

                    if (oldHasImg)
                    {
                        oldFileName = Path.GetFileName(classToEdit.UrlFoto);
                        Directory.CreateDirectory("AuxiliarDirectory");
                        auxPath = Directory.GetCurrentDirectory() + "/AuxiliarDirectory";
                        File.Move(classToEdit.UrlFoto, auxPath + $"/{oldFileName}");
                    }

                    try
                    {
                        int fileNameLength = BitConverter.ToInt32(await networkDataHelper.ReceiveAsync(Protocol.FileNameLenghtSize).ConfigureAwait(false), 0);
                        string fileNameStr = Encoding.UTF8.GetString(await networkDataHelper.ReceiveAsync(fileNameLength).ConfigureAwait(false));
                        fileName = fileNameStr;

                        long fileSize = BitConverter.ToInt64(await networkDataHelper.ReceiveAsync(Protocol.FileSizeLenghtSize).ConfigureAwait(false), 0);

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
                                buffer = await networkDataHelper.ReceiveAsync(Protocol.MaxFileChunkSize).ConfigureAwait(false);
                                offset += Protocol.MaxFileChunkSize;
                            }
                            else
                            {
                                long lastPartSize = fileSize - offset;
                                buffer = await networkDataHelper.ReceiveAsync((int)lastPartSize).ConfigureAwait(false);
                                offset += lastPartSize;
                            }
                            Directory.CreateDirectory($"Images/{finalClassName}");
                            fileStreamHelper.Write($"Images/{finalClassName}/" + fileName, buffer);
                            currentPart++;
                        }

                        imgUrl = Directory.GetCurrentDirectory() + $"/Images/{finalClassName}/" + fileName;
                        newImgUrl = imgUrl;

                        if (deleteOldFolder && oldHasImg)
                        {
                            Directory.Delete($"Images/{className}");
                            File.Delete(auxPath + $"/{oldFileName}");
                            Directory.Delete("AuxiliarDirectory");
                        }
                    }
                    catch (Exception)
                    {
                        string imgPath = Directory.GetCurrentDirectory() + $"/Images/{finalClassName}/" + fileName;
                        if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                        {
                            try 
                            {
                                if (auxPath != "") {
                                    Directory.CreateDirectory($"Images/{finalClassName}");
                                    File.Move(auxPath + $"/{oldFileName}", $"/Images/{finalClassName}/{oldFileName}");
                                }
                                File.Delete(imgPath);
                            } 
                            catch { }
                        }
                    }
                }

                lock (clasesLock)
                {
                    classToEdit.Nombre = newName;
                    classToEdit.Descripcion = newDescription;
                    classToEdit.CuposMaximos = newQuota;
                    classToEdit.Fecha = parsedDate;
                    classToEdit.Hora = newTime;
                    classToEdit.Duracion = newDuration;
                    classToEdit.Link = $"www.classlink.com/{newName.Replace(" ", "_").ToLower()}";
                    classToEdit.UrlFoto = newImgUrl;
                }

                await networkDataHelper.SendAsync(BitConverter.GetBytes(true)).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error editing class: {e.Message}");
                await networkDataHelper.SendAsync(BitConverter.GetBytes(false)).ConfigureAwait(false);
                return false;
            }
        }

        static async Task HandleHistoryAsync(NetworkDataHelper ndh, byte[] cmdData)
        {
            try
            {
                string userName = Encoding.UTF8.GetString(cmdData);
                Usuario? user;
                lock (usersLock) { user = users.FirstOrDefault(u => u.nombre == userName); }
                if (user == null)
                {
                    await ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
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

                await ndh.SendAsync(BitConverter.GetBytes(results.Count)).ConfigureAwait(false);
                foreach (var line in results.Values)
                {
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await ndh.SendAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
                    await ndh.SendAsync(bytes).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending history: {e.Message}");
                await ndh.SendAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
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
