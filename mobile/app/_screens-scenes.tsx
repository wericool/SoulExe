import { MaterialIcons } from "@expo/vector-icons";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Alert, BackHandler, FlatList, KeyboardAvoidingView, Platform, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import { ConversationMessageList } from "@/components/soul/conversation-message-list";
import type { SoulCharacter, SoulConversation, SoulExeApi, SoulPersona, SoulScene } from "@/lib/soulexe-api";
import { colors } from "@/lib/theme";
import { useConversationSync } from "@/hooks/use-conversation-sync";
import { useAndroidKeyboardLift } from "@/hooks/use-android-keyboard-lift";

import { formatTime, statusLabel, statusTone, wait, sceneFingerprint } from "./_utils";
import { styles } from "./_styles";
import { ComposerAuthorPicker, FormattedMessageText } from "./_components-chat";
import { GroupConversationProfile } from "./_components-conversation-profile";
import { ConversationMessageComposer } from "./_components-message-composer";
import { GroupConversationEditorScreen } from "./_screens-conversation-editor";

import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";

function sceneFromConversation(conversation: SoulConversation, characters: SoulCharacter[]): SoulScene {
  if (conversation.mode !== "group") throw new Error("Ожидался групповой разговор.");
  const knownCharacters = new Map(characters.map((character) => [character.id, character]));
  const participants = conversation.participants.filter((participant) => participant.kind === "Character").sort((left, right) => left.sortOrder - right.sortOrder);
  const resolveCharacter = (index: number) => {
    const participant = participants[index];
    if (!participant) return null;
    return knownCharacters.get(participant.characterId || participant.id) || { id: participant.characterId || participant.id, name: participant.displayName, avatarUrl: participant.avatarUrl };
  };
  return {
    id: conversation.id,
    name: conversation.name,
    status: conversation.turnState?.status || "paused",
    updatedAt: conversation.updatedAt,
    characterA: resolveCharacter(0),
    characterB: resolveCharacter(1),
    scenario: conversation.context.scenario,
    location: conversation.context.location,
    timeContext: conversation.context.timeContext,
    mood: conversation.context.mood,
    goal: conversation.context.goal,
    relationshipContext: conversation.context.relationshipContext,
    turnMode: conversation.turnState?.mode,
    delaySeconds: conversation.turnState?.delaySeconds,
    enforceSceneContract: conversation.turnState?.enforceContract,
    advanceSceneAndAvoidRepetition: conversation.turnState?.advanceAndAvoidRepetition,
    nextTurnAt: conversation.turnState?.nextTurnAt,
    messages: conversation.messages.map((message) => ({ kind: message.kind === "director" ? "director" : "dialogue", speakerId: message.authorParticipantId, authorKind: message.authorKind, authorPersonaId: message.authorPersonaId, author: message.author, content: message.content, createdAt: message.createdAt })),
  };
}


export function ScenesScreen({ api, appearance, onThreadChange, initialSceneId, onBackToChats }: { api: SoulExeApi; appearance: ChatAppearanceSettings; onThreadChange: (open: boolean) => void; initialSceneId: string; onBackToChats: () => void }) {
  const insets = useSafeAreaInsets();
  const [scene, setScene] = useState<SoulScene>();
  const [sceneCharacters, setSceneCharacters] = useState<SoulCharacter[]>([]);
  const [personas, setPersonas] = useState<SoulPersona[]>([]);
  const [messageAuthor, setMessageAuthor] = useState<{ kind: "user" | "persona" | "director"; personaId?: string }>({ kind: "director" });
  const [directorText, setDirectorText] = useState("");
  const [busy, setBusy] = useState(false);
  const [sceneGenerating, setSceneGenerating] = useState(false);
  const [sceneInfoOpen, setSceneInfoOpen] = useState(false);
  const [sceneEditing, setSceneEditing] = useState(false);
  const [clock, setClock] = useState(() => Date.now());
  const [sceneHistoryReady, setSceneHistoryReady] = useState(false);
  const history = useRef<FlatList<SoulScene["messages"][number]>>(null);
  const shouldInitialScroll = useRef(true);
  const stickToBottom = useRef(true);
  const keyboardLift = useAndroidKeyboardLift();
  const loadScene = useCallback(async () => {
    shouldInitialScroll.current = true; stickToBottom.current = true; setSceneHistoryReady(false);
    const [conversation, characters, loadedPersonas] = await Promise.all([api.getConversation(initialSceneId, 100), api.getCharacters(), api.getPersonas().catch(() => [])]);
    setSceneCharacters(characters); setPersonas(loadedPersonas); setScene(sceneFromConversation(conversation, characters));
  }, [api, initialSceneId]);
  useEffect(() => { onThreadChange(true); loadScene().catch((error) => Alert.alert("Групповой разговор", error instanceof Error ? error.message : "Ошибка сети")); return () => onThreadChange(false); }, [loadScene, onThreadChange]);
  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (sceneEditing) { setSceneEditing(false); return true; }
      if (sceneInfoOpen) { setSceneInfoOpen(false); return true; }
      onBackToChats(); return true;
    });
    return () => subscription.remove();
  }, [onBackToChats, sceneEditing, sceneInfoOpen]);
  useConversationSync({
    enabled: true,
    intervalMs: 1500,
    refresh: async () => {
      const fresh = sceneFromConversation(await api.getConversation(initialSceneId, 100), sceneCharacters);
      setScene((current) => sceneFingerprint(current) === sceneFingerprint(fresh) ? current : fresh);
    },
  });
  useEffect(() => { const timer = setInterval(() => setClock(Date.now()), 1000); return () => clearInterval(timer); }, []);
  const action = async (name: "start" | "pause" | "next") => {
    if (!scene || busy) return;
    const beforeCount = scene?.messages.length ?? 0;
    setBusy(true); setSceneGenerating(name === "next");
    try {
      let updated = sceneFromConversation(await api.conversationAction(scene.id, { action: name }), sceneCharacters);
      if (name === "next" && updated.messages.length <= beforeCount) {
        for (let attempt = 0; attempt < 90; attempt += 1) {
          await wait(1000);
          const fresh = sceneFromConversation(await api.getConversation(scene.id, 100), sceneCharacters);
          if (fresh.messages.length > beforeCount) { updated = fresh; break; }
        }
      }
      stickToBottom.current = true;
      setScene(updated);
    } catch (error) { Alert.alert("Групповой разговор", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setSceneGenerating(false); setBusy(false); }
  };
  const sendAuthoredMessage = async () => {
    if (!scene || !directorText.trim() || busy) return;
    setBusy(true);
    try {
      stickToBottom.current = true;
      const updated = sceneFromConversation(await api.conversationAction(scene.id, { action: "send", text: directorText.trim(), authorKind: messageAuthor.kind, authorPersonaId: messageAuthor.personaId }), sceneCharacters);
      setScene(updated); setDirectorText("");
    }
    catch (error) { Alert.alert("Групповой разговор", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  if (!scene) return <View style={styles.historyLoading}><ActivityIndicator color={colors.accentHover} /></View>;
  const currentScene = scene;
  const automaticDelay = currentScene.status === "running" && currentScene.turnMode !== "manual" ? currentScene.delaySeconds || 0 : 0;
  const scheduledTurnAt = currentScene.nextTurnAt ? new Date(currentScene.nextTurnAt).getTime() : undefined;
  const remaining = scheduledTurnAt !== undefined ? Math.max(0, Math.ceil((scheduledTurnAt - clock) / 1000)) : undefined;
  const sceneTimer = automaticDelay > 0 && remaining !== undefined ? `⌛ ${String(Math.floor(remaining / 60)).padStart(2, "0")}:${String(remaining % 60).padStart(2, "0")}` : undefined;
  if (sceneInfoOpen && sceneEditing) return <GroupConversationEditorScreen api={api} scene={currentScene} onBack={() => setSceneEditing(false)} onSaved={(updated) => { setScene(sceneFromConversation(updated, sceneCharacters)); setSceneEditing(false); setSceneInfoOpen(false); }} />;
  if (sceneInfoOpen) return <GroupConversationProfile scene={currentScene} onBack={() => setSceneInfoOpen(false)} onEdit={() => setSceneEditing(true)} />;
  return <KeyboardAvoidingView style={styles.grow} behavior={Platform.OS === "ios" ? "padding" : undefined} keyboardVerticalOffset={0}>
    <View style={[styles.grow, { paddingBottom: Math.max(insets.bottom, keyboardLift) }]}>
    <MessengerThreadHeader title={currentScene.name} subtitle={[currentScene.characterA?.name, currentScene.characterB?.name].filter(Boolean).join(" × ") || "Групповой разговор"} onBack={onBackToChats} onTitlePress={() => setSceneInfoOpen(true)} timer={sceneTimer} status={{ text: statusLabel(currentScene.status), tone: statusTone(currentScene.status) }} />
    <View style={styles.grow}>
      <ConversationMessageList
        listRef={history}
        style={[styles.grow, !sceneHistoryReady && styles.historyHidden]}
        messages={currentScene.messages}
        keyExtractor={(item, index) => `${item.createdAt}-${index}`}
        contentContainerStyle={styles.messagesList}
        initialNumToRender={Math.max(currentScene.messages.length, 1)}
        maxToRenderPerBatch={Math.max(currentScene.messages.length, 1)}
        onContentSizeChange={() => {
          if (shouldInitialScroll.current) {
            let attempts = 0;
            const settleAtLatest = () => {
              history.current?.scrollToEnd({ animated: false });
              if (++attempts < 4) requestAnimationFrame(settleAtLatest);
              else { shouldInitialScroll.current = false; setSceneHistoryReady(true); }
            };
            requestAnimationFrame(settleAtLatest);
            return;
          }
          if (stickToBottom.current) requestAnimationFrame(() => history.current?.scrollToEnd({ animated: true }));
        }}
        onScroll={({ nativeEvent }) => {
          const distance = nativeEvent.contentSize.height - (nativeEvent.contentOffset.y + nativeEvent.layoutMeasurement.height);
          stickToBottom.current = distance < 64;
        }}
        renderMessage={(item) => {
          const director = item.kind === "director" || item.author === "Режиссёр";
          const userParticipant = !director && (item.authorKind === "user" || item.authorKind === "persona");
          const secondCharacter = !director && !userParticipant && item.speakerId === currentScene.characterB?.id;
          const centred = director || userParticipant;
          return <View style={[styles.bubbleRow, secondCharacter && styles.bubbleRowMine, centred && styles.directorRowAlign]}><View style={[styles.bubble, secondCharacter ? styles.bubbleMine : styles.bubbleTheirs, centred && styles.bubbleDirector]}><Text style={styles.messageAuthor}>{director ? "Режиссёр" : item.author || "Групповой разговор"}</Text><FormattedMessageText content={item.content} mine={secondCharacter} appearance={appearance} /><Text style={[styles.messageTime, secondCharacter && styles.messageTimeMine]}>{formatTime(item.createdAt)}</Text></View></View>;
        }}
        footer={sceneGenerating ? <View style={styles.typingRow}><ActivityIndicator size="small" color={colors.accentHover} /><Text style={styles.typingText}>Групповой разговор формирует следующую реплику…</Text></View> : null}
        empty={<Text style={styles.listHint}>Запустите разговор, чтобы появилась первая реплика</Text>}
        onScrollToIndexFailed={() => requestAnimationFrame(() => history.current?.scrollToEnd({ animated: false }))}
      />
      {!sceneHistoryReady ? <View style={styles.historyLoadingOverlay}><ActivityIndicator color={colors.accentHover} /></View> : null}
    </View>
    <ConversationMessageComposer value={directorText} onChangeText={setDirectorText} placeholder={messageAuthor.kind === "director" ? "Режиссёрское событие" : "Сообщение"} onFocus={() => { requestAnimationFrame(() => history.current?.scrollToEnd({ animated: true })); setTimeout(() => history.current?.scrollToEnd({ animated: true }), 180); }} authorPicker={<ComposerAuthorPicker personas={personas} value={messageAuthor} onChange={setMessageAuthor} />} leftAction={{ icon: currentScene.status === "running" ? "pause" : "play-arrow", onPress: () => void action(currentScene.status === "running" ? "pause" : "start"), disabled: busy, accessibilityLabel: currentScene.status === "running" ? "Поставить сцену на паузу" : "Запустить сцену" }} rightActions={[{ icon: "send", primary: true, onPress: sendAuthoredMessage, disabled: busy || !directorText.trim(), accessibilityLabel: "Отправить сообщение" }, { icon: "arrow-upward", primary: true, onPress: () => void action("next"), disabled: busy, accessibilityLabel: "Следующая реплика" }]} />
    </View>
  </KeyboardAvoidingView>;
}
