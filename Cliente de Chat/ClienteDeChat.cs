using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cliente_de_Chat_
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Cliente de Chat ===");
            Console.WriteLine();

            Console.Write("Ingrese el link o código de la clase: ");
            string? link = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(link))
            {
                Console.WriteLine("Link inválido.");
                return;
            }

            Console.Write("Ingrese su nombre de usuario: ");
            string? username = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Nombre de usuario inválido.");
                return;
            }

            Console.Write("Ingrese su contraseña: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("Contraseña inválida.");
                return;
            }

            string chatServerUrl = Environment.GetEnvironmentVariable("CHAT_SERVER_URL") ?? "ws://localhost:5003/chat";

            try
            {
                using var client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(chatServerUrl), CancellationToken.None);

                // Enviar credenciales y link
                var initialMessage = $"{link}|{username}|{password}";
                var initialMessageBytes = Encoding.UTF8.GetBytes(initialMessage);
                await client.SendAsync(new ArraySegment<byte>(initialMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None);

                Console.WriteLine("\nConectado al chat. Escriba mensajes (o 'exit' para salir):\n");

                // Tarea para recibir mensajes
                var receiveTask = Task.Run(async () =>
                {
                    var buffer = new byte[1024 * 4];
                    while (client.State == WebSocketState.Open)
                    {
                        try
                        {
                            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                Console.WriteLine(message);
                            }
                            else if (result.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error recibiendo mensaje: {ex.Message}");
                            break;
                        }
                    }
                });

                // Leer mensajes del usuario y enviarlos
                while (client.State == WebSocketState.Open)
                {
                    var input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    if (input.ToLower() == "exit")
                    {
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    try
                    {
                        var messageBytes = Encoding.UTF8.GetBytes(input);
                        await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error enviando mensaje: {ex.Message}");
                        break;
                    }
                }

                await receiveTask;
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"Error de conexión: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Detalles: {ex.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nDesconectado del chat.");
        }
    }
}
