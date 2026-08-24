import { ActivityIndicator, FlatList, Pressable, Text, View } from "react-native";
import { Swipeable } from "react-native-gesture-handler";
import type { ComponentType, ReactElement } from "react";

import { EmptyState } from "@/components/soul/ui";
import { MessengerRow } from "@/components/soul/messenger-elements";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";

import type { MobileConversationEntry } from "./_types";
import { formatMessagePreview } from "./_utils";
import { styles } from "./_styles";

const SwipeableRow = Swipeable as unknown as ComponentType<any>;

export function ConversationListScreen({ entries, appearance, busy, onRefresh, onOpen, onTogglePin, onDelete, createButton }: {
  entries: MobileConversationEntry[];
  appearance: ChatAppearanceSettings;
  busy: boolean;
  onRefresh: () => void;
  onOpen: (entry: MobileConversationEntry) => void;
  onTogglePin: (entry: MobileConversationEntry) => void;
  onDelete: (entry: MobileConversationEntry) => void;
  createButton: ReactElement;
}) {
  return <View style={styles.grow}><FlatList
    data={entries}
    keyExtractor={(item) => item.conversation.id}
    contentContainerStyle={styles.dialogListWithFab}
    refreshing={busy}
    onRefresh={onRefresh}
    renderItem={({ item }) => <SwipeableRow renderLeftActions={() => <Pressable onPress={() => onDelete(item)} style={{ width: 92, justifyContent: "center", alignItems: "center", backgroundColor: colors.danger }}><Text style={{ color: colors.text, fontWeight: "800" }}>Удалить</Text></Pressable>} renderRightActions={() => <Pressable onPress={() => onTogglePin(item)} style={{ width: 104, justifyContent: "center", alignItems: "center", backgroundColor: colors.accent }}><Text style={{ color: colors.text, fontWeight: "800" }}>{item.conversation.isPinned ? "Открепить" : "Закрепить"}</Text></Pressable>}>
      <MessengerRow title={item.row.title} subtitle={formatMessagePreview(item.row.preview || item.row.subtitle, appearance)} updatedAt={item.row.updatedAt} character={item.row.mode === "personal" ? item.character : undefined} sceneCharacters={item.row.mode === "group" ? item.sceneCharacters : undefined} status={item.conversation.turnState?.status} onPress={() => onOpen(item)} />
    </SwipeableRow>}
    ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} style={{ marginTop: 40 }} /> : <EmptyState icon="chat-bubble-outline" title="Переписок пока нет" caption="Нажмите кнопку внизу, чтобы создать разговор." />}
  />
    {createButton}
  </View>;
}
