import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";

import type { SoulExeSession } from "./soulexe-api";

const SESSION_KEY = "soulexe.mobile.session.v1";
const LEGACY_SESSION_KEY = "soultext.mobile.session.v1";
const APPEARANCE_KEY = "soulexe.mobile.appearance.v1";

export type ChatAppearanceSettings = {
  actionColor: string;
  thoughtColor: string;
  speechColor: string;
  stripActionMarkers: boolean;
  stripThoughtMarkers: boolean;
  stripSpeechMarkers: boolean;
  typingSimulation: "off" | "fast" | "slow";
};

export const defaultChatAppearance: ChatAppearanceSettings = {
  actionColor: "#C8A6FF",
  thoughtColor: "#73B7FF",
  speechColor: "#FFD18A",
  stripActionMarkers: false,
  stripThoughtMarkers: false,
  stripSpeechMarkers: false,
  typingSimulation: "fast",
};

function browserStorage() {
  return typeof window === "undefined" ? null : window.localStorage;
}

export async function loadSoulExeSession(): Promise<SoulExeSession | null> {
  const read = async (key: string) => Platform.OS === "web" ? browserStorage()?.getItem(key) ?? null : await SecureStore.getItemAsync(key);
  const value = await read(SESSION_KEY) ?? await read(LEGACY_SESSION_KEY);
  if (!value) return null;
  try {
    const parsed = JSON.parse(value) as SoulExeSession;
    if (!parsed.baseUrl || !parsed.session) return null;
    if ((await read(SESSION_KEY)) === null) {
      await saveSoulExeSession(parsed);
      if (Platform.OS === "web") browserStorage()?.removeItem(LEGACY_SESSION_KEY);
      else await SecureStore.deleteItemAsync(LEGACY_SESSION_KEY);
    }
    return parsed;
  } catch {
    return null;
  }
}

export async function saveSoulExeSession(session: SoulExeSession): Promise<void> {
  const value = JSON.stringify(session);
  if (Platform.OS === "web") {
    browserStorage()?.setItem(SESSION_KEY, value);
    return;
  }
  await SecureStore.setItemAsync(SESSION_KEY, value);
}

export async function clearSoulExeSession(): Promise<void> {
  if (Platform.OS === "web") {
    browserStorage()?.removeItem(SESSION_KEY);
    browserStorage()?.removeItem(LEGACY_SESSION_KEY);
    return;
  }
  await SecureStore.deleteItemAsync(SESSION_KEY);
  await SecureStore.deleteItemAsync(LEGACY_SESSION_KEY);
}

export async function loadChatAppearance(): Promise<ChatAppearanceSettings> {
  const value = Platform.OS === "web" ? browserStorage()?.getItem(APPEARANCE_KEY) ?? null : await SecureStore.getItemAsync(APPEARANCE_KEY);
  if (!value) return defaultChatAppearance;
  try {
    const parsed = JSON.parse(value) as Partial<ChatAppearanceSettings>;
    return { ...defaultChatAppearance, ...parsed };
  } catch {
    return defaultChatAppearance;
  }
}

export async function saveChatAppearance(settings: ChatAppearanceSettings): Promise<void> {
  const value = JSON.stringify(settings);
  if (Platform.OS === "web") {
    browserStorage()?.setItem(APPEARANCE_KEY, value);
    return;
  }
  await SecureStore.setItemAsync(APPEARANCE_KEY, value);
}
