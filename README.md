# Websocket Demo

This POC is intended to show how a central SaaS service can manage and
communicate with a fleet of websocket clients (brokers, agents)

[📺 Video Demo](websocket.mp4)

https://github.com/jimweller/lab-websocket/raw/refs/heads/main/websocket.mp4

https://raw.githubusercontent.com/jimweller/lab-websocket/refs/heads/main/websocket.mp4


![architecture](architecture.drawio.svg)


## Usage

Run the terraform. It will output the api gateway websocket URL (`ws://something.execute-api....`)

1. Open two terminals
2. Run a websocket client like `wscat` in each one with a unique GET parameter `clientId`
   1. `wscat --no-color -c 'wss://hfytuh7hri.execute-api.us-east-1.amazonaws.com/dev?clientId=jim`
   2. `wscat --no-color -c 'wss://hfytuh7hri.execute-api.us-east-1.amazonaws.com/dev?clientId=bob'`

Now you can send messages with properly formatted JSON

```bash
❯ wscat --no-color -c 'wss://hfytuh7hri.execute-api.us-east-1.amazonaws.com/dev?clientId=bob'
Connected (press CTRL+C to quit)
> {"action": "sendMessage", "target":"jim", "message":"Welcome to Webockets!"}
< Thanks! You too!!
>
```

```bash
❯ wscat --no-color -c 'wss://hfytuh7hri.execute-api.us-east-1.amazonaws.com/dev?clientId=jim'
Connected (press CTRL+C to quit)
< Welcome to Webockets!
> {"action": "sendMessage", "target":"bob", "message":"Thanks! You too!!"}
```