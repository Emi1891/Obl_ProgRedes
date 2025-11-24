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
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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

        public static List<Usuario> GetUsers() => users;
        public static List<Clase> GetClasses() => classes;
        public static object GetUsersLock() => usersLock;
        public static object GetClassesLock() => clasesLock;

        static readonly Dictionary<string, HashSet<string>> cancellations = new Dictionary<string, HashSet<string>>();
        static readonly object cancelLock = new object();

        // Report cancellation support
        static CancellationTokenSource? reportCts = null;
        static readonly object reportLock = new object();

        static volatile bool running = true;

        static readonly Dictionary<string, string> classWebhooks = new Dictionary<string, string>();
        static readonly object webhookLock = new object();
        static readonly Dictionary<string, System.Threading.Timer> webhookTimers = new Dictionary<string, System.Threading.Timer>();
        static readonly object timerLock = new object();

        static async Task Main(string[] args)
        {
            LogHelper.Initialize();
            
            // Esperar a que RabbitMQ esté disponible antes de enviar logs de inicialización
            // Esto asegura que los logs de usuarios/clases por defecto se envíen correctamente
            if (!LogHelper.WaitForConnection())
            {
                Console.WriteLine("Warning: Could not establish RabbitMQ connection. Default logs may not be sent.");
            }
            else
            {
                // Pequeño delay adicional para asegurar que el servidor de logs tenga tiempo de conectarse
                await Task.Delay(1000);
            }

            users.Add(new Usuario("1", "1"));

            var usuarioBase = users.First(u => u.nombre == "1");
            string today = DateTime.Now.ToString("dd/MM/yyyy");

            // Crear clases por defecto
            classes.Add(new Clase("Yoga", "Clase de yoga para principiantes", 60, 20, usuarioBase, today, "09:00"));
            classes.Add(new Clase("Pilates", "Pilates nivel intermedio", 45, 15, usuarioBase, today, "10:00"));
            classes.Add(new Clase("Funcional", "Entrenamiento funcional", 50, 25, usuarioBase, today, "22:00"));
            classes.Add(new Clase("Spinning", "Clase de spinning intensa", 40, 10, usuarioBase, today, "22:30"));
            classes.Add(new Clase("Stretching", "Estiramientos y movilidad", 30, 12, usuarioBase, today, "23:00"));

            // Crear 3 usuarios por defecto
            var user1 = new Usuario("user1", "user1");
            var user2 = new Usuario("user2", "user2");
            var user3 = new Usuario("user3", "user3");
            users.Add(user1);
            users.Add(user2);
            users.Add(user3);

            // Generar logs de registro para los usuarios por defecto
            LogHelper.SendLog("user1", "", "User registered", "New user registered: user1");
            LogHelper.SendLog("user2", "", "User registered", "New user registered: user2");
            LogHelper.SendLog("user3", "", "User registered", "New user registered: user3");

            // Crear clases para los usuarios por defecto
            // Clase compartida por los 3 usuarios
            var sharedClassAll = new Clase("Shared Class All", "Class shared by all three users", 60, 10, user1, today, "14:00");
            classes.Add(sharedClassAll);
            LogHelper.SendLog("user1", "Shared Class All", "Class created", "User user1 created the class Shared Class All");
            sharedClassAll.AgregarUsuario(user1);
            LogHelper.SendLog("user1", "Shared Class All", "User enrolled in class", "User user1 enrolled in the class Shared Class All");
            sharedClassAll.AgregarUsuario(user2);
            LogHelper.SendLog("user2", "Shared Class All", "User enrolled in class", "User user2 enrolled in the class Shared Class All");
            sharedClassAll.AgregarUsuario(user3);
            LogHelper.SendLog("user3", "Shared Class All", "User enrolled in class", "User user3 enrolled in the class Shared Class All");

            // Clase individual para user1
            var individualClass1 = new Clase("Individual Class 1", "Individual class for user1", 45, 5, user1, today, "15:00");
            classes.Add(individualClass1);
            LogHelper.SendLog("user1", "Individual Class 1", "Class created", "User user1 created the class Individual Class 1");
            individualClass1.AgregarUsuario(user1);
            LogHelper.SendLog("user1", "Individual Class 1", "User enrolled in class", "User user1 enrolled in the class Individual Class 1");

            // Clase individual para user2
            var individualClass2 = new Clase("Individual Class 2", "Individual class for user2", 45, 5, user2, today, "16:00");
            classes.Add(individualClass2);
            LogHelper.SendLog("user2", "Individual Class 2", "Class created", "User user2 created the class Individual Class 2");
            individualClass2.AgregarUsuario(user2);
            LogHelper.SendLog("user2", "Individual Class 2", "User enrolled in class", "User user2 enrolled in the class Individual Class 2");

            // Clase individual para user3
            var individualClass3 = new Clase("Individual Class 3", "Individual class for user3", 45, 5, user3, today, "17:00");
            classes.Add(individualClass3);
            LogHelper.SendLog("user3", "Individual Class 3", "Class created", "User user3 created the class Individual Class 3");
            individualClass3.AgregarUsuario(user3);
            LogHelper.SendLog("user3", "Individual Class 3", "User enrolled in class", "User user3 enrolled in the class Individual Class 3");

            // Clase compartida por user1 y user2
            var sharedClass12 = new Clase("Shared Class 1-2", "Class shared by user1 and user2", 50, 8, user1, today, "18:00");
            classes.Add(sharedClass12);
            LogHelper.SendLog("user1", "Shared Class 1-2", "Class created", "User user1 created the class Shared Class 1-2");
            sharedClass12.AgregarUsuario(user1);
            LogHelper.SendLog("user1", "Shared Class 1-2", "User enrolled in class", "User user1 enrolled in the class Shared Class 1-2");
            sharedClass12.AgregarUsuario(user2);
            LogHelper.SendLog("user2", "Shared Class 1-2", "User enrolled in class", "User user2 enrolled in the class Shared Class 1-2");

            // Iniciar tarea para verificar webhooks
            _ = Task.Run(() => StartWebhookScheduler());

            // Iniciar servidor gRPC en background
            _ = Task.Run(async () => await StartGrpcServerAsync());

            Console.WriteLine("Starting Server Application ... ");
            Console.WriteLine("\nPress 'q' and 'Enter' at any time while not handling a client to stop the server.\n");
            Console.WriteLine("Type 'r' or 'report' and press Enter to print the classes report.\n");
            Console.WriteLine("Type 'c' or 'cancel' and press Enter to cancel a running report.\n");

            string serverHostnameString = Environment.GetEnvironmentVariable(ServerConfig.ServerIpConfigKey) ?? "0.0.0.0";
            IPAddress? serverIp;
            if (serverHostnameString == "0.0.0.0" || serverHostnameString == "::0")
            {
                serverIp = IPAddress.Any; // Usar Any para escuchar en todas las interfaces
            }
            else
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(serverHostnameString);
                serverIp = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                if (serverIp == null)
                {
                    throw new Exception($"Could not resolve server hostname: {serverHostnameString}");
                }
            }

            string serverPortString = Environment.GetEnvironmentVariable(ServerConfig.ServerPortConfigKey) ?? "5000";
            int serverPort = int.Parse(serverPortString);

            IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

            var listener = new TcpListener(serverEndPoint);
            listener.Start();
            Console.WriteLine($"Server waiting for connection on {serverIp}:{serverPort} ...");

            // Console key listener runs on a background task so we can stop the listener with 'q' and request reports
            _ = Task.Run(() =>
            {
                while (running)
                {
                    string? input = Console.ReadLine();
                    if (input == null) continue;
                    input = input.Trim().ToLowerInvariant();
                    if (input == "q")
                    {
                        Console.WriteLine("\nShutting server down...\n");
                        running = false;
                        try { listener.Stop(); } catch { }
                        break;
                    }
                    else if (input == "r" || input == "report")
                    {
                        lock (reportLock)
                        {
                            if (reportCts != null)
                            {
                                Console.WriteLine("A report is already running. Use 'c' or 'cancel' to cancel it.");
                                continue;
                            }

                            reportCts = new CancellationTokenSource();
                            var localCts = reportCts;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await PrintClassesReportAsync(localCts.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    Console.WriteLine("Report generation cancelled.");
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error generating report: {e.Message}");
                                }
                                finally
                                {
                                    lock (reportLock)
                                    {
                                        if (reportCts == localCts) reportCts = null;
                                    }
                                }
                            });
                        }
                    }
                    else if (input == "c" || input == "cancel")
                    {
                        lock (reportLock)
                        {
                            if (reportCts == null)
                            {
                                Console.WriteLine("No report is running.");
                            }
                            else
                            {
                                Console.WriteLine("Cancelling report...");
                                try { reportCts.Cancel(); } catch { }
                            }
                        }
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

        // Report generation: prints requested statistics to console (cancellable)
        static async Task PrintClassesReportAsync(CancellationToken ct)
        {
            List<Clase> snapshot;
            lock (clasesLock)
            {
                snapshot = classes.Where(c => c.Fecha.Date == DateTime.Today).ToList();
            }

            ct.ThrowIfCancellationRequested();

            int totalClases = snapshot.Count;
            double promedioDuracion = totalClases == 0 ? 0 : snapshot.Average(c => (double)c.Duracion);

            ct.ThrowIfCancellationRequested();

            int totalInscriptos = snapshot.Sum(c => c.usuarios?.Count ?? 0);
            double promedioInscriptos = totalClases == 0 ? 0 : (double)totalInscriptos / totalClases;

            ct.ThrowIfCancellationRequested();

            var clasesConImagen = snapshot.Where(c => !string.IsNullOrWhiteSpace(c.UrlFoto)).ToList();
            int cantidadClasesConImagen = clasesConImagen.Count;

            long tamañoTotalImagenes = 0;
            int imagenesEncontradas = 0;
            foreach (var c in clasesConImagen)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(c.UrlFoto))
                    {
                        var fi = new FileInfo(c.UrlFoto);
                        tamañoTotalImagenes += fi.Length;
                        imagenesEncontradas++;
                    }
                }
                catch { }

                // Simulate longer work granularity so cancellation can be observed on large sets
                await Task.Yield();
            }

            ct.ThrowIfCancellationRequested();

            double promedioTamImagen = imagenesEncontradas == 0 ? 0 : (double)tamañoTotalImagenes / imagenesEncontradas;

            Console.WriteLine($"\n=== REPORT: Today's class statistics ({DateTime.Today:dd-MM-yyyy})===");
            Console.WriteLine($"Total number of classes: {totalClases}");
            Console.WriteLine($"Average duration (minutes): {promedioDuracion:F2}");
            Console.WriteLine($"Total number of enrolled users: {totalInscriptos}");
            Console.WriteLine($"Average enrolled users per class: {promedioInscriptos:F2}");
            Console.WriteLine($"Number of classes with cover image: {cantidadClasesConImagen}");
            Console.WriteLine($"Total size of cover images (bytes): {tamañoTotalImagenes}");
            Console.WriteLine($"Average size of cover images (bytes): {promedioTamImagen:F2}");
            Console.WriteLine("===================================\n");

            await Task.CompletedTask.ConfigureAwait(false);
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
            }

            // ensure addition is done under lock
            lock (usersLock)
            {
                bool exists = users.Any(u => string.Equals(username, u.nombre, StringComparison.Ordinal));
                if (!exists) { users.Add(new Usuario(username, password)); added = true; }
            }

            if (added)
            {
                LogHelper.SendLog(username, "", "User registered", $"New user registered: {username}");
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
            if (validUser)
            {
                Console.WriteLine($"User {username} logged in.");
                LogHelper.SendLog(username, "", "User logged in", $"User {username} logged in");
            }
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
                string? webhookUrl = names.Length > 2 && !string.IsNullOrWhiteSpace(names[2]) ? names[2] : null;

                Usuario? userToEnroll;
                lock (usersLock) { userToEnroll = users.FirstOrDefault(u => u.nombre == userName); }

                bool didJoin = false;
                Clase? joinedClass = null;
                lock (clasesLock)
                {
                    var desiredClass = classes.FirstOrDefault(c => c.Nombre == className);
                    if (desiredClass != null && desiredClass.CuposDisponibles > 0 && !desiredClass.usuarios.Any(u => u.nombre == userName))
                    {
                        desiredClass.AgregarUsuario(userToEnroll);
                        didJoin = true;
                        joinedClass = desiredClass;
                        Console.WriteLine($"User {userName} joined class '{className}'.");
                    }
                }

                if (didJoin && joinedClass != null)
                {
                    LogHelper.SendLog(userName, className, "User enrolled in class", $"User {userName} enrolled in the class {className}");

                    // Guardar webhook si se proporcionó
                    if (!string.IsNullOrWhiteSpace(webhookUrl))
                    {
                        string webhookKey = $"{userName}|{className}";
                        lock (webhookLock)
                        {
                            classWebhooks[webhookKey] = webhookUrl;
                        }
                        
                        // Enviar webhook inmediato de inscripción
                        _ = Task.Run(async () => await CallEnrollmentWebhookAsync(webhookUrl, joinedClass, userName));
                        
                        // Programar webhook de alerta 1 minuto antes
                        ScheduleWebhook(joinedClass, webhookUrl, userName);
                    }

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
                LogHelper.SendLog(userName, className, "User left class", $"User {userName} left the class {className}");
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
                    imgUrl = "";
                    string imgPath = Directory.GetCurrentDirectory() + $"/Images/{name}/" + fileName;
                    if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                    {
                        try { File.Delete(imgPath); } catch { }
                    }
                }
            }

            try
            {
                var newClass = new Clase(name, desc, dur, quota, owner, startDate, startTime);
                if (imgUrl != "") newClass.UrlFoto = imgUrl;
                lock (clasesLock) { classes.Add(newClass); }
                created = true;
                if (created)
                {
                    LogHelper.SendLog(user, name, "Class created", $"User {user} created the class {name}");
                }
            }
            catch (Exception)
            {
                created = false;
            }

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
                        newImgUrl = "No image uploaded";
                        string imgPath = Directory.GetCurrentDirectory() + $"/Images/{finalClassName}/" + fileName;
                        if (!string.IsNullOrEmpty(fileName) && File.Exists(imgPath))
                        {
                            try
                            {
                                if (auxPath != "" && oldHasImg)
                                {
                                    Directory.CreateDirectory($"Images/{finalClassName}");
                                    File.Move(auxPath + $"/{oldFileName}", Directory.GetCurrentDirectory() + $"/Images/{finalClassName}/{oldFileName}");
                                }
                                File.Delete(imgPath);
                            }
                            catch { }
                        }
                        else if (oldHasImg && auxPath != "")
                        {
                            try
                            {
                                Directory.CreateDirectory($"Images/{finalClassName}");
                                File.Move(auxPath + $"/{oldFileName}", Directory.GetCurrentDirectory() + $"/Images/{finalClassName}/{oldFileName}");
                                Directory.Delete("AuxiliarDirectory");
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

        static void ScheduleWebhook(Clase clase, string webhookUrl, string userName)
        {
            try
            {
                var now = DateTime.Now;
                Console.WriteLine($"[DEBUG] ScheduleWebhook - Current server time: {now:yyyy-MM-dd HH:mm:ss}");
                
                // Intentar parsear la hora en formato HH:mm o HH:MM
                if (!TimeSpan.TryParse(clase.Hora, out var timeSpan))
                {
                    // Si falla, intentar parsear como HH:mm manualmente
                    var parts = clase.Hora.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes))
                    {
                        timeSpan = new TimeSpan(hours, minutes, 0);
                        Console.WriteLine($"[DEBUG] Time parsed manually: {timeSpan}");
                    }
                    else
                    {
                        Console.WriteLine($"[ERROR] Could not parse time: {clase.Hora}");
                        return;
                    }
                }
                
                var classStartTime = clase.Fecha.Date.Add(timeSpan);
                var webhookTime = classStartTime.AddMinutes(-1); // 1 minuto antes

                Console.WriteLine($"[DEBUG] Class: {clase.Nombre}, Date: {clase.Fecha:yyyy-MM-dd}, Time: {clase.Hora}");
                Console.WriteLine($"[DEBUG] Parsed TimeSpan: {timeSpan}");
                Console.WriteLine($"[DEBUG] Class start time: {classStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"[DEBUG] Webhook time (1 min before): {webhookTime:yyyy-MM-dd HH:mm:ss}");

                // Solo enviar el webhook si la clase aún no ha comenzado
                if (classStartTime <= now)
                {
                    // La clase ya comenzó o ya pasó, no enviar webhook
                    Console.WriteLine($"[DEBUG] Class {clase.Nombre} has already started or passed. Current time: {now:yyyy-MM-dd HH:mm:ss}, Start time: {classStartTime:yyyy-MM-dd HH:mm:ss}");
                    return;
                }

                // Si el tiempo del webhook ya pasó pero la clase aún no comenzó, no enviar
                // (esto puede pasar si el usuario se une muy cerca del inicio)
                if (webhookTime <= now)
                {
                    Console.WriteLine($"[DEBUG] The time to send the webhook for class {clase.Nombre} has already passed. Current time: {now:yyyy-MM-dd HH:mm:ss}, Webhook time: {webhookTime:yyyy-MM-dd HH:mm:ss}");
                    return;
                }

                var delay = webhookTime - now;
                string timerKey = $"{userName}|{clase.Nombre}";

                Console.WriteLine($"[DEBUG] Calculated delay: {delay.TotalSeconds:F2} seconds ({delay.TotalMinutes:F2} minutes)");

                // Validar que el delay sea positivo
                if (delay.TotalMilliseconds <= 0)
                {
                    Console.WriteLine($"[ERROR] Negative or zero delay: {delay.TotalMilliseconds} ms. Cannot schedule webhook.");
                    return;
                }

                // System.Threading.Timer tiene un límite máximo de aproximadamente 2^31-1 milisegundos
                // Si el delay es muy grande, usar Task.Delay en su lugar
                long delayMs = (long)delay.TotalMilliseconds;
                const long maxTimerDelay = int.MaxValue - 1; // ~24.8 días

                lock (timerLock)
                {
                    if (webhookTimers.TryGetValue(timerKey, out var existingTimer))
                    {
                        existingTimer?.Dispose();
                        Console.WriteLine($"[DEBUG] Existing timer removed for {timerKey}");
                    }

                    if (delayMs > maxTimerDelay)
                    {
                        // Delay muy grande, usar Task.Delay en lugar de Timer
                        Console.WriteLine($"[DEBUG] Delay too large ({delay.TotalDays:F2} days), using Task.Delay instead of Timer");
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(delay);
                            Console.WriteLine($"[DEBUG] Task.Delay completed for class {clase.Nombre} (user: {userName})");
                            await CallWebhookAsync(webhookUrl, clase, userName);
                            lock (timerLock)
                            {
                                webhookTimers.Remove(timerKey);
                                Console.WriteLine($"[DEBUG] Timer removed from list for {timerKey}");
                            }
                        });
                    }
                    else
                    {
                        // Delay normal, usar Timer
                        var timer = new System.Threading.Timer(_ =>
                        {
                            Console.WriteLine($"[DEBUG] Timer executed for class {clase.Nombre} (user: {userName})");
                            // Ejecutar el webhook de forma asíncrona
                            _ = Task.Run(async () =>
                            {
                                await CallWebhookAsync(webhookUrl, clase, userName);
                                lock (timerLock)
                                {
                                    webhookTimers.Remove(timerKey);
                                    Console.WriteLine($"[DEBUG] Timer removed from list for {timerKey}");
                                }
                            });
                        }, null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);

                        webhookTimers[timerKey] = timer;
                        Console.WriteLine($"[DEBUG] Webhook scheduled for {webhookTime:yyyy-MM-dd HH:mm:ss} for class {clase.Nombre} (user: {userName}, delay: {delay.TotalMinutes:F2} minutes, {delay.TotalSeconds:F2} seconds, {delayMs} ms)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error scheduling webhook: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        static async Task CallEnrollmentWebhookAsync(string webhookUrl, Clase clase, string userName)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var payload = new
                {
                    message = $"You have successfully enrolled in the class '{clase.Nombre}'",
                    @class = clase.Nombre,
                    user = userName,
                    date = clase.Fecha.ToString("dd/MM/yyyy"),
                    time = clase.Hora,
                    link = clase.Link,
                    type = "enrollment"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(webhookUrl, content);
                Console.WriteLine($"Enrollment webhook sent for class {clase.Nombre} (user: {userName}). Status: {response.StatusCode}");
                LogHelper.SendLog(userName, clase.Nombre, "Enrollment webhook sent", $"Enrollment webhook sent for class {clase.Nombre}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling enrollment webhook: {ex.Message}");
            }
        }

        static async Task CallWebhookAsync(string webhookUrl, Clase clase, string userName)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var payload = new
                {
                    message = $"The class '{clase.Nombre}' will start in 1 minute",
                    @class = clase.Nombre,
                    user = userName,
                    date = clase.Fecha.ToString("dd/MM/yyyy"),
                    time = clase.Hora,
                    link = clase.Link,
                    type = "alert"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(webhookUrl, content);
                Console.WriteLine($"Alert webhook sent for class {clase.Nombre} (user: {userName}). Status: {response.StatusCode}");
                LogHelper.SendLog(userName, clase.Nombre, "Alert webhook sent", $"Alert webhook sent for class {clase.Nombre}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling alert webhook: {ex.Message}");
            }
        }

        static async Task StartWebhookScheduler()
        {
            while (running)
            {
                try
                {
                    await Task.Delay(60000); // Verificar cada minuto

                    List<Clase> classesToCheck;
                    lock (clasesLock)
                    {
                        classesToCheck = classes.ToList();
                    }

                    foreach (var clase in classesToCheck)
                    {
                        TimeSpan.TryParse(clase.Hora, out var timeSpan);
                        var classStartTime = clase.Fecha.Date.Add(timeSpan);
                        var webhookTime = classStartTime.AddMinutes(-1);

                        if (webhookTime > DateTime.Now && webhookTime <= DateTime.Now.AddMinutes(1))
                        {
                            // Verificar si hay webhooks para esta clase
                            lock (webhookLock)
                            {
                                foreach (var usuario in clase.usuarios)
                                {
                                    string webhookKey = $"{usuario.nombre}|{clase.Nombre}";
                                    if (classWebhooks.TryGetValue(webhookKey, out var webhookUrl))
                                    {
                                        ScheduleWebhook(clase, webhookUrl, usuario.nombre);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in webhook scheduler: {ex.Message}");
                }
            }
        }

        static async Task StartGrpcServerAsync()
        {
            try
            {
                var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
                
                // Configurar Kestrel para aceptar HTTP/2 sin TLS
                builder.WebHost.ConfigureKestrel(options =>
                {
                    string grpcPort = Environment.GetEnvironmentVariable("GRPC_PORT") ?? "5002";
                    options.ListenAnyIP(int.Parse(grpcPort), listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });
                
                builder.Services.AddGrpc();
                var app = builder.Build();

                app.MapGrpcService<AuthServiceImpl>();
                app.MapGet("/", () => "gRPC server is running");

                string grpcPort = Environment.GetEnvironmentVariable("GRPC_PORT") ?? "5002";
                Console.WriteLine($"gRPC server listening on http://0.0.0.0:{grpcPort}");
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting gRPC server: {ex.Message}");
            }
        }
    }
}
