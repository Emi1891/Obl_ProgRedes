using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Common;
using Dominio;

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

            Thread keyListener = new Thread(() =>
            {
                while (running)
                {
                    string? input = Console.ReadLine();
                    if (input != null && input.ToLower() == "q")
                    {
                        Console.WriteLine("\nShutting server down...\n");
                        running = false;
                        try
                        {
                            serverSocket.Close();
                        }
                        catch
                        {
                            Console.WriteLine("Unexpexted socket error while shutting the server down.");
                        }
                    }

                    Thread.Sleep(100);
                }
            });
            keyListener.IsBackground = true;
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
            catch (SocketException se)
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

                    while (clientLogged && connected && running)
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
                                    Console.WriteLine("Llego a JoinClass");
                                    HandleJoinClass(networkDataHelper);
                                    break;
                                case CommandLogged.ListClasses:
                                    HandleListClasses(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.LeaveClass:
                                    HandleLeaveClass(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.History:
                                    // TODO: HandleHistory(networkDataHelper, commandData);
                                    break;
                                case CommandLogged.Exit:
                                    HandleLogout(networkDataHelper, commandData);
                                    clientLogged = false;
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
                try { 
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                } catch { }
                
                lock (clientThreadsLock) { currentClients--; }
                Console.WriteLine($"Client {clientNum} disconnected. Current clients: {currentClients}");
            }
        }

        static bool HandleLeaveClass(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            showClasesOfTheClient(networkDataHelper);
            byte[] classNameLengthBytes = networkDataHelper.Receive(sizeof(int));
            int classNameLength = BitConverter.ToInt32(classNameLengthBytes);
            byte[] classNameBytes = networkDataHelper.Receive(classNameLength);
            string data = Encoding.UTF8.GetString(classNameBytes);
            string usuarioLoggeado = data.Split('|')[0];
            string className = data.Split('|')[1];

            Clase? clase = null;
            lock (clasesLock)
            {
                clase = classes.FirstOrDefault(c => c.Nombre == className);
            }

            try
            {
                if (clase == null)
                {
                    networkDataHelper.Send(BitConverter.GetBytes(false));
                    return false;
                }

                Usuario? usuario = null;
                lock (usersLock)
                {
                    usuario = users.FirstOrDefault(u => u.nombre.Equals(usuarioLoggeado));
                }

                if (usuario == null)
                {
                    networkDataHelper.Send(BitConverter.GetBytes(false));
                    return false;
                }

                lock (clasesLock)
                {
                    if (clase.usuarios.Contains(usuario))
                    {
                        clase.usuarios.Remove(usuario);
                        networkDataHelper.Send(BitConverter.GetBytes(true));
                        return true;
                    }
                    else
                    {
                        networkDataHelper.Send(BitConverter.GetBytes(false));
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error leaving class: {e.Message}");
                networkDataHelper.Send(BitConverter.GetBytes(false));
                return false;

            }
        }

        static void showClasesOfTheClient(NetworkDataHelper networkDataHelper)
        {
            byte[] userNameLengthBytes = networkDataHelper.Receive(sizeof(int));
            int userNameLength = BitConverter.ToInt32(userNameLengthBytes);
            byte[] userNameBytes = networkDataHelper.Receive(userNameLength);
            string userName = Encoding.UTF8.GetString(userNameBytes);

            Usuario? usuarioLogeado = null;
            lock (usersLock)
            {
                usuarioLogeado = users.FirstOrDefault(u => u.nombre.Equals(userName));
            }

            if (usuarioLogeado == null)
            {
                byte[] cero = BitConverter.GetBytes(0);
                networkDataHelper.Send(cero);
                return;
            }

            int cantClases = 0;
            List<Clase> clasesUsuario = new List<Clase>();

            lock (clasesLock)
            {
                foreach (Clase c in classes)
        {
                    if (c.usuarios.Contains(usuarioLogeado)) { cantClases++; clasesUsuario.Add(c); }
                }
            }

            byte[] cantClasesBytes = BitConverter.GetBytes(cantClases);
            networkDataHelper.Send(cantClasesBytes);
            try
            {
                foreach (var clase in clasesUsuario)
                {
                    string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}";
                    byte[] claseInfoBytes = Encoding.UTF8.GetBytes(claseInfo);
                    byte[] claseInfoLengthBytes = BitConverter.GetBytes(claseInfoBytes.Length);
                    networkDataHelper.Send(claseInfoLengthBytes);
                    networkDataHelper.Send(claseInfoBytes);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error sending class list: {e.Message}");
            }
        }

        static void HandleLogout(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            Console.WriteLine("Client logged out.");
            networkDataHelper.Send(BitConverter.GetBytes(true));
        }

        static void HandleListClasses(NetworkDataHelper networkDataHelper, byte[] data)
        {
            try
            {
                lock (clasesLock)
                {

                    int cantClases = classes.Count;
                    byte[] cantClasesBytes = BitConverter.GetBytes(cantClases);
                    networkDataHelper.Send(cantClasesBytes);
                    foreach (var clase in classes)
                    {
                        string claseInfo = $"{clase.Nombre}|{clase.Descripcion}|{clase.CuposMaximos - clase.CuposDisponibles}/{clase.CuposMaximos}|{clase.Fecha:yyyy-MM-dd}|{clase.Hora:HH:mm}|{clase.Duracion}|{clase.UrlFoto}|{clase.owner.nombre}|{clase.Link}";
                        byte[] claseInfoBytes = Encoding.UTF8.GetBytes(claseInfo);
                        byte[] claseInfoLengthBytes = BitConverter.GetBytes(claseInfoBytes.Length);
                        networkDataHelper.Send(claseInfoLengthBytes);
                        networkDataHelper.Send(claseInfoBytes);
                    }
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
                                buffer = fileStreamHelper.Read(path, offset, Protocol.MaxFileChunkSize);
                                offset += Protocol.MaxFileChunkSize;
                            }
                            else
                            {
                                long lastPartSize = fileSize - offset;
                                Console.WriteLine($"Sending part {currentPart}/{partCount} of size {Protocol.MaxFileChunkSize}.");
                                buffer = fileStreamHelper.Read(path, offset, (int)lastPartSize);
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
            bool okQuota = int.TryParse(p[2], out int quota);
            string startDate = p[3];
            string startTime = p[4];
            bool okDur = int.TryParse(p[5], out int dur);
            string hasImg = p[6];
            string user = p[7];
            string imgUrl = "";

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
                            byte[] fileNameLengthBuffer = dataHelper.Receive(Protocol.FileNameLenghtSize);
                            int fileNameLength = BitConverter.ToInt32(fileNameLengthBuffer);

                            byte[] fileNameBuffer = dataHelper.Receive(fileNameLength);
                            fileName = Encoding.UTF8.GetString(fileNameBuffer);

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
                            imgUrl = Directory.GetCurrentDirectory() + "/Images/" + fileName;
                        }
                        catch (SocketException e)
                        {
                            Console.WriteLine("Connection to client lost: " + e.Message);

                            string imgPath = Directory.GetCurrentDirectory() + $"/Images/" + fileName;
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

                            throw e;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Server error: " + e.Message);

                            string imgPath = Directory.GetCurrentDirectory() + $"/Images/" + fileName;
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

        static void HandleJoinClass(NetworkDataHelper networkDataHelper)
        {
            bool joined = false;

            try
            {
                lock (clasesLock)
                {
                    int classCoutner = 0;
                    foreach (var c in classes)
                    {
                        if (c.CuposDisponibles > 0)
                        {
                            classCoutner++;
                        }
                    }
                    networkDataHelper.Send(BitConverter.GetBytes(classCoutner));

                    foreach (var c in classes)
                    {
                        if (c.CuposDisponibles > 0)
                        {
                            string classInfo = $"{c.Nombre}|{c.CuposMaximos - c.CuposDisponibles}|{c.CuposMaximos}";
                            byte[] classInfoBytes = Encoding.UTF8.GetBytes(classInfo);
                            byte[] classInfoLengthBytes = BitConverter.GetBytes(classInfoBytes.Length);
                            networkDataHelper.Send(classInfoLengthBytes);
                            networkDataHelper.Send(classInfoBytes);
                        }
                    }
                }
                byte[] classAndUserNameLengthBytes = networkDataHelper.Receive(4);

                int namesLength = BitConverter.ToInt32(classAndUserNameLengthBytes);
                byte[] classAndUserNameBytes = networkDataHelper.Receive(namesLength);
                
                string classAndUserName = Encoding.UTF8.GetString(classAndUserNameBytes);
                string[] names = classAndUserName.Split('|');
                string className = names[0];
                string userName = names[1];
                
                Usuario userToEnroll = users.Find(u => u.nombre == userName);
                Clase desiredClass = classes.Find(c => c.Nombre == className);
                
                lock (clasesLock)
                {
                    if (desiredClass.CuposDisponibles > 0 && !desiredClass.usuarios.Any(u => u.nombre == userName))
                    {
                        classes.Find(c => c.Nombre == className).AgregarUsuario(userToEnroll);
                        joined = true;
                        Console.WriteLine($"User {userName} joined class '{className}'.");
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
    }
}
