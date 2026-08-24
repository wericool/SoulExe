import type { SoulConversation } from "./soulexe-api";

export type ConversationListRow = {
  id: string;
  mode: "personal" | "group";
  title: string;
  subtitle: string;
  preview: string;
  updatedAt: string;
  participantNames: string[];
  isRunning: boolean;
};

export function toConversationListRow(conversation: SoulConversation): ConversationListRow {
  const participants = conversation.participants
    .filter((participant) => participant.kind === "Character")
    .sort((left, right) => left.sortOrder - right.sortOrder);
  const names = participants.map((participant) => participant.displayName).filter(Boolean);
  const last = conversation.messages.at(-1);
  const preview = (last?.content || conversation.context.scenario || "Нет сообщений").replace(/\s+/g, " ").trim();
  // Participant-derived fallback keeps the unified list compatible with early schema-v9 servers.
  const mode = conversation.mode || (participants.length > 1 ? "group" : "personal");
  const isGroup = mode === "group";

  return {
    id: conversation.id,
    mode,
    title: isGroup ? (conversation.name || names.join(" · ") || "Групповой разговор") : (names[0] || conversation.name || "Разговор"),
    subtitle: isGroup ? names.join(" · ") : conversation.name,
    preview,
    updatedAt: conversation.updatedAt || last?.createdAt || conversation.createdAt,
    participantNames: names,
    isRunning: conversation.turnState?.status === "running",
  };
}

export function sortConversationRows(rows: ConversationListRow[]): ConversationListRow[] {
  return [...rows].sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt));
}
