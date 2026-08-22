import type { SoulConversation } from "./soultext-api";

export type ConversationListRow = {
  id: string;
  kind: "direct" | "scene";
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
  const isScene = conversation.kind === "scene";

  return {
    id: conversation.id,
    kind: conversation.kind,
    title: isScene ? (conversation.name || names.join(" · ") || "Сцена") : (names[0] || conversation.name || "Диалог"),
    subtitle: isScene ? names.join(" · ") : conversation.name,
    preview,
    updatedAt: conversation.updatedAt || last?.createdAt || conversation.createdAt,
    participantNames: names,
    isRunning: conversation.turnState?.status === "running",
  };
}

export function sortConversationRows(rows: ConversationListRow[]): ConversationListRow[] {
  return [...rows].sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt));
}
