using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

class ChatClient
{
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Error); // You can set your desired log level here
    });
    private static readonly ILogger Logger = LoggerFactory.CreateLogger<ChatClient>();

    private static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Logger.LogInformation("Usage: chat wss://<WebSocket Server URL> <ConnectionName>");
            return;
        }

        string serverUri = args[0];
        string clientId = args[1];

        string connectionName = $"{serverUri}/?clientId={clientId}";

        using (var client = new ClientWebSocket())
        {
            Logger.LogInformation($"Connecting to connectionName");

            Console.WriteLine("\nUsage: \n@broker message to send that broker \n@everybody will broadcast to all connected brokers\n");

            try
            {
                await client.ConnectAsync(new Uri(connectionName), CancellationToken.None);
                Logger.LogInformation($"Connected to {serverUri} as {clientId}");

                var receiveTask = ReceiveMessages(client);
                var sendTask = SendMessages(client, connectionName);

                await Task.WhenAll(receiveTask, sendTask);
            }
            catch (Exception ex)
            {
                Logger.LogError($"WebSocket error: {ex.Message}");
            }
        }
    }

    private static async Task ReceiveMessages(ClientWebSocket client)
    {
        var buffer = new byte[1024 * 4];

        while (client.State == WebSocketState.Open)
        {
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                Console.WriteLine("Connection closed by the server");
            }
            else
            {
                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                // Remove any validation that flags "Invalid message format"
                Console.WriteLine($"\n{message}");
                Console.Write("> "); // Command prompt
            }
        }
    }



    private static async Task SendMessages(ClientWebSocket client, string connectionName)
    {
        while (client.State == WebSocketState.Open)
        {
            Console.Write("> "); // Command prompt
            string input = Console.ReadLine() ?? "";
            if (string.IsNullOrEmpty(input)) continue;

            // Parse input to extract target and message
            if (!input.StartsWith("@") && !input.StartsWith("#"))
            {
                Logger.LogWarning("Invalid format. Use '@target message' or '#command message'");
                continue;
            }

            int spaceIndex = input.IndexOf(' ');
            if (spaceIndex == -1)
            {
                Logger.LogWarning("Invalid format. Use '@target message' or '#command message'");
                continue;
            }

            string target = input.Substring(0, spaceIndex); // Extract target with the @ or # prefix
            string message = input.Substring(spaceIndex + 1); // Extract message

            var jsonMessage = new
            {
                action = "sendMessage",
                target = target, // Preserve the @ or # prefix
                message = message
            };
            string formattedMessage = System.Text.Json.JsonSerializer.Serialize(jsonMessage);

            Logger.LogInformation($"Sending {formattedMessage}");
            var bytes = Encoding.UTF8.GetBytes(formattedMessage);

            await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        }
    }
}
