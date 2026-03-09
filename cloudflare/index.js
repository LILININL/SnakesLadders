import { Container, getContainer } from "@cloudflare/containers";

export class SnakesLaddersContainer extends Container {
  defaultPort = 8080;
  sleepAfter = "2h";
  envVars = {
    ASPNETCORE_URLS: "http://0.0.0.0:8080",
    ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"
  };
}

export default {
  async fetch(request, env) {
    const container = getContainer(env.APP_CONTAINER, "singleton");
    await container.startAndWaitForPorts();
    return container.fetch(request);
  }
};
