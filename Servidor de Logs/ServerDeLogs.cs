using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Servidor_de_Logs_
{
    public class LogEntry
    {
        public string Usuario { get; set; } = "";
        public string Clase { get; set; } = "";
        public string Evento { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Detalles { get; set; } = "";
    }

    public class LogFilter
    {
        public string? Usuario { get; set; }
        public string? Clase { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string? Evento { get; set; }
    }

    public static class LogService
    {
        private static readonly List<LogEntry> logs = new List<LogEntry>();
        private static readonly object logsLock = new object();

        public static void AddLog(LogEntry logEntry)
        {
            lock (logsLock)
            {
                logs.Add(logEntry);
            }
        }

        public static List<LogEntry> GetAllLogs()
        {
            lock (logsLock)
            {
                return new List<LogEntry>(logs);
            }
        }

        public static List<LogEntry> GetFilteredLogs(LogFilter filter)
        {
            lock (logsLock)
            {
                IEnumerable<LogEntry> query = logs;

                if (!string.IsNullOrEmpty(filter.Usuario))
                {
                    query = query.Where(l => l.Usuario.Contains(filter.Usuario, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(filter.Clase))
                {
                    query = query.Where(l => l.Clase.Contains(filter.Clase, StringComparison.OrdinalIgnoreCase));
                }

                if (filter.FechaDesde.HasValue)
                {
                    query = query.Where(l => l.Fecha >= filter.FechaDesde.Value);
                }

                if (filter.FechaHasta.HasValue)
                {
                    query = query.Where(l => l.Fecha <= filter.FechaHasta.Value);
                }

                if (!string.IsNullOrEmpty(filter.Evento))
                {
                    query = query.Where(l => l.Evento.Contains(filter.Evento, StringComparison.OrdinalIgnoreCase));
                }

                return query.OrderByDescending(l => l.Fecha).ToList();
            }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Log Server...");
            _ = Task.Run(() => StartRabbitMQConsumer());
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();

            app.MapControllers();

            Console.WriteLine("Log Server REST API listening on http://localhost:5001");
            await app.RunAsync("http://0.0.0.0:5001");
        }

        static void StartRabbitMQConsumer()
        {
            while (true)
            {
                try
                {
                    var factory = new ConnectionFactory()
                    {
                        HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
                        Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
                        UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
                        Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest"
                    };

                    using var connection = factory.CreateConnection();
                    using var channel = connection.CreateModel();

                    channel.QueueDeclare(queue: "logs", durable: false, exclusive: false, autoDelete: false, arguments: null);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);

                        try
                        {
                            var logEntry = JsonSerializer.Deserialize<LogEntry>(message);
                            if (logEntry != null)
                            {
                                LogService.AddLog(logEntry);
                                Console.WriteLine($"Log received: {logEntry.Evento} - {logEntry.Usuario} - {logEntry.Clase}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing log message: {ex.Message}");
                        }
                    };

                    channel.BasicConsume(queue: "logs", autoAck: true, consumer: consumer);

                    Console.WriteLine("RabbitMQ consumer started. Waiting for logs...");

                    var waitHandle = new ManualResetEventSlim(false);
                    connection.ConnectionShutdown += (_, __) =>
                    {
                        Console.WriteLine("RabbitMQ connection closed. Reconnecting...");
                        waitHandle.Set();
                    };

                    waitHandle.Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error starting RabbitMQ consumer: {ex.Message}. Retrying in 5 seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }
            }
        }
    }
}
