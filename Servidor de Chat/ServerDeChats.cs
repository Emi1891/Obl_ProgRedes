using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Threading;

namespace Servidor_de_Chat
{
    public class ChatRoom
    {
        public string ClassName { get; set; } = "";
        public string Link { get; set; } = "";
        public List<WebSocket> Clients { get; set; } = new List<WebSocket>();
        public readonly object Lock = new object();
    }

    class Program
    {
        private static readonly Dictionary<string, ChatRoom> chatRooms = new Dictionary<string, ChatRoom>();
        private static readonly object roomsLock = new object();
        private static readonly string MainServerGrpcUrl = Environment.GetEnvironmentVariable("MAIN_SERVER_GRPC_URL") ?? "http://localhost:5002";

        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Chat Server...");

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.UseWebSockets();

            app.Map("/chat", async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                string? className = null;
                string? username = null;

                try
                {
                    var buffer = new byte[1024 * 4];
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var parts = message.Split('|');
                        if (parts.Length >= 3)
                        {
                            string link = parts[0];
                            username = parts[1];
                            string password = parts[2];

                            bool isValid = await ValidateUserAndLink(link, username, password);
                            if (!isValid)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid credentials or link", CancellationToken.None);
                                return;
                            }

                            className = ExtractClassNameFromLink(link);
                            if (string.IsNullOrEmpty(className))
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Invalid link", CancellationToken.None);
                                return;
                            }

                            lock (roomsLock)
                            {
                                if (!chatRooms.ContainsKey(className))
                                {
                                    chatRooms[className] = new ChatRoom { ClassName = className, Link = link };
                                }
                                lock (chatRooms[className].Lock)
                                {
                                    chatRooms[className].Clients.Add(webSocket);
                                }
                            }

                            var welcomeMsg = $"[System] {username} joined the chat";
                            await BroadcastMessage(className, welcomeMsg, webSocket);

                            while (webSocket.State == WebSocketState.Open)
                            {
                                try
                                {
                                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                                    if (result.MessageType == WebSocketMessageType.Text)
                                    {
                                        var chatMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                        var fullMessage = $"[{username}] {chatMessage}";
                                        await BroadcastMessage(className, fullMessage, webSocket);
                                    }
                                    else if (result.MessageType == WebSocketMessageType.Close)
                                    {
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing message from {username}: {ex.Message}");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in WebSocket: {ex.Message}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(className))
                    {
                        lock (roomsLock)
                        {
                            if (chatRooms.ContainsKey(className))
                            {
                                lock (chatRooms[className].Lock)
                                {
                                    chatRooms[className].Clients.Remove(webSocket);
                                }
                                if (chatRooms[className].Clients.Count == 0)
                                {
                                    chatRooms.Remove(className);
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(username))
                        {
                            var leaveMsg = $"[System] {username} left the chat";
                            await BroadcastMessage(className, leaveMsg, null);
                        }
                    }

                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                    }
                    catch { }
                }
            });

            app.MapGet("/health", async (HttpContext context) =>
            {
                await context.Response.WriteAsync("OK");
            });

            string chatPort = Environment.GetEnvironmentVariable("CHAT_PORT") ?? "5003";
            Console.WriteLine($"Chat Server listening on http://localhost:{chatPort}");
            await app.RunAsync($"http://0.0.0.0:{chatPort}");
        }

        static async Task<bool> ValidateUserAndLink(string link, string username, string password)
        {
            try
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                using var channel = Grpc.Net.Client.GrpcChannel.ForAddress(MainServerGrpcUrl);
                var client = new Servidor_de_Chat.AuthService.AuthServiceClient(channel);

                var authRequest = new Servidor_de_Chat.ValidateRequest { Username = username, Password = password };
                var authResponse = await client.ValidateCredentialsAsync(authRequest);
                if (!authResponse.Valid)
                {
                    Console.WriteLine($"Invalid credentials for user: {username}");
                    return false;
                }

                var linkRequest = new Servidor_de_Chat.ClassLinkRequest { Link = link };
                var linkResponse = await client.ValidateClassLinkAsync(linkRequest);
                if (!linkResponse.Valid)
                {
                    Console.WriteLine($"Invalid link: {link}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error validating user/link: {ex.Message}");
                return false;
            }
        }

        static string ExtractClassNameFromLink(string link)
        {
            var parts = link.Split('/');
            if (parts.Length > 0)
            {
                return parts[parts.Length - 1].Replace("_", " ");
            }
            return "";
        }

        static async Task BroadcastMessage(string className, string message, WebSocket? sender)
        {
            ChatRoom? room = null;
            lock (roomsLock)
            {
                if (!chatRooms.ContainsKey(className)) return;
                room = chatRooms[className];
            }

            List<WebSocket> clientsToNotify;
            lock (room.Lock)
            {
                clientsToNotify = new List<WebSocket>(room.Clients);
            }

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var tasks = new List<Task>();

            foreach (var client in clientsToNotify)
            {
                if (client != sender && client.State == WebSocketState.Open)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending message to client: {ex.Message}");
                        }
                    }));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
    }

}
