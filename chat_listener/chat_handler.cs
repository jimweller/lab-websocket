using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Amazon.Runtime;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ChatListener;

public class ChatListener
{
    public const string ConnectionIdField = "connectionId";
    public const string ClientIdField = "clientId";
    private const string DYANMODB_TABLE_ENV = "DYANMODB_TABLE_NAME";

    /// <summary>
    /// DynamoDB table used to store the open connection ids. More advanced use cases could store logged on user map to their connection id to implement direct message chatting.
    /// </summary>
    string ConnectionMappingTable { get; }

    /// <summary>
    /// DynamoDB service client used to store and retieve connection information from the ConnectionMappingTable
    /// </summary>
    IAmazonDynamoDB DDBClient { get; }

    /// <summary>
    /// Factory func to create the AmazonApiGatewayManagementApiClient. This is needed to created per endpoint of the a connection. It is a factory to make it easy for tests
    /// to moq the creation.
    /// </summary>
    Func<string, IAmazonApiGatewayManagementApi> ApiGatewayManagementApiClientFactory { get; }


    /// <summary>
    /// Default constructor that Lambda will invoke.
    /// </summary>
    public ChatListener()
    {


        DDBClient = new AmazonDynamoDBClient();

        // Grab the name of the DynamoDB from the environment variable setup in the CloudFormation template serverless.template
        if (Environment.GetEnvironmentVariable(DYANMODB_TABLE_ENV) == null)
        {
            throw new ArgumentException($"Missing required environment variable DYANMODB_TABLE_NAME");
        }

        ConnectionMappingTable = Environment.GetEnvironmentVariable(DYANMODB_TABLE_ENV) ?? "";

        this.ApiGatewayManagementApiClientFactory = (Func<string, AmazonApiGatewayManagementApiClient>)((endpoint) =>
        {
            return new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
            {
                ServiceURL = endpoint
            });
        });
    }

    public async Task<APIGatewayProxyResponse> OnConnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {

        context.Logger.LogInformation($"OnConnectHandler()");

        try
        {
            var connectionId = request.RequestContext.ConnectionId;
            var clientId = request.QueryStringParameters["clientId"];
            context.Logger.LogInformation($"ConnectionId: {connectionId}, ClientId: {clientId}, Table {ConnectionMappingTable}");


            Dictionary<string, AttributeValue> attributes = new Dictionary<string, AttributeValue>();
            attributes[ClientIdField] = new AttributeValue { S = clientId };
            attributes[ConnectionIdField] = new AttributeValue { S = connectionId };

            PutItemRequest ddbRequest = new PutItemRequest
            {
                TableName = ConnectionMappingTable,
                Item = attributes
            };


            await DDBClient.PutItemAsync(ddbRequest);

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = "Connected."
            };
        }
        catch (Exception e)
        {
            context.Logger.LogInformation("Error connecting: " + e.Message);
            context.Logger.LogInformation(e.StackTrace);
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = $"Failed to connect: {e.Message}"
            };
        }
    }



    public async Task<APIGatewayProxyResponse> SendMessageHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            var connectionId = request.RequestContext.ConnectionId;
            var domainName = request.RequestContext.DomainName;
            var stage = request.RequestContext.Stage;
            var endpoint = $"https://{domainName}/{stage}";

            var apiClient = ApiGatewayManagementApiClientFactory(endpoint);

            // Parse the request body
            string message = string.Empty;
            string target = string.Empty;

            using (var jsonDoc = JsonDocument.Parse(request.Body))
            {
                if (jsonDoc.RootElement.TryGetProperty("message", out JsonElement messageElement))
                {
                    message = messageElement.GetString() ?? string.Empty;
                }

                if (jsonDoc.RootElement.TryGetProperty("target", out JsonElement targetElement))
                {
                    target = targetElement.GetString() ?? "message was missing in request body JSON";
                }
            }

            context.Logger.LogInformation($"Handling message for target (clientId): {target}, message: {message}");

            if (string.IsNullOrEmpty(target))
            {
                context.Logger.LogWarning("Target field is missing or empty.");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 404,
                    Body = "Target field is missing or empty."
                };
            }

            if (target.StartsWith("#"))
            {
                context.Logger.LogWarning("Calling HandleCommand");
                string recipient = target.Substring(1); // Remove the '#' symbol
                return await HandleCommand(apiClient, recipient, message, context);
            }
            else if (target.StartsWith("@"))
            {
                context.Logger.LogWarning("Calling HandleMessage");
                string recipient = target.Substring(1); // Remove the '@' symbol
                return await HandleMessage(apiClient, recipient, message, context);
            }
            else
            {
                context.Logger.LogInformation($"Received target: {target}");
                context.Logger.LogWarning("Invalid target prefix.");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Invalid target prefix. Use '@' for messages or '#' for commands."
                };
            }
        }
        catch (Exception e)
        {
            context.Logger.LogError($"Error sending message: {e.Message}");
            context.Logger.LogError(e.StackTrace);
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = "An unexpected error occurred."
            };
        }
    }




    private async Task<APIGatewayProxyResponse> HandleMessage(IAmazonApiGatewayManagementApi apiClient, string target, string message, ILambdaContext context)
    {
        try
        {
            if (target.Equals("everybody", StringComparison.OrdinalIgnoreCase))
            {
                // Send message to all connections
                var scanRequest = new ScanRequest
                {
                    TableName = ConnectionMappingTable,
                    ProjectionExpression = ConnectionIdField,
                    IndexName = "ConnectionIdIndex"
                };

                var scanResponse = await DDBClient.ScanAsync(scanRequest);

                foreach (var item in scanResponse.Items)
                {
                    var postConnectionRequest = new PostToConnectionRequest
                    {
                        ConnectionId = item[ConnectionIdField].S,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(message))
                    };

                    try
                    {
                        await apiClient.PostToConnectionAsync(postConnectionRequest);
                        context.Logger.LogInformation($"Message sent to connection: {item[ConnectionIdField].S}");
                    }
                    catch (AmazonServiceException e)
                    {
                        if (e.StatusCode == HttpStatusCode.Gone)
                        {
                            context.Logger.LogInformation($"Connection {item[ConnectionIdField].S} is gone. Cleaning up.");
                            var ddbDeleteRequest = new DeleteItemRequest
                            {
                                TableName = ConnectionMappingTable,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    { ClientIdField, new AttributeValue { S = item[ClientIdField].S } },
                                    { ConnectionIdField, new AttributeValue { S = item[ConnectionIdField].S } }
                                }
                            };
                            await DDBClient.DeleteItemAsync(ddbDeleteRequest);
                        }
                        else
                        {
                            context.Logger.LogError($"Error sending message to {item[ConnectionIdField].S}: {e.Message}");
                        }
                    }
                }

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Message sent to all connections."
                };
            }
            else
            {
                // Target is a specific clientId - lookup connectionId
                var queryRequest = new QueryRequest
                {
                    TableName = ConnectionMappingTable,
                    KeyConditionExpression = "clientId = :v_clientId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":v_clientId", new AttributeValue { S = target } }
                    },
                    ProjectionExpression = ConnectionIdField
                };

                var queryResponse = await DDBClient.QueryAsync(queryRequest);

                if (queryResponse.Items == null || queryResponse.Items.Count == 0)
                {
                    context.Logger.LogWarning($"No connection found for clientId: {target}");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 404,
                        Body = $"No connection found for clientId: {target}"
                    };
                }

                var targetConnectionId = queryResponse.Items[0][ConnectionIdField].S;

                // Send message to the specific connection
                var postConnectionRequest = new PostToConnectionRequest
                {
                    ConnectionId = targetConnectionId,
                    Data = new MemoryStream(Encoding.UTF8.GetBytes(message))
                };

                try
                {
                    await apiClient.PostToConnectionAsync(postConnectionRequest);
                    context.Logger.LogInformation($"Message sent to clientId: {target}");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 200,
                        Body = "Message sent."
                    };
                }
                catch (AmazonServiceException e)
                {
                    if (e.StatusCode == HttpStatusCode.Gone)
                    {
                        context.Logger.LogInformation($"Connection {targetConnectionId} is gone. Cleaning up.");
                        var ddbDeleteRequest = new DeleteItemRequest
                        {
                            TableName = ConnectionMappingTable,
                            Key = new Dictionary<string, AttributeValue>
                    {
                        { ClientIdField, new AttributeValue { S = target } },
                        { ConnectionIdField, new AttributeValue { S = targetConnectionId } }
                    }
                        };
                        await DDBClient.DeleteItemAsync(ddbDeleteRequest);
                    }
                    else
                    {
                        context.Logger.LogError($"Error sending message to clientId: {target}: {e.Message}");
                        return new APIGatewayProxyResponse
                        {
                            StatusCode = 500,
                            Body = $"Error sending message to clientId: {target}"
                        };
                    }
                }
            }
        }
        catch (Exception e)
        {
            context.Logger.LogError($"Error in HandleMessage: {e.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = "An error occurred."
            };
        }

        // Default return when no other return is hit
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)HttpStatusCode.NotFound,
            Body = "No matching action found."
        };
    }

    private async Task<APIGatewayProxyResponse> HandleCommand(IAmazonApiGatewayManagementApi apiClient, string clientId, string command, ILambdaContext context)
    {
        try
        {
            context.Logger.LogInformation($"Received command \"{command}\" for clientId \"{clientId}\"");

            // Query the DynamoDB table to get the connectionId for the given clientId
            var queryRequest = new QueryRequest
            {
                TableName = ConnectionMappingTable,
                KeyConditionExpression = "clientId = :v_clientId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":v_clientId", new AttributeValue { S = clientId } }
            },
                ProjectionExpression = ConnectionIdField
            };

            var queryResponse = await DDBClient.QueryAsync(queryRequest);

            if (queryResponse.Items == null || queryResponse.Items.Count == 0)
            {
                context.Logger.LogWarning($"No connection found for clientId: {clientId}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 404,
                    Body = $"No connection found for clientId: {clientId}"
                };
            }

            var targetConnectionId = queryResponse.Items[0][ConnectionIdField].S;

            // Create a response message for the client
            string responseMessage = $"Received command \"{command}\"";

            // Send the response to the client
            var postConnectionRequest = new PostToConnectionRequest
            {
                ConnectionId = targetConnectionId,
                Data = new MemoryStream(Encoding.UTF8.GetBytes(responseMessage))
            };

            try
            {
                await apiClient.PostToConnectionAsync(postConnectionRequest);
                context.Logger.LogInformation($"Command response sent to clientId: {clientId}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Command executed."
                };
            }
            catch (AmazonServiceException e)
            {
                if (e.StatusCode == HttpStatusCode.Gone)
                {
                    context.Logger.LogInformation($"Connection {targetConnectionId} is gone. Cleaning up.");
                    var ddbDeleteRequest = new DeleteItemRequest
                    {
                        TableName = ConnectionMappingTable,
                        Key = new Dictionary<string, AttributeValue>
                    {
                        { ClientIdField, new AttributeValue { S = clientId } },
                        { ConnectionIdField, new AttributeValue { S = targetConnectionId } }
                    }
                    };
                    await DDBClient.DeleteItemAsync(ddbDeleteRequest);
                }
                else
                {
                    context.Logger.LogError($"Error sending command response to clientId: {clientId}: {e.Message}");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 500,
                        Body = $"Error sending command response to clientId: {clientId}"
                    };
                }
            }
        }
        catch (Exception e)
        {
            context.Logger.LogError($"Error in HandleCommand: {e.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = "An error occurred while processing the command."
            };
        }

        // Default return when no other return is hit
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)HttpStatusCode.NotFound,
            Body = "No matching action found."
        };
    }




    public async Task<APIGatewayProxyResponse> OnDisconnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {

        context.Logger.LogInformation($"OnDisconnectHandler()");

        try
        {

            var connectionId = request.RequestContext.ConnectionId;

            var queryRequest = new QueryRequest
            {
                TableName = ConnectionMappingTable,
                KeyConditionExpression = "connectionId = :v_connectionId",
                IndexName = "ConnectionIdIndex",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":v_connectionId", new AttributeValue { S = connectionId } }
                },
                ProjectionExpression = "clientId" // Only retrieve the clientId
            };


            var queryResponse = await DDBClient.QueryAsync(queryRequest);


            if (queryResponse.Items == null || queryResponse.Items.Count == 0)
            {
                context.Logger.LogError($"ClientId not found or ClientId missing in table: {connectionId}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 404,
                    Body = "ClientId not found or ClientId missing in table"
                };
            }


            if (queryResponse.Items.Count > 0)
            {
                var deleteRequests = queryResponse.Items.Select(item => new WriteRequest
                {
                    DeleteRequest = new DeleteRequest
                    {
                        Key = new Dictionary<string, AttributeValue>
                        {
                            { "clientId", new AttributeValue { S = item["clientId"].S } },
                            { "connectionId", new AttributeValue { S = connectionId } }
                        }
                    }
                }).ToList();

                for (int i = 0; i < deleteRequests.Count; i += 25)
                {
                    var batch = deleteRequests.Skip(i).Take(25).ToList();
                    var batchRequest = new BatchWriteItemRequest
                    {
                        RequestItems = new Dictionary<string, List<WriteRequest>>
                        {
                            { ConnectionMappingTable, batch }
                        }
                    };
                    await DDBClient.BatchWriteItemAsync(batchRequest);
                }
            }


            // await DDBClient.DeleteItemAsync(ddbRequest);

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = "Disconnected."
            };
        }
        catch (AmazonDynamoDBException dbEx)
        {
            context.Logger.LogError($"DynamoDB Error: {dbEx.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = $"DynamoDB error: {dbEx.Message}"
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error disconnecting: {ex.Message}");
            context.Logger.LogError(ex.StackTrace);
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = $"Failed to disconnect: {ex.Message}"
            };
        }
    }
}