using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Common;
using Dominio;

namespace Server
{
    internal class Server
    {
        // --- límites y estado de conexiones ---
        static readonly int maxClientsAllowed = 3;
        static readonly object clientThreadsLock = new object();
        static int currentClients = 0;

        // --- configuración y dominio ---
        static readonly SettingManager settingsMgr = new SettingManager();

        static readonly List<Usuario> users = new List<Usuario>();
        static readonly object usersLock = new object();

        static readonly List<Clase> clases = new List<Clase>();
        static readonly object clasesLock = new object();


        static void Main(string[] args)
        {
            // Usuario semilla (opcional)
            users.Add(new Usuario("1", "1"));

            // Clases de ejemplo creadas por el usuario base
            var usuarioBase = users.First(u => u.nombre == "1");
            clases.Add(new Clase("Yoga", "Clase de yoga para principiantes", 60, 20, usuarioBase));
            clases.Add(new Clase("Pilates", "Pilates nivel intermedio", 45, 15, usuarioBase));
            clases.Add(new Clase("Funcional", "Entrenamiento funcional", 50, 25, usuarioBase));
            clases.Add(new Clase("Spinning", "Clase de spinning intensa", 40, 10, usuarioBase));
            clases.Add(new Clase("Stretching", "Estiramientos y movilidad", 30, 12, usuarioBase));

            Console.WriteLine("Starting Server Application ... ");

            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));
            var serverEndPoint = new IPEndPoint(serverIp, serverPort);

            // Bind + Listen
            serverSocket.Bind(serverEndPoint);
            serverSocket.Listen();
            Console.WriteLine($"Server waiting for connection on {serverIp}:{serverPort} ...");

            // Acepta SIEMPRE, controla cupo por conexión
            while (true)
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
                    // opcional: enviar un mensaje breve
                    try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
                    clientSocket.Close();
                    continue;
                }

                int clientNum = currentClients;
                Console.WriteLine($"Client connected. Current clients: {currentClients}");

                var clientThread = new Thread(() => HandleClient(clientSocket, clientNum)) { IsBackground = true };
                clientThread.Start();
            }

            // (no se llega; si necesitás cierre controlado, agregá una señal externa)
            // serverSocket.Close();
        }

        static void HandleClient(Socket clientSocket, int clientNum)
        {
            bool connected = true;
            bool clientLogged = false;

            var networkDataHelper = new NetworkDataHelper(clientSocket);

            try
            {
                while (connected)
                {
                    // --- Bucle previo al login ---
                    while (!clientLogged && connected)
                    {
                        try
                        {
                            byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);
                            byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes);
                            Command command = (Command)int.Parse(commandString);

                            byte[] commandDataLength = networkDataHelper.Receive(sizeof(int));
                            int length = BitConverter.ToInt32(commandDataLength);
                            byte[] commandData = length > 0 ? networkDataHelper.Receive(length) : Array.Empty<byte>();

                            switch (command)
                            {
                                case Command.Register:
                                    HandleRegister(networkDataHelper, commandData);
                                    break;
                                case Command.Login:
                                    clientLogged = HandleLogin(networkDataHelper, commandData);
                                    break;
                                default:
                                    Console.WriteLine($"Unknown pre-login command from client {clientNum}: {command}");
                                    break;
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

                    // --- Bucle con cliente logueado ---
                    while (clientLogged && connected)
                    {
                        try
                        {
                            byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);

                            byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                            string commandString = Encoding.UTF8.GetString(commandStringBytes);
                            CommandLogged commandLogged = (CommandLogged)int.Parse(commandString);

                            byte[] commandDataLength = networkDataHelper.Receive(sizeof(int));
                            int length = BitConverter.ToInt32(commandDataLength);
                            byte[] commandData = length > 0 ? networkDataHelper.Receive(length) : Array.Empty<byte>();

                            switch (commandLogged)
                            {
                                case CommandLogged.CreateClass:
                                    HandleCreateClass(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.JoinClass:
                                    // TODO: HandleJoinClass(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.ListClasses:
                                    Console.WriteLine("Llego a ListClasses");
                                    HandleListClasses(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.LeaveClass:
                                    // TODO: HandleLeaveClass(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.History:
                                    // TODO: HandleHistory(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.Exit:
                                    HandleLogout(networkDataHelper, commandData);
                                    clientLogged = false; // vuelve al menú inicial del cliente
                                    break;
                                default:
                                    Console.WriteLine($"Unknown logged-in command from client {clientNum}: {commandLogged}");
                                    break;
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
                try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
                clientSocket.Close();
                lock (clientThreadsLock) { currentClients--; }
                Console.WriteLine($"Client {clientNum} disconnected. Current clients: {currentClients}");
            }
        }

        // --- Comandos ---

        static void HandleLogout(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            Console.WriteLine("Client logged out.");
            // Confirmamos al cliente que el logout fue exitoso
            networkDataHelper.Send(BitConverter.GetBytes(true));
        }

        static void HandleListClasses(NetworkDataHelper networkDataHelper, byte[] data)
        {
            //Primero enviamos la cantidad de clases
                    Console.WriteLine("envio la cantidad de clases");

            //Luego enviamos cada clase
            try
            {


                lock (clasesLock)
                {

                    int cantClases = clases.Count;
                    byte[] cantClasesBytes = BitConverter.GetBytes(cantClases);
                    networkDataHelper.Send(cantClasesBytes);
                    foreach (var clase in clases)
                    {
                        string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Fecha:HH:mm}|{clase.Duracion}|{clase.owner.nombre}";
                        byte[] claseInfoBytes = Encoding.UTF8.GetBytes(claseInfo);
                        byte[] claseInfoLengthBytes = BitConverter.GetBytes(claseInfoBytes.Length);
                        networkDataHelper.Send(claseInfoLengthBytes);
                        networkDataHelper.Send(claseInfoBytes);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending class list: {e.Message}");
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
                if (!exists)
                {
                    users.Add(new Usuario(username, password));
                    added = true;
                }
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
                validUser = users.Any(u =>
                    string.Equals(username, u.nombre, StringComparison.Ordinal) &&
                    u.contrasena == password);
            }

            networkDataHelper.Send(BitConverter.GetBytes(validUser));
            if (validUser) Console.WriteLine($"User {username} logged in.");
            return validUser;
        }

        // Cliente envía: name|desc|quota|yyyy-MM-dd|HH:mm|dur|hasImg|user
        static bool HandleCreateClass(NetworkDataHelper dataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] p = dataString.Split('|');
            if (p.Length < 6)
            {
                dataHelper.Send(BitConverter.GetBytes(false));
                return false;
            }

            string name = p[0];
            string desc = p[1];
            bool okQuota = int.TryParse(p[2], out int quota); // No entendi bien esta validacion. Capaz es mejor hacerla en el cliente -> santi: Es para fijarse si el cupo es un numero y no otra cosa
            string startDate = p[3];
            string startTime = p[4];
            bool okDur = int.TryParse(p[5], out int dur);
            string hasImg = p[6];
            string user = p[7];

            if (!okQuota || !okDur)
            {
                dataHelper.Send(BitConverter.GetBytes(false));
                return false;
            }

            Usuario owner;
            lock (usersLock)
            {
                owner = users.FirstOrDefault(u => string.Equals(user, u.nombre, StringComparison.Ordinal));
            }
            if (owner == null)
            {
                dataHelper.Send(BitConverter.GetBytes(false));
                return false;
            }

            bool created = false;
            lock (clasesLock)
            {
                bool exists = clases.Any(c => string.Equals(c.Nombre, name, StringComparison.Ordinal));
                if (!exists)
                {
                    if (hasImg == "y")
                    {
                        try
                        {
                            byte[] fileNameLengthBuffer = dataHelper.Receive(Protocol.FileNameLenghtSize);
                            int fileNameLength = BitConverter.ToInt32(fileNameLengthBuffer);

                            byte[] fileNameBuffer = dataHelper.Receive(fileNameLength);
                            string fileName = Encoding.UTF8.GetString(fileNameBuffer);

                            byte[] fileSizeBuffer = dataHelper.Receive(Protocol.FileSizeLenghtSize);
                            long fileSize = BitConverter.ToInt64(fileSizeBuffer);

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
                                    buffer = dataHelper.Receive(Protocol.MaxFileChunkSize);
                                    offset += Protocol.MaxFileChunkSize;
                                }
                                else
                                {
                                    long lastPartSize = fileSize - offset;
                                    Console.WriteLine($"Receiving part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                                    buffer = dataHelper.Receive((int)lastPartSize);
                                    offset += lastPartSize;
                                }
                                Directory.CreateDirectory("Images");
                                fileStreamHelper.Write("Images/" + fileName, buffer);
                                currentPart++;
                            }

                            Console.WriteLine($"File {fileName} received successfully.");
                        }
                        catch (SocketException e)
                        {
                            // Agregar manejo de error
                            // Ejemplo de clase: clientActive = false;
                            Console.WriteLine("SERVER CAYO EN SOCKET EXCEPTION: " + e.Message);
                        }
                        catch (Exception e)
                        {
                            // Agregar manejo de error
                            Console.WriteLine("SERVER CAYO EN EXCEPTION: "+ e.Message);
                        }
                    }
                    var nueva = new Clase(name, desc, dur, quota, owner);
                    clases.Add(nueva);     // IMPORTANTE: agregar a la lista
                    created = true;
                }
            }

            dataHelper.Send(BitConverter.GetBytes(created));
            return created;
        }
    }
}
