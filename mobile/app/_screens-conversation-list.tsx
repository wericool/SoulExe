import { ActivityIndicator, FlatList, View } from "react-native";
import type { ReactElement } from "react";

import { EmptyState } from "@/components/soul/ui";
import { MessengerRow } from "@/components/soul/messenger-elements";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";

import type { MobileConversationEntry } from "./_types";
import { formatMessagePreview } from "./_utils";
import { styles } from "./_styles";

export function ConversationListScreen({ entries, appearance, busy, onRefresh, onOpen, footer }: {
  entries: MobileConversationEntry[];
  appearance: ChatAppearanceSettings;
  busy: boolean;
  onRefresh: () => void;
  onOpen: (entry: MobileConversationEntry) => void;
  footer: ReactElement;
}) {
  return <View style={styles.grow}><FlatList
    data={entries}
    keyExtractor={(item) => item.conversation.id}
    contentContainerStyle={styles.dialogListWithFab}
    refreshing={busy}
    onRefresh={onRefresh}
    renderItem={({ item }) => <MessengerRow
      title={item.row.title}
      subtitle={formatMessagePreview(item.row.preview || item.row.subtitle, appearance)}
      updatedAt={item.row.updatedAt}
      character={item.row.mode === "personal" ? item.character : undefined}
      sceneCharacters={item.row.mode === "group" ? item.sceneCharacters : undefined}
      status={item.conversation.turnState?.status}
      onPress={() => onOpen(item)}
    />}
    ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} style={{ marginTop: 40 }} /> : <EmptyState icon="chat-bubble-outline" title="Переписок пока нет" caption="Нажмите кнопку внизу, чтобы создать разговор." />}
    ListFooterComponent={footer}
  /></View>;
}
