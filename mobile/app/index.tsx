import { MaterialIcons } from "@expo/vector-icons";
import { StatusBar } from "expo-status-bar";
import * as ImagePicker from "expo-image-picker";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  BackHandler,
  FlatList,
  Keyboard,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  useWindowDimensions,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { Avatar, Button, Card, EmptyState, Field, IconButton, PageHeader, Screen, StatusPill } from "@/components/soul/ui";
import { MessengerRow, MessengerThreadHeader } from "@/components/soul/messenger-elements";
import { checkSoulTextServer, normalizeServerUrl, SoulTextApi, type ChatMessage, type SoulCharacter, type SoulCharacterDraft, type SoulChat, type SoulConversation, type SoulExeApi, type SoulScene, type SoulSceneSummary, type SoulTextSession } from "@/lib/soultext-api";
import { createSoulExeDemoApi } from "@/lib/soulexe-demo-api";
import { sortConversationRows, toConversationListRow, type ConversationListRow } from "@/lib/conversation-adapter";
import { discoverSoulTextServers, type DiscoveredSoulTextServer } from "@/lib/soultext-discovery";
import { clearSoulTextSession, defaultChatAppearance, loadChatAppearance, loadSoulTextSession, saveChatAppearance, saveSoulTextSession, type ChatAppearanceSettings } from "@/lib/soultext-storage";
import { colors, radii, space, typography } from "@/lib/theme";

type TabKey = "chats" | "scenes" | "characters" | "settings";
type MobileChatEntry = { id: string; character: SoulCharacter; chat: SoulChat; preview?: string; previewAt?: string };
type MobileSceneEntry = { id: string; scene: SoulSceneSummary; preview?: string; previewAt?: string };
type MobileThreadEntry = { kind: "chat"; value: MobileChatEntry } | { kind: "scene"; value: MobileSceneEntry };
type MobileConversationEntry = {
  conversation: SoulConversation;
  row: ConversationListRow;
  character?: SoulCharacter;
  sceneCharacters: [SoulCharacter | undefined, SoulCharacter | undefined];
};

function toMobileConversationEntry(conversation: SoulConversation, characters: SoulCharacter[]): MobileConversationEntry {
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

function formatTime(value?: string) {
  if (!value) return "";
  try {
    return new Date(value).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
  } catch {
    return "";
  }
}

function messageDayKey(value?: string) {
  const date = value ? new Date(value) : new Date(0);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function messageDayLabel(value?: string) {
  const date = value ? new Date(value) : new Date();
  if (Number.isNaN(date.getTime())) return "Сообщения";
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (messageDayKey(value) === messageDayKey(today.toISOString())) return "Сегодня";
  if (messageDayKey(value) === messageDayKey(yesterday.toISOString())) return "Вчера";
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: date.getFullYear() === today.getFullYear() ? undefined : "numeric" }).format(date);
}

function needsDateDivider<T extends { createdAt?: string }>(items: T[], index: number) {
  return index === 0 || messageDayKey(items[index - 1]?.createdAt) !== messageDayKey(items[index]?.createdAt);
}

function ChatDateDivider({ value }: { value?: string }) {
  return <View style={styles.dateDivider}><View style={styles.dateDividerLine} /><Text style={styles.dateDividerText}>{messageDayLabel(value)}</Text><View style={styles.dateDividerLine} /></View>;
}

function lastSeenLabel(messages: ChatMessage[]) {
  const last = messages[messages.length - 1];
  return last ? `Был(а) в ${formatTime(last.createdAt)}` : "Нет сообщений";
}

function statusTone(status?: string): "success" | "muted" | "danger" | "accent" {
  if (status === "running") return "success";
  if (status === "finished") return "danger";
  if (status === "paused") return "accent";
  return "muted";
}

function statusLabel(status?: string) {
  if (status === "running") return "Идёт";
  if (status === "paused") return "Пауза";
  if (status === "finished") return "Готово";
  return status || "—";
}

function formatMessagePreview(content: string, appearance: ChatAppearanceSettings) {
  let preview = content;
  if (appearance.stripThoughtMarkers) preview = preview.replace(/<\/?think\b[^>]*>/gi, "");
  if (appearance.stripActionMarkers) preview = preview.replace(/\*([^*\n]+)\*/g, "$1");
  if (appearance.stripSpeechMarkers) preview = preview.replace(/«([^»\n]+)»|"([^"\n]+)"/g, "$1$2");
  return preview.replace(/\s+/g, " ").trim();
}

const wait = (milliseconds: number) => new Promise<void>((resolve) => setTimeout(resolve, milliseconds));

const sceneFingerprint = (scene?: SoulScene) => {
  const last = scene?.messages?.[scene.messages.length - 1];
  return `${scene?.status || ""}|${scene?.messages?.length || 0}|${last?.createdAt || ""}|${last?.content || ""}`;
};

const chatFingerprint = (messages: ChatMessage[]) => messages
  .map((message) => `${message.id || ""}|${message.role}|${message.createdAt}|${message.content}`)
  .join("\u001f");

const chatEntryFingerprint = (entry: MobileChatEntry) => [
  entry.id,
  entry.character.name,
  entry.character.title || "",
  entry.character.avatarUrl || "",
  entry.chat.name,
  entry.chat.updatedAt || "",
  entry.preview || "",
  entry.previewAt || "",
].join("\u001e");

const chatEntryListFingerprint = (entries: MobileChatEntry[]) => entries.map(chatEntryFingerprint).join("\u001f");

const activeChatIdentityFingerprint = (entry: MobileChatEntry) => [
  entry.id,
  entry.character.name,
  entry.character.title || "",
  entry.character.avatarUrl || "",
  entry.chat.name,
].join("\u001e");

const sceneEntryListFingerprint = (entries: MobileSceneEntry[]) => entries.map((entry) => [
  entry.id,
  entry.scene.name,
  entry.scene.status,
  entry.scene.updatedAt || "",
  entry.scene.nextTurnAt || "",
  entry.scene.characterA?.name || "",
  entry.scene.characterA?.avatarUrl || "",
  entry.scene.characterB?.name || "",
  entry.scene.characterB?.avatarUrl || "",
  entry.preview || "",
  entry.previewAt || "",
].join("\u001e")).join("\u001f");

const conversationEntryListFingerprint = (entries: MobileConversationEntry[]) => entries.map((entry) => [
  entry.conversation.id,
  entry.row.kind,
  entry.row.title,
  entry.row.subtitle,
  entry.row.preview,
  entry.row.updatedAt,
  entry.conversation.turnState?.status || "",
  entry.sceneCharacters.map((character) => `${character?.name || ""}|${character?.avatarUrl || ""}`).join("\u001e"),
].join("\u001d")).join("\u001f");

function useAndroidKeyboardLift() {
  const [keyboardHeight, setKeyboardHeight] = useState(0);
  const { height: windowHeight } = useWindowDimensions();
  const restingWindowHeight = useRef(windowHeight);

  useEffect(() => {
    if (Platform.OS !== "android") return;
    const onShow = Keyboard.addListener("keyboardDidShow", (event) => setKeyboardHeight(Math.max(0, event.endCoordinates.height)));
    const onHide = Keyboard.addListener("keyboardDidHide", () => setKeyboardHeight(0));
    return () => { onShow.remove(); onHide.remove(); };
  }, []);

  useEffect(() => {
    if (keyboardHeight === 0) restingWindowHeight.current = windowHeight;
  }, [keyboardHeight, windowHeight]);

  if (Platform.OS !== "android" || keyboardHeight === 0) return 0;
  const resizeHeight = Math.max(0, restingWindowHeight.current - windowHeight);
  return Math.max(0, keyboardHeight - resizeHeight);
}

async function revealText(text: string, onUpdate: (value: string) => void) {
  const chunkSize = Math.max(8, Math.ceil(text.length / 42));
  for (let end = chunkSize; end < text.length; end += chunkSize) {
    onUpdate(text.slice(0, end));
    await wait(28);
  }
  onUpdate(text);
}

export default function SoulExeMobile() {
  const [session, setSession] = useState<SoulTextSession | null>(null);
  const [booting, setBooting] = useState(true);
  const [tab, setTab] = useState<TabKey>("chats");
  const [demoMode, setDemoMode] = useState(false);
  const [appearance, setAppearance] = useState<ChatAppearanceSettings>(defaultChatAppearance);
  const demoApi = useMemo(() => createSoulExeDemoApi(), []);
  const liveApi = useMemo(() => session ? new SoulTextApi(session) : null, [session]);

  useEffect(() => {
    Promise.all([loadSoulTextSession(), loadChatAppearance()]).then(([nextSession, nextAppearance]) => { setSession(nextSession); setAppearance(nextAppearance); }).finally(() => setBooting(false));
  }, []);
  const updateAppearance = useCallback((changes: Partial<ChatAppearanceSettings>) => {
    setAppearance((current) => {
      const next = { ...current, ...changes };
      void saveChatAppearance(next);
      return next;
    });
  }, []);

  if (booting) {
    return <SplashScreen />;
  }
  if (!session && !demoMode) {
    return (
      <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
        <StatusBar style="light" />
        <ConnectionScreen onConnected={async (next) => {
          await saveSoulTextSession(next);
          setSession(next);
          setDemoMode(false);
          setTab("chats");
        }} onEnterDemo={() => { setDemoMode(true); setTab("chats"); }} />
      </SafeAreaView>
    );
  }
  return (
    <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
      <StatusBar style="light" />
      <ConnectedApp
        api={demoMode ? demoApi : liveApi!}
        isDemo={demoMode}
        appearance={appearance}
        onAppearanceChange={updateAppearance}
        tab={tab}
        onTabChange={setTab}
        onLogout={async () => {
          if (demoMode) { setDemoMode(false); return; }
          await clearSoulTextSession(); setSession(null);
        }}
      />
    </SafeAreaView>
  );
}

function SplashScreen() {
  return (
    <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
      <StatusBar style="light" />
      <View style={styles.boot}>
        <View style={styles.logoMark}><MaterialIcons name="auto-awesome" size={28} color={colors.text} /></View>
        <Text style={styles.bootTitle}>SoulExe</Text>
        <ActivityIndicator color={colors.accentHover} style={{ marginTop: 18 }} />
      </View>
    </SafeAreaView>
  );
}

function ConnectionScreen({ onConnected, onEnterDemo }: { onConnected: (session: SoulTextSession) => Promise<void>; onEnterDemo: () => void }) {
  const [serverUrl, setServerUrl] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [status, setStatus] = useState("Найдите SoulExe в Wi‑Fi или введите адрес вручную.");
  const [servers, setServers] = useState<DiscoveredSoulTextServer[]>([]);
  const [busy, setBusy] = useState(false);
  const [step, setStep] = useState<"start" | "servers" | "login">("start");

  const discover = async () => {
    setBusy(true);
    setServers([]);
    setStep("servers");
    try {
      const found = await discoverSoulTextServers(setStatus);
      setServers(found);
      if (!found.length) setStatus("SoulExe в сети не найден. Проверьте, что мобильный доступ включён на ПК.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Не удалось начать поиск.");
    } finally {
      setBusy(false);
    }
  };

  const selectServer = (server: DiscoveredSoulTextServer) => { setServerUrl(server.baseUrl); setStatus(`Вход в SoulExe по адресу ${server.baseUrl}`); setStep("login"); };

  const connect = async () => {
    const baseUrl = normalizeServerUrl(serverUrl);
    if (!baseUrl || !username || !password) {
      setStatus("Укажите адрес, логин и пароль из «Мобильный доступ» на ПК.");
      return;
    }
    setBusy(true);
    setStatus("Проверяю сервер и вхожу…");
    try {
      await checkSoulTextServer(baseUrl);
      await onConnected(await SoulTextApi.login(baseUrl, username, password));
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Подключение не удалось.");
    } finally {
      setBusy(false);
    }
  };

  if (step === "servers") return <View style={styles.connectPlain}><View style={styles.connectPlainBrand}><View style={styles.authOrb}><MaterialIcons name="wifi-find" size={34} color={colors.text} /></View><Text style={styles.connectPlainTitle}>Выберите компьютер</Text><Text style={styles.connectPlainText}>{status}</Text></View><FlatList style={styles.serverPickerList} data={servers} keyExtractor={(item) => item.baseUrl} ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} style={{ marginTop: 24 }} /> : <Text style={styles.connectEmptyText}>Компьютеры пока не найдены.</Text>} renderItem={({ item }) => <Pressable onPress={() => selectServer(item)} style={({ pressed }) => [styles.serverPickerRow, pressed && styles.serverPickerRowPressed]}><MaterialIcons name="desktop-windows" size={22} color={colors.accentHover} /><View style={{ flex: 1 }}><Text style={styles.serverTitle}>{item.name || "SoulExe на ПК"}</Text><Text style={styles.serverUrl}>{item.baseUrl}</Text></View><MaterialIcons name="chevron-right" size={22} color={colors.dim} /></Pressable>} ListFooterComponent={<Button title={busy ? "Ищу…" : "Искать ещё раз"} variant="secondary" icon="refresh" disabled={busy} onPress={discover} style={{ marginTop: 20 }} />} /><Pressable onPress={() => setStep("start")} style={styles.connectBack}><Text style={styles.connectBackText}>Назад</Text></Pressable></View>;
  if (step === "login") return <KeyboardAvoidingView style={styles.grow} behavior={Platform.OS === "ios" ? "padding" : "height"} keyboardVerticalOffset={0}><ScrollView contentContainerStyle={[styles.connectPlain, styles.connectPlainLogin]} keyboardDismissMode="interactive" keyboardShouldPersistTaps="handled"><View style={styles.connectPlainBrand}><View style={styles.authOrb}><MaterialIcons name="lock-outline" size={34} color={colors.text} /></View><Text style={styles.connectPlainTitle}>Вход в SoulExe</Text><Text style={styles.connectPlainText}>{serverUrl}</Text></View><View style={styles.loginFields}><Field label="Логин" value={username} onChangeText={setUsername} autoCapitalize="none" autoCorrect={false} placeholder="Как в SoulExe на ПК" /><Field label="Пароль" value={password} onChangeText={setPassword} secureTextEntry placeholder="Пароль мобильного доступа" onSubmitEditing={connect} /><Button title={busy ? "Вход…" : "Войти"} icon="login" disabled={busy} loading={busy} onPress={connect} /><Text style={styles.connectStatus}>{status}</Text></View><Pressable onPress={() => setStep("servers")} style={styles.connectBack}><Text style={styles.connectBackText}>Выбрать другой компьютер</Text></Pressable></ScrollView></KeyboardAvoidingView>;
  return <View style={styles.connectPlain}><View style={styles.connectPlainBrand}><View style={styles.authOrb}><MaterialIcons name="forum" size={34} color={colors.text} /></View><Text style={styles.authProductName}>SoulExe Mobile</Text><Text style={styles.connectPlainTitle}>Ваши персонажи — рядом</Text><Text style={styles.connectPlainText}>Подключитесь к SoulExe на компьютере или сначала посмотрите приложение в демо-режиме.</Text></View><View style={styles.connectActions}><Button title="Найти SoulExe в Wi‑Fi" icon="wifi-find" disabled={busy} loading={busy} onPress={discover} /><Button title="Открыть демо-режим" icon="play-circle-outline" variant="secondary" disabled={busy} onPress={onEnterDemo} /></View></View>;
}

function ConnectedApp({ api, isDemo, appearance, onAppearanceChange, tab, onTabChange, onLogout }: { api: SoulExeApi; isDemo: boolean; appearance: ChatAppearanceSettings; onAppearanceChange: (changes: Partial<ChatAppearanceSettings>) => void; tab: TabKey; onTabChange: (tab: TabKey) => void; onLogout: () => Promise<void> }) {
  const [threadOpen, setThreadOpen] = useState(false);
  const changeTab = (next: TabKey) => { setThreadOpen(false); onTabChange(next); };
  return (
    <Screen>
      <View style={styles.content}>
        <View style={[styles.tabPage, tab !== "chats" && styles.tabPageHidden]}><ChatsScreen api={api} appearance={appearance} isVisible={tab === "chats"} onThreadChange={setThreadOpen} /></View>
        {tab === "characters" ? <CharactersScreen api={api} /> : null}
        {tab === "settings" ? <SettingsScreen baseUrl={isDemo ? "Автономная демонстрация" : "Подключение к SoulExe на ПК"} isDemo={isDemo} appearance={appearance} onAppearanceChange={onAppearanceChange} onLogout={onLogout} /> : null}
      </View>
      {!threadOpen ? <View style={styles.tabBar}>
        <TabButton icon="chat-bubble-outline" label="Чаты" active={tab === "chats"} onPress={() => changeTab("chats")} />
        <TabButton icon="people-outline" label="Персонажи" active={tab === "characters"} onPress={() => changeTab("characters")} />
        <TabButton icon="settings" label="Ещё" active={tab === "settings"} onPress={() => changeTab("settings")} />
      </View> : null}
    </Screen>
  );
}

function TabButton({ icon, label, active, onPress }: { icon: keyof typeof MaterialIcons.glyphMap; label: string; active: boolean; onPress: () => void }) {
  return <Pressable onPress={onPress} style={[styles.tabButton, active && styles.tabButtonActive]}><MaterialIcons name={icon} size={22} color={active ? colors.accentHover : colors.muted} /><Text style={[styles.tabLabel, active && styles.tabLabelActive]}>{label}</Text></Pressable>;
}

function FloatingCreateButton({ icon, onPress, accessibilityLabel }: { icon: keyof typeof MaterialIcons.glyphMap; onPress: () => void; accessibilityLabel: string }) {
  return <Pressable accessibilityRole="button" accessibilityLabel={accessibilityLabel} onPress={onPress} style={({ pressed }) => [styles.floatingCreate, pressed && styles.floatingCreatePressed]}><MaterialIcons name={icon} size={25} color={colors.text} /></Pressable>;
}

type ComposerAction = { icon: keyof typeof MaterialIcons.glyphMap; onPress: () => void; disabled?: boolean; primary?: boolean; accessibilityLabel: string };

function MessageComposer({ value, onChangeText, placeholder, leftAction, onSend, sendDisabled, rightActions }: {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  leftAction?: ComposerAction;
  onSend?: () => void;
  sendDisabled?: boolean;
  rightActions?: ComposerAction[];
}) {
  const resolvedRightActions = rightActions ?? (onSend ? [{
    icon: "arrow-upward" as const,
    onPress: onSend,
    disabled: sendDisabled || !value.trim(),
    primary: true,
    accessibilityLabel: "Отправить",
  }] : []);
  return (
    <View style={styles.sceneComposer}>
      {leftAction ? (
        <Pressable
          onPress={leftAction.onPress}
          disabled={leftAction.disabled}
          style={({ pressed }) => [styles.composerAction, (pressed || leftAction.disabled) && styles.composerActionPressed]}
        >
          <MaterialIcons name={leftAction.icon} size={22} color={colors.muted} />
        </Pressable>
      ) : null}
      <TextInput
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.dim}
        multiline
        maxLength={8000}
        textAlignVertical="center"
        style={styles.sceneComposerInput}
      />
      {resolvedRightActions.map((action, index) => (
        <Pressable
          key={`ra-${index}`}
          onPress={action.onPress}
          disabled={action.disabled}
          style={({ pressed }) => [
            styles.composerAction,
            action.primary && styles.composerActionPrimary,
            (pressed || action.disabled) && styles.composerActionPressed,
          ]}
        >
          <MaterialIcons name={action.icon} size={22} color={action.primary ? colors.text : colors.muted} />
        </Pressable>
      ))}
    </View>
  );
}

function CompactSelector<T extends { id: string; name?: string }>({ label, items, selected, onSelect }: { label: string; items: T[]; selected?: string; onSelect: (id: string) => void }) {
  return (
    <View style={styles.selectorBlock}>
      <Text style={styles.selectorLabel}>{label}</Text>
      <FlatList
        data={items}
        keyExtractor={(item) => item.id}
        nestedScrollEnabled
        showsVerticalScrollIndicator={false}
        style={styles.selectorList}
        contentContainerStyle={styles.selectorContent}
        renderItem={({ item }) => (
          <Pressable onPress={() => onSelect(item.id)} style={[styles.selectorItem, item.id === selected && styles.selectorItemActive]}>
            <View style={[styles.selectorDot, item.id === selected && styles.selectorDotActive]} />
            <Text style={[styles.selectorText, item.id === selected && styles.selectorTextActive]} numberOfLines={1}>{item.name || "Без названия"}</Text>
          </Pressable>
        )}
      />
    </View>
  );
}

function SceneCharacterPicker({ label, characters, selectedId, excludeId, onSelect }: { label: string; characters: SoulCharacter[]; selectedId?: string; excludeId?: string; onSelect: (id: string) => void }) {
  const [open, setOpen] = useState(false);
  const selected = characters.find((character) => character.id === selectedId);
  const choices = characters.filter((character) => character.id !== excludeId);
  return <View style={styles.selectorBlock}><Text style={styles.selectorLabel}>{label}</Text><Pressable onPress={() => setOpen(true)} style={({ pressed }) => [styles.scenePickerTrigger, pressed && styles.scenePickerTriggerPressed]}>{selected ? <Avatar character={selected} size={34} /> : <View style={styles.scenePickerEmptyAvatar}><MaterialIcons name="person-outline" size={19} color={colors.dim} /></View>}<View style={{ flex: 1 }}><Text style={styles.scenePickerName}>{selected?.name || "Выберите персонажа"}</Text><Text style={styles.scenePickerSubtitle} numberOfLines={1}>{selected?.title || "Нажмите, чтобы открыть список"}</Text></View><MaterialIcons name="expand-more" size={22} color={colors.muted} /></Pressable><Modal transparent visible={open} animationType="fade" onRequestClose={() => setOpen(false)}><View style={styles.scenePickerModal}><Pressable style={styles.scenePickerDismiss} onPress={() => setOpen(false)} /><View style={styles.scenePickerSheet}><View style={styles.scenePickerSheetHead}><Text style={styles.scenePickerSheetTitle}>{label}</Text><Pressable onPress={() => setOpen(false)} style={styles.scenePickerClose}><MaterialIcons name="close" size={20} color={colors.muted} /></Pressable></View><FlatList data={choices} keyExtractor={(item) => item.id} contentContainerStyle={styles.scenePickerList} ListEmptyComponent={<Text style={styles.scenePickerEmpty}>Нет доступных персонажей.</Text>} renderItem={({ item }) => <Pressable onPress={() => { onSelect(item.id); setOpen(false); }} style={({ pressed }) => [styles.scenePickerRow, item.id === selectedId && styles.scenePickerRowActive, pressed && styles.scenePickerRowPressed]}><Avatar character={item} size={42} /><View style={{ flex: 1 }}><Text style={styles.scenePickerName}>{item.name}</Text><Text style={styles.scenePickerSubtitle} numberOfLines={1}>{item.title || item.description || "Персонаж"}</Text></View>{item.id === selectedId ? <MaterialIcons name="check-circle" size={21} color={colors.accentHover} /> : null}</Pressable>} /></View></View></Modal></View>;
}

function FormattedMessageText({ content, mine = false, appearance = defaultChatAppearance }: { content: string; mine?: boolean; appearance?: ChatAppearanceSettings }) {
  const parts = content.split(/(<think\b[^>]*>[\s\S]*?<\/think>|\*[^*\n]+\*|«[^»\n]+»|"[^"\n]+")/gi);
  return <Text style={[styles.messageText, mine && styles.messageTextMine]}>{parts.map((part, index) => {
    if (/^<think\b/i.test(part)) return <Text key={index} style={[styles.messageThought, { color: appearance.thoughtColor }]}>{appearance.stripThoughtMarkers ? part.replace(/<\/?think\b[^>]*>/gi, "") : part}</Text>;
    if (/^\*/.test(part)) return <Text key={index} style={[styles.messageAction, { color: appearance.actionColor }]}>{appearance.stripActionMarkers ? part.slice(1, -1) : part}</Text>;
    if (/^(«|\")/.test(part)) return <Text key={index} style={[styles.messageSpeech, { color: appearance.speechColor }]}>{appearance.stripSpeechMarkers ? part.slice(1, -1) : part}</Text>;
    return <Text key={index}>{part}</Text>;
  })}</Text>;
}

function MessageBubble({ message, appearance }: { message: ChatMessage; appearance: ChatAppearanceSettings }) {
  const mine = message.role === "user";
  return (
    <View style={[styles.bubbleRow, mine && styles.bubbleRowMine]}>
      <View style={[styles.bubble, mine ? styles.bubbleMine : styles.bubbleTheirs]}>
        {!mine && message.author ? <Text style={styles.messageAuthor}>{message.author}</Text> : null}
        <FormattedMessageText content={message.content} mine={mine} appearance={appearance} />
        <Text style={[styles.messageTime, mine && styles.messageTimeMine]}>{formatTime(message.createdAt)}</Text>
      </View>
    </View>
  );
}

function ChatsScreen({ api, appearance, isVisible, onThreadChange }: { api: SoulExeApi; appearance: ChatAppearanceSettings; isVisible: boolean; onThreadChange: (open: boolean) => void }) {
  const [entries, setEntries] = useState<MobileChatEntry[]>([]);
  const [sceneEntries, setSceneEntries] = useState<MobileSceneEntry[]>([]);
  const [conversationEntries, setConversationEntries] = useState<MobileConversationEntry[] | null>(null);
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
  const keyboardLift = useAndroidKeyboardLift();

  const loadList = useCallback(async (quiet = false) => {
    if (!quiet) setBusy(true);
    try {
      const characters = await api.getCharacters();
      try {
        const page = await api.getConversationPage({ limit: 100, take: 1 });
        const unified = sortConversationRows(page.items.map(toConversationListRow))
          .map((row) => toMobileConversationEntry(page.items.find((conversation) => conversation.id === row.id)!, characters));
        const directEntries = unified
          .filter((entry) => entry.row.kind === "direct" && entry.character)
          .map((entry) => ({
            id: `${entry.character!.id}:${entry.conversation.id}`,
            character: entry.character!,
            chat: { id: entry.conversation.id, name: entry.conversation.name || entry.row.subtitle, updatedAt: entry.row.updatedAt },
            preview: entry.row.preview,
            previewAt: entry.row.updatedAt,
          }));
        setConversationEntries((current) => current && conversationEntryListFingerprint(current) === conversationEntryListFingerprint(unified) ? current : unified);
        setEntries((current) => chatEntryListFingerprint(current) === chatEntryListFingerprint(directEntries) ? current : directEntries);
        setActive((current) => {
          if (!current) return current;
          const fresh = directEntries.find((entry) => entry.id === current.id);
          return !fresh || activeChatIdentityFingerprint(current) === activeChatIdentityFingerprint(fresh) ? current : fresh;
        });
        return;
      } catch {
        // Windows clients released before the Conversation API continue through the existing routes.
        setConversationEntries(null);
      }

      const scenes = await api.getScenes().catch(() => []);
      const grouped = await Promise.all(characters.map(async (character) => ({ character, chats: await api.getChats(character.id) })));
      const bare: MobileChatEntry[] = grouped.flatMap(({ character, chats }) => chats.map((chat) => ({ id: `${character.id}:${chat.id}`, character, chat })));
      const withPreview = await Promise.all(bare.map(async (entry) => {
        try {
          const stored = await api.getMessages(entry.character.id, entry.chat.id);
          const last = stored[stored.length - 1];
          return { ...entry, preview: last?.content, previewAt: last?.createdAt };
        } catch { return entry; }
      }));
      withPreview.sort((a, b) => new Date(b.previewAt || b.chat.updatedAt || 0).getTime() - new Date(a.previewAt || a.chat.updatedAt || 0).getTime());
      setEntries((current) => chatEntryListFingerprint(current) === chatEntryListFingerprint(withPreview) ? current : withPreview);
      setActive((current) => {
        if (!current) return current;
        const fresh = withPreview.find((entry) => entry.id === current.id);
        return !fresh || activeChatIdentityFingerprint(current) === activeChatIdentityFingerprint(fresh) ? current : fresh;
      });
      const sceneWithPreview = await Promise.all(scenes.map(async (scene) => {
        try {
          const full = await api.getScene(scene.id);
          const last = full.messages.at(-1);
          return { id: scene.id, scene: { ...scene, characterA: full.characterA ?? scene.characterA, characterB: full.characterB ?? scene.characterB }, preview: last?.content, previewAt: last?.createdAt || scene.updatedAt };
        } catch { return { id: scene.id, scene, previewAt: scene.updatedAt }; }
      }));
      setSceneEntries((current) => sceneEntryListFingerprint(current) === sceneEntryListFingerprint(sceneWithPreview) ? current : sceneWithPreview);
    } finally { if (!quiet) setBusy(false); }
  }, [api]);
  const activeCharacterId = active?.character.id;
  const activeChatId = active?.chat.id;
  const loadMessages = useCallback(async () => {
    if (!activeCharacterId || !activeChatId || !isVisible) return;
    shouldInitialScroll.current = true;
    stickToBottom.current = true;
    setHistoryLoaded(false);
    setHistoryReady(false);
    setMessages([]);
    const loaded = await api.getMessages(activeCharacterId, activeChatId);
    setMessages(loaded);
    setHistoryLoaded(true);
  }, [activeCharacterId, activeChatId, api, isVisible]);
  useEffect(() => { loadList().catch((error) => Alert.alert("Чаты", error instanceof Error ? error.message : "Ошибка сети")); }, [loadList]);
  useEffect(() => { loadMessages().catch((error) => Alert.alert("Чат", error instanceof Error ? error.message : "Ошибка сети")); }, [loadMessages]);
  useEffect(() => {
    if (!isVisible) return;
    const timer = setInterval(() => { void loadList(true).catch(() => undefined); }, 2500);
    return () => clearInterval(timer);
  }, [isVisible, loadList]);
  useEffect(() => {
    if (!activeCharacterId || !activeChatId || !isVisible || busy) return;
    let disposed = false;
    const refreshActiveHistory = async () => {
      try {
        const fresh = await api.getMessages(activeCharacterId, activeChatId);
        if (!disposed) setMessages((current) => chatFingerprint(current) === chatFingerprint(fresh) ? current : fresh);
      } catch { /* The next refresh pass retries without interrupting an open chat. */ }
    };
    const timer = setInterval(() => { void refreshActiveHistory(); }, 1500);
    return () => { disposed = true; clearInterval(timer); };
  }, [activeCharacterId, activeChatId, api, busy, isVisible]);
  useEffect(() => {
    if (isVisible) return;
    setActive(undefined); setSceneId(undefined); setProfileOpen(false); setProfileEditing(false); setNewChatOpen(false); setNewSceneOpen(false); setCreationPicker(false);
  }, [isVisible]);
  useEffect(() => { onThreadChange(isVisible && Boolean(active || sceneId || newChatOpen || newSceneOpen || creationPicker)); }, [active, sceneId, newChatOpen, newSceneOpen, creationPicker, isVisible, onThreadChange]);
  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (!isVisible) return false;
      if (profileEditing) { setProfileEditing(false); return true; }
      if (profileOpen) { setProfileOpen(false); return true; }
      if (newChatOpen) { setNewChatOpen(false); return true; }
      if (newSceneOpen) { setNewSceneOpen(false); return true; }
      if (creationPicker) { setCreationPicker(false); return true; }
      if (sceneId) { setSceneId(undefined); return true; }
      if (active) { setActive(undefined); return true; }
      return false;
    });
    return () => subscription.remove();
  }, [active, creationPicker, isVisible, newChatOpen, newSceneOpen, profileEditing, profileOpen, sceneId]);
  useEffect(() => {
    if (!active || !messages.length || !stickToBottom.current) return;
    requestAnimationFrame(() => history.current?.scrollToEnd({ animated: !shouldInitialScroll.current }));
  }, [active?.id, messages.length, messages.at(-1)?.content]);

  const openNewChat = async () => {
    try {
      const characters = await api.getCharacters();
      if (!characters.length) { Alert.alert("Новый чат", "Сначала создайте персонажа."); return; }
      setNewChatCharacters(characters);
      setNewChatCharacterId(characters[0].id);
      setNewChatName("");
      setNewChatOpen(true);
    } catch (error) { Alert.alert("Новый чат", error instanceof Error ? error.message : "Ошибка сети"); }
  };
  const createChat = async () => {
    const character = newChatCharacters.find((item) => item.id === newChatCharacterId);
    if (!character) return;
    try {
      const chat = await api.createChat(character.id, newChatName.trim() || "Новый чат");
      const entry = { id: `${character.id}:${chat.id}`, character, chat };
      setEntries((current) => [entry, ...current]);
      setNewChatOpen(false);
      setActive(entry);
    } catch (error) { Alert.alert("Новый чат", error instanceof Error ? error.message : "Ошибка сети"); }
  };
  const send = async () => {
    if (!active || !draft.trim() || busy) return;
    const outgoing = draft.trim();
    const optimisticId = `local-user-${Date.now()}`;
    const replyId = `local-assistant-${Date.now()}`;
    stickToBottom.current = true;
    setMessages((current) => [...current, { id: optimisticId, role: "user", author: "Вы", content: outgoing, createdAt: new Date().toISOString() }]);
    setDraft(""); setBusy(true); setTyping(true);
    try {
      const result = await api.sendMessage(active.character.id, active.chat.id, outgoing);
      setTyping(false);
      setMessages((current) => [...current, { id: replyId, role: "assistant", author: active.character.name, content: "", createdAt: new Date().toISOString() }]);
      await revealText(result.reply || "Модель не вернула текст.", (content) => setMessages((current) => current.map((message) => message.id === replyId ? { ...message, content } : message)));
      try { const fresh = await api.getMessages(active.character.id, active.chat.id); if (fresh.some((message) => message.content === result.reply)) setMessages(fresh); } catch { /* keep visible answer */ }
    } catch (error) {
      setMessages((current) => current.filter((message) => message.id !== optimisticId));
      Alert.alert("Не удалось отправить", error instanceof Error ? error.message : "Ошибка сети");
    } finally { setTyping(false); setBusy(false); }
  };

  if (creationPicker) return <NewConversationChoiceScreen onBack={() => setCreationPicker(false)} onChat={() => { setCreationPicker(false); void openNewChat(); }} onScene={() => { setCreationPicker(false); setNewSceneOpen(true); }} />;
  if (newChatOpen) return <NewChatScreen characters={newChatCharacters} characterId={newChatCharacterId} name={newChatName} busy={busy} onCharacterChange={setNewChatCharacterId} onNameChange={setNewChatName} onBack={() => setNewChatOpen(false)} onCreate={() => void createChat()} />;
  if (newSceneOpen) return <NewSceneScreen api={api} onBack={() => setNewSceneOpen(false)} onCreated={() => { setNewSceneOpen(false); void loadList(); }} />;
  if (sceneId) return <ScenesScreen api={api} appearance={appearance} onThreadChange={onThreadChange} initialSceneId={sceneId} onBackToChats={() => setSceneId(undefined)} />;
  const threads: MobileThreadEntry[] = [
    ...entries.map((value) => ({ kind: "chat" as const, value })),
    ...sceneEntries.map((value) => ({ kind: "scene" as const, value })),
  ].sort((a, b) => new Date((b.value.previewAt || (b.kind === "chat" ? b.value.chat.updatedAt : b.value.scene.updatedAt)) || 0).getTime() - new Date((a.value.previewAt || (a.kind === "chat" ? a.value.chat.updatedAt : a.value.scene.updatedAt)) || 0).getTime());
  if (!active && conversationEntries) return <View style={styles.grow}><FlatList data={conversationEntries} keyExtractor={(item) => item.conversation.id} contentContainerStyle={styles.dialogListWithFab} refreshing={busy} onRefresh={loadList} renderItem={({ item }) => <MessengerRow title={item.row.title} subtitle={formatMessagePreview(item.row.preview || item.row.subtitle, appearance)} updatedAt={item.row.updatedAt} character={item.row.kind === "direct" ? item.character : undefined} sceneCharacters={item.row.kind === "scene" ? item.sceneCharacters : undefined} status={item.conversation.turnState?.status} onPress={() => { if (item.row.kind === "scene") setSceneId(item.conversation.id); else if (item.character) setActive({ id: `${item.character.id}:${item.conversation.id}`, character: item.character, chat: { id: item.conversation.id, name: item.conversation.name || item.row.subtitle, updatedAt: item.row.updatedAt }, preview: item.row.preview, previewAt: item.row.updatedAt }); }} />} ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} style={{ marginTop: 40 }} /> : <EmptyState icon="chat-bubble-outline" title="Переписок пока нет" caption="Нажмите кнопку внизу, чтобы создать чат или сцену." />} /><FloatingCreateButton icon="edit" onPress={() => setCreationPicker(true)} accessibilityLabel="Создать чат или сцену" /></View>;
  if (!active) return <View style={styles.grow}><FlatList data={threads} keyExtractor={(item) => `${item.kind}:${item.value.id}`} contentContainerStyle={styles.dialogListWithFab} refreshing={busy} onRefresh={loadList} renderItem={({ item }) => item.kind === "chat" ? <MessengerRow title={item.value.character.name} subtitle={formatMessagePreview(item.value.preview || item.value.chat.name, appearance)} updatedAt={item.value.previewAt || item.value.chat.updatedAt} character={item.value.character} onPress={() => setActive(item.value)} /> : <MessengerRow title={item.value.scene.name} subtitle={formatMessagePreview(item.value.preview || [item.value.scene.characterA?.name, item.value.scene.characterB?.name].filter(Boolean).join(" × ") || "Участники сцены", appearance)} updatedAt={item.value.previewAt || item.value.scene.updatedAt} sceneCharacters={[item.value.scene.characterA, item.value.scene.characterB]} status={item.value.scene.status} onPress={() => setSceneId(item.value.scene.id)} />} ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} style={{ marginTop: 40 }} /> : <EmptyState icon="chat-bubble-outline" title="Переписок пока нет" caption="Нажмите кнопку внизу, чтобы создать чат или сцену." />} /><FloatingCreateButton icon="edit" onPress={() => setCreationPicker(true)} accessibilityLabel="Создать чат или сцену" /></View>;

  if (profileOpen && profileEditing) return <CharacterEditorScreen api={api} character={active.character} onBack={() => setProfileEditing(false)} onSaved={(character) => { setActive((current) => current ? { ...current, character } : current); setEntries((current) => current.map((entry) => entry.character.id === character.id ? { ...entry, character } : entry)); setProfileEditing(false); setProfileOpen(false); }} />;
  if (profileOpen) return <CharacterProfilePreview character={active.character} chatName={active.chat.name} onBack={() => setProfileOpen(false)} onEdit={() => setProfileEditing(true)} />;
  return (
    <KeyboardAvoidingView style={styles.grow} behavior={Platform.OS === "ios" ? "padding" : undefined} keyboardVerticalOffset={0}>
      <View style={[styles.grow, keyboardLift > 0 && { paddingBottom: keyboardLift }]}>
      <MessengerThreadHeader
        title={active.character.name}
        subtitle={typing ? "Печатает…" : lastSeenLabel(messages)}
        character={active.character}
        onBack={() => setActive(undefined)}
        onTitlePress={() => setProfileOpen(true)}
      />
      {historyLoaded ? (
        <View style={styles.grow}>
        <FlatList
          key={active.id}
          ref={history}
          style={[styles.grow, !historyReady && styles.historyHidden]}
          keyboardDismissMode="interactive"
          keyboardShouldPersistTaps="handled"
          data={messages}
          keyExtractor={(item, index) => item.id || `${item.createdAt}-${index}`}
          contentContainerStyle={styles.messagesList}
          initialNumToRender={Math.max(messages.length, 1)}
          maxToRenderPerBatch={Math.max(messages.length, 1)}
          windowSize={7}
          onContentSizeChange={() => {
            if (shouldInitialScroll.current) {
              let attempts = 0;
              const settleAtLatest = () => {
                history.current?.scrollToEnd({ animated: false });
                if (++attempts < 4) requestAnimationFrame(settleAtLatest);
                else { shouldInitialScroll.current = false; setHistoryReady(true); }
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
          scrollEventThrottle={16}
          renderItem={({ item, index }) => <>{needsDateDivider(messages, index) ? <ChatDateDivider value={item.createdAt} /> : null}<MessageBubble message={item} appearance={appearance} /></>}
          ListFooterComponent={typing ? <View style={styles.typingRow}><ActivityIndicator size="small" color={colors.accentHover} /><Text style={styles.typingText}>{active.character.name} печатает…</Text></View> : null}
          ListEmptyComponent={<Text style={styles.listHint}>Напишите первое сообщение</Text>}
          onScrollToIndexFailed={() => requestAnimationFrame(() => history.current?.scrollToEnd({ animated: false }))}
        />
        {!historyReady ? <View style={styles.historyLoadingOverlay}><ActivityIndicator color={colors.accentHover} /></View> : null}
        </View>
      ) : <View style={styles.historyLoading}><ActivityIndicator color={colors.accentHover} /></View>}
      <MessageComposer value={draft} onChangeText={setDraft} placeholder="Сообщение" leftAction={{ icon: "star-outline", onPress: () => setDraft((current) => current.trim() ? `*${current}*` : "*"), accessibilityLabel: "Оформить как действие" }} onSend={send} sendDisabled={busy} />
      </View>
    </KeyboardAvoidingView>
  );
}

function ConversationSectionSwitch({ value, onChange }: { value: "dialogs" | "scenes"; onChange: (value: "dialogs" | "scenes") => void }) {
  return <View style={styles.conversationSwitch}><Pressable onPress={() => onChange("dialogs")} style={[styles.conversationSwitchItem, value === "dialogs" && styles.conversationSwitchItemActive]}><MaterialIcons name="chat-bubble-outline" size={17} color={value === "dialogs" ? colors.text : colors.muted} /><Text style={[styles.conversationSwitchText, value === "dialogs" && styles.conversationSwitchTextActive]}>Диалоги</Text></Pressable><Pressable onPress={() => onChange("scenes")} style={[styles.conversationSwitchItem, value === "scenes" && styles.conversationSwitchItemActive]}><MaterialIcons name="auto-awesome" size={17} color={value === "scenes" ? colors.text : colors.muted} /><Text style={[styles.conversationSwitchText, value === "scenes" && styles.conversationSwitchTextActive]}>Сцены</Text></Pressable></View>;
}

function NewConversationChoiceScreen({ onBack, onChat, onScene }: { onBack: () => void; onChat: () => void; onScene: () => void }) {
  return <View style={styles.grow}><MessengerThreadHeader title="Создать" subtitle="Выберите тип переписки" onBack={onBack} /><View style={styles.creationChoiceList}><Pressable onPress={onChat} style={({ pressed }) => [styles.creationChoice, pressed && styles.creationChoicePressed]}><View style={styles.creationChoiceIcon}><MaterialIcons name="chat-bubble-outline" size={23} color={colors.accentHover} /></View><View style={{ flex: 1 }}><Text style={styles.creationChoiceTitle}>Чат с персонажем</Text><Text style={styles.creationChoiceSubtitle}>Выберите персонажа и назовите диалог</Text></View><MaterialIcons name="chevron-right" size={22} color={colors.dim} /></Pressable><Pressable onPress={onScene} style={({ pressed }) => [styles.creationChoice, pressed && styles.creationChoicePressed]}><View style={styles.creationChoiceIcon}><MaterialIcons name="auto-awesome" size={23} color={colors.accentHover} /></View><View style={{ flex: 1 }}><Text style={styles.creationChoiceTitle}>Сцена</Text><Text style={styles.creationChoiceSubtitle}>Два участника, сценарий и правила развития</Text></View><MaterialIcons name="chevron-right" size={22} color={colors.dim} /></Pressable></View></View>;
}

function NewSceneScreen({ api, onBack, onCreated }: { api: SoulExeApi; onBack: () => void; onCreated: () => void }) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [characterAId, setCharacterAId] = useState("");
  const [characterBId, setCharacterBId] = useState("");
  const [name, setName] = useState("Новая сцена");
  const [scenario, setScenario] = useState("");
  const [location, setLocation] = useState("");
  const [timeContext, setTimeContext] = useState("");
  const [mood, setMood] = useState("");
  const [goal, setGoal] = useState("");
  const [relationshipContext, setRelationshipContext] = useState("");
  const [turnMode, setTurnMode] = useState<"alternate" | "manual">("alternate");
  const [delaySeconds, setDelaySeconds] = useState("10");
  const [enforceSceneContract, setEnforceSceneContract] = useState(true);
  const [advanceSceneAndAvoidRepetition, setAdvanceSceneAndAvoidRepetition] = useState(true);
  const [busy, setBusy] = useState(false);
  useEffect(() => { api.getCharacters().then((items) => { setCharacters(items); setCharacterAId(items[0]?.id || ""); setCharacterBId(items[1]?.id || items[0]?.id || ""); }).catch((error) => Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети")); }, [api]);
  const create = async () => {
    if (!characterAId || !characterBId || characterAId === characterBId) { Alert.alert("Сцена", "Выберите двух разных участников."); return; }
    setBusy(true);
    try { await api.createScene({ characterAId, characterBId, name: name.trim() || "Новая сцена", scenario: scenario.trim(), location: location.trim(), timeContext: timeContext.trim(), mood: mood.trim(), goal: goal.trim(), relationshipContext: relationshipContext.trim(), turnMode, delaySeconds: Math.max(0, Number(delaySeconds) || 0), enforceSceneContract, advanceSceneAndAvoidRepetition }); onCreated(); }
    catch (error) { Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.newSceneScroll} keyboardShouldPersistTaps="handled"><MessengerThreadHeader title="Новая сцена" subtitle="Настройте участников и ход истории" onBack={onBack} /><View style={styles.newSceneContent}><Field label="Название" value={name} onChangeText={setName} placeholder="Например, Тайна старого маяка" /><SceneCharacterPicker label="Первый участник" characters={characters} selectedId={characterAId} excludeId={characterBId} onSelect={setCharacterAId} /><SceneCharacterPicker label="Второй участник" characters={characters} selectedId={characterBId} excludeId={characterAId} onSelect={setCharacterBId} /><Field label="Сценарий" value={scenario} onChangeText={setScenario} placeholder="Что происходит и с чего начинается сцена" multiline style={styles.largeField} /><Field label="Место" value={location} onChangeText={setLocation} placeholder="Например, заброшенный маяк у моря" /><Field label="Время и контекст" value={timeContext} onChangeText={setTimeContext} placeholder="Например, поздний вечер после шторма" /><Field label="Настроение" value={mood} onChangeText={setMood} placeholder="Например, тревожное, но тёплое" /><Field label="Цель сцены" value={goal} onChangeText={setGoal} placeholder="Что должно измениться или открыться" /><Field label="Отношения участников" value={relationshipContext} onChangeText={setRelationshipContext} placeholder="Например, союзники, но ещё не доверяют друг другу" multiline style={styles.largeField} /><Text style={styles.sceneOptionLabel}>РЕЖИМ ХОДОВ</Text><View style={styles.sceneTurnModeRow}><Button title="По очереди" variant={turnMode === "alternate" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("alternate")} /><Button title="Вручную" variant={turnMode === "manual" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("manual")} /></View><Field label="Пауза между репликами (сек.)" value={delaySeconds} onChangeText={setDelaySeconds} keyboardType="numeric" placeholder="10; 0 — вручную" /><MarkerToggleRow label="Соблюдать рамки сцены" description="Учитывать заданные место, отношения и цель" value={enforceSceneContract} onChange={setEnforceSceneContract} /><MarkerToggleRow label="Развивать сцену" description="Добавлять новые детали и избегать повторов" value={advanceSceneAndAvoidRepetition} onChange={setAdvanceSceneAndAvoidRepetition} /><Button title={busy ? "Создаю…" : "Создать сцену"} icon="auto-awesome" disabled={busy || characters.length < 2} loading={busy} onPress={() => void create()} style={{ marginTop: 8 }} /></View></ScrollView>;
}

function NewChatScreen({ characters, characterId, name, busy, onCharacterChange, onNameChange, onBack, onCreate }: { characters: SoulCharacter[]; characterId: string; name: string; busy: boolean; onCharacterChange: (id: string) => void; onNameChange: (value: string) => void; onBack: () => void; onCreate: () => void }) {
  return <View style={styles.grow}><MessengerThreadHeader title="Новый чат" subtitle="Выберите персонажа и название" onBack={onBack} /><FlatList data={characters} keyExtractor={(item) => item.id} contentContainerStyle={styles.newChatList} ListHeaderComponent={<View><Field label="Название диалога" value={name} onChangeText={onNameChange} placeholder="Например, Вечер в кафе" /><Text style={styles.selectorLabel}>ПЕРСОНАЖ</Text></View>} renderItem={({ item }) => <Pressable onPress={() => onCharacterChange(item.id)} style={[styles.choiceRow, item.id === characterId && styles.choiceRowActive]}><Avatar character={item} size={42} /><View style={{ flex: 1 }}><Text style={styles.characterName}>{item.name}</Text><Text numberOfLines={1} style={styles.chatMeta}>{item.title || item.description || "Персонаж"}</Text></View>{item.id === characterId ? <MaterialIcons name="check-circle" size={22} color={colors.accentHover} /> : null}</Pressable>} ListFooterComponent={<Button title={busy ? "Создаю…" : "Создать чат"} icon="chat" disabled={busy || !characterId} loading={busy} onPress={onCreate} style={{ marginTop: 16 }} />} /></View>;
}

function CharacterProfilePreview({ character, chatName, onBack, onEdit }: { character: SoulCharacter; chatName?: string; onBack: () => void; onEdit: () => void }) {
  return <ScrollView contentContainerStyle={styles.profileScroll}><MessengerThreadHeader title="Профиль" onBack={onBack} onEdit={onEdit} /><View style={styles.profileHero}><Avatar character={character} size={96} /><Text style={styles.profileName}>{character.name}</Text><Text style={styles.profileTitle}>{character.title || "Персонаж SoulExe"}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ДИАЛОГ</Text><Text style={styles.profileValue}>{chatName || "Без названия"}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ОПИСАНИЕ</Text><Text style={styles.profileValue}>{character.description || "Описание ещё не заполнено."}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ЛИЧНОСТЬ</Text><Text style={styles.profileValue}>{character.personality || "Черты личности ещё не заполнены."}</Text></View></ScrollView>;
}

function SceneProfilePreview({ scene, onBack, onEdit }: { scene: SoulSceneSummary | SoulScene; onBack: () => void; onEdit: () => void }) {
  const messageCount = "messages" in scene ? scene.messages.length : 0;
  const full = "messages" in scene ? scene : undefined;
  const details = [["Место", full?.location], ["Время и контекст", full?.timeContext], ["Настроение", full?.mood], ["Цель сцены", full?.goal], ["Отношения участников", full?.relationshipContext]].filter(([, value]) => Boolean(value));
  return <ScrollView contentContainerStyle={styles.profileScroll}><MessengerThreadHeader title="О сцене" onBack={onBack} onEdit={onEdit} /><View style={styles.profileHero}><View style={styles.sceneProfileEmblem}><MaterialIcons name="auto-awesome" size={38} color={colors.accentHover} /></View><Text style={styles.profileName}>{scene.name}</Text><Text style={styles.profileTitle}>{statusLabel(scene.status)} · {messageCount} реплик</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>УЧАСТНИКИ</Text>{scene.characterA ? <View style={styles.sceneParticipantRow}><Avatar character={scene.characterA} size={38} /><View><Text style={styles.sceneParticipantName}>{scene.characterA.name}</Text><Text style={styles.chatMeta}>{scene.characterA.title || "Персонаж"}</Text></View></View> : null}{scene.characterB ? <View style={styles.sceneParticipantRow}><Avatar character={scene.characterB} size={38} /><View><Text style={styles.sceneParticipantName}>{scene.characterB.name}</Text><Text style={styles.chatMeta}>{scene.characterB.title || "Персонаж"}</Text></View></View> : null}</View><View style={styles.profileCard}><Text style={styles.profileLabel}>СЦЕНАРИЙ</Text><Text style={styles.profileValue}>{full?.scenario || "Сценарий пока не задан."}</Text></View>{details.length ? <View style={styles.profileCard}><Text style={styles.profileLabel}>ПАРАМЕТРЫ СЦЕНЫ</Text>{details.map(([label, value]) => <View key={label} style={styles.sceneDetailRow}><Text style={styles.sceneDetailLabel}>{label}</Text><Text style={styles.profileValue}>{value}</Text></View>)}</View> : null}<View style={styles.profileCard}><Text style={styles.profileLabel}>РЕЖИМ И СОСТОЯНИЕ</Text><Text style={styles.profileValue}>{full?.turnMode === "manual" ? "Ручная очередность ходов" : "Автоматическая очередность ходов"}{full?.delaySeconds ? ` · пауза ${full.delaySeconds} сек.` : ""}</Text>{full ? <><Text style={[styles.profileValue, styles.sceneStateText]}>Рамки сцены: {full.enforceSceneContract ? "соблюдаются" : "свободное развитие"}</Text><Text style={[styles.profileValue, styles.sceneStateText]}>Развитие сцены: {full.advanceSceneAndAvoidRepetition ? "включено" : "выключено"}</Text></> : null}<Text style={[styles.profileValue, styles.sceneStateText]}>{statusLabel(scene.status)}{scene.updatedAt ? ` · обновлено в ${formatTime(scene.updatedAt)}` : ""}</Text></View></ScrollView>;
}

function SceneEditorScreen({ api, scene, onBack, onSaved }: { api: SoulExeApi; scene: SoulScene; onBack: () => void; onSaved: (scene: SoulScene) => void }) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [characterAId, setCharacterAId] = useState(scene.characterA?.id || "");
  const [characterBId, setCharacterBId] = useState(scene.characterB?.id || "");
  const [name, setName] = useState(scene.name);
  const [scenario, setScenario] = useState(scene.scenario || "");
  const [location, setLocation] = useState(scene.location || "");
  const [timeContext, setTimeContext] = useState(scene.timeContext || "");
  const [mood, setMood] = useState(scene.mood || "");
  const [goal, setGoal] = useState(scene.goal || "");
  const [relationshipContext, setRelationshipContext] = useState(scene.relationshipContext || "");
  const [turnMode, setTurnMode] = useState<"alternate" | "manual">(scene.turnMode === "manual" ? "manual" : "alternate");
  const [delaySeconds, setDelaySeconds] = useState(String(scene.delaySeconds ?? 10));
  const [enforceSceneContract, setEnforceSceneContract] = useState(scene.enforceSceneContract ?? true);
  const [advanceSceneAndAvoidRepetition, setAdvanceSceneAndAvoidRepetition] = useState(scene.advanceSceneAndAvoidRepetition ?? true);
  const [busy, setBusy] = useState(false);
  useEffect(() => { api.getCharacters().then(setCharacters).catch((error) => Alert.alert("Сцена", error instanceof Error ? error.message : "Не удалось загрузить персонажей.")); }, [api]);
  const save = async () => {
    if (!characterAId || !characterBId || characterAId === characterBId) { Alert.alert("Сцена", "Выберите двух разных участников."); return; }
    setBusy(true);
    try { onSaved(await api.updateScene(scene.id, { characterAId, characterBId, name: name.trim() || "Сцена", scenario: scenario.trim(), location: location.trim(), timeContext: timeContext.trim(), mood: mood.trim(), goal: goal.trim(), relationshipContext: relationshipContext.trim(), turnMode, delaySeconds: Math.max(0, Number(delaySeconds) || 0), enforceSceneContract, advanceSceneAndAvoidRepetition })); }
    catch (error) { Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.newSceneScroll} keyboardShouldPersistTaps="handled"><MessengerThreadHeader title="Параметры сцены" subtitle="Изменения сохраняются на ПК" onBack={onBack} /><View style={styles.newSceneContent}><Field label="Название" value={name} onChangeText={setName} placeholder="Название сцены" /><SceneCharacterPicker label="Первый участник" characters={characters} selectedId={characterAId} excludeId={characterBId} onSelect={setCharacterAId} /><SceneCharacterPicker label="Второй участник" characters={characters} selectedId={characterBId} excludeId={characterAId} onSelect={setCharacterBId} /><Field label="Сценарий" value={scenario} onChangeText={setScenario} placeholder="Что происходит и с чего начинается сцена" multiline style={styles.largeField} /><Field label="Место" value={location} onChangeText={setLocation} placeholder="Место сцены" /><Field label="Время и контекст" value={timeContext} onChangeText={setTimeContext} placeholder="Время и текущая ситуация" /><Field label="Настроение" value={mood} onChangeText={setMood} placeholder="Настроение сцены" /><Field label="Цель сцены" value={goal} onChangeText={setGoal} placeholder="Что должно измениться или открыться" /><Field label="Отношения участников" value={relationshipContext} onChangeText={setRelationshipContext} placeholder="Общие отношения и контекст" multiline style={styles.largeField} /><Text style={styles.sceneOptionLabel}>РЕЖИМ ХОДОВ</Text><View style={styles.sceneTurnModeRow}><Button title="По очереди" variant={turnMode === "alternate" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("alternate")} /><Button title="Вручную" variant={turnMode === "manual" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("manual")} /></View><Field label="Пауза между репликами (сек.)" value={delaySeconds} onChangeText={setDelaySeconds} keyboardType="numeric" placeholder="10; 0 — вручную" /><MarkerToggleRow label="Соблюдать рамки сцены" description="Учитывать заданные место, отношения и цель" value={enforceSceneContract} onChange={setEnforceSceneContract} /><MarkerToggleRow label="Развивать сцену" description="Добавлять новые детали и избегать повторов" value={advanceSceneAndAvoidRepetition} onChange={setAdvanceSceneAndAvoidRepetition} /><Button title={busy ? "Сохраняю…" : "Сохранить параметры"} loading={busy} disabled={busy || characters.length < 2} onPress={() => void save()} style={{ marginTop: 8 }} /></View></ScrollView>;
}

function ScenesScreen({ api, appearance, onThreadChange, initialSceneId, onBackToChats }: { api: SoulExeApi; appearance: ChatAppearanceSettings; onThreadChange: (open: boolean) => void; initialSceneId: string; onBackToChats: () => void }) {
  const [scene, setScene] = useState<SoulScene>();
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
  const loadScene = useCallback(async () => { shouldInitialScroll.current = true; stickToBottom.current = true; setSceneHistoryReady(false); setScene(await api.getScene(initialSceneId)); }, [api, initialSceneId]);
  useEffect(() => { onThreadChange(true); loadScene().catch((error) => Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети")); return () => onThreadChange(false); }, [loadScene, onThreadChange]);
  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (sceneEditing) { setSceneEditing(false); return true; }
      if (sceneInfoOpen) { setSceneInfoOpen(false); return true; }
      onBackToChats(); return true;
    });
    return () => subscription.remove();
  }, [onBackToChats, sceneEditing, sceneInfoOpen]);
  useEffect(() => {
    let disposed = false;
    const timer = setInterval(() => { api.getScene(initialSceneId).then((fresh) => { if (!disposed) setScene((current) => sceneFingerprint(current) === sceneFingerprint(fresh) ? current : fresh); }).catch(() => undefined); }, 1500);
    return () => { disposed = true; clearInterval(timer); };
  }, [initialSceneId, api]);
  useEffect(() => { const timer = setInterval(() => setClock(Date.now()), 1000); return () => clearInterval(timer); }, []);
  const action = async (name: "start" | "pause" | "next") => {
    if (!scene || busy) return;
    const beforeCount = scene?.messages.length ?? 0;
    setBusy(true); setSceneGenerating(name === "next");
    try {
      let updated = await api.sceneAction(scene.id, name);
      if (name === "next" && updated.messages.length <= beforeCount) {
        for (let attempt = 0; attempt < 90; attempt += 1) { await wait(1000); const fresh = await api.getScene(scene.id); if (fresh.messages.length > beforeCount) { updated = fresh; break; } }
      }
      stickToBottom.current = true;
      setScene(updated);
    } catch (error) { Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setSceneGenerating(false); setBusy(false); }
  };
  const addDirector = async () => {
    if (!scene || !directorText.trim() || busy) return;
    setBusy(true);
    try { stickToBottom.current = true; setScene(await api.addDirectorEvent(scene.id, directorText.trim())); setDirectorText(""); }
    catch (error) { Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  if (!scene) return <View style={styles.historyLoading}><ActivityIndicator color={colors.accentHover} /></View>;
  const currentScene = scene;
  const automaticDelay = currentScene.status === "running" && currentScene.turnMode !== "manual" ? currentScene.delaySeconds || 0 : 0;
  const scheduledTurnAt = currentScene.nextTurnAt ? new Date(currentScene.nextTurnAt).getTime() : undefined;
  const remaining = scheduledTurnAt !== undefined ? Math.max(0, Math.ceil((scheduledTurnAt - clock) / 1000)) : undefined;
  const sceneTimer = automaticDelay > 0 && remaining !== undefined ? `⌛ ${String(Math.floor(remaining / 60)).padStart(2, "0")}:${String(remaining % 60).padStart(2, "0")}` : undefined;
  if (sceneInfoOpen && sceneEditing) return <SceneEditorScreen api={api} scene={currentScene} onBack={() => setSceneEditing(false)} onSaved={(updated) => { setScene(updated); setSceneEditing(false); setSceneInfoOpen(false); }} />;
  if (sceneInfoOpen) return <SceneProfilePreview scene={currentScene} onBack={() => setSceneInfoOpen(false)} onEdit={() => setSceneEditing(true)} />;
  return <KeyboardAvoidingView style={styles.grow} behavior={Platform.OS === "ios" ? "padding" : undefined} keyboardVerticalOffset={0}>
    <View style={[styles.grow, keyboardLift > 0 && { paddingBottom: keyboardLift }]}>
    <MessengerThreadHeader title={currentScene.name} subtitle={[currentScene.characterA?.name, currentScene.characterB?.name].filter(Boolean).join(" × ") || "Сцена"} onBack={onBackToChats} onTitlePress={() => setSceneInfoOpen(true)} timer={sceneTimer} status={{ text: statusLabel(currentScene.status), tone: statusTone(currentScene.status) }} />
    <View style={styles.grow}>
      <FlatList ref={history} style={[styles.grow, !sceneHistoryReady && styles.historyHidden]} data={currentScene.messages} keyExtractor={(item, index) => `${item.createdAt}-${index}`} contentContainerStyle={styles.messagesList} initialNumToRender={Math.max(currentScene.messages.length, 1)} maxToRenderPerBatch={Math.max(currentScene.messages.length, 1)} windowSize={7} onContentSizeChange={() => { if (shouldInitialScroll.current) { let attempts = 0; const settleAtLatest = () => { history.current?.scrollToEnd({ animated: false }); if (++attempts < 4) requestAnimationFrame(settleAtLatest); else { shouldInitialScroll.current = false; setSceneHistoryReady(true); } }; requestAnimationFrame(settleAtLatest); return; } if (stickToBottom.current) requestAnimationFrame(() => history.current?.scrollToEnd({ animated: true })); }} onScroll={({ nativeEvent }) => { const distance = nativeEvent.contentSize.height - (nativeEvent.contentOffset.y + nativeEvent.layoutMeasurement.height); stickToBottom.current = distance < 64; }} scrollEventThrottle={16} renderItem={({ item, index }) => { const director = item.kind === "director" || item.author === "Режиссёр"; const secondParticipant = !director && item.speakerId === currentScene.characterB?.id; return <>{needsDateDivider(currentScene.messages, index) ? <ChatDateDivider value={item.createdAt} /> : null}<View style={[styles.bubbleRow, secondParticipant && styles.bubbleRowMine, director && styles.directorRowAlign]}><View style={[styles.bubble, secondParticipant ? styles.bubbleMine : styles.bubbleTheirs, director && styles.bubbleDirector]}><Text style={styles.messageAuthor}>{director ? "Режиссёр" : item.author || "Сцена"}</Text><FormattedMessageText content={item.content} mine={secondParticipant} appearance={appearance} /><Text style={[styles.messageTime, secondParticipant && styles.messageTimeMine]}>{formatTime(item.createdAt)}</Text></View></View></>; }} ListFooterComponent={sceneGenerating ? <View style={styles.typingRow}><ActivityIndicator size="small" color={colors.accentHover} /><Text style={styles.typingText}>Сцена формирует следующую реплику…</Text></View> : null} ListEmptyComponent={<Text style={styles.listHint}>Запустите сцену, чтобы появилась первая реплика</Text>} onScrollToIndexFailed={() => requestAnimationFrame(() => history.current?.scrollToEnd({ animated: false }))} />
      {!sceneHistoryReady ? <View style={styles.historyLoadingOverlay}><ActivityIndicator color={colors.accentHover} /></View> : null}
    </View>
    <MessageComposer value={directorText} onChangeText={setDirectorText} placeholder="Режиссёрское событие" leftAction={{ icon: currentScene.status === "running" ? "pause" : "play-arrow", onPress: () => void action(currentScene.status === "running" ? "pause" : "start"), disabled: busy, accessibilityLabel: currentScene.status === "running" ? "Поставить сцену на паузу" : "Запустить сцену" }} rightActions={[{ icon: "movie-creation", primary: true, onPress: addDirector, disabled: busy || !directorText.trim(), accessibilityLabel: "Добавить режиссёрское событие" }, { icon: "arrow-upward", primary: true, onPress: () => void action("next"), disabled: busy, accessibilityLabel: "Следующая реплика" }]} />
    </View>
  </KeyboardAvoidingView>;
}

function CharactersScreen({ api }: { api: SoulExeApi }) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [busy, setBusy] = useState(false);
  const [active, setActive] = useState<SoulCharacter>();
  const [creating, setCreating] = useState(false);
  const load = useCallback(async (quiet = false) => {
    if (!quiet) setBusy(true);
    try {
      const fresh = await api.getCharacters();
      setCharacters(fresh);
      setActive((current) => current ? fresh.find((item) => item.id === current.id) ?? current : current);
    } finally { if (!quiet) setBusy(false); }
  }, [api]);
  useEffect(() => { load().catch((error) => Alert.alert("Персонажи", error instanceof Error ? error.message : "Ошибка сети")); }, [load]);
  useEffect(() => { const timer = setInterval(() => { void load(true).catch(() => undefined); }, 3000); return () => clearInterval(timer); }, [load]);
  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (creating) { setCreating(false); return true; }
      if (active) { setActive(undefined); return true; }
      return false;
    });
    return () => subscription.remove();
  }, [active, creating]);
  if (creating) return <CharacterCreateScreen api={api} onBack={() => setCreating(false)} onCreated={(character) => { setCharacters((current) => [character, ...current]); setCreating(false); setActive(character); }} />;
  if (active) return <CharacterEditorScreen api={api} character={active} onBack={() => { setActive(undefined); void load(); }} onSaved={(character) => { setActive(character); setCharacters((current) => current.map((item) => item.id === character.id ? character : item)); }} />;
  return <View style={styles.grow}><FlatList data={characters} keyExtractor={(item) => item.id} contentContainerStyle={styles.characterListWithFab} refreshing={busy} onRefresh={load} ListEmptyComponent={busy ? <ActivityIndicator color={colors.accentHover} /> : <EmptyState icon="groups" title="Пусто" caption="Создайте персонажа кнопкой внизу." />} renderItem={({ item }) => <Pressable onPress={() => setActive(item)} style={styles.characterCard}><Avatar character={item} size={48} /><View style={{ flex: 1 }}><Text style={styles.characterName}>{item.name}</Text><Text style={styles.chatMeta} numberOfLines={2}>{item.title || item.description || "Карточка персонажа"}</Text></View><MaterialIcons name="edit" size={19} color={colors.dim} /></Pressable>} /><FloatingCreateButton icon="person-add" onPress={() => setCreating(true)} accessibilityLabel="Создать персонажа" /></View>;
}

function CharacterCreateScreen({ api, onBack, onCreated }: { api: SoulExeApi; onBack: () => void; onCreated: (character: SoulCharacter) => void }) {
  const [mode, setMode] = useState<"manual" | "generate">("manual");
  const [name, setName] = useState("");
  const [idea, setIdea] = useState("");
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    if (mode === "manual" && !name.trim()) { Alert.alert("Персонаж", "Укажите имя."); return; }
    if (mode === "generate" && !idea.trim()) { Alert.alert("Персонаж", "Опишите персонажа для генерации."); return; }
    setBusy(true);
    try { onCreated(mode === "manual" ? await api.createCharacter({ name: name.trim() }) : await api.generateCharacter(idea.trim())); }
    catch (error) { Alert.alert("Персонаж", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.editorScroll}><MessengerThreadHeader title="Новый персонаж" subtitle="Создайте сами или заполните через ИИ" onBack={onBack} /><View style={styles.modeRow}><Button title="Вручную" variant={mode === "manual" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setMode("manual")} /><Button title="Сгенерировать" variant={mode === "generate" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setMode("generate")} /></View>{mode === "manual" ? <Field label="Имя" value={name} onChangeText={setName} placeholder="Имя персонажа" /> : <Field label="Идея" value={idea} onChangeText={setIdea} placeholder="Кратко опишите персонажа по-русски" multiline style={styles.largeField} />}<Button title={busy ? "Создаю…" : mode === "manual" ? "Создать персонажа" : "Сгенерировать персонажа"} loading={busy} disabled={busy} onPress={() => void submit()} style={{ marginTop: 16 }} /></ScrollView>;
}

function CharacterEditorScreen({ api, character, onBack, onSaved }: { api: SoulExeApi; character: SoulCharacter; onBack: () => void; onSaved: (character: SoulCharacter) => void }) {
  const [draft, setDraft] = useState<SoulCharacterDraft>({ name: character.name, title: character.title || "", description: character.description || "", personality: character.personality || "", scenario: character.scenario || "", systemPrompt: character.systemPrompt || "", soulMemoryEnabled: character.soulMemoryEnabled, autoSummaryEnabled: character.autoSummaryEnabled });
  const [busy, setBusy] = useState(false);
  const update = <K extends keyof SoulCharacterDraft>(key: K, value: SoulCharacterDraft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const save = async () => { if (!draft.name.trim()) { Alert.alert("Персонаж", "Имя обязательно."); return; } setBusy(true); try { onSaved(await api.updateCharacter(character.id, draft)); } catch (error) { Alert.alert("Персонаж", error instanceof Error ? error.message : "Ошибка сети"); } finally { setBusy(false); } };
  const chooseAvatar = async () => {
    try {
      const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ImagePicker.MediaTypeOptions.Images, allowsEditing: true, aspect: [1, 1], quality: 0.85 });
      if (result.canceled || !result.assets[0]) return;
      setBusy(true);
      onSaved(await api.uploadCharacterAvatar(character.id, result.assets[0]));
    } catch (error) { Alert.alert("Аватар", error instanceof Error ? error.message : "Не удалось сохранить аватар."); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.editorScroll} keyboardShouldPersistTaps="handled"><MessengerThreadHeader title="Профиль персонажа" subtitle="Изменения сохраняются на ПК" character={character} onBack={onBack} /><View style={styles.profileHero}><Avatar character={character} size={84} /><Text style={styles.profileName}>{character.name}</Text><Button title="Изменить фото" icon="photo-library" variant="secondary" disabled={busy} onPress={() => void chooseAvatar()} style={styles.avatarUploadButton} /></View><Field label="Имя" value={draft.name} onChangeText={(value) => update("name", value)} /><Field label="Подзаголовок" value={draft.title || ""} onChangeText={(value) => update("title", value)} /><Field label="Описание" value={draft.description || ""} onChangeText={(value) => update("description", value)} multiline style={styles.largeField} /><Field label="Личность" value={draft.personality || ""} onChangeText={(value) => update("personality", value)} multiline style={styles.largeField} /><Field label="Сценарий" value={draft.scenario || ""} onChangeText={(value) => update("scenario", value)} multiline style={styles.largeField} /><Field label="Системный промпт" value={draft.systemPrompt || ""} onChangeText={(value) => update("systemPrompt", value)} multiline style={styles.largeField} /><Button title={busy ? "Сохраняю…" : "Сохранить"} loading={busy} disabled={busy} onPress={() => void save()} style={{ marginTop: 16 }} /></ScrollView>;
}

const markupPalette = ["#C8A6FF", "#73B7FF", "#FFD18A", "#FF9EBE", "#79D8B1", "#FFB86B", "#C2D2FF", "#F2A6FF"];

function MarkupColorRow({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return <View style={styles.markupSetting}><Text style={styles.markupSettingLabel}>{label}</Text><View style={styles.colorSwatches}>{markupPalette.map((color) => <Pressable key={color} onPress={() => onChange(color)} style={[styles.colorSwatch, { backgroundColor: color }, value === color && styles.colorSwatchSelected]}><MaterialIcons name="check" size={13} color="#0B0D15" /></Pressable>)}</View></View>;
}

function MarkerToggleRow({ label, description, value, onChange }: { label: string; description: string; value: boolean; onChange: (value: boolean) => void }) {
  return <Pressable onPress={() => onChange(!value)} style={({ pressed }) => [styles.markerToggle, pressed && styles.markerTogglePressed]}><View style={{ flex: 1 }}><Text style={styles.markerToggleTitle}>{label}</Text><Text style={styles.markerToggleSubtitle}>{description}</Text></View><View style={[styles.markerToggleTrack, value && styles.markerToggleTrackOn]}><View style={[styles.markerToggleThumb, value && styles.markerToggleThumbOn]} /></View></Pressable>;
}

function ChatAppearanceSettings({ appearance, onChange }: { appearance: ChatAppearanceSettings; onChange: (changes: Partial<ChatAppearanceSettings>) => void }) {
  return <Card style={{ gap: 12, marginBottom: 12 }}><Text style={styles.settingTitle}>Оформление сообщений</Text><Text style={styles.helper}>Цвета применяются сразу к чатам и сценам и сохраняются на этом устройстве.</Text><MarkupColorRow label="Действия *…*" value={appearance.actionColor} onChange={(actionColor) => onChange({ actionColor })} /><MarkupColorRow label="Мысли &lt;think&gt;…&lt;/think&gt;" value={appearance.thoughtColor} onChange={(thoughtColor) => onChange({ thoughtColor })} /><MarkupColorRow label={'Речь «…» / "…"'} value={appearance.speechColor} onChange={(speechColor) => onChange({ speechColor })} /><View style={styles.markupDivider} /><Text style={styles.markupGroupTitle}>Очистка визуальных маркеров</Text><MarkerToggleRow label="Убирать звёздочки" description="Показывать действие без символов *…*" value={appearance.stripActionMarkers} onChange={(stripActionMarkers) => onChange({ stripActionMarkers })} /><MarkerToggleRow label="Убирать теги мыслей" description="Скрывать &lt;think&gt; и &lt;/think&gt;" value={appearance.stripThoughtMarkers} onChange={(stripThoughtMarkers) => onChange({ stripThoughtMarkers })} /><MarkerToggleRow label="Убирать кавычки речи" description={'Показывать реплики без «…» и "…"'} value={appearance.stripSpeechMarkers} onChange={(stripSpeechMarkers) => onChange({ stripSpeechMarkers })} /><View style={styles.markupPreview}><Text style={styles.markupPreviewLabel}>ПРЕДПРОСМОТР</Text><FormattedMessageText content={'*Луна смотрит в окно.*\n<think>Кажется, начался дождь.</think>\n«Останемся ещё немного?»'} appearance={appearance} /></View></Card>;
}

function SettingsScreen({ baseUrl, isDemo, appearance, onAppearanceChange, onLogout }: { baseUrl: string; isDemo: boolean; appearance: ChatAppearanceSettings; onAppearanceChange: (changes: Partial<ChatAppearanceSettings>) => void; onLogout: () => Promise<void> }) {
  return <ScrollView contentContainerStyle={{ paddingBottom: 24 }}><PageHeader title="Настройки" subtitle={isDemo ? "Автономный просмотр интерфейса" : "Подключение и сессия"} /><Card style={{ gap: 8, marginBottom: 12 }}><Text style={styles.settingTitle}>{isDemo ? "Режим работы" : "Текущий сервер"}</Text><Text style={styles.helper}>{baseUrl}</Text><StatusPill text={isDemo ? "Демонстрация" : "Локальная сеть"} tone={isDemo ? "accent" : "success"} /></Card><ChatAppearanceSettings appearance={appearance} onChange={onAppearanceChange} /><Card style={{ gap: 10 }}><Text style={styles.settingTitle}>{isDemo ? "Демо-режим" : "Сессия"}</Text><Text style={styles.helper}>{isDemo ? "Это пример данных: чаты, сцены, создание и редактирование персонажей работают только внутри приложения и не меняют данные на ПК." : "Выход сбросит только вход на телефоне. Чаты, сцены и модели на ПК не удаляются."}</Text><Button title={isDemo ? "Завершить демо" : "Сменить ПК / выйти"} variant="danger" icon="logout" onPress={() => Alert.alert(isDemo ? "Завершить демо?" : "Выйти?", isDemo ? "Вы вернётесь к экрану подключения." : "Потребуется снова ввести адрес и пароль.", [{ text: "Отмена", style: "cancel" }, { text: isDemo ? "Завершить" : "Выйти", style: "destructive", onPress: () => void onLogout() }])} /></Card></ScrollView>;
}

const styles = StyleSheet.create({
  sceneComposer: { minHeight: 56, flexDirection: "row", alignItems: "flex-end", gap: 4, paddingHorizontal: 6, paddingTop: 6, paddingBottom: 6, backgroundColor: colors.panel, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.hairline },
  sceneComposerInput: { flex: 1, minHeight: 48, maxHeight: 112, borderRadius: 24, paddingHorizontal: 14, paddingVertical: 11, color: colors.text, backgroundColor: colors.input, fontSize: 16, lineHeight: 21, textAlignVertical: "center" },

  root: { flex: 1, backgroundColor: colors.background },
  grow: { flex: 1, minHeight: 0 },
  boot: { flex: 1, alignItems: "center", justifyContent: "center" },
  bootTitle: { ...typography.title, color: colors.text, marginTop: 12 },
  logoMark: { width: 64, height: 64, borderRadius: 20, backgroundColor: colors.accentSoft, borderWidth: 1, borderColor: colors.borderStrong, alignItems: "center", justifyContent: "center" },
  connectScroll: { flexGrow: 1, paddingHorizontal: space.lg, paddingTop: 36, paddingBottom: 28, gap: 18 },
  connectHero: { alignItems: "center", gap: 8, paddingHorizontal: 18, paddingBottom: 14 },
  authOrbOuter: { width: 96, height: 96, borderRadius: 48, alignItems: "center", justifyContent: "center", backgroundColor: "rgba(124,92,255,0.10)", borderWidth: 1, borderColor: "rgba(154,135,255,0.25)", marginBottom: 8 },
  authOrb: { width: 74, height: 74, borderRadius: 37, alignItems: "center", justifyContent: "center", backgroundColor: colors.accent, shadowColor: colors.accent, shadowOpacity: 0.34, shadowRadius: 18, shadowOffset: { width: 0, height: 7 }, elevation: 7 },
  authProductName: { color: colors.accentHover, fontSize: 12, fontWeight: "800", letterSpacing: 1.2, textTransform: "uppercase" },
  authTitle: { color: colors.text, fontSize: 26, fontWeight: "800", letterSpacing: -0.5, textAlign: "center", marginTop: 2 },
  authSubtitle: { ...typography.body, color: colors.muted, textAlign: "center", lineHeight: 21, maxWidth: 310 },
  authForm: { gap: 12, padding: 16, borderRadius: 22, backgroundColor: colors.panel, borderWidth: 1, borderColor: colors.border, shadowColor: "#000000", shadowOpacity: 0.16, shadowRadius: 18, shadowOffset: { width: 0, height: 8 }, elevation: 3 },
  authFormHeading: { flexDirection: "row", alignItems: "center", gap: 10, marginBottom: 2 },
  authFormIcon: { width: 36, height: 36, borderRadius: 12, alignItems: "center", justifyContent: "center", backgroundColor: colors.accentSoft },
  authFormTitle: { color: colors.text, fontSize: 15, fontWeight: "800" },
  authStatus: { color: colors.muted, fontSize: 12, marginTop: 2, lineHeight: 17 },
  demoButton: { flexDirection: "row", alignItems: "center", gap: 10, padding: 12, borderRadius: radii.md, backgroundColor: colors.accentSoft, borderWidth: 1, borderColor: "rgba(154,135,255,0.34)" },
  demoButtonPressed: { opacity: 0.72, transform: [{ scale: 0.985 }] },
  demoButtonTitle: { color: colors.accentHover, fontSize: 14, fontWeight: "800" },
  demoButtonSubtitle: { color: colors.muted, fontSize: 11, marginTop: 2 },
  authFootnote: { flexDirection: "row", justifyContent: "center", alignItems: "flex-start", gap: 6, paddingHorizontal: 20 },
  connectPlain: { flex: 1, paddingHorizontal: 24, paddingVertical: 34, justifyContent: "center", backgroundColor: colors.background }, connectPlainLogin: { flexGrow: 1, minHeight: "100%" }, connectPlainBrand: { alignItems: "center", gap: 10, marginBottom: 30 }, connectPlainTitle: { color: colors.text, fontSize: 25, fontWeight: "800", letterSpacing: -0.4, textAlign: "center" }, connectPlainText: { color: colors.muted, fontSize: 14, lineHeight: 21, textAlign: "center", maxWidth: 310 }, connectActions: { gap: 12, width: "100%" }, serverPickerList: { flexGrow: 0, width: "100%" }, serverPickerRow: { minHeight: 68, marginBottom: 8, paddingHorizontal: 14, flexDirection: "row", alignItems: "center", gap: 12, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border }, serverPickerRowPressed: { opacity: 0.7 }, connectEmptyText: { color: colors.muted, textAlign: "center", marginTop: 18, fontSize: 13 }, loginFields: { width: "100%", gap: 12 }, connectStatus: { color: colors.muted, textAlign: "center", fontSize: 12, lineHeight: 18 }, connectBack: { alignSelf: "center", paddingVertical: 16, paddingHorizontal: 12, marginTop: 12 }, connectBackText: { color: colors.accentHover, fontSize: 13, fontWeight: "800" },
  sectionTitle: { ...typography.section, color: colors.text }, helper: { ...typography.body, color: colors.muted }, securityNote: { ...typography.caption, color: colors.dim, textAlign: "center", flexShrink: 1, lineHeight: 17 },
  serverRow: { flexDirection: "row", alignItems: "center", gap: 10, padding: 12, borderRadius: radii.md, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, serverRowSelected: { borderColor: colors.accent, backgroundColor: colors.accentSoft }, serverTitle: { color: colors.text, fontWeight: "700", fontSize: 14 }, serverUrl: { color: colors.muted, fontSize: 12, marginTop: 2 },
  topBar: { flexDirection: "row", alignItems: "center", paddingHorizontal: space.lg, paddingTop: 6, paddingBottom: 10, borderBottomWidth: 1, borderBottomColor: colors.border }, topTitle: { ...typography.section, color: colors.text }, topSubtitle: { ...typography.caption, color: colors.muted }, onlinePill: { flexDirection: "row", alignItems: "center", gap: 6, backgroundColor: colors.elevated, borderRadius: radii.pill, paddingHorizontal: 10, paddingVertical: 6, borderWidth: 1, borderColor: colors.border }, onlineDot: { width: 8, height: 8, borderRadius: 4, backgroundColor: colors.online }, onlineText: { color: colors.muted, fontSize: 11, fontWeight: "700" }, headerActions: { flexDirection: "row", gap: 8 },
  content: { flex: 1, minHeight: 0 }, tabPage: { flex: 1, minHeight: 0 }, tabPageHidden: { display: "none" }, tabBar: { flexDirection: "row", borderTopWidth: 1, borderTopColor: colors.border, backgroundColor: colors.panel, paddingBottom: 4, paddingTop: 4 }, tabButton: { flex: 1, alignItems: "center", justifyContent: "center", paddingVertical: 8, gap: 2, borderRadius: radii.md, marginHorizontal: 4 }, tabButtonActive: { backgroundColor: colors.accentSoft }, tabLabel: { fontSize: 11, color: colors.muted, fontWeight: "600" }, tabLabelActive: { color: colors.accentHover },
  selectorBlock: { marginBottom: 8 }, selectorLabel: { color: colors.dim, fontSize: 10, fontWeight: "800", letterSpacing: 0.8, marginBottom: 5 }, selectorList: { maxHeight: 192, flexGrow: 0 }, selectorContent: { gap: 7, paddingBottom: 2 }, selectorItem: { width: "100%", minHeight: 42, paddingHorizontal: 12, flexDirection: "row", alignItems: "center", gap: 8, borderRadius: radii.sm, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, selectorItemActive: { backgroundColor: colors.accentSoft, borderColor: colors.accent }, selectorDot: { width: 7, height: 7, borderRadius: 4, backgroundColor: colors.dim, flexShrink: 0 }, selectorDotActive: { backgroundColor: colors.accentHover }, selectorText: { flex: 1, color: colors.muted, fontSize: 13, fontWeight: "700" }, selectorTextActive: { color: colors.text },
  chatHeader: { flexDirection: "row", alignItems: "center", gap: 12, padding: 12, borderRadius: radii.lg, backgroundColor: colors.panel, borderWidth: 1, borderColor: colors.border, marginBottom: 8 }, chatName: { color: colors.text, fontWeight: "700", fontSize: 15 }, chatMeta: { color: colors.muted, fontSize: 12, marginTop: 2 }, messagesList: { paddingVertical: 10, paddingBottom: 12, flexGrow: 1, justifyContent: "flex-end" }, bubbleRow: { marginBottom: 6, alignItems: "flex-start", paddingHorizontal: 8 },
  bubbleRowMine: { alignItems: "flex-end" },
  bubble: { maxWidth: "86%", paddingHorizontal: 10, paddingTop: 6, paddingBottom: 5 },
  bubbleTheirs: { backgroundColor: colors.bubbleIn, borderRadius: 16, borderBottomLeftRadius: 4 },
  bubbleMine: { backgroundColor: colors.bubbleOut, borderRadius: 16, borderBottomRightRadius: 4 }, bubbleDirector: { backgroundColor: colors.bubbleDirector, borderRadius: 16, borderBottomLeftRadius: 16, borderBottomRightRadius: 16 }, directorRowAlign: { alignItems: "center" }, messageAuthor: { color: "#B4C0E8", fontSize: 10, marginBottom: 3, fontWeight: "700" }, messageText: { color: colors.text, fontSize: 14, lineHeight: 20 }, messageTextMine: { color: "#FFFFFF" }, messageAction: { color: "#C8A6FF", fontStyle: "italic" }, messageThought: { color: "#73B7FF", fontStyle: "italic" }, messageSpeech: { color: "#FFD18A" }, messageTime: { color: colors.dim, fontSize: 10, marginTop: 4, alignSelf: "flex-end" }, messageTimeMine: { color: "rgba(255,255,255,0.7)" }, listHint: { color: colors.muted, textAlign: "center", marginTop: 28 },
  messageComposer: { backgroundColor: colors.panel }, messageComposerSurface: { minHeight: 52, flexDirection: "row", alignItems: "flex-end", gap: 3, padding: 4, borderRadius: 26, backgroundColor: colors.input, borderWidth: 1, borderColor: colors.borderStrong }, messageComposerText: { flex: 1, minWidth: 0, minHeight: 44, maxHeight: 112, color: colors.text, fontSize: 16, lineHeight: 21, paddingHorizontal: 8, paddingVertical: 10 }, composerAction: { width: 44, height: 44, flexShrink: 0, alignItems: "center", justifyContent: "center", borderRadius: 22, backgroundColor: "transparent" }, composerActionPrimary: { backgroundColor: colors.accent }, composerActionPressed: { opacity: 0.48, transform: [{ scale: 0.96 }] }, historyLoading: { flex: 1, alignItems: "center", justifyContent: "center" }, historyHidden: { opacity: 0 }, historyLoadingOverlay: { ...StyleSheet.absoluteFillObject, alignItems: "center", justifyContent: "center", backgroundColor: colors.background }, dateDivider: { flexDirection: "row", alignItems: "center", gap: 9, paddingHorizontal: 18, paddingTop: 15, paddingBottom: 9 }, dateDividerLine: { flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: colors.border }, dateDividerText: { color: colors.dim, fontSize: 11, fontWeight: "700" }, typingRow: { flexDirection: "row", alignItems: "center", gap: 8, paddingVertical: 10, paddingHorizontal: 12 }, typingText: { color: colors.muted, fontSize: 12, fontWeight: "600" }, sceneHeader: { flexDirection: "row", alignItems: "center", gap: 10, backgroundColor: colors.panel, borderColor: colors.border, borderWidth: 1, borderRadius: radii.lg, padding: 12, marginBottom: 8 }, avatarStack: { flexDirection: "row", alignItems: "center" }, actionRow: { flexDirection: "row", gap: 8, paddingHorizontal: 12, paddingTop: 8 }, directorComposer: { flexDirection: "row", gap: 8, paddingHorizontal: 12, paddingTop: 8, paddingBottom: 6, alignItems: "flex-end" },
  dialogList: { paddingBottom: 18 }, dialogListWithFab: { paddingBottom: 104 }, floatingCreate: { position: "absolute", right: 20, bottom: 20, width: 56, height: 56, borderRadius: 28, alignItems: "center", justifyContent: "center", backgroundColor: colors.accent, shadowColor: "#000000", shadowOpacity: 0.34, shadowRadius: 12, shadowOffset: { width: 0, height: 6 }, elevation: 8 }, floatingCreatePressed: { opacity: 0.78, transform: [{ scale: 0.96 }] }, newChatList: { padding: 16, gap: 8, paddingBottom: 28 }, choiceRow: { minHeight: 64, paddingHorizontal: 12, flexDirection: "row", alignItems: "center", gap: 12, borderRadius: radii.lg, backgroundColor: colors.panel, borderWidth: 1, borderColor: colors.border }, choiceRowActive: { backgroundColor: colors.accentSoft, borderColor: colors.accent }, characterList: { gap: 10, paddingBottom: 20 }, characterListWithFab: { gap: 10, paddingBottom: 104 }, characterCard: { flexDirection: "row", alignItems: "center", gap: 12, minHeight: 76, padding: 12, borderRadius: radii.lg, backgroundColor: colors.panel, borderColor: colors.border, borderWidth: 1 }, characterName: { color: colors.text, fontSize: 16, fontWeight: "800", marginBottom: 2 }, settingTitle: { color: colors.text, fontWeight: "800", fontSize: 15 },
  profileScroll: { paddingBottom: 28 }, profileHero: { alignItems: "center", paddingVertical: 26, gap: 7, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border }, profileName: { color: colors.text, fontSize: 22, fontWeight: "800" }, profileTitle: { color: colors.muted, fontSize: 13 }, profileCard: { marginHorizontal: 16, marginTop: 12, padding: 14, borderRadius: radii.lg, backgroundColor: colors.panel, borderColor: colors.border, borderWidth: 1 }, profileLabel: { color: colors.dim, fontSize: 10, fontWeight: "800", letterSpacing: 0.8, marginBottom: 6 }, profileValue: { color: colors.text, fontSize: 14, lineHeight: 20 }, editorScroll: { paddingBottom: 32 }, avatarUploadButton: { marginTop: 5 }, modeRow: { flexDirection: "row", gap: 8, paddingHorizontal: 16, paddingTop: 16 }, largeField: { minHeight: 108, textAlignVertical: "top" },
  markupSetting: { gap: 7 }, markupSettingLabel: { color: colors.text, fontSize: 13, fontWeight: "700" }, colorSwatches: { flexDirection: "row", flexWrap: "wrap", gap: 9 }, colorSwatch: { width: 27, height: 27, borderRadius: 14, alignItems: "center", justifyContent: "center", borderWidth: 2, borderColor: "transparent" }, colorSwatchSelected: { borderColor: colors.text, transform: [{ scale: 1.08 }] }, markupDivider: { height: StyleSheet.hairlineWidth, backgroundColor: colors.border, marginVertical: 2 }, markupGroupTitle: { color: colors.dim, fontSize: 10, letterSpacing: 0.8, fontWeight: "800" }, markerToggle: { flexDirection: "row", alignItems: "center", gap: 12, paddingVertical: 7 }, markerTogglePressed: { opacity: 0.72 }, markerToggleTitle: { color: colors.text, fontSize: 13, fontWeight: "700" }, markerToggleSubtitle: { color: colors.muted, fontSize: 11, lineHeight: 16, marginTop: 2 }, markerToggleTrack: { width: 42, height: 24, borderRadius: 12, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border, padding: 3 }, markerToggleTrackOn: { backgroundColor: colors.accent, borderColor: colors.accent }, markerToggleThumb: { width: 16, height: 16, borderRadius: 8, backgroundColor: colors.dim }, markerToggleThumbOn: { alignSelf: "flex-end", backgroundColor: colors.text }, markupPreview: { gap: 6, padding: 11, borderRadius: radii.md, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, markupPreviewLabel: { color: colors.dim, fontSize: 9, letterSpacing: 0.9, fontWeight: "800" },
  conversationSwitch: { flexDirection: "row", marginHorizontal: 16, marginVertical: 10, padding: 3, borderRadius: radii.md, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, conversationSwitchItem: { flex: 1, minHeight: 36, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 7, borderRadius: radii.sm }, conversationSwitchItemActive: { backgroundColor: colors.accent }, conversationSwitchText: { color: colors.muted, fontSize: 12, fontWeight: "800" }, conversationSwitchTextActive: { color: colors.text }, creationChoiceList: { padding: 16, gap: 10 }, creationChoice: { minHeight: 88, padding: 13, flexDirection: "row", alignItems: "center", gap: 12, borderRadius: radii.lg, backgroundColor: colors.panel, borderWidth: 1, borderColor: colors.border }, creationChoicePressed: { opacity: 0.72, transform: [{ scale: 0.985 }] }, creationChoiceIcon: { width: 48, height: 48, borderRadius: 16, alignItems: "center", justifyContent: "center", backgroundColor: colors.accentSoft }, creationChoiceTitle: { color: colors.text, fontSize: 15, fontWeight: "800" }, creationChoiceSubtitle: { color: colors.muted, fontSize: 12, lineHeight: 17, marginTop: 3 }, newSceneScroll: { paddingBottom: 30 }, newSceneContent: { padding: 16, gap: 12 },
  sceneProfileEmblem: { width: 96, height: 96, borderRadius: 48, backgroundColor: colors.accentSoft, borderWidth: 1, borderColor: colors.borderStrong, alignItems: "center", justifyContent: "center" }, sceneParticipantRow: { flexDirection: "row", alignItems: "center", gap: 10, paddingTop: 8 }, sceneParticipantName: { color: colors.text, fontSize: 14, fontWeight: "800" }, sceneDetailRow: { gap: 3, paddingVertical: 8, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border }, sceneDetailLabel: { color: colors.dim, fontSize: 10, fontWeight: "800", letterSpacing: 0.5 }, sceneStateText: { marginTop: 8 },
  sceneOptionLabel: { color: colors.dim, fontSize: 10, letterSpacing: 0.8, fontWeight: "800", marginBottom: -5 }, sceneTurnModeRow: { flexDirection: "row", gap: 8 }, scenePickerTrigger: { minHeight: 58, paddingHorizontal: 11, flexDirection: "row", alignItems: "center", gap: 10, borderRadius: radii.md, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, scenePickerTriggerPressed: { opacity: 0.74 }, scenePickerEmptyAvatar: { width: 34, height: 34, borderRadius: 17, alignItems: "center", justifyContent: "center", backgroundColor: colors.input }, scenePickerName: { color: colors.text, fontSize: 14, fontWeight: "800" }, scenePickerSubtitle: { color: colors.muted, fontSize: 11, marginTop: 2 }, scenePickerModal: { flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.55)" }, scenePickerDismiss: { ...StyleSheet.absoluteFillObject }, scenePickerSheet: { maxHeight: "72%", minHeight: 240, borderTopLeftRadius: 24, borderTopRightRadius: 24, backgroundColor: colors.panel, borderTopWidth: 1, borderColor: colors.border }, scenePickerSheetHead: { minHeight: 60, paddingHorizontal: 18, flexDirection: "row", alignItems: "center", justifyContent: "space-between", borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border }, scenePickerSheetTitle: { color: colors.text, fontSize: 17, fontWeight: "800" }, scenePickerClose: { width: 36, height: 36, borderRadius: 18, alignItems: "center", justifyContent: "center", backgroundColor: colors.elevated }, scenePickerList: { padding: 12, gap: 7, paddingBottom: 28 }, scenePickerRow: { minHeight: 64, flexDirection: "row", alignItems: "center", gap: 11, paddingHorizontal: 11, borderRadius: radii.md, backgroundColor: colors.elevated, borderWidth: 1, borderColor: colors.border }, scenePickerRowActive: { backgroundColor: colors.accentSoft, borderColor: colors.accent }, scenePickerRowPressed: { opacity: 0.7 }, scenePickerEmpty: { color: colors.muted, textAlign: "center", paddingVertical: 24 },
});
