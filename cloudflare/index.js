import { Container } from "@cloudflare/containers";

const CONTAINER_NAME = "singleton-apac-v1";
const CONTAINER_LOCATION_HINT = "apac";

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
    const id = env.APP_CONTAINER.idFromName(CONTAINER_NAME);
    const container = env.APP_CONTAINER.get(id, {
      locationHint: CONTAINER_LOCATION_HINT,
    });
    await container.startAndWaitForPorts();
    const response = await container.fetch(request);
    return withAssetCaching(request, response);
  }
};

function withAssetCaching(request, response) {
  if (!response) {
    return response;
  }

  const pathname = new URL(request.url).pathname.toLowerCase();
  const extension = pathname.match(/\.[^./]+$/)?.[0] ?? "";
  if (!extension) {
    return response;
  }

  const headers = new Headers(response.headers);
  if ([".html", ".css", ".js"].includes(extension)) {
    headers.set("Cache-Control", "no-store, no-cache, must-revalidate");
    headers.set("Pragma", "no-cache");
    headers.set("Expires", "0");
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  }

  if (
    [
      ".png",
      ".jpg",
      ".jpeg",
      ".gif",
      ".webp",
      ".svg",
      ".ico",
      ".glb",
      ".gltf",
      ".bin",
      ".usdz",
    ].includes(extension)
  ) {
    headers.set("Cache-Control", "public, max-age=604800");
    headers.delete("Pragma");
    headers.delete("Expires");
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  }

  return response;
}
