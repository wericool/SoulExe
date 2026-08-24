import type { ChatMessage, SoulCharacter, SoulConversation, SoulScene } from "@/lib/soulexe-api";
import { toConversationListRow } from "@/lib/conversation-adapter";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import type { MobileChatEntry, MobileConversationEntry } from "./_types";

export function toMobileConversationEntry(conversation: SoulConversation, characters: SoulCharacter[]): MobileConversationEntry {
  const knownCharacters = new Map(characters.map((character) => [character.id, character]));
  const participants = conversation.participants
    .filter((participant) => participant.kind === "Character")
    .sort((left, right) => left.sortOrder - right.sortOrder)
    .map((participant) => knownCharacters.get(participant.characterId || participant.id) || {
      id: participant.characterId || participant.id,
      name: participant.displayName,
      avatarUrl: participant.avatarUrl,
    });
  return {
    conversation,
    row: toConversationListRow(conversation),
    character: participants[0],
    sceneCharacters: [participants[0], participants[1]],
  };
}

export function formatTime(value?: string) {
  if (!value) return "";
  try {
    return new Date(value).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
  } catch {
    return "";
  }
}

export function messageDayKey(value?: string) {
  const date = value ? new Date(value) : new Date(0);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

export function messageDayLabel(value?: string) {
  const date = value ? new Date(value) : new Date();
  if (Number.isNaN(date.getTime())) return "Сообщения";
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (messageDayKey(value) === messageDayKey(today.toISOString())) return "Сегодня";
  if (messageDayKey(value) === messageDayKey(yesterday.toISOString())) return "Вчера";
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: date.getFullYear() === today.getFullYear() ? undefined : "numeric" }).format(date);
}

export function needsDateDivider<T extends { createdAt?: string }>(items: T[], index: number) {
  return index === 0 || messageDayKey(items[index - 1]?.createdAt) !== messageDayKey(items[index]?.createdAt);
}

export function lastSeenLabel(messages: ChatMessage[]) {
  const last = messages[messages.length - 1];
  return last ? `Был(а) в ${formatTime(last.createdAt)}` : "Нет сообщений";
}

export function statusTone(status?: string): "success" | "muted" | "danger" | "accent" {
  if (status === "running") return "success";
  if (status === "finished") return "danger";
  if (status === "paused") return "accent";
  return "muted";
}

export function statusLabel(status?: string) {
  if (status === "running") return "Идёт";
  if (status === "paused") return "Пауза";
  if (status === "finished") return "Готово";
  return status || "—";
}

export function formatMessagePreview(content: string, appearance: ChatAppearanceSettings) {
  let preview = content;
  if (appearance.stripThoughtMarkers) preview = preview.replace(/<\/?think\b[^>]*>/gi, "");
  if (appearance.stripActionMarkers) preview = preview.replace(/\*([^*\n]+)\*/g, "$1");
  if (appearance.stripSpeechMarkers) preview = preview.replace(/«([^»\n]+)»|"([^"\n]+)"/g, "$1$2");
  return preview.replace(/\s+/g, " ").trim();
}

export const wait = (milliseconds: number) => new Promise<void>((resolve) => setTimeout(resolve, milliseconds));

export async function revealText(text: string, onUpdate: (value: string) => void) {
  const chunkSize = Math.max(8, Math.ceil(text.length / 42));
  for (let end = chunkSize; end < text.length; end += chunkSize) {
    onUpdate(text.slice(0, end));
    await wait(28);
  }
  onUpdate(text);
}

export const sceneFingerprint = (scene?: SoulScene) => {
  const last = scene?.messages?.[scene.messages.length - 1];
  return `${scene?.status || ""}|${scene?.messages?.length || 0}|${last?.createdAt || ""}|${last?.content || ""}`;
};

export const chatFingerprint = (messages: ChatMessage[]) => messages
  .map((message) => `${message.id || ""}|${message.role}|${message.createdAt}|${message.content}`)
  .join("\u001f");

export const chatEntryFingerprint = (entry: MobileChatEntry) => [
  entry.id,
  entry.character.name,
  entry.character.title || "",
  entry.character.avatarUrl || "",
  entry.chat.name,
  entry.chat.updatedAt || "",
  entry.preview || "",
  entry.previewAt || "",
].join("\u001e");

export const chatEntryListFingerprint = (entries: MobileChatEntry[]) => entries.map(chatEntryFingerprint).join("\u001f");

export const activeChatIdentityFingerprint = (entry: MobileChatEntry) => [
  entry.id,
  entry.character.name,
  entry.character.title || "",
  entry.character.avatarUrl || "",
  entry.chat.name,
].join("\u001e");

export const conversationEntryListFingerprint = (entries: MobileConversationEntry[]) => entries.map((entry) => [
  entry.conversation.id,
  entry.row.mode,
  entry.row.title,
  entry.row.subtitle,
  entry.row.preview,
  entry.row.updatedAt,
  entry.conversation.turnState?.status || "",
  entry.sceneCharacters.map((character) => `${character?.name || ""}|${character?.avatarUrl || ""}`).join("\u001e"),
].join("\u001d")).join("\u001f");
