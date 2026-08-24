import { MaterialIcons } from "@expo/vector-icons";
import { useCallback, useEffect, useRef, useState } from "react";
import { Alert, BackHandler, FlatList, Pressable, ScrollView, Text, TextInput, View } from "react-native";

import { Avatar, Button, Field } from "@/components/soul/ui";
import type { ChatMessage, SoulCharacter, SoulConversation, SoulExeApi, SoulPersona } from "@/lib/soulexe-api";
import { colors } from "@/lib/theme";
import { useConversationSync } from "@/hooks/use-conversation-sync";
import { useAndroidKeyboardLift } from "@/hooks/use-android-keyboard-lift";

import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import type {
  MobileChatEntry,
  MobileConversationEntry,
} from "./_types";
import {
  toMobileConversationEntry,
  formatTime,
  lastSeenLabel,
  chatFingerprint,
  chatEntryListFingerprint,
  activeChatIdentityFingerprint,
  conversationEntryListFingerprint,
} from "./_utils";
import { sortConversationRows, toConversationListRow } from "@/lib/conversation-adapter";
import { styles } from "./_styles";
import { CharacterEditorScreen, CharacterProfilePreview } from "./_screens-characters";
import { ScenesScreen } from "./_screens-scenes";
import { NewChatScreen, NewConversationChoiceScreen, NewSceneScreen } from "./_screens-conversation-create";
import { ConversationListScreen } from "./_screens-conversation-list";
import { DirectConversationThreadScreen } from "./_screens-direct-conversation-thread";

function directConversationMessages(conversation: SoulConversation): ChatMessage[] {
  const userIds = new Set(conversation.participants.filter((participant) => participant.kind === "User").map((participant) => participant.id));
  return conversation.messages
    .filter((message) => message.kind === "message" || message.kind === "director")
    .map((message) => ({
      id: message.id,
      // A director event is rendered as a distinct centred system bubble, not
      // as an outgoing user message. ChatMessage keeps two transport roles.
      role: message.kind === "director" ? "assistant" : message.authorParticipantId && userIds.has(message.authorParticipantId) ? "user" : "assistant",
      author: message.author,
      content: message.content,
      createdAt: message.createdAt,
      authorKind: message.authorKind,
      authorPersonaId: message.authorPersonaId,
    }));
}

async function revealAssistantReply(messages: ChatMessage[], mode: ChatAppearanceSettings["typingSimulation"], update: (messages: ChatMessage[]) => void) {
  if (mode === "off") {
    update(messages);
    return;
  }

  let replyIndex = -1;
  for (let index = messages.length - 1; index >= 0; index -= 1) {
    if (messages[index].role === "assistant" && messages[index].authorKind !== "director") {
      replyIndex = index;
      break;
    }
  }
  if (replyIndex < 0 || !messages[replyIndex].content) {
    update(messages);
    return;
  }

  const reply = messages[replyIndex];
  const chunkSize = mode === "slow" ? 9 : 28;
  const delay = mode === "slow" ? 45 : 32;
  update(messages.map((message, index) => index === replyIndex ? { ...message, content: "" } : message));
  for (let length = chunkSize; length < reply.content.length; length += chunkSize) {
    await new Promise<void>((resolve) => setTimeout(resolve, delay));
    update(messages.map((message, index) => index === replyIndex ? { ...message, content: reply.content.slice(0, length) } : message));
  }
  update(messages);
}

export function ChatsScreen({
  api,
  appearance,
  isVisible,
  onThreadChange,
}: {
  api: SoulExeApi;
  appearance: ChatAppearanceSettings;
  isVisible: boolean;
  onThreadChange: (open: boolean) => void;
}) {
  const [entries, setEntries] = useState<MobileChatEntry[]>([]);
  const [conversationEntries, setConversationEntries] = useState<MobileConversationEntry[]>([]);
  const [personas, setPersonas] = useState<SoulPersona[]>([]);
  const [messageAuthor, setMessageAuthor] = useState<{ kind: "user" | "persona" | "director"; personaId?: string }>({ kind: "user" });
  const [active, setActive] = useState<MobileChatEntry>();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  const [typing, setTyping] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [profileEditing, setProfileEditing] = useState(false);
  const [historyLoaded, setHistoryLoaded] = useState(false);
  const [historyReady, setHistoryReady] = useState(false);
  const [newChatOpen, setNewChatOpen] = useState(false);
  const [newChatCharacters, setNewChatCharacters] = useState<SoulCharacter[]>([]);
  const [newChatCharacterId, setNewChatCharacterId] = useState("");
  const [newChatName, setNewChatName] = useState("");
  const [creationPicker, setCreationPicker] = useState(false);
  const [newSceneOpen, setNewSceneOpen] = useState(false);
  const [sceneId, setSceneId] = useState<string>();
  const shouldInitialScroll = useRef(true);
  const stickToBottom = useRef(true);
  const history = useRef<FlatList<ChatMessage>>(null);
  const mountedRef = useRef(true);
  const keyboardLift = useAndroidKeyboardLift();

  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const loadList = useCallback(
    async (quiet = false) => {
      if (!quiet) setBusy(true);
      try {
        const characters = await api.getCharacters();
        api.getPersonas().then(setPersonas).catch(() => setPersonas([]));
        const page = await api.getConversationPage({ limit: 100, take: 1 });
          const unified = sortConversationRows(page.items.map(toConversationListRow)).map((row) =>
            toMobileConversationEntry(
              page.items.find((conversation) => conversation.id === row.id)!,
              characters,
            ),
          );
          const directEntries = unified
            .filter((entry) => entry.row.mode === "personal" && entry.character)
            .map((entry) => ({
              id: `${entry.character!.id}:${entry.conversation.id}`,
              character: entry.character!,
              chat: {
                id: entry.conversation.id,
                name: entry.conversation.name || entry.row.subtitle,
                updatedAt: entry.row.updatedAt,
              },
              preview: entry.row.preview,
              previewAt: entry.row.updatedAt,
            }));
          setConversationEntries(
            (current) =>
              current && conversationEntryListFingerprint(current) === conversationEntryListFingerprint(unified)
                ? current
                : unified,
          );
          setEntries((current) =>
            chatEntryListFingerprint(current) === chatEntryListFingerprint(directEntries)
              ? current
              : directEntries,
          );
          setActive((current) => {
            if (!current) return current;
            const fresh = directEntries.find((entry) => entry.id === current.id);
            return !fresh || activeChatIdentityFingerprint(current) === activeChatIdentityFingerprint(fresh)
              ? current
              : fresh;
          });
        return;

      } finally {
        if (!quiet) setBusy(false);
      }
    },
    [api],
  );

  const activeCharacterId = active?.character.id;
  const activeChatId = active?.chat.id;

  const loadMessages = useCallback(async () => {
    if (!activeCharacterId || !activeChatId || !isVisible) return;
    shouldInitialScroll.current = true;
    stickToBottom.current = true;
    setHistoryLoaded(false);
    setHistoryReady(false);
    setMessages([]);
    const loaded = directConversationMessages(await api.getConversation(activeChatId, 30));
    setMessages(loaded);
    setHistoryLoaded(true);
  }, [activeCharacterId, activeChatId, api, isVisible]);

  useEffect(() => {
    loadList().catch((error) =>
      Alert.alert("Разговоры", error instanceof Error ? error.message : "Ошибка сети"),
    );
  }, [loadList]);

  useEffect(() => {
    loadMessages().catch((error) =>
      Alert.alert("Чат", error instanceof Error ? error.message : "Ошибка сети"),
    );
  }, [loadMessages]);

  useConversationSync({ enabled: isVisible, intervalMs: 2500, refresh: () => loadList(true) });
  useConversationSync({
    enabled: Boolean(activeCharacterId && activeChatId && isVisible && !busy),
    intervalMs: 1500,
    refresh: async () => {
      const fresh = directConversationMessages(await api.getConversation(activeChatId!, 30));
      setMessages((current) =>
        chatFingerprint(current) === chatFingerprint(fresh) ? current : fresh,
      );
    },
  });

  useEffect(() => {
    if (isVisible) return;
    setActive(undefined);
    setSceneId(undefined);
    setProfileOpen(false);
    setProfileEditing(false);
    setNewChatOpen(false);
    setNewSceneOpen(false);
    setCreationPicker(false);
  }, [isVisible]);

  useEffect(() => {
    onThreadChange(
      isVisible && Boolean(active || sceneId || newChatOpen || newSceneOpen || creationPicker),
    );
  }, [active, sceneId, newChatOpen, newSceneOpen, creationPicker, isVisible, onThreadChange]);

  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (!isVisible) return false;
      if (profileEditing) {
        setProfileEditing(false);
        return true;
      }
      if (profileOpen) {
        setProfileOpen(false);
        return true;
      }
      if (newChatOpen) {
        setNewChatOpen(false);
        return true;
      }
      if (newSceneOpen) {
        setNewSceneOpen(false);
        return true;
      }
      if (creationPicker) {
        setCreationPicker(false);
        return true;
      }
      if (sceneId) {
        setSceneId(undefined);
        return true;
      }
      if (active) {
        setActive(undefined);
        return true;
      }
      return false;
    });
    return () => subscription.remove();
  }, [active, creationPicker, isVisible, newChatOpen, newSceneOpen, profileEditing, profileOpen, sceneId]);

  useEffect(() => {
    if (!active || !messages.length || !stickToBottom.current) return;
    requestAnimationFrame(() => history.current?.scrollToEnd({ animated: !shouldInitialScroll.current }));
  }, [active, messages.length]);

  const openNewChat = async () => {
    setBusy(true);
    try {
      const characters = await api.getCharacters();
      setNewChatCharacters(characters);
      setNewChatCharacterId(characters[0]?.id || "");
      setNewChatName("Новый разговор");
      setCreationPicker(false);
      setNewChatOpen(true);
    } catch (error) { Alert.alert("Новый разговор", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };

  const toggleConversationPin = async (entry: MobileConversationEntry) => {
    try {
      await api.conversationAction(entry.conversation.id, { action: entry.conversation.isPinned ? "unpin" : "pin" });
      await loadList();
    } catch (error) {
      Alert.alert("Разговор", error instanceof Error ? error.message : "Не удалось изменить закрепление.");
    }
  };

  const confirmConversationDelete = (entry: MobileConversationEntry) => {
    Alert.alert("Удалить разговор?", `«${entry.row.title}» будет удалён без возможности восстановления.`, [
      { text: "Отмена", style: "cancel" },
      { text: "Удалить", style: "destructive", onPress: () => void api.deleteConversation(entry.conversation.id).then(() => loadList()).catch((error) => Alert.alert("Разговор", error instanceof Error ? error.message : "Не удалось удалить разговор.")) },
    ]);
  };

  const createChat = async () => {
    if (!newChatCharacterId || busy) return;
    setBusy(true);
    try {
      const character = newChatCharacters.find((value) => value.id === newChatCharacterId);
      if (!character) throw new Error("Персонаж не найден.");
      const conversation = await api.createConversation({ characterIds: [character.id], name: newChatName.trim() || "Новый разговор" });
      const chat = { id: conversation.id, name: conversation.name, updatedAt: conversation.updatedAt };
      setNewChatOpen(false);
      setActive({ id: `${character.id}:${chat.id}`, character, chat });
      await loadList(true);
    } catch (error) { Alert.alert("Новый разговор", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };

  const send = async () => {
    if (!active || !draft.trim() || busy) return;
    const text = draft.trim();
    setDraft("");
    setBusy(true);
    setTyping(true);
    stickToBottom.current = true;
    try {
      const optimisticAuthor = messageAuthor.kind === "director"
        ? "Режиссёр"
        : messageAuthor.kind === "persona"
          ? personas.find((persona) => persona.id === messageAuthor.personaId)?.name || "Персона"
          : "Вы";
      setMessages((current) => [...current, {
        id: `pending-${Date.now()}`,
        role: messageAuthor.kind === "director" ? "assistant" : "user",
        author: optimisticAuthor,
        content: text,
        createdAt: new Date().toISOString(),
        authorKind: messageAuthor.kind,
        authorPersonaId: messageAuthor.personaId,
      }]);
      const fresh = directConversationMessages(await api.sendConversationMessage(active.chat.id, text, { authorKind: messageAuthor.kind, authorPersonaId: messageAuthor.personaId }));
      await revealAssistantReply(fresh, appearance.typingSimulation, setMessages);
      await loadList(true);
    } catch (error) {
      setDraft(text);
      Alert.alert("Разговор", error instanceof Error ? error.message : "Ошибка сети");
    } finally { setTyping(false); setBusy(false); }
  };

  if (creationPicker) return <NewConversationChoiceScreen onBack={() => setCreationPicker(false)} onChat={() => void openNewChat()} onScene={() => { setCreationPicker(false); setNewSceneOpen(true); }} />;
  if (newChatOpen) return <NewChatScreen characters={newChatCharacters} characterId={newChatCharacterId} name={newChatName} busy={busy} onCharacterChange={setNewChatCharacterId} onNameChange={setNewChatName} onBack={() => setNewChatOpen(false)} onCreate={() => void createChat()} />;
  if (newSceneOpen) return <NewSceneScreen api={api} onBack={() => setNewSceneOpen(false)} onCreated={() => { setNewSceneOpen(false); void loadList(); }} />;
  if (sceneId) return <ScenesScreen api={api} appearance={appearance} onThreadChange={onThreadChange} initialSceneId={sceneId} onBackToChats={() => setSceneId(undefined)} />;

  const createButton = <FloatingCreateButton icon="edit" onPress={() => setCreationPicker(true)} accessibilityLabel="Создать разговор" />;
  if (!active) return <ConversationListScreen entries={conversationEntries} appearance={appearance} busy={busy} onRefresh={loadList} onTogglePin={(item) => void toggleConversationPin(item)} onDelete={confirmConversationDelete} createButton={createButton} onOpen={(item) => {
    if (item.row.mode === "group") setSceneId(item.conversation.id);
    else if (item.character) setActive({ id: `${item.character.id}:${item.conversation.id}`, character: item.character, chat: { id: item.conversation.id, name: item.conversation.name || item.row.subtitle, updatedAt: item.row.updatedAt }, preview: item.row.preview, previewAt: item.row.updatedAt });
  }} />;

  if (profileOpen && profileEditing) return <CharacterEditorScreen api={api} character={active.character} onBack={() => setProfileEditing(false)} onSaved={(character) => {
    setActive((current) => current ? { ...current, character } : current);
    setEntries((current) => current.map((entry) => entry.character.id === character.id ? { ...entry, character } : entry));
    setProfileEditing(false); setProfileOpen(false);
  }} />;
  if (profileOpen) return <CharacterProfilePreview character={active.character} chatName={active.chat.name} onBack={() => setProfileOpen(false)} onEdit={() => setProfileEditing(true)} />;

  return <DirectConversationThreadScreen
    active={active} messages={messages} appearance={appearance} typing={typing}
    historyLoaded={historyLoaded} historyReady={historyReady} keyboardLift={keyboardLift}
    draft={draft} messageAuthor={messageAuthor} personas={personas}
    unifiedApiAvailable busy={busy} listRef={history}
    onBack={() => setActive(undefined)} onOpenProfile={() => setProfileOpen(true)}
    onDraftChange={setDraft} onAuthorChange={setMessageAuthor} onSend={send}
    onContentSizeChange={() => {
      if (shouldInitialScroll.current) {
        let attempts = 0;
        const settleAtLatest = () => { history.current?.scrollToEnd({ animated: false }); if (++attempts < 4) requestAnimationFrame(settleAtLatest); else { shouldInitialScroll.current = false; setHistoryReady(true); } };
        requestAnimationFrame(settleAtLatest); return;
      }
      if (stickToBottom.current) requestAnimationFrame(() => history.current?.scrollToEnd({ animated: true }));
    }}
    onScroll={({ nativeEvent }) => { const distance = nativeEvent.contentSize.height - (nativeEvent.contentOffset.y + nativeEvent.layoutMeasurement.height); stickToBottom.current = distance < 64; }}
    onScrollToIndexFailed={() => requestAnimationFrame(() => history.current?.scrollToEnd({ animated: false }))}
    onComposerFocus={() => { requestAnimationFrame(() => history.current?.scrollToEnd({ animated: true })); setTimeout(() => history.current?.scrollToEnd({ animated: true }), 180); }}
  />;
}

function FloatingCreateButton({
  icon,
  onPress,
  accessibilityLabel,
}: {
  icon: keyof typeof MaterialIcons.glyphMap;
  onPress: () => void;
  accessibilityLabel: string;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      onPress={onPress}
      style={({ pressed }) => [styles.floatingCreate, pressed && styles.floatingCreatePressed]}
    >
      <MaterialIcons name={icon} size={25} color={colors.text} />
    </Pressable>
  );
}
