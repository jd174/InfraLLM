import * as signalR from "@microsoft/signalr";

// With nginx reverse proxy, /hubs/* is proxied to the backend automatically.
export function createHubConnection(
  hubPath: string,
  tokenFactory: () => string | null
): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(hubPath, {
      accessTokenFactory: () => tokenFactory() ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();
}

export function createCommandHub(tokenFactory: () => string | null): signalR.HubConnection {
  return createHubConnection("/hubs/command", tokenFactory);
}

export function createChatHub(tokenFactory: () => string | null): signalR.HubConnection {
  return createHubConnection("/hubs/chat", tokenFactory);
}
