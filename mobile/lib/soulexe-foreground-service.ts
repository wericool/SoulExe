import * as Notifications from "expo-notifications";
import { Linking, NativeModules, Platform } from "react-native";

import type { SoulExeSession } from "@/lib/soulexe-api";

type ForegroundServiceModule = {
  start(baseUrl: string, session: string): Promise<void>;
  stop(): Promise<void>;
};

const service = NativeModules.SoulExeForegroundService as
  | ForegroundServiceModule
  | undefined;

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: true,
    shouldSetBadge: false,
  }),
});

export async function startSoulExeForegroundService(
  session: SoulExeSession,
): Promise<void> {
  if (Platform.OS !== "android" || !service) return;
  const existing = await Notifications.getPermissionsAsync();
  if (existing.status !== "granted")
    await Notifications.requestPermissionsAsync();
  await service.start(session.baseUrl, session.session);
}

export async function stopSoulExeForegroundService(): Promise<void> {
  if (Platform.OS !== "android" || !service) return;
  await service.stop();
}

export function conversationIdFromServiceUrl(url: string | null): string | null {
  if (!url) return null;
  const match = url.match(/\/conversation\/([0-9a-f-]{36})(?:[/?#]|$)/i);
  return match?.[1] ?? null;
}

export function subscribeToForegroundServiceLinks(
  listener: (conversationId: string) => void,
) {
  const handle = (url: string | null) => {
    const conversationId = conversationIdFromServiceUrl(url);
    if (conversationId) listener(conversationId);
  };
  const subscription = Linking.addEventListener("url", ({ url }) => handle(url));
  void Linking.getInitialURL().then(handle);
  return () => subscription.remove();
}
