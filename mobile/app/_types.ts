import type { SoulCharacter, SoulChat, SoulConversation } from "@/lib/soulexe-api";
import type { ConversationListRow } from "@/lib/conversation-adapter";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";

import type { MaterialIcons } from "@expo/vector-icons";

export type TabKey = "chats" | "scenes" | "characters" | "settings";

export type MobileChatEntry = { id: string; character: SoulCharacter; chat: SoulChat; preview?: string; previewAt?: string };

export type MobileConversationEntry = {
  conversation: SoulConversation;
  row: ConversationListRow;
  character?: SoulCharacter;
  sceneCharacters: [SoulCharacter | undefined, SoulCharacter | undefined];
};

export type ComposerAction = {
  icon: keyof typeof MaterialIcons.glyphMap;
  onPress: () => void;
  disabled?: boolean;
  primary?: boolean;
  accessibilityLabel: string;
};
