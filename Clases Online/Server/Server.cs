
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
        static List<Usuario> users = new List<Usuario>();
        static readonly object usersLock = new object();
        static List<Clase> clases = new List<Clase>();


        static void Main(string[] args)
        {
            Usuario U = new Usuario("1", "1");
            users.Add(U);
            Console.WriteLine("Starting Server Application ... ");
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ServerConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ServerConfig.ServerPortConfigKey));

            IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

            serverSocket.Bind(serverEndPoint);
            serverSocket.Listen();
            Console.WriteLine("Server waiting for conection...");

            while (currentClients < maxClientsAllowed)
            {
                Socket clientSocket = serverSocket.Accept();
                lock (clientThreadsLock)
                {
                    currentClients++;
                }
                int clientNum = currentClients;
                Console.WriteLine("Client connected. Current clients: " + currentClients);
                Thread clientThread = new Thread(() => HandleClient(clientSocket, clientNum));
                clientThread.Start();
            }

            serverSocket.Close();

        }


        static void HandleClient(Socket clientSocket, int clientNum)
        {
            bool connected = true;
            NetworkDataHelper networkDataHelper = new NetworkDataHelper(clientSocket);
            bool clientLogged = false;
            while (connected)                                                                              //CLIENTE POR LOGGEAR
            {
                while (!clientLogged)
                {
                    try
                    {
                        byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);

                        byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                        string commandString = Encoding.UTF8.GetString(commandStringBytes);
                        Command command = (Command)int.Parse(commandString);

                        byte[] commandDataLenght = networkDataHelper.Receive(sizeof(int));
                        int lenght = BitConverter.ToInt32(commandDataLenght);
                        byte[] commandData = networkDataHelper.Receive(lenght);

                        switch (command)
                        {
                            case Command.Register:
                                HandleRegister(networkDataHelper, commandData);
                                break;
                            case Command.Login:
                                clientLogged = HandleLogin(networkDataHelper, commandData);
                                break;
                            default:
                                Console.WriteLine("Unknown command received from client " + clientNum);
                                break;
                        }
                    }
                    catch (SocketException se)
                    {
                        Console.WriteLine("Socket error with client " + clientNum + ": " + se.Message);
                        connected = false;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error handling client " + clientNum + ": " + e.Message);
                        connected = false;
                    }
                }

                while (clientLogged)                                                                                        //CLIENTE LOGGEADO
                {
                    try
                    {
                        byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);

                        byte[] commandStringBytes = networkDataHelper.Receive(Protocol.CommandStringLength);
                        string commandString = Encoding.UTF8.GetString(commandStringBytes);
                        CommandLogged commandLogged = (CommandLogged)int.Parse(commandString);

                        byte[] commandDataLenght = networkDataHelper.Receive(sizeof(int));
                        int lenght = BitConverter.ToInt32(commandDataLenght);
                        byte[] commandData = networkDataHelper.Receive(lenght);

                        switch (commandLogged)
                        {
                            case CommandLogged.CreateClass:
                                HandleCreateClass(networkDataHelper, commandData);
                                break;
                            case CommandLogged.JoinClass:
                                // HandleJoinClass(networkDataHelper, commandData);
                                break;
                            case CommandLogged.ListClasses:
                                HandleListClasses(networkDataHelper);
                                break;
                            case CommandLogged.LeaveClass:
                                // HandleLeaveClass(networkDataHelper, commandData);
                                break;
                            case CommandLogged.History:
                                // HandleHistory(networkDataHelper, commandData);
                                break;
                            case CommandLogged.Exit:
                                clientLogged = false;
                                break;
                            default:
                                Console.WriteLine("Unknown command received from client " + clientNum);
                                break;
                        }
                    }
                    catch (SocketException se)
                    {
                        Console.WriteLine("Socket error with client " + clientNum + ": " + se.Message);
                        connected = false;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error handling client " + clientNum + ": " + e.Message);
                        connected = false;
                    }
                }


            }
            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
            lock (clientThreadsLock)
            {
                currentClients--;
            }
            Console.WriteLine("Client " + clientNum + " disconnected. Current clients: " + currentClients);
        }
        static void HandleListClasses(NetworkDataHelper networkDataHelper)
        {
            StringBuilder sb = new StringBuilder();
            lock (usersLock)
            {
                foreach (var clase in clases)
                {
                    sb.AppendLine($"Class: {clase.Nombre}, Description: {clase.Descripcion}, Duration: {clase.Duracion}, Available Spots: {clase.CuposDisponibles}");
                }
            }
            string classList = sb.ToString();
            byte[] response = Encoding.UTF8.GetBytes(classList);
            networkDataHelper.Send(response);
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
                if (!users.Any(u => string.Equals(username, u.nombre)))
                {
                    users.Add(new Usuario(username, password));
                    added = true;
                }
            }
            byte[] response = BitConverter.GetBytes(added);
            networkDataHelper.Send(response);
        }

        static bool HandleLogin(NetworkDataHelper networkDataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] nameAndPass = dataString.Split('|');
            string username = nameAndPass[0].Trim();
            string password = nameAndPass[1];
            bool validUser = false;
            lock (usersLock)
            {
                validUser = users.Any(u => string.Equals(username, u.nombre) && u.contrasena == password);

            }
            if (validUser)
            {
                Console.WriteLine("User " + username + " logged in.");
                byte[] response = BitConverter.GetBytes(validUser);
                networkDataHelper.Send(response);
                return true;
            }
            else
            {
                byte[] response = BitConverter.GetBytes(validUser);
                networkDataHelper.Send(response);
                return false;
            }

        }

        static bool HandleCreateClass(NetworkDataHelper dataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] classData = dataString.Split('|');
            string className = classData[0];
            string description = classData[1];
            int duration = int.Parse(classData[2]);
            int quota = int.Parse(classData[3]);
            string username = classData[4];
            bool validClass = false;
            bool created = false;

            Usuario owner = users.FirstOrDefault(u => string.Equals(username, u.nombre));

            lock (usersLock)
            {
                validClass = !(clases.Any(c => string.Equals(c.Nombre, className)));

            }
            if (validClass)
            {
                Clase newClass = new Clase(className, description, duration, quota, owner);
                created = true;
                byte[] response = BitConverter.GetBytes(created);
                dataHelper.Send(response);
                return true;
            }
            else
            {
                byte[] response = BitConverter.GetBytes(created);
                dataHelper.Send(response);
                return true;
            }
        }
    }
}

