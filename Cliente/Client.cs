using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Cliente
{
    public class Client
    {
        // Managers/Configs que ya usás en tu solución
        static readonly SettingsManager settingsMgr = new SettingsManager();

        static void PrintMenu()
        {
            Console.WriteLine();
            Console.WriteLine("Menu:");
            Console.WriteLine("1. Register");
            Console.WriteLine("2. Login");
            Console.WriteLine("3. Create Class");
            Console.WriteLine("4. Exit");
            Console.Write("Choose an option: ");
        }

        public static void Main()
        {
            Console.WriteLine("Starting Client Application...");

            // Socket de cliente
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // (Opcional) bind local si tu profe lo pide; si no, lo podés omitir:
            // OJO: ClientPort es TU puerto local, no el del server
            IPAddress clientIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ClientIpConfigKey));
            int clientPort = int.Parse(settingsMgr.ReadSetting(ClientConfig.ClientPortConfigKey));
            IPEndPoint clientEndPoint = new IPEndPoint(clientIp, clientPort);
            try { clientSocket.Bind(clientEndPoint); } catch { /* si ya está ocupado, podés ignorar o reintentar */ }

            // Endpoint del servidor
            IPAddress serverIp = IPAddress.Parse(settingsMgr.ReadSetting(ClientConfig.ServerIpConfigKey));
            int serverPort = int.Parse(settingsMgr.ReadSetting(ClientConfig.ServerPortConfigKey)); // corregido: ClientConfig
            IPEndPoint serverEndPoint = new IPEndPoint(serverIp, serverPort);

            try
            {
                clientSocket.Connect(serverEndPoint);
            }
            catch (Exception)
            {
                Console.WriteLine("Could not connect to server: shutting down client...");
                Thread.Sleep(1500);
                return;
            }

            Console.WriteLine("Connected to server");
            bool running = true;
            NetworkDataHelper dataHelper = new NetworkDataHelper(clientSocket);

            while (running)
            {
                PrintMenu();
                string? option = Console.ReadLine();

                switch (option)
                {
                    case "1": // Register
                        {
                            string username = PromptUsername();
                            string password = PromptPassword();

                            // Si quisieras, podés repetir confirmación de password aquí
                            string regMessage = $"REGISTER|{username}|{password}";
                            dataHelper.SendData(regMessage);
                            string regResponse = dataHelper.ReceiveData();
                            Console.WriteLine($"Server response: {regResponse}");
                            break;
                        }
                    case "2": // Login
                        {
                            string username = PromptUsername();
                            string password = PromptPassword();

                            string loginMessage = $"LOGIN|{username}|{password}";
                            dataHelper.SendData(loginMessage);
                            string loginResponse = dataHelper.ReceiveData();
                            Console.WriteLine($"Server response: {loginResponse}");
                            break;
                        }
                    case "3": // Create class
                        {
                            Console.Write("Enter class name: ");
                            string? className = Console.ReadLine();
                            className = (className ?? "").Trim();

                            if (string.IsNullOrWhiteSpace(className))
                            {
                                Console.WriteLine("Class name cannot be empty.");
                                break;
                            }

                            string createClassMessage = $"CREATE_CLASS|{className}";
                            dataHelper.SendData(createClassMessage);
                            string createClassResponse = dataHelper.ReceiveData();
                            Console.WriteLine($"Server response: {createClassResponse}");
                            break;
                        }
                    case "4":
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }
            }

            try
            {
                clientSocket.Shutdown(SocketShutdown.Both);
            }
            catch { /* ignore */ }
            finally
            {
                clientSocket.Close();
            }
        }

        // ------------------ Validaciones de credenciales ------------------

        // Reglas sugeridas (podés ajustarlas a lo que pida tu cátedra):
        // - Username: 3..20 chars, alfanumérico + . _ - (sin espacios)
        // - Password: 6..64 chars, sin espacios. (Podés agregar reglas de complejidad si querés)
        private static string PromptUsername()
        {
            while (true)
            {
                Console.Write("Enter username: ");
                string? input = Console.ReadLine();
                input = (input ?? "").Trim();

                if (!ValidateUsername(input, out string? error))
                { Console.WriteLine($"Invalid username: {error}"); continue; }

                return input;
            }
        }

        private static string PromptPassword()
        {
            while (true)
            {
                Console.Write("Enter password: ");
                string input = ReadPassword(); // oculta la contraseña al tipear
                if (!ValidatePassword(input, out string? error))
                { Console.WriteLine($"Invalid password: {error}"); continue; }
                return input;
            }
        }

        private static bool ValidateUsername(string username, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(username)) { error = "cannot be empty."; return false; }
            if (username.Length < 3 || username.Length > 20) { error = "length must be 3–20 characters."; return false; }
            if (username.Contains(' ')) { error = "spaces are not allowed."; return false; }
            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9._-]+$"))
            {
                error = "only letters, numbers, dot, underscore and hyphen are allowed.";
                return false;
            }
            return true;
        }

        private static bool ValidatePassword(string password, out string? error)
        {
            error = null;
            if (password is null) { error = "cannot be null."; return false; }
            if (password.Length < 6 || password.Length > 64) { error = "length must be 6–64 characters."; return false; }
            if (password.Any(char.IsWhiteSpace)) { error = "spaces are not allowed."; return false; }
            // Opcional: complejidad mínima (al menos una letra y un número, por ejemplo)
            // if (!Regex.IsMatch(password, @"^(?=.*[A-Za-z])(?=.*\d).+$"))
            // { error = "must include at least one letter and one digit."; return false; }
            return true;
        }

        // Lee password sin mostrarla (estilo terminal)
        private static string ReadPassword()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Remove(sb.Length - 1, 1);
                        // borrar el último asterisco en pantalla si querés mostrar *
                        // Console.Write("\b \b");
                    }
                    continue;
                }
                // Mostrar * si querés feedback visual:
                // Console.Write("*");
                if (!char.IsControl(key.KeyChar))
                    sb.Append(key.KeyChar);
            }
            return sb.ToString();
        }
    }
}
