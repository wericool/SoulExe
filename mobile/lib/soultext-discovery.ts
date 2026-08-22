import * as Network from "expo-network";

import { checkSoulTextServer, normalizeServerUrl, type ServerHealth } from "./soultext-api";

export type DiscoveredSoulTextServer = {
  baseUrl: string;
  name: string;
  health: ServerHealth;
};

function subnetHosts(ipAddress: string): string[] {
  const parts = ipAddress.split(".").map(Number);
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) return [];
  return Array.from({ length: 254 }, (_, index) => `${parts[0]}.${parts[1]}.${parts[2]}.${index + 1}`);
}

async function runBatches<T>(items: string[], batchSize: number, task: (item: string) => Promise<T | null>): Promise<T[]> {
  const found: T[] = [];
  for (let start = 0; start < items.length; start += batchSize) {
    const batch = items.slice(start, start + batchSize);
    const result = await Promise.all(batch.map(task));
    for (const value of result) {
      if (value !== null) found.push(value);
    }
  }
  return found;
}

export async function discoverSoulTextServers(onStatus?: (message: string) => void): Promise<DiscoveredSoulTextServer[]> {
  const ipAddress = await Network.getIpAddressAsync();
  const hosts = subnetHosts(ipAddress);
  if (!hosts.length || ipAddress === "0.0.0.0") throw new Error("Не удалось определить Wi‑Fi/LAN-сеть телефона.");

  onStatus?.("Ищу SoulExe на устройствах в этой сети…");
  const servers = await runBatches(hosts, 24, async (host) => {
    const baseUrl = normalizeServerUrl(`${host}:8000`);
    try {
      const health = await checkSoulTextServer(baseUrl, 650);
      return { baseUrl, name: "SoulExe на ПК", health };
    } catch {
      return null;
    }
  });
  onStatus?.(servers.length ? `Найдено серверов: ${servers.length}` : "SoulExe не найден. Проверьте, что сервер запущен на ПК.");
  return servers;
}
