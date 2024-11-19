https://medium.com/@lumeilin301/building-aws-lambda-using-net-core-for-beginners-e7a5f00dab74

dotnet tools list
dotnet new list
dotnet new serverless.AspNetCoreWebAPI --name Lb

https://github.com/hashicorp/terraform-provider-aws/tree/main/examples/api-gateway-websocket-chat-app

Stupid Terraform and rate limits
https://stackoverflow.com/questions/35987294/aws-api-gateway-error-429-too-many-requests

https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/LowLevelDotNetItemCRUD.html

wscat --no-color -c 'wss://xjj12qsirl.execute-api.us-east-1.amazonaws.com/dev?clientId=master'
wscat --no-color -c 'wss://xjj12qsirl.execute-api.us-east-1.amazonaws.com/dev?clientId=greg'
wscat --no-color -c 'wss://xjj12qsirl.execute-api.us-east-1.amazonaws.com/dev?clientId=jim'
wscat --no-color -c 'wss://xjj12qsirl.execute-api.us-east-1.amazonaws.com/dev?clientId=yana'


{"action": "sendMessage", "target": "bob", "message": "👉 JIM"}
{"action": "sendMessage", "target": "greg", "message": "👉 GREG"}
{"action": "sendMessage", "target": "yana", "message": "👉 YANA"}
{"action": "sendMessage", "target": "everybody", "message": "🎉 EVERYBODY"}

curl -X POST https://s1ec7rpfie.execute-api.us-east-1.amazonaws.com/dev/backchannel -H "Content-Type: application/json" -d '{"action":"sendMessage","target":"bob","message":"Hello, Bob!"}'

Sample Event for Payload 2.0 for backchannel_listener lambda

```json
{
  "version": "2.0",
  "routeKey": "GET /backchannel/send",
  "rawPath": "/backchannel/send",
  "rawQueryString": "name=exampleName&message=hello",
  "headers": {
    "accept": "*/*",
    "content-length": "0",
    "host": "your-api-id.execute-api.us-east-1.amazonaws.com",
    "user-agent": "curl/7.64.1",
    "x-amzn-trace-id": "Root=1-67891233-abcdef012345678912345678",
    "x-forwarded-for": "192.0.2.1",
    "x-forwarded-port": "443",
    "x-forwarded-proto": "https"
  },
  "queryStringParameters": {
    "name": "exampleName",
    "message": "hello"
  },
  "requestContext": {
    "accountId": "12345678901",
    "apiId": "9d520aw6kb",
    "domainName": "9d520aw6kb.execute-api.us-east-1.amazonaws.com",
    "domainPrefix": "9d520aw6kb",
    "http": {
      "method": "GET",
      "path": "/backchannel/send",
      "protocol": "HTTP/1.1",
      "sourceIp": "192.0.2.1",
      "userAgent": "curl/7.64.1"
    },
    "requestId": "id",
    "routeKey": "GET /backchannel/send",
    "stage": "dev",
    "time": "12/Mar/2022:19:03:58 +0000",
    "timeEpoch": 1647107038297
  },
  "isBase64Encoded": false
}
```