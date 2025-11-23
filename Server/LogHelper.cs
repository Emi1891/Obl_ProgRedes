using System;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Server
{
    public class LogHelper
    {
        private static IConnection? connection;
        private static IModel? channel;
        private static readonly object lockObject = new object();

        public static void Initialize()
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

                connection = factory.CreateConnection();
                channel = connection.CreateModel();
                channel.QueueDeclare(queue: "logs", durable: false, exclusive: false, autoDelete: false, arguments: null);
                Console.WriteLine("RabbitMQ connection established for logging.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not connect to RabbitMQ: {ex.Message}");
            }
        }

        public static void SendLog(string usuario, string clase, string evento, string detalles = "")
        {
            try
            {
                if (channel == null || !channel.IsOpen)
                {
                    Initialize();
                }

                if (channel != null && channel.IsOpen)
                {
                    var logEntry = new
                    {
                        Usuario = usuario,
                        Clase = clase,
                        Evento = evento,
                        Fecha = DateTime.Now,
                        Detalles = detalles
                    };

                    var message = JsonSerializer.Serialize(logEntry);
                    var body = Encoding.UTF8.GetBytes(message);

                    channel.BasicPublish(exchange: "", routingKey: "logs", basicProperties: null, body: body);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending log to RabbitMQ: {ex.Message}");
            }
        }

        public static void Close()
        {
            try
            {
                channel?.Close();
                connection?.Close();
            }
            catch { }
        }
    }
}

