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
            Console.WriteLine("=== Chat Client ===");
            Console.WriteLine();

            Console.Write("Enter class link: ");
            string? link = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(link))
            {
                Console.WriteLine("Invalid link.");
                return;
            }

            Console.Write("Enter your username: ");
            string? username = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Invalid username.");
                return;
            }

            Console.Write("Enter your password: ");
            string? password = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("Invalid password.");
                return;
            }

            string chatServerUrl = Environment.GetEnvironmentVariable("CHAT_SERVER_URL") ?? "ws://localhost:5003/chat";

            try
            {
                using var client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(chatServerUrl), CancellationToken.None);

                var initialMessage = $"{link}|{username}|{password}";
                var initialMessageBytes = Encoding.UTF8.GetBytes(initialMessage);
                await client.SendAsync(new ArraySegment<byte>(initialMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None);

                Console.WriteLine("\nConectado al chat. Escriba mensajes (o 'exit' para salir):\n");

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
                            Console.WriteLine($"Error receiving message: {ex.Message}");
                            break;
                        }
                    }
                });

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
                        Console.WriteLine($"Error sending message: {ex.Message}");
                        break;
                    }
                }

                await receiveTask;
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nDisconnected from chat.");
        }
    }
}
