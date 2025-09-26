
using System.ComponentModel;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Common;
using Server.Dominio;

namespace Server
{

    internal class Server
    {
        static readonly int maxClientsAllowed = 3;
        static readonly object clientThreadsLock = new object();
        static int currentClientCount = 0;
        static readonly SettingsManager settingsMgr = new SettingsManager();
        static List<Usuario> users = new List<Usuario>;
        static readonly object usersLock = new object();

        static void main(String[] args)
        {
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
                    currentClientCount++;
                }
                int clientNum = currentClientCount;
                Console.WriteLine("Client connected. Current clients: " + currentClientCount);
                Thread clientThread = new Thread(() => HandleClient(clientSocket, clientNum));
                clientThread.Start();
            }

            serverSocket.Close();

        }

    }
    static void HandleClient(Socket clientSocket, int clientNum)
        {
            bool connected = true;
            NetworkDataHelper dataHelper = new NetworkDataHelper(clientSocket);

            while (connected)
            {
                try
                {
                    byte[] messageType = networkDataHelper.Receive(Protocol.MessageTypeLength);

                    byte[] commandStringBytes = NetworkDataHelper.Receive(Protocol.CommandStringLength);
                    string commandString = Encoding.UTF8.GetString(commandStringBytes);
                    commandString command = (Command)int.Parse(commandString);

                    byte[] commandDataLenght = NetworkDataHelper.Receive(sizeof(int));
                    int lenght = BitConverter.ToInt32(commandDataLenght);
                    byte[] commandData = NetworkDataHelper.Receive(lenght);

                    switch (command)
                    {
                        case 1:
                            HandleRegister(dataHelper, commandData);
                            break;
                        case 2:
                            HandleLogin(dataHelper, commandData);
                            break;
                        case 3:
                            HandleCreateClass(dataHelper, commandData);
                            break;
                        case 4:
                            connected = false;
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
            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
            lock (clientThreadsLock)
            {
                currentClientCount--;
            }
            Console.WriteLine("Client " + clientNum + " disconnected. Current clients: " + currentClientCount);
        }

        static void HandleRegister(NetworkDataHelper dataHelper, byte[] commandData)
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

        static void HandleLogin(NetworkDataHelper dataHelper, byte[] commandData)
        {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] nameAndPass = dataString.Split('|');
            string username = nameAndPass[0].Trim();
            string password = nameAndPass[1];
            bool validUser = false;
            lock (usersLock)
            {
                validUser = users.Any(u => string.Equals(username, u.nombre) && u.Password == password);
            }
            byte[] response = BitConverter.GetBytes(validUser);
            networkDataHelper.Send(response);
        }

        static void HandleCreateClass(NetworkDataHelper dataHelper, byte[] commandData) {
            string dataString = Encoding.UTF8.GetString(commandData);
            string[] classData = dataString.Split('|');
            string className = classData[0];
            string description = classData[1];
            int duration = int.Parse(classData[2]);
            int quota = int.Parse(classData[3]);
            string username = classData[4];
            Usuario owner = usuarios.FirstOrDefault(u => string.Equals(username, u.nombre));
            Clase newClass = new Clase(className, description, duration, quota, owner);

        }

