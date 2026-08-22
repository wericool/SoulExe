import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";

import type { SoulTextSession } from "./soultext-api";

const SESSION_KEY = "soultext.mobile.session.v1";
const APPEARANCE_KEY = "soulexe.mobile.appearance.v1";

export type ChatAppearanceSettings = {
  actionColor: string;
  thoughtColor: string;
  speechColor: string;
  stripActionMarkers: boolean;
  stripThoughtMarkers: boolean;
  stripSpeechMarkers: boolean;
};

export const defaultChatAppearance: ChatAppearanceSettings = {
  actionColor: "#C8A6FF",
  thoughtColor: "#73B7FF",
  speechColor: "#FFD18A",
  stripActionMarkers: false,
  stripThoughtMarkers: false,
  stripSpeechMarkers: false,
};

function browserStorage() {
  return typeof window === "undefined" ? null : window.localStorage;
}

export async function loadSoulTextSession(): Promise<SoulTextSession | null> {
  const value = Platform.OS === "web" ? browserStorage()?.getItem(SESSION_KEY) ?? null : await SecureStore.getItemAsync(SESSION_KEY);
  if (!value) return null;
  try {
    const parsed = JSON.parse(value) as SoulTextSession;
    return parsed.baseUrl && parsed.session ? parsed : null;
  } catch {
    return null;
  }
}

export async function saveSoulTextSession(session: SoulTextSession): Promise<void> {
  const value = JSON.stringify(session);
  if (Platform.OS === "web") {
    browserStorage()?.setItem(SESSION_KEY, value);
    return;
  }
  await SecureStore.setItemAsync(SESSION_KEY, value);
}

export async function clearSoulTextSession(): Promise<void> {
  if (Platform.OS === "web") {
    browserStorage()?.removeItem(SESSION_KEY);
    return;
  }
  await SecureStore.deleteItemAsync(SESSION_KEY);
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
