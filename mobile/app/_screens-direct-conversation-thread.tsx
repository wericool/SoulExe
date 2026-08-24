import type { RefObject } from "react";
import { ActivityIndicator, FlatList, KeyboardAvoidingView, Platform, Text, View, type NativeScrollEvent, type NativeSyntheticEvent } from "react-native";

import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import { ConversationMessageList } from "@/components/soul/conversation-message-list";
import type { ChatMessage, SoulPersona } from "@/lib/soulexe-api";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";

import type { MobileChatEntry } from "./_types";
import { lastSeenLabel } from "./_utils";
import { styles } from "./_styles";
import { ComposerAuthorPicker, MessageBubble, MessageComposer } from "./_components-chat";

type MessageAuthor = { kind: "user" | "persona" | "director"; personaId?: string };

export function DirectConversationThreadScreen({ active, messages, appearance, typing, historyLoaded, historyReady, keyboardLift, draft, messageAuthor, personas, unifiedApiAvailable, busy, listRef, onBack, onOpenProfile, onDraftChange, onAuthorChange, onSend, onContentSizeChange, onScroll, onScrollToIndexFailed, onComposerFocus }: {
  active: MobileChatEntry;
  messages: ChatMessage[];
  appearance: ChatAppearanceSettings;
  typing: boolean;
  historyLoaded: boolean;
  historyReady: boolean;
  keyboardLift: number;
  draft: string;
  messageAuthor: MessageAuthor;
  personas: SoulPersona[];
  unifiedApiAvailable: boolean;
  busy: boolean;
  listRef: RefObject<FlatList<ChatMessage> | null>;
  onBack: () => void;
  onOpenProfile: () => void;
  onDraftChange: (value: string) => void;
  onAuthorChange: (value: MessageAuthor) => void;
  onSend: () => void;
  onContentSizeChange: () => void;
  onScroll: (event: NativeSyntheticEvent<NativeScrollEvent>) => void;
  onScrollToIndexFailed: () => void;
  onComposerFocus: () => void;
}) {
  return <KeyboardAvoidingView style={styles.grow} behavior={Platform.OS === "ios" ? "padding" : undefined} keyboardVerticalOffset={0}>
    <View style={[styles.grow, keyboardLift > 0 && { paddingBottom: keyboardLift }]}>
      <MessengerThreadHeader title={active.character.name} subtitle={typing ? "Печатает…" : lastSeenLabel(messages)} character={active.character} onBack={onBack} onTitlePress={onOpenProfile} />
      {historyLoaded ? <View style={styles.grow}>
        <ConversationMessageList
          key={active.id}
          listRef={listRef}
          style={[styles.grow, !historyReady && styles.historyHidden]}
          messages={messages}
          keyExtractor={(item, index) => item.id || `${item.createdAt}-${index}`}
          contentContainerStyle={styles.messagesList}
          initialNumToRender={Math.max(messages.length, 1)}
          maxToRenderPerBatch={Math.max(messages.length, 1)}
          windowSize={7}
          onContentSizeChange={onContentSizeChange}
          onScroll={onScroll}
          renderMessage={(item) => <MessageBubble message={item} appearance={appearance} />}
          footer={typing ? <View style={styles.typingRow}><ActivityIndicator size="small" color={colors.accentHover} /><Text style={styles.typingText}>{active.character.name} печатает…</Text></View> : null}
          empty={<Text style={styles.listHint}>Напишите первое сообщение</Text>}
          onScrollToIndexFailed={onScrollToIndexFailed}
        />
        {!historyReady ? <View style={styles.historyLoadingOverlay}><ActivityIndicator color={colors.accentHover} /></View> : null}
      </View> : <View style={styles.historyLoading}><ActivityIndicator color={colors.accentHover} /></View>}
      <MessageComposer value={draft} onChangeText={onDraftChange} placeholder={messageAuthor.kind === "director" ? "Режиссёрское событие" : "Сообщение"} onSend={onSend} sendDisabled={busy} onFocus={onComposerFocus} authorPicker={unifiedApiAvailable ? <ComposerAuthorPicker personas={personas} value={messageAuthor} onChange={onAuthorChange} /> : undefined} />
    </View>
  </KeyboardAvoidingView>;
}
