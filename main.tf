############################################
# Websocket API Gateway
############################################


resource "aws_apigatewayv2_api" "websocket_apigw" {
  name                       = "websocket_apigw"
  protocol_type              = "WEBSOCKET"
  route_selection_expression = "$request.body.action"
}

resource "aws_apigatewayv2_deployment" "websocket_deployment" {
  api_id = aws_apigatewayv2_api.websocket_apigw.id

  depends_on = [
    aws_apigatewayv2_route.websocket_connect_route,
    aws_apigatewayv2_route.websocket_disconnect_route,
    aws_apigatewayv2_route.websocket_sendmessage_route
  ]
}

resource "aws_apigatewayv2_stage" "websocket_stage" {
  api_id        = aws_apigatewayv2_api.websocket_apigw.id
  deployment_id = aws_apigatewayv2_deployment.websocket_deployment.id
  name          = "dev"

  default_route_settings {
    data_trace_enabled       = true
    detailed_metrics_enabled = true
    logging_level            = "INFO"
    throttling_burst_limit   = 400
    throttling_rate_limit    = 300
  }

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.websocket_api_logs.arn
    format = jsonencode({
      requestId  = "$context.requestId",
      sourceIp   = "$context.identity.sourceIp",
      httpMethod = "$context.httpMethod",
      path       = "$context.path",
      status     = "$context.status"
    })
  }
}


# connect
resource "aws_apigatewayv2_route" "websocket_connect_route" {
  api_id    = aws_apigatewayv2_api.websocket_apigw.id
  route_key = "$connect"
  #  authorization_type = "NONE"
  operation_name = "OnConnect"
  target         = "integrations/${aws_apigatewayv2_integration.websocket_connect_integration.id}"
}

resource "aws_apigatewayv2_integration" "websocket_connect_integration" {
  api_id             = aws_apigatewayv2_api.websocket_apigw.id
  integration_type   = "AWS_PROXY"
  integration_uri    = aws_lambda_function.chat_listener_connect_lambda.invoke_arn
  integration_method = "POST"
}


# disconnect
resource "aws_apigatewayv2_route" "websocket_disconnect_route" {
  api_id    = aws_apigatewayv2_api.websocket_apigw.id
  route_key = "$disconnect"
  #  authorization_type = "NONE"
  operation_name = "OnDisconnect"
  target         = "integrations/${aws_apigatewayv2_integration.websocket_disconnect_integration.id}"
}

resource "aws_apigatewayv2_integration" "websocket_disconnect_integration" {
  api_id             = aws_apigatewayv2_api.websocket_apigw.id
  integration_type   = "AWS_PROXY"
  integration_uri    = aws_lambda_function.chat_listener_disconnect_lambda.invoke_arn
  integration_method = "POST"
}

# send message
resource "aws_apigatewayv2_route" "websocket_sendmessage_route" {
  api_id    = aws_apigatewayv2_api.websocket_apigw.id
  route_key = "sendMessage"
  #  authorization_type = "NONE"
  operation_name = "OnSendMessage"
  target         = "integrations/${aws_apigatewayv2_integration.websocket_sendmessage_integration.id}"
}

resource "aws_apigatewayv2_integration" "websocket_sendmessage_integration" {
  api_id             = aws_apigatewayv2_api.websocket_apigw.id
  integration_type   = "AWS_PROXY"
  integration_uri    = aws_lambda_function.chat_listener_sendmessage_lambda.invoke_arn
  integration_method = "POST"
}








resource "aws_lambda_permission" "allow_connect_websocket" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.chat_listener_connect_lambda.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.websocket_apigw.execution_arn}/*"
}

resource "aws_lambda_permission" "allow_disconnect_websocket" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.chat_listener_disconnect_lambda.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.websocket_apigw.execution_arn}/*"
}

resource "aws_lambda_permission" "allow_sendmessage_websocket" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.chat_listener_sendmessage_lambda.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.websocket_apigw.execution_arn}/*"
}








############################################
# Backend
############################################

resource "aws_dynamodb_table" "websocket_connections_table" {
  name      = "WebSocketConnections"
  hash_key  = "clientId"
  range_key = "connectionId"

  server_side_encryption {
    enabled = true
  }

  attribute {
    name = "clientId"
    type = "S"
  }
  attribute {
    name = "connectionId"
    type = "S"
  }
  billing_mode = "PAY_PER_REQUEST"

  global_secondary_index {
    name            = "ConnectionIdIndex" # Example name, adjust as needed
    hash_key        = "connectionId"
    range_key       = "clientId" # Optional, if you need a composite index
    projection_type = "ALL"      # Adjust projection type as needed
  }
}


resource "aws_lambda_function" "chat_listener_connect_lambda" {
  function_name    = "chat_listener_connect"
  handler          = "chat_listener::ChatListener.ChatListener::OnConnectHandler"
  runtime          = "dotnet8"
  role             = aws_iam_role.lambda_execution_role.arn
  filename         = "chat_listener.zip"
  source_code_hash = data.archive_file.chat_listener.output_base64sha256
  depends_on       = [data.archive_file.chat_listener]
  architectures    = ["arm64"]
  timeout          = 15
  environment {
    variables = {
      DYANMODB_TABLE_NAME = aws_dynamodb_table.websocket_connections_table.name
    }
  }
}

resource "aws_lambda_function" "chat_listener_disconnect_lambda" {
  function_name    = "chat_listener_disconnect"
  handler          = "chat_listener::ChatListener.ChatListener::OnDisconnectHandler"
  runtime          = "dotnet8"
  role             = aws_iam_role.lambda_execution_role.arn
  filename         = "chat_listener.zip"
  source_code_hash = data.archive_file.chat_listener.output_base64sha256
  depends_on       = [data.archive_file.chat_listener]
  architectures    = ["arm64"]
  timeout          = 15
  environment {
    variables = {
      DYANMODB_TABLE_NAME = aws_dynamodb_table.websocket_connections_table.name
    }
  }
}


resource "aws_lambda_function" "chat_listener_sendmessage_lambda" {
  function_name    = "chat_listener_sendmessage"
  handler          = "chat_listener::ChatListener.ChatListener::SendMessageHandler"
  runtime          = "dotnet8"
  role             = aws_iam_role.lambda_execution_role.arn
  filename         = "chat_listener.zip"
  source_code_hash = data.archive_file.chat_listener.output_base64sha256
  depends_on       = [data.archive_file.chat_listener]
  architectures    = ["arm64"]
  memory_size      = 512
  timeout          = 15
  environment {
    variables = {
      DYANMODB_TABLE_NAME = aws_dynamodb_table.websocket_connections_table.name
    }
  }
}






resource "aws_iam_role" "lambda_execution_role" {
  name = "lambda_execution_role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "lambda.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_policy" "lambda_dynamo_policy" {
  name = "lambda_execution_policy"
  policy = jsonencode({
    "Version" : "2012-10-17",
    "Statement" : [{
      "Effect" : "Allow",
      "Action" : [
        "dynamodb:GetItem",
        "dynamodb:DeleteItem",
        "dynamodb:PutItem",
        "dynamodb:Scan",
        "dynamodb:Query",
        "dynamodb:UpdateItem",
        "dynamodb:BatchWriteItem",
        "dynamodb:BatchGetItem",
        "dynamodb:DescribeTable"
      ],
      "Resource" : [
        "${aws_dynamodb_table.websocket_connections_table.arn}",
        "${aws_dynamodb_table.websocket_connections_table.arn}/index/*"
      ]
      },
      {
        Action = [
          "execute-api:ManageConnections"
        ],
        Effect = "Allow",
        Resource = [
          "arn:aws:execute-api:us-east-1:12345678901:*"
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_execution_policy_attachment" {
  role       = aws_iam_role.lambda_execution_role.name
  policy_arn = aws_iam_policy.lambda_dynamo_policy.arn
}

resource "aws_iam_role_policy_attachment" "lambda_logs" {
  role       = aws_iam_role.lambda_execution_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

















resource "aws_iam_role" "websocket_api_role" {
  name = "websocket_cloudwatch_role"
  assume_role_policy = jsonencode({
    "Version" : "2012-10-17",
    "Statement" : [
      {
        "Effect" : "Allow",
        "Principal" : {
          "Service" : "apigateway.amazonaws.com"
        },
        "Action" : "sts:AssumeRole"
      },
    ]
  })
}

resource "aws_iam_role_policy_attachment" "websocket_cloudwatch_attachment" {
  role       = aws_iam_role.websocket_api_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonAPIGatewayPushToCloudWatchLogs"
}

resource "aws_cloudwatch_log_group" "websocket_api_logs" {
  name              = "/fcc-demo-websocket/websocket_logs"
  retention_in_days = 1
}













output "websocket_api_endpoint" {
  value       = aws_apigatewayv2_stage.websocket_stage.invoke_url
  description = "The endpoint URL for the WebSocket API Gateway"
}

