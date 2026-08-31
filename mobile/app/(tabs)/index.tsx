import { MaterialIcons } from "@expo/vector-icons";
import { Image } from "expo-image";
import * as ImagePicker from "expo-image-picker";
import { useFonts } from "expo-font";
import {
  Fragment,
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  ActivityIndicator,
  Alert,
  Animated,
  BackHandler,
  FlatList,
  Modal,
  PanResponder,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  Switch,
  View,
  type StyleProp,
  type TextStyle,
} from "react-native";
import {
  KeyboardAvoidingView,
  KeyboardChatScrollView,
  KeyboardStickyView,
  type KeyboardChatScrollViewRef,
} from "react-native-keyboard-controller";
import { ScreenContainer } from "@/components/screen-container";
import {
  SoulExeApiClient,
  type SoulCharacter,
  type SoulConversation,
  type SoulConversationAction,
  type SoulExeSession,
  type SoulLorebookSummary,
  type SoulPersona,
  type SoulPromptPreset,
} from "@/lib/soulexe-api";
import {
  discoverSoulExeServers,
  type DiscoveredSoulExeServer,
} from "@/lib/soulexe-discovery";
import {
  clearSoulExeSession,
  defaultChatAppearance,
  loadChatAppearance,
  loadSoulExeSession,
  saveChatAppearance,
  saveSoulExeSession,
} from "@/lib/soulexe-storage";
import {
  startSoulExeForegroundService,
  stopSoulExeForegroundService,
  subscribeToForegroundServiceLinks,
} from "@/lib/soulexe-foreground-service";

const C = {
  ink: "#020617",
  navy: "#051424",
  glass: "rgba(22, 36, 54, 0.94)",
  card: "#0D1C2D",
  elevated: "#1C2B3C",
  border: "rgba(212, 228, 250, 0.10)",
  text: "#F2F4FF",
  muted: "#A8B0C2",
  violet: "#8B5CF6",
  lavender: "#D0BCFF",
  green: "#10B981",
  amber: "#F59E0B",
};

type LibraryEntity = {
  id: string;
  conversationParticipantId?: string;
  name: string;
  role: string;
  description: string;
  glyph: string;
  tint: string;
  affinity: number;
  avatarUrl?: string | null;
  promptText?: string;
  personality?: string;
  scenario?: string;
  systemPrompt?: string;
  personalityExpressionLevel?: "vivid" | "natural" | "subtle";
  replyLanguage?: string;
  useRoleplayResponseFormatting?: boolean;
  defaultUserProfile?: string;
  defaultRelationshipContext?: string;
  exampleDialogue?: string;
  selectedPromptPresetId?: string | null;
  lorebookIds?: string[];
  cognitiveArchitectureEnabled?: boolean;
  soulMemoryEnabled?: boolean;
  soulMemoryPreset?: string;
  soulMemoryIntervalMessages?: number;
  autoSummaryEnabled?: boolean;
  autoSummaryIntervalMessages?: number;
  proactiveMessagesEnabled?: boolean;
  proactiveQuietHoursEnabled?: boolean;
  proactiveQuietHoursStart?: string;
  proactiveQuietHoursEnd?: string;
  realisticMessagingEnabled?: boolean;
  selectedPersonaId?: string | null;
};

type Message = {
  id: string;
  sender: "director" | "character" | "persona" | "user";
  text: string;
  time?: string;
  participantIndex?: number;
  authorName?: string;
  createdAt?: string;
};

type ConversationPreview = {
  id: string;
  title: string;
  preview: string;
  time: string;
  status?: string;
  character: LibraryEntity;
  participants?: LibraryEntity[];
  source?: "remote" | "demo";
  updatedAt?: string;
};

type EditorState = {
  kind: "character" | "persona";
  item?: LibraryEntity;
};

type ComputerChoice = {
  baseUrl: string;
  name: string;
};

type NewConversationDetails = {
  name: string;
  scenario?: string;
  location?: string;
  mood?: string;
  goal?: string;
  delaySeconds?: number;
  enforceContract?: boolean;
  advanceAndAvoidRepetition?: boolean;
};

type TextFormatting = {
  actions: boolean;
  thoughts: boolean;
  speech: boolean;
};

const defaultTextFormatting: TextFormatting = {
  actions: true,
  thoughts: true,
  speech: true,
};

const CHAT_BACKGROUNDS = [
  {
    id: "midnight",
    label: "Полночное небо",
    color: "#020617",
    accent: "#18243A",
    texture: "stars",
  },
  {
    id: "deep-ocean",
    label: "Глубокий океан",
    color: "#031525",
    accent: "#164E63",
    texture: "waves",
  },
  {
    id: "graphite",
    label: "Графитовая сетка",
    color: "#10131A",
    accent: "#3F4654",
    texture: "grid",
  },
  {
    id: "plum",
    label: "Сливовое сияние",
    color: "#160E20",
    accent: "#5B2B68",
    texture: "sparkles",
  },
] as const;
type ChatBackgroundId = (typeof CHAT_BACKGROUNDS)[number]["id"];
type MessageStyleId = "glass" | "contrast" | "soft";

const MESSAGE_STYLES: {
  id: MessageStyleId;
  label: string;
  description: string;
}[] = [
  {
    id: "glass",
    label: "Стеклянный",
    description: "Полупрозрачные пузыри и тонкая светлая граница.",
  },
  {
    id: "contrast",
    label: "Контрастный",
    description: "Насыщенные цвета и чёткое разделение собеседников.",
  },
  {
    id: "soft",
    label: "Мягкий",
    description: "Спокойные матовые пузыри с более круглыми углами.",
  },
];

const SOUL_MEMORY_PRESETS = [
  {
    id: "full",
    title: "Полная память",
    description:
      "Запоминает важные факты и отношения, ведёт дневник и отдельные воспоминания о людях, местах и событиях. Самый подробный вариант.",
  },
  {
    id: "index-diary",
    title: "Факты и дневник",
    description:
      "Запоминает основные факты и отношения и сохраняет личные впечатления персонажа, но не создаёт отдельные тематические воспоминания.",
  },
  {
    id: "index",
    title: "Только основные факты",
    description:
      "Обновляет главное о персонаже, пользователе и отношениях. Самый быстрый вариант без тем и дневника.",
  },
  {
    id: "diary",
    title: "Только дневник",
    description:
      "Сохраняет личные впечатления персонажа, но не изменяет основные факты, отношения и тематические воспоминания.",
  },
] as const;

function resolveChatBackground(id: string) {
  return (
    CHAT_BACKGROUNDS.find((option) => option.id === id) ?? CHAT_BACKGROUNDS[0]
  );
}

function ChatTexture({ backgroundId }: { backgroundId: ChatBackgroundId }) {
  const texture = resolveChatBackground(backgroundId).texture;
  if (texture === "grid") {
    return (
      <View pointerEvents="none" style={styles.chatTexture}>
        {[12, 32, 52, 72, 92].map((top) => (
          <View
            key={`h-${top}`}
            style={[styles.textureLineHorizontal, { top: `${top}%` }]}
          />
        ))}
        {[10, 30, 50, 70, 90].map((left) => (
          <View
            key={`v-${left}`}
            style={[styles.textureLineVertical, { left: `${left}%` }]}
          />
        ))}
      </View>
    );
  }
  if (texture === "waves") {
    return (
      <View pointerEvents="none" style={styles.chatTexture}>
        <View style={[styles.textureWave, styles.textureWaveTop]} />
        <View style={[styles.textureWave, styles.textureWaveBottom]} />
      </View>
    );
  }
  const points =
    texture === "stars"
      ? [
          [8, 14],
          [18, 73],
          [31, 40],
          [44, 88],
          [58, 19],
          [69, 61],
          [82, 34],
          [91, 82],
        ]
      : [
          [12, 22],
          [24, 80],
          [39, 52],
          [55, 16],
          [68, 72],
          [84, 41],
        ];
  return (
    <View pointerEvents="none" style={styles.chatTexture}>
      {points.map(([top, left], index) => (
        <View
          key={`${top}-${left}`}
          style={[
            texture === "stars" ? styles.textureStar : styles.textureSparkle,
            {
              top: `${top}%`,
              left: `${left}%`,
              opacity: 0.18 + (index % 3) * 0.08,
            },
          ]}
        />
      ))}
    </View>
  );
}

function SwipeSheet({
  visible,
  onClose,
  children,
}: {
  visible: boolean;
  onClose: () => void;
  children: ReactNode;
}) {
  const translateY = useRef(new Animated.Value(0)).current;
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    if (visible) translateY.setValue(0);
  }, [translateY, visible]);
  const panResponder = useRef(
    PanResponder.create({
      onMoveShouldSetPanResponder: (_, gesture) =>
        gesture.dy > 8 && Math.abs(gesture.dy) > Math.abs(gesture.dx),
      onPanResponderMove: (_, gesture) =>
        translateY.setValue(Math.max(0, gesture.dy)),
      onPanResponderRelease: (_, gesture) => {
        if (gesture.dy > 80 || gesture.vy > 0.75) {
          Animated.timing(translateY, {
            toValue: 520,
            duration: 190,
            useNativeDriver: true,
          }).start(() => closeRef.current());
          return;
        }
        Animated.spring(translateY, {
          toValue: 0,
          useNativeDriver: true,
          damping: 22,
          stiffness: 220,
        }).start();
      },
    }),
  ).current;
  return (
    <Modal
      transparent
      visible={visible}
      animationType="fade"
      statusBarTranslucent
      navigationBarTranslucent
      onRequestClose={onClose}
    >
      <Pressable style={styles.modalBackdrop} onPress={onClose}>
        <Animated.View
          {...panResponder.panHandlers}
          style={[styles.sheet, { transform: [{ translateY }] }]}
          onStartShouldSetResponder={() => true}
        >
          <View style={styles.sheetHandle} />
          {children}
        </Animated.View>
      </Pressable>
    </Modal>
  );
}

function formatLastSeen(message?: Message) {
  if (!message?.createdAt) {
    if (message?.time === "сейчас") return "был(а) только что";
    return message?.time ? `был(а) в ${message.time}` : "был(а) недавно";
  }
  const date = new Date(message.createdAt);
  if (Number.isNaN(date.getTime())) return "был(а) недавно";
  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();
  if (sameDay)
    return `был(а) в ${date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}`;
  return `был(а) ${date.toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "short",
    year: date.getFullYear() === now.getFullYear() ? undefined : "numeric",
  })}`;
}

function messageDateKey(message?: Message) {
  if (!message?.createdAt) return null;
  const date = new Date(message.createdAt);
  if (Number.isNaN(date.getTime())) return null;
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function formatMessageDate(message: Message) {
  if (!message.createdAt) return null;
  const date = new Date(message.createdAt);
  if (Number.isNaN(date.getTime())) return null;
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const day = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const dayDifference = Math.round(
    (today.getTime() - day.getTime()) / 86_400_000,
  );
  if (dayDifference === 0) return "Сегодня";
  if (dayDifference === 1) return "Вчера";
  return date.toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "long",
    year: date.getFullYear() === now.getFullYear() ? undefined : "numeric",
  });
}

/** Android's edge-back gesture emits the same event as the system back key.
 * Screens managed inside this one Expo route need to consume it themselves;
 * otherwise Android closes the whole application. */
function useSystemBack(onBack: () => boolean | void) {
  useEffect(() => {
    const subscription = BackHandler.addEventListener(
      "hardwareBackPress",
      () => {
        return onBack() !== false;
      },
    );
    return () => subscription.remove();
  }, [onBack]);
}

function FormattedMessageText({
  text,
  formatting,
  style,
}: {
  text: string;
  formatting: TextFormatting;
  style?: StyleProp<TextStyle>;
}) {
  const parts: Array<{
    text: string;
    kind: "plain" | "action" | "thought" | "speech";
  }> = [];
  const pattern =
    /(<think\b[^>]*>[\s\S]*?<\/think>)|(\*[^*\n]+\*)|(«[^»\n]+»|"[^"\n]+")/gi;
  let cursor = 0;
  for (const match of text.matchAll(pattern)) {
    const start = match.index ?? 0;
    if (start > cursor)
      parts.push({ text: text.slice(cursor, start), kind: "plain" });
    const token = match[0];
    if (match[1]) {
      parts.push({
        text: token.replace(/^<think\b[^>]*>/i, "").replace(/<\/think>$/i, ""),
        kind: "thought",
      });
    } else if (match[2]) {
      parts.push({ text: token.slice(1, -1), kind: "action" });
    } else {
      parts.push({ text: token.slice(1, -1), kind: "speech" });
    }
    cursor = start + token.length;
  }
  if (cursor < text.length)
    parts.push({ text: text.slice(cursor), kind: "plain" });

  return (
    <Text style={style}>
      {parts.map((part, index) => (
        <Text
          key={`${part.kind}-${index}`}
          style={
            part.kind === "action" && formatting.actions
              ? styles.messageActionText
              : part.kind === "thought" && formatting.thoughts
                ? styles.messageThoughtText
                : part.kind === "speech" && formatting.speech
                  ? styles.messageSpeechText
                  : undefined
          }
        >
          {part.text}
        </Text>
      ))}
    </Text>
  );
}

function TypingDots() {
  const opacity = useRef(new Animated.Value(0.25)).current;
  useEffect(() => {
    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, {
          toValue: 1,
          duration: 520,
          useNativeDriver: true,
        }),
        Animated.timing(opacity, {
          toValue: 0.25,
          duration: 520,
          useNativeDriver: true,
        }),
      ]),
    );
    animation.start();
    return () => animation.stop();
  }, [opacity]);

  return (
    <Animated.Text style={[styles.headerTypingDots, { opacity }]}>
      •••
    </Animated.Text>
  );
}

function toCharacterEntity(character: SoulCharacter): LibraryEntity {
  return {
    id: character.id,
    name: character.name,
    role: character.title?.trim() || "Персонаж SoulExe",
    description:
      character.description?.trim() ||
      character.personality?.trim() ||
      "Описание пока не добавлено.",
    glyph: "✦",
    tint: "#6E7DAA",
    affinity: 0,
    avatarUrl: character.avatarUrl,
    personality: character.personality,
    scenario: character.scenario,
    systemPrompt: character.systemPrompt,
    personalityExpressionLevel: character.personalityExpressionLevel,
    replyLanguage: character.replyLanguage,
    useRoleplayResponseFormatting: character.useRoleplayResponseFormatting,
    defaultUserProfile: character.defaultUserProfile,
    defaultRelationshipContext: character.defaultRelationshipContext,
    exampleDialogue: character.exampleDialogue,
    selectedPromptPresetId: character.selectedPromptPresetId,
    lorebookIds: character.lorebookIds,
    cognitiveArchitectureEnabled: character.cognitiveArchitectureEnabled,
    soulMemoryEnabled: character.soulMemoryEnabled,
    soulMemoryPreset: character.soulMemoryPreset,
    soulMemoryIntervalMessages: character.soulMemoryIntervalMessages,
    autoSummaryEnabled: character.autoSummaryEnabled,
    autoSummaryIntervalMessages: character.autoSummaryIntervalMessages,
    proactiveMessagesEnabled: character.proactiveMessagesEnabled,
    proactiveQuietHoursEnabled: character.proactiveQuietHoursEnabled,
    proactiveQuietHoursStart: character.proactiveQuietHoursStart,
    proactiveQuietHoursEnd: character.proactiveQuietHoursEnd,
    realisticMessagingEnabled: character.realisticMessagingEnabled,
    selectedPersonaId: character.selectedPersonaId,
  };
}

function toPersonaEntity(persona: SoulPersona): LibraryEntity {
  return {
    id: persona.id,
    name: persona.name,
    role: "Персона",
    description:
      persona.description?.trim() ||
      persona.promptText?.trim() ||
      "Описание пока не добавлено.",
    glyph: "◌",
    tint: "#7566A4",
    affinity: 0,
    avatarUrl: persona.avatarUrl,
    promptText: persona.promptText,
  };
}

function toConversationPreview(
  conversation: SoulConversation,
  characters: LibraryEntity[],
): ConversationPreview | null {
  const participants = conversation.participants
    .filter((participant) => participant.kind === "Character")
    .map((participant) => {
      const libraryCharacter = characters.find(
        (character) => character.id === participant.characterId,
      );
      return libraryCharacter
        ? {
            ...libraryCharacter,
            conversationParticipantId: participant.id,
          }
        : {
            id: participant.characterId || participant.id,
            conversationParticipantId: participant.id,
            name: participant.displayName,
            role: "Персонаж SoulExe",
            description: "Описание доступно в библиотеке.",
            glyph: "✦",
            tint: "#6E7DAA",
            affinity: 0,
            avatarUrl: participant.avatarUrl,
          };
    });
  const character = participants[0];
  if (!character) return null;
  const lastMessage = conversation.messages.at(-1);
  const timestamp = new Date(conversation.updatedAt);
  const time = Number.isNaN(timestamp.getTime())
    ? ""
    : timestamp.toLocaleTimeString("ru-RU", {
        hour: "2-digit",
        minute: "2-digit",
      });
  return {
    id: conversation.id,
    title: conversation.name || character.name,
    preview: lastMessage
      ? `${lastMessage.author}: ${lastMessage.content}`
      : "Новый разговор",
    time,
    status: conversation.turnState?.status === "running" ? "Идёт" : undefined,
    character,
    participants,
    source: "remote",
    updatedAt: conversation.updatedAt,
  };
}

function sortConversationPreviews(items: ConversationPreview[]) {
  return [...items].sort((left, right) => {
    const leftTime = Date.parse(left.updatedAt ?? "");
    const rightTime = Date.parse(right.updatedAt ?? "");
    return (
      (Number.isFinite(rightTime) ? rightTime : 0) -
      (Number.isFinite(leftTime) ? leftTime : 0)
    );
  });
}

function toConversationMessages(conversation: SoulConversation): Message[] {
  const characterParticipants = conversation.participants.filter(
    (participant) => participant.kind === "Character",
  );
  return conversation.messages.map((message) => {
    const participantIndexById = characterParticipants.findIndex(
      (participant) => participant.id === message.authorParticipantId,
    );
    const participantIndex =
      participantIndexById >= 0
        ? participantIndexById
        : characterParticipants.findIndex(
            (participant) =>
              participant.displayName.trim().toLocaleLowerCase("ru-RU") ===
              message.author.trim().toLocaleLowerCase("ru-RU"),
          );
    const authorKind = message.authorKind?.toLowerCase();
    const authorIsCharacter =
      participantIndex >= 0 ||
      characterParticipants.some(
        (participant) =>
          participant.displayName.trim().toLocaleLowerCase("ru-RU") ===
          message.author.trim().toLocaleLowerCase("ru-RU"),
      );
    // The server may still tag a generated reply as `user`, but its
    // participant id is authoritative. This keeps character replies on the
    // left (or on their own side in a group) and reserves the right for us.
    const sender =
      message.kind === "director" || authorKind === "director"
        ? "director"
        : authorIsCharacter
          ? "character"
          : authorKind === "persona"
            ? "persona"
            : authorKind === "user"
              ? "user"
              : "character";
    const created = new Date(message.createdAt);
    return {
      id: message.id,
      sender,
      text: message.content,
      authorName: message.author,
      time: Number.isNaN(created.getTime())
        ? undefined
        : created.toLocaleTimeString("ru-RU", {
            hour: "2-digit",
            minute: "2-digit",
          }),
      createdAt: message.createdAt,
      participantIndex: participantIndex >= 0 ? participantIndex : 0,
    };
  });
}

const initialCharacters: LibraryEntity[] = [
  {
    id: "elara",
    name: "Элара",
    role: "Лесная эльфийка",
    description: "Мудрая хранительница леса с таинственным прошлым.",
    glyph: "✦",
    tint: "#6E7DAA",
    affinity: 62,
  },
  {
    id: "marcus",
    name: "Маркус",
    role: "Ветеран-наёмник",
    description: "Молчаливый стратег, который всегда видит второй путь.",
    glyph: "◈",
    tint: "#8D654E",
    affinity: 38,
  },
  {
    id: "cyber",
    name: "Кибер-советник",
    role: "ИИ из 2144 года",
    description: "Логический модуль, который учится понимать человеческое.",
    glyph: "⌁",
    tint: "#5B8A9C",
    affinity: 74,
  },
];

const initialPersonas: LibraryEntity[] = [
  {
    id: "kai",
    name: "Кай",
    role: "Странник",
    description: "Твой основной профиль пользователя в историях.",
    glyph: "◌",
    tint: "#7566A4",
    affinity: 0,
  },
  {
    id: "detective",
    name: "Детектив Райт",
    role: "Следователь",
    description: "Наблюдательный и собранный голос для расследований.",
    glyph: "⌕",
    tint: "#526A79",
    affinity: 0,
  },
];

const conversations: ConversationPreview[] = [
  {
    id: "forest",
    title: "Глухой Лес",
    preview: "Элара: Там кто-то есть. Держи меч наготове.",
    time: "00:04",
    status: "Идёт",
    character: initialCharacters[0],
    participants: [initialCharacters[0], initialCharacters[1]],
    source: "demo",
  },
  {
    id: "tavern",
    title: "Таверна «Пьяный дракон»",
    preview: "Маркус: Нам нужно уходить сейчас.",
    time: "11:20",
    status: "Идёт",
    character: initialCharacters[1],
    source: "demo",
  },
  {
    id: "system",
    title: "Кибер-советник",
    preview: "Системы в норме. Готов продолжить анализ.",
    time: "Вчера",
    character: initialCharacters[2],
    source: "demo",
  },
];

const openingMessages: Message[] = [
  { id: "m1", sender: "director", text: "Глухой Лес, Полночь" },
  {
    id: "m2",
    sender: "character",
    text: "Ты слышишь это? Лес словно затаил дыхание перед бурей.",
    time: "00:01",
    participantIndex: 0,
  },
  {
    id: "m3",
    sender: "user",
    text: "Да, ветер становится холоднее. Нам нужно найти укрытие.",
    time: "00:02",
  },
  {
    id: "m4",
    sender: "director",
    text: "Вдалеке послышался хруст веток. Ветер усилился.",
  },
  {
    id: "m5",
    sender: "character",
    text: "Там кто-то есть. Держи меч наготове.",
    time: "00:04",
    participantIndex: 1,
  },
];

function Avatar({
  entity,
  size = 52,
}: {
  entity: LibraryEntity;
  size?: number;
}) {
  return (
    <View
      style={[
        styles.avatar,
        {
          width: size,
          height: size,
          borderRadius: size / 2,
          backgroundColor: entity.tint,
        },
      ]}
    >
      {entity.avatarUrl ? (
        <Image
          source={entity.avatarUrl}
          style={styles.avatarImage}
          contentFit="cover"
          transition={120}
        />
      ) : (
        <>
          <View style={styles.avatarGlow} />
          <Text style={[styles.avatarGlyph, { fontSize: size * 0.46 }]}>
            {entity.glyph}
          </Text>
        </>
      )}
    </View>
  );
}

function IconButton({
  name,
  onPress,
}: {
  name: keyof typeof MaterialIcons.glyphMap;
  onPress?: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.iconButton, pressed && styles.pressed]}
    >
      <MaterialIcons name={name} size={23} color={C.lavender} />
    </Pressable>
  );
}

function TopBar({
  title,
  onBack,
  rightIcon = "more-vert",
  subtitle,
  onRightPress,
}: {
  title: string;
  onBack?: () => void;
  rightIcon?: keyof typeof MaterialIcons.glyphMap;
  subtitle?: string;
  onRightPress?: () => void;
}) {
  return (
    <View style={styles.topBar}>
      <IconButton
        name={onBack ? "arrow-back" : "auto-awesome"}
        onPress={onBack}
      />
      <View style={styles.topTitleWrap}>
        <Text style={styles.topTitle}>{title}</Text>
        {subtitle ? <Text style={styles.topSubtitle}>{subtitle}</Text> : null}
      </View>
      {onRightPress ? (
        <IconButton name={rightIcon} onPress={onRightPress} />
      ) : (
        <View style={styles.iconButton} />
      )}
    </View>
  );
}

function BottomNav({
  active,
  onChange,
}: {
  active: "chat" | "library" | "more";
  onChange: (tab: "chat" | "library" | "more") => void;
}) {
  const items = [
    ["chat", "chat-bubble-outline", "Разговоры"],
    ["library", "auto-stories", "Библиотека"],
    ["more", "more-horiz", "Ещё"],
  ] as const;
  return (
    <View style={styles.bottomNav}>
      {items.map(([id, icon, label]) => {
        const selected = active === id;
        return (
          <Pressable
            key={id}
            onPress={() => onChange(id)}
            style={({ pressed }) => [styles.navItem, pressed && styles.pressed]}
          >
            <MaterialIcons
              name={icon}
              size={22}
              color={selected ? C.violet : C.muted}
            />
            <Text style={[styles.navLabel, selected && styles.navLabelActive]}>
              {label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

function ConversationsScreen({
  items = conversations,
  onOpenConversation,
  onCreate,
  onRename,
}: {
  items?: ConversationPreview[];
  onOpenConversation: (item: ConversationPreview) => void;
  onCreate: () => void;
  onRename: (item: ConversationPreview, title: string) => Promise<void>;
}) {
  const [titleOverrides, setTitleOverrides] = useState<Record<string, string>>(
    {},
  );
  const [editingChat, setEditingChat] = useState<ConversationPreview | null>(
    null,
  );
  const [draftTitle, setDraftTitle] = useState("");
  const openTitleEditor = (item: ConversationPreview) => {
    setEditingChat(item);
    setDraftTitle(titleOverrides[item.id] ?? item.title);
  };
  const saveTitle = async () => {
    if (!editingChat || !draftTitle.trim()) return;
    const title = draftTitle.trim();
    try {
      await onRename(editingChat, title);
      setTitleOverrides((current) => ({ ...current, [editingChat.id]: title }));
      setEditingChat(null);
    } catch (error) {
      Alert.alert(
        "Не удалось сохранить название",
        error instanceof Error ? error.message : "Повторите попытку.",
      );
    }
  };
  return (
    <ScreenContainer
      edges={["top", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <View style={styles.flex}>
        <FlatList
          data={items}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.listContent}
          renderItem={({ item }) => (
            <Pressable
              onPress={() => onOpenConversation(item)}
              style={({ pressed }) => [
                styles.chatCard,
                pressed && styles.pressed,
              ]}
            >
              {item.participants ? (
                <View style={styles.chatAvatarStack}>
                  {item.participants.slice(0, 2).map((participant, index) => (
                    <View
                      key={participant.id}
                      style={[
                        styles.chatAvatarStackItem,
                        {
                          marginLeft: index === 0 ? 0 : -18,
                          zIndex: 2 - index,
                        },
                      ]}
                    >
                      <Avatar entity={participant} size={48} />
                    </View>
                  ))}
                </View>
              ) : (
                <Avatar entity={item.character} size={52} />
              )}
              <View style={styles.chatCopy}>
                <View style={styles.rowBetween}>
                  <Text style={styles.chatTitle} numberOfLines={1}>
                    {titleOverrides[item.id] ?? item.title}
                  </Text>
                  <Text style={styles.time}>{item.time}</Text>
                </View>
                <Text style={styles.chatPreview} numberOfLines={1}>
                  {item.preview}
                </Text>
              </View>
              <Pressable
                onPress={(event) => {
                  event.stopPropagation();
                  openTitleEditor(item);
                }}
                style={({ pressed }) => [
                  styles.chatEditButton,
                  pressed && styles.pressed,
                ]}
              >
                <MaterialIcons name="edit" size={17} color={C.lavender} />
              </Pressable>
              <MaterialIcons name="chevron-right" size={21} color={C.muted} />
            </Pressable>
          )}
        />
        {editingChat ? (
          <Modal
            transparent
            visible
            animationType="fade"
            onRequestClose={() => setEditingChat(null)}
          >
            <KeyboardAvoidingView
              style={styles.modalKeyboard}
              behavior="padding"
            >
              <Pressable
                style={styles.modalBackdrop}
                onPress={() => setEditingChat(null)}
              >
                <View
                  style={styles.inlineEditSheet}
                  onStartShouldSetResponder={() => true}
                >
                  <Text style={styles.sheetEyebrow}>РАЗГОВОР</Text>
                  <Text style={styles.sheetTitle}>Изменить название</Text>
                  <TextInput
                    value={draftTitle}
                    onChangeText={setDraftTitle}
                    autoFocus
                    placeholder="Название истории"
                    placeholderTextColor="#68758A"
                    style={styles.fieldInput}
                  />
                  <View style={styles.inlineEditActions}>
                    <Pressable
                      onPress={() => setEditingChat(null)}
                      style={styles.secondaryButton}
                    >
                      <Text style={styles.secondaryButtonText}>Отмена</Text>
                    </Pressable>
                    <Pressable
                      onPress={() => void saveTitle()}
                      style={styles.primaryButton}
                    >
                      <Text style={styles.primaryButtonText}>Сохранить</Text>
                    </Pressable>
                  </View>
                </View>
              </Pressable>
            </KeyboardAvoidingView>
          </Modal>
        ) : null}
        <Pressable
          onPress={onCreate}
          style={({ pressed }) => [styles.fab, pressed && styles.fabPressed]}
        >
          <MaterialIcons name="add" size={28} color="#2F116C" />
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

function LibraryScreen({
  characters,
  personas,
  onEdit,
  onCreate,
  onOpenCharacter,
  onOpenPersona,
}: {
  characters: LibraryEntity[];
  personas: LibraryEntity[];
  onEdit: (kind: "character" | "persona", item: LibraryEntity) => void;
  onCreate: (kind: "character" | "persona") => void;
  onOpenCharacter: (character: LibraryEntity) => void;
  onOpenPersona: (persona: LibraryEntity) => void;
}) {
  const [mode, setMode] = useState<"characters" | "personas">("characters");
  const isCharacters = mode === "characters";
  const list = isCharacters ? characters : personas;
  const kind = isCharacters ? "character" : "persona";
  return (
    <ScreenContainer
      edges={["top", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <View style={styles.flex}>
        <View style={styles.segmented}>
          <Pressable
            onPress={() => setMode("characters")}
            style={[styles.segment, isCharacters && styles.segmentActive]}
          >
            <Text
              style={[
                styles.segmentText,
                isCharacters && styles.segmentTextActive,
              ]}
            >
              Персонажи
            </Text>
          </Pressable>
          <Pressable
            onPress={() => setMode("personas")}
            style={[styles.segment, !isCharacters && styles.segmentActive]}
          >
            <Text
              style={[
                styles.segmentText,
                !isCharacters && styles.segmentTextActive,
              ]}
            >
              Персоны
            </Text>
          </Pressable>
        </View>
        <FlatList
          data={list}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.listContent}
          renderItem={({ item }) => (
            <Pressable
              onPress={() =>
                isCharacters ? onOpenCharacter(item) : onOpenPersona(item)
              }
              style={({ pressed }) => [
                styles.libraryCard,
                pressed && styles.pressed,
              ]}
            >
              <Avatar entity={item} />
              <View style={styles.characterCopy}>
                <Text style={styles.characterName}>{item.name}</Text>
                <Text style={styles.characterRole}>{item.role}</Text>
                <Text style={styles.characterDescription} numberOfLines={2}>
                  {item.description}
                </Text>
              </View>
              <Pressable
                onPress={() => onEdit(kind, item)}
                style={({ pressed }) => [
                  styles.editButton,
                  pressed && styles.pressed,
                ]}
              >
                <MaterialIcons name="edit" size={19} color={C.lavender} />
              </Pressable>
            </Pressable>
          )}
        />
        <Pressable
          onPress={() => onCreate(kind)}
          style={({ pressed }) => [styles.fab, pressed && styles.fabPressed]}
        >
          <MaterialIcons name="add" size={28} color="#2F116C" />
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

function EditorScreen({
  state,
  personas,
  promptPresets,
  lorebooks,
  onClose,
  onSave,
  onGeneratePersonaDescription,
  onGenerateEntity,
  onExpandCharacterField,
}: {
  state: EditorState;
  personas: LibraryEntity[];
  promptPresets: SoulPromptPreset[];
  lorebooks: SoulLorebookSummary[];
  onClose: () => void;
  onSave: (
    item: LibraryEntity,
    kind: "character" | "persona",
    avatar?: {
      uri: string;
      fileName?: string | null;
      mimeType?: string | null;
    },
  ) => Promise<void> | void;
  onGeneratePersonaDescription?: (idea: string) => Promise<string>;
  onGenerateEntity?: (
    kind: "character" | "persona",
    idea: string,
  ) => Promise<LibraryEntity>;
  onExpandCharacterField?: (
    characterId: string,
    field: "description" | "personality" | "scenario",
  ) => Promise<LibraryEntity>;
}) {
  const existing = state.item;
  const [generatedEntity, setGeneratedEntity] = useState<LibraryEntity | null>(
    null,
  );
  const [generationIdea, setGenerationIdea] = useState("");
  const [name, setName] = useState(existing?.name ?? "");
  const [role, setRole] = useState(existing?.role ?? "");
  const [description, setDescription] = useState(existing?.description ?? "");
  const [promptText, setPromptText] = useState(existing?.promptText ?? "");
  const [personality, setPersonality] = useState(existing?.personality ?? "");
  const [scenario, setScenario] = useState(existing?.scenario ?? "");
  const [systemPrompt, setSystemPrompt] = useState(
    existing?.systemPrompt ?? "",
  );
  const [personalityExpressionLevel, setPersonalityExpressionLevel] = useState<
    "vivid" | "natural" | "subtle"
  >(existing?.personalityExpressionLevel ?? "natural");
  const [replyLanguage, setReplyLanguage] = useState(
    existing?.replyLanguage ?? "Русский",
  );
  const [useRoleplayResponseFormatting, setUseRoleplayResponseFormatting] =
    useState(existing?.useRoleplayResponseFormatting ?? false);
  const [defaultUserProfile, setDefaultUserProfile] = useState(
    existing?.defaultUserProfile ?? "",
  );
  const [defaultRelationshipContext, setDefaultRelationshipContext] = useState(
    existing?.defaultRelationshipContext ?? "",
  );
  const [exampleDialogue, setExampleDialogue] = useState(
    existing?.exampleDialogue ?? "",
  );
  const [selectedPromptPresetId, setSelectedPromptPresetId] = useState<
    string | null
  >(existing?.selectedPromptPresetId ?? null);
  const [lorebookIds, setLorebookIds] = useState<string[]>(
    existing?.lorebookIds ?? [],
  );
  const [selectedPersonaId, setSelectedPersonaId] = useState<string | null>(
    existing?.selectedPersonaId ?? null,
  );
  const [cognitiveArchitectureEnabled, setCognitiveArchitectureEnabled] =
    useState(existing?.cognitiveArchitectureEnabled ?? false);
  const [soulMemoryEnabled, setSoulMemoryEnabled] = useState(
    existing?.soulMemoryEnabled ?? true,
  );
  const [autoSummaryEnabled, setAutoSummaryEnabled] = useState(
    existing?.autoSummaryEnabled ?? true,
  );
  const [soulMemoryPreset, setSoulMemoryPreset] = useState(
    SOUL_MEMORY_PRESETS.some(
      (preset) => preset.id === existing?.soulMemoryPreset,
    )
      ? (existing?.soulMemoryPreset ?? "full")
      : "full",
  );
  const [soulMemoryInterval, setSoulMemoryInterval] = useState(
    String(existing?.soulMemoryIntervalMessages ?? 12),
  );
  const [autoSummaryInterval, setAutoSummaryInterval] = useState(
    String(existing?.autoSummaryIntervalMessages ?? 12),
  );
  const [proactiveMessagesEnabled, setProactiveMessagesEnabled] = useState(
    existing?.proactiveMessagesEnabled ?? false,
  );
  const [proactiveQuietHoursEnabled, setProactiveQuietHoursEnabled] = useState(
    existing?.proactiveQuietHoursEnabled ?? true,
  );
  const [proactiveQuietHoursStart, setProactiveQuietHoursStart] = useState(
    existing?.proactiveQuietHoursStart ?? "23:00",
  );
  const [proactiveQuietHoursEnd, setProactiveQuietHoursEnd] = useState(
    existing?.proactiveQuietHoursEnd ?? "08:00",
  );
  const [realisticMessagingEnabled, setRealisticMessagingEnabled] = useState(
    existing?.realisticMessagingEnabled ?? false,
  );
  const [isGenerating, setIsGenerating] = useState(false);
  const [generatingField, setGeneratingField] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [avatar, setAvatar] = useState<
    | { uri: string; fileName?: string | null; mimeType?: string | null }
    | undefined
  >();
  const isCharacter = state.kind === "character";
  useSystemBack(onClose);
  const save = async () => {
    if (isSaving) return;
    const cleanName =
      name.trim() || (isCharacter ? "Новый персонаж" : "Новая персона");
    try {
      setIsSaving(true);
      await onSave(
        {
          id:
            existing?.id ??
            generatedEntity?.id ??
            `${state.kind}-${Date.now()}`,
          name: cleanName,
          role: role.trim() || "Без роли",
          description: description.trim() || "Описание появится здесь.",
          glyph:
            existing?.glyph ??
            generatedEntity?.glyph ??
            (isCharacter ? "✦" : "◌"),
          tint:
            existing?.tint ??
            generatedEntity?.tint ??
            (isCharacter ? "#6E7DAA" : "#7566A4"),
          affinity: existing?.affinity ?? generatedEntity?.affinity ?? 0,
          avatarUrl:
            avatar?.uri ?? existing?.avatarUrl ?? generatedEntity?.avatarUrl,
          promptText: promptText.trim(),
          personality: personality.trim(),
          scenario: scenario.trim(),
          systemPrompt: systemPrompt.trim(),
          personalityExpressionLevel,
          replyLanguage: replyLanguage.trim() || "Русский",
          useRoleplayResponseFormatting,
          defaultUserProfile: defaultUserProfile.trim(),
          defaultRelationshipContext: defaultRelationshipContext.trim(),
          exampleDialogue: exampleDialogue.trim(),
          selectedPromptPresetId,
          lorebookIds,
          cognitiveArchitectureEnabled,
          soulMemoryEnabled,
          soulMemoryPreset,
          soulMemoryIntervalMessages: Math.max(
            1,
            Number.parseInt(soulMemoryInterval, 10) || 12,
          ),
          autoSummaryEnabled,
          autoSummaryIntervalMessages: Math.max(
            1,
            Number.parseInt(autoSummaryInterval, 10) || 12,
          ),
          proactiveMessagesEnabled,
          proactiveQuietHoursEnabled,
          proactiveQuietHoursStart: proactiveQuietHoursStart.trim() || "23:00",
          proactiveQuietHoursEnd: proactiveQuietHoursEnd.trim() || "08:00",
          realisticMessagingEnabled,
          selectedPersonaId,
        },
        state.kind,
        avatar,
      );
    } finally {
      setIsSaving(false);
    }
  };
  const pickAvatar = async () => {
    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(
        "Нужен доступ к фото",
        "Разрешите доступ к галерее, чтобы выбрать аватар.",
      );
      return;
    }
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ["images"],
      allowsEditing: true,
      aspect: [1, 1],
      quality: 0.9,
    });
    if (!result.canceled) {
      const asset = result.assets[0];
      setAvatar({
        uri: asset.uri,
        fileName: asset.fileName,
        mimeType: asset.mimeType,
      });
    }
  };
  const generatePersonaDescription = async () => {
    if (!description.trim()) {
      Alert.alert(
        "Добавьте основу",
        "Сначала напишите несколько фактов в поле «Описание».",
      );
      return;
    }
    if (!onGeneratePersonaDescription) {
      Alert.alert(
        "Нужен SoulExe Desktop",
        "Генерация описания работает после подключения к запущенному SoulExe Desktop.",
      );
      return;
    }
    try {
      setIsGenerating(true);
      setDescription(await onGeneratePersonaDescription(description.trim()));
    } catch (error) {
      Alert.alert(
        "Не удалось сгенерировать",
        error instanceof Error ? error.message : "Повторите попытку.",
      );
    } finally {
      setIsGenerating(false);
    }
  };
  const generateFullEntity = async () => {
    if (!generationIdea.trim()) {
      Alert.alert(
        "Добавьте идею",
        isCharacter
          ? "Кратко опишите, какого персонажа нужно создать."
          : "Кратко опишите свою персону.",
      );
      return;
    }
    if (!onGenerateEntity) {
      Alert.alert(
        "Нужен SoulExe Desktop",
        "ИИ-генерация работает после подключения к ПК с запущенной моделью.",
      );
      return;
    }
    try {
      setIsGenerating(true);
      const generated = await onGenerateEntity(
        state.kind,
        generationIdea.trim(),
      );
      setGeneratedEntity(generated);
      setName(generated.name);
      setRole(generated.role);
      setDescription(generated.description);
      setPromptText(generated.promptText ?? "");
      setPersonality(generated.personality ?? "");
      setScenario(generated.scenario ?? "");
      setSystemPrompt(generated.systemPrompt ?? "");
    } catch (error) {
      Alert.alert(
        "Не удалось сгенерировать",
        error instanceof Error ? error.message : "Повторите попытку.",
      );
    } finally {
      setIsGenerating(false);
    }
  };
  const expandCharacterField = async (
    field: "description" | "personality" | "scenario",
  ) => {
    const characterId = existing?.id ?? generatedEntity?.id;
    if (!characterId || !onExpandCharacterField) {
      Alert.alert(
        "Сначала создайте персонажа",
        "Для нового персонажа сначала используйте полную ИИ-генерацию или сохраните карточку.",
      );
      return;
    }
    try {
      setGeneratingField(field);
      const updated = await onExpandCharacterField(characterId, field);
      setGeneratedEntity(updated);
      if (field === "description") setDescription(updated.description);
      if (field === "personality") setPersonality(updated.personality ?? "");
      if (field === "scenario") setScenario(updated.scenario ?? "");
    } catch (error) {
      Alert.alert(
        "Не удалось дополнить поле",
        error instanceof Error ? error.message : "Повторите попытку.",
      );
    } finally {
      setGeneratingField(null);
    }
  };
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#020617]"
    >
      <KeyboardAvoidingView style={styles.flex} behavior="padding">
        <TopBar
          title={existing ? "Редактирование" : "Создание"}
          onBack={onClose}
        />
        <ScrollView
          contentContainerStyle={styles.editorContent}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
        >
          <View style={styles.editorHero}>
            <Pressable onPress={() => void pickAvatar()}>
              <Avatar
                entity={{
                  id: "draft",
                  name,
                  role,
                  description,
                  glyph: existing?.glyph ?? (isCharacter ? "✦" : "◌"),
                  tint: existing?.tint ?? (isCharacter ? "#6E7DAA" : "#7566A4"),
                  affinity: 0,
                  avatarUrl: avatar?.uri ?? existing?.avatarUrl,
                }}
                size={88}
              />
            </Pressable>
            <Pressable
              onPress={() => void pickAvatar()}
              style={styles.avatarAction}
            >
              <MaterialIcons name="photo-camera" size={16} color={C.lavender} />
              <Text style={styles.avatarActionText}>Изменить фото</Text>
            </Pressable>
            <Text style={styles.editorKind}>
              {isCharacter ? "ПЕРСОНАЖ" : "ПЕРСОНА"}
            </Text>
          </View>
          {!existing && !generatedEntity ? (
            <View style={styles.aiGeneratorCard}>
              <View style={styles.aiGeneratorHeading}>
                <MaterialIcons
                  name="auto-awesome"
                  size={20}
                  color={C.lavender}
                />
                <View style={styles.settingCopy}>
                  <Text style={styles.settingTitle}>Создать с помощью ИИ</Text>
                  <Text style={styles.settingDescription}>
                    Модель на ПК заполнит основные поля, а вы сможете их
                    изменить.
                  </Text>
                </View>
              </View>
              <TextInput
                value={generationIdea}
                onChangeText={setGenerationIdea}
                placeholder={
                  isCharacter
                    ? "Например: уставший наёмник с добрым характером"
                    : "Например: спокойный и любознательный путешественник"
                }
                placeholderTextColor="#68758A"
                multiline
                style={styles.aiIdeaInput}
              />
              <Pressable
                onPress={() => void generateFullEntity()}
                disabled={isGenerating}
                style={({ pressed }) => [
                  styles.generateButton,
                  (pressed || isGenerating) && styles.pressed,
                ]}
              >
                {isGenerating ? (
                  <ActivityIndicator size="small" color={C.lavender} />
                ) : (
                  <MaterialIcons
                    name="auto-awesome"
                    size={18}
                    color={C.lavender}
                  />
                )}
                <Text style={styles.generateButtonText}>
                  {isGenerating ? "Генерация…" : "Сгенерировать карточку"}
                </Text>
              </Pressable>
            </View>
          ) : null}
          <Field
            label="Имя"
            value={name}
            onChangeText={setName}
            placeholder={isCharacter ? "Например, Элара" : "Например, Кай"}
          />
          {isCharacter && (
            <Field
              label="Подзаголовок"
              value={role}
              onChangeText={setRole}
              placeholder="Роль или архетип"
            />
          )}
          <Field
            label="Описание"
            value={description}
            onChangeText={setDescription}
            placeholder="Коротко опиши характер и контекст"
            multiline
          />
          {isCharacter ? (
            <Pressable
              onPress={() => void expandCharacterField("description")}
              disabled={generatingField !== null}
              style={styles.fieldGenerateButton}
            >
              {generatingField === "description" ? (
                <ActivityIndicator size="small" color={C.lavender} />
              ) : (
                <MaterialIcons
                  name="auto-awesome"
                  size={17}
                  color={C.lavender}
                />
              )}
              <Text style={styles.fieldGenerateText}>
                Дополнить описание с ИИ
              </Text>
            </Pressable>
          ) : null}
          {!isCharacter && (
            <Pressable
              onPress={() => void generatePersonaDescription()}
              disabled={isGenerating}
              style={({ pressed }) => [
                styles.generateButton,
                (pressed || isGenerating) && styles.pressed,
              ]}
            >
              {isGenerating ? (
                <ActivityIndicator size="small" color={C.lavender} />
              ) : (
                <MaterialIcons
                  name="auto-awesome"
                  size={18}
                  color={C.lavender}
                />
              )}
              <Text style={styles.generateButtonText}>
                {isGenerating ? "Генерация…" : "Сгенерировать описание"}
              </Text>
            </Pressable>
          )}
          {isCharacter ? (
            <>
              <Field
                label="Личность"
                value={personality}
                onChangeText={setPersonality}
                placeholder="Черты, привычки, манера общения"
                multiline
                maxLength={1000}
              />
              <Pressable
                onPress={() => void expandCharacterField("personality")}
                disabled={generatingField !== null}
                style={styles.fieldGenerateButton}
              >
                {generatingField === "personality" ? (
                  <ActivityIndicator size="small" color={C.lavender} />
                ) : (
                  <MaterialIcons
                    name="auto-awesome"
                    size={17}
                    color={C.lavender}
                  />
                )}
                <Text style={styles.fieldGenerateText}>
                  Дополнить личность с ИИ
                </Text>
              </Pressable>
              <ChoiceSelector
                label="Выразительность личности"
                value={personalityExpressionLevel}
                options={[
                  { value: "vivid", label: "Ярко" },
                  { value: "natural", label: "Естественно" },
                  { value: "subtle", label: "Сдержанно" },
                ]}
                onChange={(value) =>
                  setPersonalityExpressionLevel(
                    value as "vivid" | "natural" | "subtle",
                  )
                }
              />
              <Field
                label="Исходные факты о пользователе"
                value={defaultUserProfile}
                onChangeText={setDefaultUserProfile}
                placeholder="Что персонаж должен знать о пользователе в начале истории"
                multiline
                maxLength={1600}
              />
              <Field
                label="Исходные отношения"
                value={defaultRelationshipContext}
                onChangeText={setDefaultRelationshipContext}
                placeholder="Кем персонаж и пользователь приходятся друг другу"
                multiline
                maxLength={1600}
              />
              <Field
                label="Сценарий"
                value={scenario}
                onChangeText={setScenario}
                placeholder="Стартовый контекст и мир истории"
                multiline
                maxLength={1000}
              />
              <Pressable
                onPress={() => void expandCharacterField("scenario")}
                disabled={generatingField !== null}
                style={styles.fieldGenerateButton}
              >
                {generatingField === "scenario" ? (
                  <ActivityIndicator size="small" color={C.lavender} />
                ) : (
                  <MaterialIcons
                    name="auto-awesome"
                    size={17}
                    color={C.lavender}
                  />
                )}
                <Text style={styles.fieldGenerateText}>
                  Дополнить сценарий с ИИ
                </Text>
              </Pressable>
              <Field
                label="Инструкция для диалога"
                value={systemPrompt}
                onChangeText={setSystemPrompt}
                placeholder="Дополнительные правила для персонажа"
                multiline
                maxLength={1400}
              />
              <Field
                label="Язык ответа"
                value={replyLanguage}
                onChangeText={setReplyLanguage}
                placeholder="Русский, English или Любой язык"
              />
              <EditorToggle
                title="Ролевое оформление ответа"
                hint="Разделять действия, мысли и прямую речь по правилам SoulExe."
                value={useRoleplayResponseFormatting}
                onValueChange={setUseRoleplayResponseFormatting}
              />
              <Field
                label="Пример диалога"
                value={exampleDialogue}
                onChangeText={setExampleDialogue}
                placeholder="Покажи желаемую манеру реплик персонажа"
                multiline
                maxLength={2400}
              />
              <View style={styles.editorSection}>
                <Text style={styles.editorSectionTitle}>
                  Персона для диалога
                </Text>
                <Text style={styles.editorSectionHint}>
                  Выбранная персона будет использоваться в новых историях с этим
                  персонажем.
                </Text>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.personaChoiceRow}
                >
                  <Pressable
                    onPress={() => setSelectedPersonaId(null)}
                    style={[
                      styles.personaChoice,
                      !selectedPersonaId && styles.personaChoiceActive,
                    ]}
                  >
                    <MaterialIcons
                      name="person-outline"
                      size={20}
                      color={!selectedPersonaId ? "#2F116C" : C.lavender}
                    />
                    <Text
                      style={[
                        styles.personaChoiceText,
                        !selectedPersonaId && styles.personaChoiceTextActive,
                      ]}
                    >
                      Не выбрана
                    </Text>
                  </Pressable>
                  {personas.map((persona) => {
                    const active = persona.id === selectedPersonaId;
                    return (
                      <Pressable
                        key={persona.id}
                        onPress={() => setSelectedPersonaId(persona.id)}
                        style={[
                          styles.personaChoice,
                          active && styles.personaChoiceActive,
                        ]}
                      >
                        <Avatar entity={persona} size={26} />
                        <Text
                          numberOfLines={1}
                          style={[
                            styles.personaChoiceText,
                            active && styles.personaChoiceTextActive,
                          ]}
                        >
                          {persona.name}
                        </Text>
                      </Pressable>
                    );
                  })}
                </ScrollView>
              </View>
              <View style={styles.editorSection}>
                <Text style={styles.editorSectionTitle}>Лорбуки</Text>
                <Text style={styles.editorSectionHint}>
                  Подключи знания мира к персонажу. Содержимое лорбуков
                  редактируется в библиотеке ПК.
                </Text>
                {lorebooks.length ? (
                  lorebooks.map((lorebook) => (
                    <EditorToggle
                      key={lorebook.id}
                      title={lorebook.name}
                      hint={`${lorebook.entriesCount} записей${lorebook.description ? ` · ${lorebook.description}` : ""}`}
                      value={lorebookIds.includes(lorebook.id)}
                      onValueChange={(enabled) =>
                        setLorebookIds((current) =>
                          enabled
                            ? [
                                ...current.filter((id) => id !== lorebook.id),
                                lorebook.id,
                              ]
                            : current.filter((id) => id !== lorebook.id),
                        )
                      }
                    />
                  ))
                ) : (
                  <Text style={styles.editorEmptyText}>
                    На ПК пока нет созданных лорбуков.
                  </Text>
                )}
              </View>
              <View style={styles.editorSection}>
                <Text style={styles.editorSectionTitle}>Пресет инструкции</Text>
                <Text style={styles.editorSectionHint}>
                  Тот же набор системных инструкций, который выбирается в
                  редакторе ПК.
                </Text>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.personaChoiceRow}
                >
                  <Pressable
                    onPress={() => setSelectedPromptPresetId(null)}
                    style={[
                      styles.personaChoice,
                      !selectedPromptPresetId && styles.personaChoiceActive,
                    ]}
                  >
                    <Text
                      style={[
                        styles.personaChoiceText,
                        !selectedPromptPresetId &&
                          styles.personaChoiceTextActive,
                      ]}
                    >
                      По умолчанию
                    </Text>
                  </Pressable>
                  {promptPresets.map((preset) => {
                    const active = preset.id === selectedPromptPresetId;
                    return (
                      <Pressable
                        key={preset.id}
                        onPress={() => setSelectedPromptPresetId(preset.id)}
                        style={[
                          styles.personaChoice,
                          active && styles.personaChoiceActive,
                        ]}
                      >
                        <Text
                          style={[
                            styles.personaChoiceText,
                            active && styles.personaChoiceTextActive,
                          ]}
                        >
                          {preset.name}
                        </Text>
                      </Pressable>
                    );
                  })}
                </ScrollView>
                {promptPresets.find(
                  (preset) => preset.id === selectedPromptPresetId,
                )?.description ? (
                  <Text style={styles.selectedPresetDescription}>
                    {
                      promptPresets.find(
                        (preset) => preset.id === selectedPromptPresetId,
                      )?.description
                    }
                  </Text>
                ) : null}
              </View>
              <View style={styles.editorSection}>
                <Text style={styles.editorSectionTitle}>
                  Память и поведение
                </Text>
                <EditorToggle
                  title="Когнитивная архитектура"
                  hint="Использовать расширенную внутреннюю модель персонажа."
                  value={cognitiveArchitectureEnabled}
                  onValueChange={setCognitiveArchitectureEnabled}
                />
                <EditorToggle
                  title="Память души"
                  hint="Сохранять ключевые факты для следующих сообщений."
                  value={soulMemoryEnabled}
                  onValueChange={setSoulMemoryEnabled}
                />
                {soulMemoryEnabled && (
                  <>
                    <MemoryPresetSelector
                      value={soulMemoryPreset}
                      onChange={setSoulMemoryPreset}
                    />
                    <Field
                      label="Обновлять память через сообщений"
                      value={soulMemoryInterval}
                      onChangeText={setSoulMemoryInterval}
                      placeholder="12"
                      keyboardType="number-pad"
                    />
                  </>
                )}
                <EditorToggle
                  title="Автосводка"
                  hint="Периодически сжимать историю, чтобы диалог оставался целостным."
                  value={autoSummaryEnabled}
                  onValueChange={setAutoSummaryEnabled}
                />
                {autoSummaryEnabled && (
                  <Field
                    label="Обновлять сводку через сообщений"
                    value={autoSummaryInterval}
                    onChangeText={setAutoSummaryInterval}
                    placeholder="12"
                    keyboardType="number-pad"
                  />
                )}
              </View>
              <View style={styles.editorSection}>
                <Text style={styles.editorSectionTitle}>
                  Самостоятельность в переписке
                </Text>
                <Text style={styles.editorSectionHint}>
                  Меняется только время ответа. Характер и содержание берутся из
                  текущего диалога и карточки персонажа.
                </Text>
                <EditorToggle
                  title="Инициативные сообщения"
                  hint="После тишины персонаж может написать сам: случайно через 20 минут–5 часов, не больше трёх раз в сутки."
                  value={proactiveMessagesEnabled}
                  onValueChange={setProactiveMessagesEnabled}
                />
                {proactiveMessagesEnabled && (
                  <View style={styles.editorSection}>
                    <EditorToggle
                      title="Не писать ночью"
                      hint="Во время тихих часов инициативное сообщение будет перенесено на утро."
                      value={proactiveQuietHoursEnabled}
                      onValueChange={setProactiveQuietHoursEnabled}
                    />
                    {proactiveQuietHoursEnabled && (
                      <>
                        <Field
                          label="С"
                          value={proactiveQuietHoursStart}
                          onChangeText={setProactiveQuietHoursStart}
                          placeholder="23:00"
                        />
                        <Field
                          label="До"
                          value={proactiveQuietHoursEnd}
                          onChangeText={setProactiveQuietHoursEnd}
                          placeholder="08:00"
                        />
                      </>
                    )}
                  </View>
                )}
                <EditorToggle
                  title="Реалистичная переписка"
                  hint="Ответ задерживается на 3–120 секунд. Короткие фразы до 20 символов отвечаются быстрее, длинные добавляют время на чтение. Continue и групповой автоплей не задерживаются."
                  value={realisticMessagingEnabled}
                  onValueChange={setRealisticMessagingEnabled}
                />
              </View>
            </>
          ) : (
            <Field
              label="Инструкция для диалога"
              value={promptText}
              onChangeText={setPromptText}
              placeholder="Как персонажи должны обращаться и реагировать на тебя"
              multiline
              maxLength={1200}
            />
          )}
          <View style={styles.editorHint}>
            <MaterialIcons name="auto-awesome" size={18} color={C.lavender} />
            <Text style={styles.editorHintText}>
              {isCharacter
                ? "Этот персонаж станет доступен в новых разговорах."
                : "Эта персона будет голосом игрока в выбранных историях."}
            </Text>
          </View>
        </ScrollView>
        <View style={styles.editorFooter}>
          <Pressable
            onPress={() => void save()}
            disabled={isSaving}
            style={({ pressed }) => [
              styles.primaryButton,
              (pressed || isSaving) && styles.pressed,
            ]}
          >
            <Text style={styles.primaryButtonText}>
              {isSaving
                ? "Сохранение…"
                : existing
                  ? "Сохранить изменения"
                  : `Создать ${isCharacter ? "персонажа" : "персону"}`}
            </Text>
            {isSaving ? (
              <ActivityIndicator size="small" color="#2F116C" />
            ) : (
              <MaterialIcons name="check" size={20} color="#2F116C" />
            )}
          </Pressable>
        </View>
      </KeyboardAvoidingView>
    </ScreenContainer>
  );
}

function MemoryPresetSelector({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <View style={styles.memoryPresetSection}>
      <Text style={styles.fieldLabel}>Как должна работать память</Text>
      <Text style={styles.memoryPresetIntro}>
        Выберите готовый вариант. Вводить техническое название вручную не нужно.
      </Text>
      <View style={styles.memoryPresetList}>
        {SOUL_MEMORY_PRESETS.map((preset) => {
          const active = preset.id === value;
          return (
            <Pressable
              key={preset.id}
              onPress={() => onChange(preset.id)}
              style={({ pressed }) => [
                styles.memoryPresetCard,
                active && styles.memoryPresetCardActive,
                pressed && styles.pressed,
              ]}
            >
              <MaterialIcons
                name={
                  active ? "radio-button-checked" : "radio-button-unchecked"
                }
                size={21}
                color={active ? C.lavender : C.muted}
              />
              <View style={styles.memoryPresetCopy}>
                <Text
                  style={[
                    styles.memoryPresetTitle,
                    active && styles.memoryPresetTitleActive,
                  ]}
                >
                  {preset.title}
                </Text>
                <Text style={styles.memoryPresetDescription}>
                  {preset.description}
                </Text>
              </View>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

function ChoiceSelector({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  return (
    <View style={styles.field}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <View style={styles.choiceSelector}>
        {options.map((option) => {
          const active = option.value === value;
          return (
            <Pressable
              key={option.value}
              onPress={() => onChange(option.value)}
              style={({ pressed }) => [
                styles.choiceSelectorItem,
                active && styles.choiceSelectorItemActive,
                pressed && styles.pressed,
              ]}
            >
              <Text
                style={[
                  styles.choiceSelectorText,
                  active && styles.choiceSelectorTextActive,
                ]}
              >
                {option.label}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

function EditorToggle({
  title,
  hint,
  value,
  onValueChange,
}: {
  title: string;
  hint: string;
  value: boolean;
  onValueChange: (next: boolean) => void;
}) {
  return (
    <View style={styles.editorToggle}>
      <View style={styles.editorToggleCopy}>
        <Text style={styles.editorToggleTitle}>{title}</Text>
        <Text style={styles.editorToggleHint}>{hint}</Text>
      </View>
      <Switch
        value={value}
        onValueChange={onValueChange}
        trackColor={{ false: "#344159", true: C.violet }}
        thumbColor={value ? C.lavender : "#D7DDEA"}
      />
    </View>
  );
}

function Field({
  label,
  value,
  onChangeText,
  placeholder,
  multiline = false,
  maxLength,
  keyboardType,
}: {
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  multiline?: boolean;
  maxLength?: number;
  keyboardType?: "default" | "number-pad";
}) {
  return (
    <View style={styles.field}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor="#68758A"
        style={[styles.fieldInput, multiline && styles.fieldInputMultiline]}
        multiline={multiline}
        maxLength={maxLength ?? (multiline ? 600 : 80)}
        keyboardType={keyboardType}
      />
    </View>
  );
}

function NewChatTypeScreen({
  onBack,
  onChoose,
}: {
  onBack: () => void;
  onChoose: (type: "personal" | "group") => void;
}) {
  useSystemBack(onBack);
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#020617]"
    >
      <TopBar title="Новый разговор" onBack={onBack} />
      <View style={styles.newChatIntro}>
        <Text style={styles.eyebrow}>SOUL exe / NEW SESSION</Text>
        <Text style={styles.newChatTitle}>Выберите формат истории</Text>
        <Text style={styles.newChatText}>
          Реши, кто будет рядом с тобой в следующей сцене.
        </Text>
      </View>
      <View style={styles.typeCards}>
        <Pressable
          onPress={() => onChoose("personal")}
          style={({ pressed }) => [styles.typeCard, pressed && styles.pressed]}
        >
          <View style={styles.typeIcon}>
            <MaterialIcons name="person" size={27} color={C.lavender} />
          </View>
          <View style={styles.typeCopy}>
            <Text style={styles.typeTitle}>Личный разговор</Text>
            <Text style={styles.typeDescription}>
              Один персонаж. История, настроенная только под тебя.
            </Text>
          </View>
          <MaterialIcons name="chevron-right" size={23} color={C.muted} />
        </Pressable>
        <Pressable
          onPress={() => onChoose("group")}
          style={({ pressed }) => [styles.typeCard, pressed && styles.pressed]}
        >
          <View style={styles.typeIcon}>
            <MaterialIcons name="groups" size={27} color={C.lavender} />
          </View>
          <View style={styles.typeCopy}>
            <Text style={styles.typeTitle}>Групповой разговор</Text>
            <Text style={styles.typeDescription}>
              Несколько персонажей и больше неожиданных поворотов.
            </Text>
          </View>
          <MaterialIcons name="chevron-right" size={23} color={C.muted} />
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

function PersonalChatSetup({
  characters,
  onBack,
  onCreate,
}: {
  characters: LibraryEntity[];
  onBack: () => void;
  onCreate: (entity: LibraryEntity) => Promise<void> | void;
}) {
  const [selectedId, setSelectedId] = useState(characters[0]?.id);
  const selected =
    characters.find((item) => item.id === selectedId) ?? characters[0];
  useSystemBack(onBack);
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <TopBar title="Личный разговор" onBack={onBack} />
      <View style={styles.setupIntro}>
        <Text style={styles.eyebrow}>ЛИЧНАЯ ИСТОРИЯ</Text>
        <Text style={styles.setupTitle}>Выбери собеседника</Text>
        <Text style={styles.setupText}>
          Сцена будет развиваться вокруг одного персонажа.
        </Text>
      </View>
      <FlatList
        data={characters}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.listContent}
        renderItem={({ item }) => (
          <Pressable
            onPress={() => setSelectedId(item.id)}
            style={[
              styles.selectCard,
              selectedId === item.id && styles.selectCardActive,
            ]}
          >
            <Avatar entity={item} size={49} />
            <View style={styles.characterCopy}>
              <Text style={styles.characterName}>{item.name}</Text>
              <Text style={styles.characterRole}>{item.role}</Text>
            </View>
            <MaterialIcons
              name={
                selectedId === item.id
                  ? "radio-button-checked"
                  : "radio-button-unchecked"
              }
              size={23}
              color={selectedId === item.id ? C.lavender : C.muted}
            />
          </Pressable>
        )}
      />
      <View style={styles.setupFooter}>
        <Pressable
          onPress={() => {
            if (selected) void onCreate(selected);
          }}
          style={styles.primaryButton}
        >
          <Text style={styles.primaryButtonText}>Создать личный чат</Text>
          <MaterialIcons name="arrow-forward" size={20} color="#2F116C" />
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

function GroupChatSetup({
  characters,
  onBack,
  onCreate,
}: {
  characters: LibraryEntity[];
  onBack: () => void;
  onCreate: (
    participants: LibraryEntity[],
    details: NewConversationDetails,
  ) => Promise<void> | void;
}) {
  const [selectedIds, setSelectedIds] = useState<string[]>(
    characters.slice(0, 2).map((item) => item.id),
  );
  const [title, setTitle] = useState("Новая история");
  const [scenario, setScenario] = useState("");
  const [place, setPlace] = useState("");
  const [mood, setMood] = useState("");
  const [goal, setGoal] = useState("");
  const [delay, setDelay] = useState("10");
  const [followScript, setFollowScript] = useState(true);
  const [developPlot, setDevelopPlot] = useState(true);
  const toggle = (id: string) =>
    setSelectedIds((current) =>
      current.includes(id)
        ? current.filter((value) => value !== id)
        : current.length < 2
          ? [...current, id]
          : current,
    );
  const selected = characters.filter((item) => selectedIds.includes(item.id));
  useSystemBack(onBack);
  const submit = () => {
    if (selected.length === 2)
      void onCreate(selected, {
        name: title.trim() || "Новая история",
        scenario,
        location: place,
        mood,
        goal,
        delaySeconds: Number(delay),
        enforceContract: followScript,
        advanceAndAvoidRepetition: developPlot,
      });
  };
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <KeyboardAvoidingView style={styles.flex} behavior="padding">
        <TopBar title="Групповой разговор" onBack={onBack} />
        <ScrollView
          contentContainerStyle={styles.groupForm}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
        >
          <View style={styles.setupIntro}>
            <Text style={styles.eyebrow}>ГРУППОВАЯ ИСТОРИЯ</Text>
            <Text style={styles.setupTitle}>Собери сцену</Text>
            <Text style={styles.setupText}>
              Выбери ровно двух персонажей и задай направление истории.
            </Text>
          </View>
          <Field
            label="Название разговора"
            value={title}
            onChangeText={setTitle}
            placeholder="Например, Ночная встреча"
          />
          <Text style={styles.formSectionLabel}>УЧАСТНИКИ · 2</Text>
          <View style={styles.groupParticipants}>
            {characters.map((item) => (
              <Pressable
                key={item.id}
                onPress={() => toggle(item.id)}
                style={[
                  styles.selectCard,
                  selectedIds.includes(item.id) && styles.selectCardActive,
                ]}
              >
                <Avatar entity={item} size={46} />
                <View style={styles.characterCopy}>
                  <Text style={styles.characterName}>{item.name}</Text>
                  <Text style={styles.characterRole}>{item.role}</Text>
                </View>
                <MaterialIcons
                  name={
                    selectedIds.includes(item.id)
                      ? "check-circle"
                      : "radio-button-unchecked"
                  }
                  size={23}
                  color={selectedIds.includes(item.id) ? C.lavender : C.muted}
                />
              </Pressable>
            ))}
          </View>
          <Text style={styles.formSectionLabel}>КОНТЕКСТ ИСТОРИИ</Text>
          <Field
            label="С чего начинается разговор"
            value={scenario}
            onChangeText={setScenario}
            placeholder="Опиши первый момент сцены"
            multiline
          />
          <Field
            label="Место"
            value={place}
            onChangeText={setPlace}
            placeholder="Например, старая станция"
          />
          <Field
            label="Настроение"
            value={mood}
            onChangeText={setMood}
            placeholder="Например, тревожное и тихое"
          />
          <Field
            label="Цель сцены"
            value={goal}
            onChangeText={setGoal}
            placeholder="Чего должны добиться участники?"
          />
          <Text style={styles.formSectionLabel}>ПОВЕДЕНИЕ ИСТОРИИ</Text>
          <View style={styles.behaviorCard}>
            <View style={styles.behaviorRow}>
              <View style={styles.typeCopy}>
                <Text style={styles.behaviorTitle}>Задержка реплик</Text>
                <Text style={styles.behaviorText}>
                  Секунды между автоматическими ответами
                </Text>
              </View>
              <View style={styles.delayPill}>
                <Text style={styles.delayText}>{delay} сек</Text>
              </View>
            </View>
            <View style={styles.delayButtons}>
              {["5", "10", "20"].map((value) => (
                <Pressable
                  key={value}
                  onPress={() => setDelay(value)}
                  style={[
                    styles.delayButton,
                    delay === value && styles.delayButtonActive,
                  ]}
                >
                  <Text
                    style={[
                      styles.delayButtonText,
                      delay === value && styles.delayButtonTextActive,
                    ]}
                  >
                    {value}
                  </Text>
                </Pressable>
              ))}
            </View>
            <View style={styles.behaviorRow}>
              <View style={styles.typeCopy}>
                <Text style={styles.behaviorTitle}>Соблюдать сценарий</Text>
                <Text style={styles.behaviorText}>
                  Не отходить от заданного контекста
                </Text>
              </View>
              <Switch
                value={followScript}
                onValueChange={setFollowScript}
                trackColor={{ false: "#324158", true: C.violet }}
                thumbColor={C.text}
              />
            </View>
            <View style={styles.behaviorRow}>
              <View style={styles.typeCopy}>
                <Text style={styles.behaviorTitle}>Развивать сюжет</Text>
                <Text style={styles.behaviorText}>
                  Позволить истории двигаться самостоятельно
                </Text>
              </View>
              <Switch
                value={developPlot}
                onValueChange={setDevelopPlot}
                trackColor={{ false: "#324158", true: C.violet }}
                thumbColor={C.text}
              />
            </View>
          </View>
          <Pressable
            disabled={selected.length !== 2}
            onPress={submit}
            style={[
              styles.primaryButton,
              selected.length !== 2 && styles.disabledButton,
            ]}
          >
            <Text style={styles.primaryButtonText}>
              Создать групповой разговор
            </Text>
            <MaterialIcons name="arrow-forward" size={20} color="#2F116C" />
          </Pressable>
        </ScrollView>
      </KeyboardAvoidingView>
    </ScreenContainer>
  );
}

function CharacterProfile({
  character,
  onBack,
  onEdit,
}: {
  character: LibraryEntity;
  onBack: () => void;
  onEdit?: () => void;
}) {
  useSystemBack(onBack);
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <TopBar
        title={
          character.role === "Персона" ? "Профиль персоны" : "Профиль персонажа"
        }
        onBack={onBack}
        rightIcon="edit"
        onRightPress={onEdit}
      />
      <ScrollView contentContainerStyle={styles.profileContent}>
        <View style={styles.profileHero}>
          <Avatar entity={character} size={112} />
          <Text style={styles.profileName}>{character.name}</Text>
          <Text style={styles.profileRole}>{character.role}</Text>
        </View>
        <View style={styles.profileSection}>
          <Text style={styles.profileLabel}>ОПИСАНИЕ</Text>
          <Text style={styles.profileBody}>{character.description}</Text>
        </View>
        {character.personality?.trim() ? (
          <View style={styles.profileSection}>
            <Text style={styles.profileLabel}>ЛИЧНОСТЬ</Text>
            <Text style={styles.profileBody}>{character.personality}</Text>
          </View>
        ) : null}
        {character.promptText?.trim() ? (
          <View style={styles.profileSection}>
            <Text style={styles.profileLabel}>ИНСТРУКЦИЯ ДЛЯ ДИАЛОГА</Text>
            <Text style={styles.profileBody}>{character.promptText}</Text>
          </View>
        ) : null}
      </ScrollView>
    </ScreenContainer>
  );
}

function ConversationScreen({
  character,
  onBack,
  participants = [character],
  personas = initialPersonas,
  conversationTitle,
  textFormatting = defaultTextFormatting,
  chatBackground = "midnight",
  messageStyle = "glass",
  fontSize = 16,
  initialMessages,
  initialTurnState,
  session,
  conversationId,
  isDemo = false,
  onRemoteConversation,
  onEditCharacter,
}: {
  character: LibraryEntity;
  onBack: () => void;
  participants?: LibraryEntity[];
  personas?: LibraryEntity[];
  conversationTitle?: string;
  textFormatting?: TextFormatting;
  chatBackground?: ChatBackgroundId;
  messageStyle?: MessageStyleId;
  fontSize?: number;
  initialMessages?: Message[];
  initialTurnState?: SoulConversation["turnState"];
  session?: SoulExeSession | null;
  conversationId?: string;
  isDemo?: boolean;
  onRemoteConversation?: (conversation: SoulConversation) => void;
  onEditCharacter?: (character: LibraryEntity) => void;
}) {
  const isGroup = participants.length > 1;
  const messageListRef = useRef<KeyboardChatScrollViewRef | null>(null);
  const isNearLatestRef = useRef(true);
  const didInitialScrollRef = useRef(false);
  const lastSyncedConversationRef = useRef<string | null>(null);
  const [messages, setMessages] = useState<Message[]>(
    initialMessages ?? (isDemo ? openingMessages : []),
  );
  const [input, setInput] = useState("");
  const [paused, setPaused] = useState(false);
  const [typing, setTyping] = useState(false);
  const [isRequestingNext, setIsRequestingNext] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [groupHeaderOpen, setGroupHeaderOpen] = useState(false);
  const [participantProfile, setParticipantProfile] =
    useState<LibraryEntity | null>(null);
  const [authorSheetOpen, setAuthorSheetOpen] = useState(false);
  const [authorMode, setAuthorMode] = useState<"persona" | "director">(
    "persona",
  );
  const [selectedPersona, setSelectedPersona] = useState<LibraryEntity | null>(
    null,
  );
  const [turnState, setTurnState] = useState(initialTurnState);
  const [secondsUntilNext, setSecondsUntilNext] = useState<number | null>(null);
  const [showJumpToLatest, setShowJumpToLatest] = useState(false);
  const scrollToLatest = useCallback((animated = true) => {
    requestAnimationFrame(() =>
      messageListRef.current?.scrollToEnd({ animated }),
    );
  }, []);
  useSystemBack(onBack);
  useEffect(() => {
    // A remote empty conversation is really empty. Sample dialogue belongs
    // solely to the explicit offline demo mode.
    setMessages(initialMessages ?? (isDemo ? openingMessages : []));
    // A newly opened conversation always starts at the newest message. Later
    // remote updates honour the reader's own scroll position.
    if (
      !didInitialScrollRef.current &&
      (isDemo || initialMessages !== undefined)
    ) {
      didInitialScrollRef.current = true;
      isNearLatestRef.current = true;
      const timer = setTimeout(() => scrollToLatest(false), 0);
      return () => clearTimeout(timer);
    }
  }, [initialMessages, isDemo, scrollToLatest]);
  useEffect(() => {
    setTurnState(initialTurnState);
  }, [initialTurnState]);
  useEffect(() => {
    const update = () => {
      if (turnState?.status !== "running" || !turnState.nextTurnAt) {
        setSecondsUntilNext(null);
        return;
      }
      setSecondsUntilNext(
        Math.max(
          0,
          Math.ceil(
            (new Date(turnState.nextTurnAt).getTime() - Date.now()) / 1000,
          ),
        ),
      );
    };
    update();
    const timer = setInterval(update, 500);
    return () => clearInterval(timer);
  }, [turnState]);
  const typingParticipant = isGroup
    ? (participants.find(
        (participant) =>
          participant.conversationParticipantId ===
          turnState?.nextParticipantId,
      ) ??
      participants[0] ??
      character)
    : character;
  const runRemotePersonalGeneration = async (
    action: SoulConversationAction,
  ) => {
    if (!session || !conversationId)
      throw new Error("Нет активного разговора SoulExe.");
    const api = new SoulExeApiClient(session);
    const previewId = `stream-${Date.now()}`;
    let previewAdded = false;
    const applyPreview = (text: string) => {
      if (!text) return;
      setMessages((current) => {
        const preview: Message = {
          id: previewId,
          sender: "character",
          text,
          time: "сейчас",
          participantIndex: 0,
          authorName: character.name,
          createdAt: new Date().toISOString(),
        };
        const index = current.findIndex((message) => message.id === previewId);
        if (index < 0) {
          previewAdded = true;
          return [...current, preview];
        }
        return current.map((message) =>
          message.id === previewId ? preview : message,
        );
      });
    };
    await api.startConversationAction(conversationId, action);
    await new Promise<void>((resolve, reject) => {
      const poll = async () => {
        try {
          const preview =
            await api.getConversationGenerationPreview(conversationId);
          if (preview.text) applyPreview(preview.text);
          if (preview.error) {
            setMessages((current) =>
              current.filter((message) => message.id !== previewId),
            );
            reject(new Error(preview.error));
            return;
          }
          if (preview.isGenerating) {
            setTimeout(() => void poll(), 90);
            return;
          }
          const updated = await api.getConversation(conversationId, 80);
          setMessages(toConversationMessages(updated));
          onRemoteConversation?.(updated);
          resolve();
        } catch (error) {
          if (previewAdded)
            setMessages((current) =>
              current.filter((message) => message.id !== previewId),
            );
          reject(error);
        }
      };
      setTimeout(() => void poll(), 90);
    });
  };
  const reply = async (text: string) => {
    if (!text.trim() || typing) return;
    const cleanText = text.trim();
    const ownMessage: Message = {
      id: `${Date.now()}`,
      sender:
        authorMode === "director"
          ? "director"
          : selectedPersona
            ? "persona"
            : "user",
      text: cleanText,
      time: "сейчас",
      authorName:
        authorMode === "director"
          ? "Режиссёр"
          : (selectedPersona?.name ?? "Вы"),
    };
    setMessages((current) => [...current, ownMessage]);
    setInput("");
    setTyping(true);
    if (session && conversationId) {
      try {
        const author =
          authorMode === "director"
            ? { authorKind: "director" as const }
            : selectedPersona
              ? {
                  authorKind: "persona" as const,
                  authorPersonaId: selectedPersona.id,
                }
              : { authorKind: "user" as const };
        if (!isGroup) {
          await runRemotePersonalGeneration({
            action: "send",
            text: cleanText,
            ...author,
          });
        } else {
          const updated = await new SoulExeApiClient(
            session,
          ).sendConversationMessage(conversationId, cleanText, author);
          setMessages(toConversationMessages(updated));
          onRemoteConversation?.(updated);
        }
      } catch (error) {
        setMessages((current) =>
          current.filter((message) => message.id !== ownMessage.id),
        );
        Alert.alert(
          "Сообщение не отправлено",
          error instanceof Error
            ? error.message
            : "Проверьте соединение с SoulExe Desktop.",
        );
      } finally {
        setTyping(false);
      }
      return;
    }
    setTimeout(() => {
      setTyping(false);
      setMessages((current) => [
        ...current,
        {
          id: `${Date.now()}-reply`,
          sender: "character",
          text: "Тогда оставайся рядом. Я покажу путь, но решение всё равно за тобой.",
          time: "сейчас",
          participantIndex: participants.length > 1 ? 1 : 0,
        },
      ]);
    }, 650);
  };
  const toggleTurn = async () => {
    if (!session || !conversationId) {
      setPaused((value) => !value);
      return;
    }
    try {
      const updated = await new SoulExeApiClient(session).conversationAction(
        conversationId,
        { action: turnState?.status === "running" ? "pause" : "start" },
      );
      setTurnState(updated.turnState);
      setMessages(toConversationMessages(updated));
      onRemoteConversation?.(updated);
    } catch (error) {
      Alert.alert(
        "Не удалось изменить ход истории",
        error instanceof Error
          ? error.message
          : "Проверьте связь с SoulExe Desktop.",
      );
    }
  };
  const requestNextTurn = useCallback(async () => {
    if (isRequestingNext) return;
    setIsRequestingNext(true);
    if (!session || !conversationId) {
      if (typing) {
        setIsRequestingNext(false);
        return;
      }
      setTyping(true);
      setTimeout(() => {
        setMessages((current) => [
          ...current,
          {
            id: `${Date.now()}-next`,
            sender: "character",
            text: "Я продолжу историю. Слушаю тебя и двигаюсь дальше.",
            time: "сейчас",
            participantIndex: isGroup ? 1 : 0,
          },
        ]);
        setTyping(false);
        setIsRequestingNext(false);
      }, 550);
      return;
    }
    try {
      setTyping(true);
      if (!isGroup) {
        await runRemotePersonalGeneration({ action: "next" });
      } else {
        const updated = await new SoulExeApiClient(session).conversationAction(
          conversationId,
          { action: "next" },
        );
        setTurnState(updated.turnState);
        setMessages(toConversationMessages(updated));
        onRemoteConversation?.(updated);
      }
    } catch (error) {
      Alert.alert(
        "Не удалось запросить реплику",
        error instanceof Error
          ? error.message
          : "Проверьте связь с SoulExe Desktop.",
      );
    } finally {
      setTyping(false);
      setIsRequestingNext(false);
    }
  }, [
    conversationId,
    isGroup,
    isRequestingNext,
    onRemoteConversation,
    session,
    typing,
  ]);
  useEffect(() => {
    if (!isGroup || !session || !conversationId) return;

    let cancelled = false;
    let inFlight = false;
    const refreshFromServer = async () => {
      if (cancelled || inFlight) return;
      inFlight = true;
      try {
        const updated = await new SoulExeApiClient(session).getConversation(
          conversationId,
          80,
        );
        if (cancelled) return;
        const revision = [
          updated.updatedAt,
          updated.messages.length,
          updated.turnState?.status ?? "",
          updated.turnState?.nextTurnAt ?? "",
          updated.turnState?.nextParticipantId ?? "",
        ].join("|");
        if (lastSyncedConversationRef.current === revision) return;
        lastSyncedConversationRef.current = revision;
        setTurnState(updated.turnState);
        setMessages(toConversationMessages(updated));
        onRemoteConversation?.(updated);
      } catch {
        // This is background synchronisation. An occasional missed poll must
        // not interrupt the reader; the next pass will retry automatically.
      } finally {
        inFlight = false;
      }
    };

    void refreshFromServer();
    const interval = setInterval(
      () => void refreshFromServer(),
      turnState?.status === "running" ? 1200 : 4000,
    );
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [
    conversationId,
    isGroup,
    onRemoteConversation,
    session,
    turnState?.status,
  ]);
  if (participantProfile)
    return (
      <CharacterProfile
        character={participantProfile}
        onBack={() => setParticipantProfile(null)}
        onEdit={() => onEditCharacter?.(participantProfile)}
      />
    );
  if (profileOpen)
    return (
      <CharacterProfile
        character={character}
        onBack={() => setProfileOpen(false)}
        onEdit={() => onEditCharacter?.(character)}
      />
    );
  const isRunning =
    session && conversationId ? turnState?.status === "running" : !paused;
  const latestCharacterMessage = [...messages]
    .reverse()
    .find((message) => message.sender === "character");
  const characterIsTyping =
    typing || (isGroup && isRunning && !turnState?.nextTurnAt);
  const presenceText = characterIsTyping
    ? `${typingParticipant.name} печатает`
    : isGroup
      ? `${participants.length} участника`
      : formatLastSeen(latestCharacterMessage);
  const chatTitle = isGroup
    ? conversationTitle?.trim() || "Групповой разговор"
    : character.name;
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#020617]"
      style={{ backgroundColor: resolveChatBackground(chatBackground).color }}
    >
      <View style={styles.flex}>
        <View style={styles.chatHeader}>
          <IconButton name="arrow-back" onPress={onBack} />
          <Pressable
            onPress={() =>
              isGroup
                ? setGroupHeaderOpen((value) => !value)
                : setProfileOpen(true)
            }
            style={isGroup ? styles.groupHeaderAvatars : undefined}
          >
            {isGroup ? (
              participants.slice(0, 2).map((participant, index) => (
                <View
                  key={participant.id}
                  style={[
                    styles.groupHeaderAvatar,
                    index > 0 && styles.groupHeaderAvatarOverlap,
                  ]}
                >
                  <Avatar entity={participant} size={38} />
                </View>
              ))
            ) : (
              <Avatar entity={character} size={46} />
            )}
          </Pressable>
          <Pressable
            onPress={() =>
              isGroup
                ? setGroupHeaderOpen((value) => !value)
                : setProfileOpen(true)
            }
            style={styles.chatHeaderCopy}
          >
            <Text style={styles.chatName}>{chatTitle}</Text>
            <View style={styles.headerPresence}>
              <Text style={styles.typing}>{presenceText}</Text>
              {characterIsTyping ? <TypingDots /> : null}
            </View>
          </Pressable>
          <IconButton name="more-vert" onPress={() => setPaused(true)} />
        </View>
        {isGroup && groupHeaderOpen ? (
          <View style={styles.groupHeaderPanel}>
            <Text style={styles.groupParticipantLegend}>
              УЧАСТНИКИ · {participants.length}
            </Text>
            {participants.map((participant, index) => (
              <Pressable
                key={participant.id}
                onPress={() => setParticipantProfile(participant)}
                style={styles.groupParticipantRow}
              >
                <Avatar entity={participant} size={34} />
                <View style={styles.groupParticipantText}>
                  <Text style={styles.groupParticipantName}>
                    {participant.name}
                  </Text>
                  <Text style={styles.groupParticipantRole}>
                    {index === 0 ? "Первый персонаж" : "Второй персонаж"} ·{" "}
                    {participant.role}
                  </Text>
                </View>
              </Pressable>
            ))}
          </View>
        ) : null}
        <View style={styles.messageArea}>
          <ChatTexture backgroundId={chatBackground} />
          <KeyboardChatScrollView
            ref={messageListRef}
            style={styles.messageList}
            contentContainerStyle={styles.chatContent}
            keyboardLiftBehavior="whenAtEnd"
            showsVerticalScrollIndicator={false}
            keyboardShouldPersistTaps="handled"
            scrollEventThrottle={16}
            onLayout={() => {
              if (!didInitialScrollRef.current) scrollToLatest(false);
            }}
            onContentSizeChange={() => {
              if (isNearLatestRef.current) scrollToLatest(false);
            }}
            onScroll={({ nativeEvent }) => {
              const distanceToLatest =
                nativeEvent.contentSize.height -
                nativeEvent.layoutMeasurement.height -
                nativeEvent.contentOffset.y;
              const nearLatest = distanceToLatest < 72;
              isNearLatestRef.current = nearLatest;
              setShowJumpToLatest(!nearLatest);
            }}
          >
            {messages.map((message, index) => (
              <Fragment key={message.id}>
                {messageDateKey(message) &&
                messageDateKey(messages[index - 1]) !==
                  messageDateKey(message) ? (
                  <View style={styles.messageDateChip}>
                    <Text style={styles.messageDateText}>
                      {formatMessageDate(message)}
                    </Text>
                  </View>
                ) : null}
                {message.sender === "director" ? (
                  <FormattedMessageText
                    text={message.text}
                    formatting={textFormatting}
                    style={styles.directorText}
                  />
                ) : (
                  <View
                    style={[
                      styles.messageRow,
                      isGroup
                        ? message.sender === "character"
                          ? message.participantIndex === 1
                            ? styles.messageRowRight
                            : styles.messageRow
                          : styles.messageRowCenter
                        : message.sender === "character"
                          ? styles.messageRow
                          : message.sender === "user" ||
                              message.sender === "persona"
                            ? styles.messageRowRight
                            : styles.messageRowCenter,
                    ]}
                  >
                    {message.sender === "character" && isGroup ? (
                      <Avatar
                        entity={participants[message.participantIndex ?? 0]}
                        size={28}
                      />
                    ) : null}
                    <View
                      style={[
                        styles.bubble,
                        message.sender === "user"
                          ? styles.userBubble
                          : message.sender === "persona"
                            ? styles.personaBubble
                            : [
                                styles.characterBubble,
                                isGroup &&
                                  message.participantIndex === 1 &&
                                  styles.characterBubbleAlt,
                              ],
                        messageStyle === "contrast"
                          ? message.sender === "character"
                            ? styles.contrastCharacterBubble
                            : styles.contrastOwnBubble
                          : messageStyle === "soft"
                            ? message.sender === "character"
                              ? styles.softCharacterBubble
                              : styles.softOwnBubble
                            : message.sender === "character"
                              ? styles.glassCharacterBubble
                              : styles.glassOwnBubble,
                      ]}
                    >
                      {!isGroup &&
                      message.sender === "persona" &&
                      message.authorName ? (
                        <Text style={styles.personaMessageAuthor}>
                          {message.authorName}
                        </Text>
                      ) : null}
                      <FormattedMessageText
                        text={message.text}
                        formatting={textFormatting}
                        style={[
                          styles.bubbleText,
                          { fontSize, lineHeight: Math.round(fontSize * 1.5) },
                        ]}
                      />
                      {message.time ? (
                        <Text style={styles.messageTime}>{message.time}</Text>
                      ) : null}
                    </View>
                  </View>
                )}
              </Fragment>
            ))}
          </KeyboardChatScrollView>
        </View>
        <KeyboardStickyView style={styles.composerDock}>
          {showJumpToLatest ? (
            <Pressable
              accessibilityLabel="К последнему сообщению"
              onPress={() => {
                isNearLatestRef.current = true;
                setShowJumpToLatest(false);
                scrollToLatest(true);
              }}
              style={styles.jumpToLatestButton}
            >
              <MaterialIcons name="south" size={21} color="#2F116C" />
            </Pressable>
          ) : null}
          <View style={styles.composerWrap}>
            <View style={styles.composerToolbar}>
              {isGroup ? (
                <View style={styles.groupComposerControls}>
                  <Pressable
                    onPress={() => void requestNextTurn()}
                    disabled={isRequestingNext}
                    style={({ pressed }) => [
                      styles.nextTurnButton,
                      isRequestingNext && styles.continueButtonBusy,
                      pressed && styles.controlButtonPressed,
                    ]}
                  >
                    {isRequestingNext ? (
                      <ActivityIndicator size="small" color={C.lavender} />
                    ) : (
                      <MaterialIcons
                        name="skip-next"
                        size={20}
                        color={C.lavender}
                      />
                    )}
                  </Pressable>
                  <Pressable
                    onPress={() => void toggleTurn()}
                    style={({ pressed }) => [
                      styles.playButton,
                      isRunning && styles.playButtonRunning,
                      pressed && styles.controlButtonPressed,
                    ]}
                  >
                    <MaterialIcons
                      name={isRunning ? "pause" : "play-arrow"}
                      size={21}
                      color={isRunning ? "#052E22" : C.lavender}
                    />
                  </Pressable>
                  <Text style={styles.turnTimer}>
                    {isRunning
                      ? secondsUntilNext === null
                        ? "Авто"
                        : `${secondsUntilNext} с`
                      : "Пауза"}
                  </Text>
                </View>
              ) : (
                <Pressable
                  onPress={() => void requestNextTurn()}
                  disabled={isRequestingNext}
                  style={({ pressed }) => [
                    styles.continueButton,
                    isRequestingNext && styles.continueButtonBusy,
                    pressed && styles.controlButtonPressed,
                  ]}
                >
                  {isRequestingNext ? (
                    <ActivityIndicator size="small" color={C.lavender} />
                  ) : (
                    <MaterialIcons
                      name="skip-next"
                      size={20}
                      color={C.lavender}
                    />
                  )}
                </Pressable>
              )}
              <View style={styles.toolbarSpacer} />
              <Pressable
                onPress={() => {
                  if (authorMode === "director") {
                    setAuthorMode("persona");
                    return;
                  }
                  setAuthorSheetOpen(true);
                }}
                style={[
                  styles.authorModeButton,
                  authorMode === "persona" && styles.authorModeButtonActive,
                ]}
              >
                <Text style={styles.modeText}>
                  {selectedPersona?.name ?? "Вы"}
                </Text>
                <MaterialIcons
                  name="expand-more"
                  size={17}
                  color={C.lavender}
                />
              </Pressable>
              <Pressable
                onPress={() => {
                  setAuthorMode("director");
                }}
                style={[
                  styles.directorButton,
                  authorMode === "director" && styles.directorButtonActive,
                ]}
              >
                <MaterialIcons
                  name="movie-filter"
                  size={21}
                  color={C.lavender}
                />
              </Pressable>
            </View>
            <View style={styles.composer}>
              <TextInput
                value={input}
                onChangeText={setInput}
                placeholder={
                  authorMode === "director"
                    ? "Режиссёрское событие"
                    : "Напишите сообщение…"
                }
                placeholderTextColor="#68758A"
                style={styles.input}
                multiline
                maxLength={240}
                onSubmitEditing={() => void reply(input)}
              />
              <Pressable
                onPress={() => void reply(input)}
                style={({ pressed }) => [
                  styles.sendButton,
                  pressed && styles.fabPressed,
                ]}
              >
                <MaterialIcons name="send" size={22} color="#2F116C" />
              </Pressable>
            </View>
          </View>
        </KeyboardStickyView>
        <SwipeSheet
          visible={authorSheetOpen}
          onClose={() => setAuthorSheetOpen(false)}
        >
          <Text style={styles.sheetEyebrow}>АВТОР СООБЩЕНИЯ</Text>
          <Text style={styles.sheetTitle}>Писать как</Text>
          <Pressable
            onPress={() => {
              setAuthorMode("persona");
              setSelectedPersona(null);
              setAuthorSheetOpen(false);
            }}
            style={styles.authorOption}
          >
            <View style={styles.authorOptionCopy}>
              <Text style={styles.authorOptionTitle}>Вы</Text>
              <Text style={styles.authorOptionSubtitle}>
                Обычное сообщение от пользователя
              </Text>
            </View>
            <MaterialIcons
              name={
                authorMode === "persona" && !selectedPersona
                  ? "radio-button-checked"
                  : "radio-button-unchecked"
              }
              size={22}
              color={
                authorMode === "persona" && !selectedPersona
                  ? C.lavender
                  : C.muted
              }
            />
          </Pressable>
          {personas.map((persona) => (
            <Pressable
              key={persona.id}
              onPress={() => {
                setAuthorMode("persona");
                setSelectedPersona(persona);
                setAuthorSheetOpen(false);
              }}
              style={styles.authorOption}
            >
              <Avatar entity={persona} size={42} />
              <View style={styles.authorOptionCopy}>
                <Text style={styles.authorOptionTitle}>{persona.name}</Text>
                <Text style={styles.authorOptionSubtitle}>{persona.role}</Text>
              </View>
              <MaterialIcons
                name={
                  authorMode === "persona" && selectedPersona?.id === persona.id
                    ? "radio-button-checked"
                    : "radio-button-unchecked"
                }
                size={22}
                color={
                  authorMode === "persona" && selectedPersona?.id === persona.id
                    ? C.lavender
                    : C.muted
                }
              />
            </Pressable>
          ))}
        </SwipeSheet>
      </View>
    </ScreenContainer>
  );
}

function MoreScreen({
  onBack,
  onLogout,
  textFormatting,
  onChangeTextFormatting,
  chatBackground,
  onChangeChatBackground,
  messageStyle,
  onChangeMessageStyle,
  chatFontSize,
  onChangeChatFontSize,
}: {
  onBack: () => void;
  onLogout: () => Promise<void> | void;
  textFormatting: TextFormatting;
  onChangeTextFormatting: (next: TextFormatting) => void;
  chatBackground: ChatBackgroundId;
  onChangeChatBackground: (next: ChatBackgroundId) => void;
  messageStyle: MessageStyleId;
  onChangeMessageStyle: (next: MessageStyleId) => void;
  chatFontSize: number;
  onChangeChatFontSize: (next: number) => void;
}) {
  const [backgroundOpen, setBackgroundOpen] = useState(false);
  const [messageStyleOpen, setMessageStyleOpen] = useState(false);
  const [formattingOpen, setFormattingOpen] = useState(false);
  const [logoutOpen, setLogoutOpen] = useState(false);
  useSystemBack(onBack);
  return (
    <ScreenContainer
      edges={["top", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <ScrollView contentContainerStyle={styles.moreContent}>
        <Text style={styles.eyebrow}>ОФОРМЛЕНИЕ</Text>
        <Pressable
          onPress={() => setBackgroundOpen(true)}
          style={({ pressed }) => [
            styles.settingCard,
            pressed && styles.pressed,
          ]}
        >
          <MaterialIcons name="wallpaper" size={22} color={C.lavender} />
          <View style={styles.settingCopy}>
            <Text style={styles.settingTitle}>Фон чата</Text>
            <Text style={styles.settingDescription}>
              {resolveChatBackground(chatBackground).label}
            </Text>
          </View>
          <MaterialIcons name="chevron-right" size={22} color={C.muted} />
        </Pressable>
        <Pressable
          onPress={() => setMessageStyleOpen(true)}
          style={({ pressed }) => [
            styles.settingCard,
            pressed && styles.pressed,
          ]}
        >
          <MaterialIcons
            name="format-color-fill"
            size={22}
            color={C.lavender}
          />
          <View style={styles.settingCopy}>
            <Text style={styles.settingTitle}>Оформление сообщений</Text>
            <Text style={styles.settingDescription}>
              {MESSAGE_STYLES.find((item) => item.id === messageStyle)?.label}
            </Text>
          </View>
          <MaterialIcons name="chevron-right" size={22} color={C.muted} />
        </Pressable>
        <Pressable
          onPress={() => setFormattingOpen(true)}
          style={({ pressed }) => [
            styles.settingCard,
            pressed && styles.pressed,
          ]}
        >
          <MaterialIcons name="format-italic" size={22} color={C.lavender} />
          <View style={styles.settingCopy}>
            <Text style={styles.settingTitle}>Разметка текста</Text>
            <Text style={styles.settingDescription}>
              Действия, мысли и реплики
            </Text>
          </View>
          <MaterialIcons name="chevron-right" size={22} color={C.muted} />
        </Pressable>
        <View style={styles.fontSizeCard}>
          <View style={styles.settingCopy}>
            <Text style={styles.settingTitle}>Размер текста</Text>
            <Text style={styles.settingDescription}>{chatFontSize} px</Text>
          </View>
          <Pressable
            accessibilityLabel="Уменьшить текст"
            onPress={() => onChangeChatFontSize(Math.max(13, chatFontSize - 1))}
            style={styles.fontStepButton}
          >
            <MaterialIcons name="remove" size={20} color={C.lavender} />
          </Pressable>
          <Pressable
            accessibilityLabel="Увеличить текст"
            onPress={() => onChangeChatFontSize(Math.min(21, chatFontSize + 1))}
            style={styles.fontStepButton}
          >
            <MaterialIcons name="add" size={20} color={C.lavender} />
          </Pressable>
        </View>
        <View
          style={[
            styles.chatAppearancePreview,
            { backgroundColor: resolveChatBackground(chatBackground).color },
          ]}
        >
          <ChatTexture backgroundId={chatBackground} />
          <Text style={styles.previewEyebrow}>ПРЕДПРОСМОТР ЧАТА</Text>
          <View
            style={[
              styles.previewBubble,
              styles.previewBubbleLeft,
              messageStyle === "contrast"
                ? styles.contrastCharacterBubble
                : messageStyle === "soft"
                  ? styles.softCharacterBubble
                  : styles.glassCharacterBubble,
            ]}
          >
            <Text
              style={[
                styles.previewBubbleText,
                {
                  fontSize: chatFontSize,
                  lineHeight: Math.round(chatFontSize * 1.45),
                },
              ]}
            >
              Как прошёл твой день?
            </Text>
          </View>
          <View
            style={[
              styles.previewBubble,
              styles.previewBubbleRight,
              messageStyle === "contrast"
                ? styles.contrastOwnBubble
                : messageStyle === "soft"
                  ? styles.softOwnBubble
                  : styles.glassOwnBubble,
            ]}
          >
            <Text
              style={[
                styles.previewBubbleText,
                {
                  fontSize: chatFontSize,
                  lineHeight: Math.round(chatFontSize * 1.45),
                },
              ]}
            >
              Сейчас расскажу.
            </Text>
          </View>
        </View>
        <Text style={[styles.eyebrow, styles.accountLabel]}>АККАУНТ</Text>
        <Pressable
          onPress={() => setLogoutOpen(true)}
          style={({ pressed }) => [
            styles.logoutButton,
            pressed && styles.pressed,
          ]}
        >
          <MaterialIcons name="logout" size={21} color="#FFB4AB" />
          <Text style={styles.logoutText}>Выйти</Text>
        </Pressable>
        <View style={styles.aboutCard}>
          <Text style={styles.aboutLogo}>✦</Text>
          <Text style={styles.aboutTitle}>SoulExe</Text>
          <Text style={styles.aboutText}>Истории, которые отвечают тебе.</Text>
          <Text style={styles.aboutVersion}>v2.0 · локальная сеть</Text>
        </View>
      </ScrollView>
      <SwipeSheet
        visible={backgroundOpen}
        onClose={() => setBackgroundOpen(false)}
      >
        <Text style={styles.sheetEyebrow}>ФОН ЧАТА</Text>
        <Text style={styles.sheetTitle}>Выбери фон разговоров</Text>
        <Text style={styles.sheetText}>
          Настройка применяется только к ленте сообщений и не меняет тему
          приложения.
        </Text>
        {CHAT_BACKGROUNDS.map((option) => (
          <Pressable
            key={option.id}
            onPress={() => {
              onChangeChatBackground(option.id);
              setBackgroundOpen(false);
            }}
            style={styles.appearanceOption}
          >
            <View style={styles.backgroundOptionCopy}>
              <View
                style={[
                  styles.backgroundSwatch,
                  {
                    backgroundColor: option.color,
                    borderColor: option.accent,
                  },
                ]}
              />
              <Text style={styles.appearanceOptionText}>{option.label}</Text>
            </View>
            <MaterialIcons
              name={
                chatBackground === option.id
                  ? "radio-button-checked"
                  : "radio-button-unchecked"
              }
              size={22}
              color={chatBackground === option.id ? C.lavender : C.muted}
            />
          </Pressable>
        ))}
      </SwipeSheet>
      <Modal
        transparent
        visible={logoutOpen}
        animationType="fade"
        onRequestClose={() => setLogoutOpen(false)}
      >
        <View style={styles.modalBackdrop}>
          <View style={styles.resultSheet}>
            <View style={styles.resultIcon}>
              <MaterialIcons name="logout" size={25} color="#FFB4AB" />
            </View>
            <Text style={styles.sheetEyebrow}>ЗАВЕРШИТЬ СЕАНС</Text>
            <Text style={styles.sheetTitle}>Выйти из SoulExe?</Text>
            <Text style={styles.sheetText}>
              Данные разговоров останутся на ПК, а сессия будет удалена с
              телефона.
            </Text>
            <Pressable
              onPress={() => setLogoutOpen(false)}
              style={styles.primaryButton}
            >
              <Text style={styles.primaryButtonText}>Остаться</Text>
            </Pressable>
            <Pressable
              onPress={() => {
                setLogoutOpen(false);
                void onLogout();
              }}
              style={styles.secondaryButton}
            >
              <Text style={styles.secondaryButtonText}>Выйти</Text>
            </Pressable>
          </View>
        </View>
      </Modal>
      <SwipeSheet
        visible={messageStyleOpen}
        onClose={() => setMessageStyleOpen(false)}
      >
        <Text style={styles.sheetEyebrow}>СТИЛЬ СООБЩЕНИЙ</Text>
        <Text style={styles.sheetTitle}>Выбери характер реплик</Text>
        <Text style={styles.sheetText}>
          Это только визуальная настройка пузырей в диалоге.
        </Text>
        {MESSAGE_STYLES.map((option) => (
          <Pressable
            key={option.id}
            onPress={() => {
              onChangeMessageStyle(option.id);
              setMessageStyleOpen(false);
            }}
            style={styles.appearanceOption}
          >
            <View style={styles.authorOptionCopy}>
              <Text style={styles.appearanceOptionText}>{option.label}</Text>
              <Text style={styles.authorOptionSubtitle}>
                {option.description}
              </Text>
            </View>
            <MaterialIcons
              name={
                messageStyle === option.id
                  ? "radio-button-checked"
                  : "radio-button-unchecked"
              }
              size={22}
              color={messageStyle === option.id ? C.lavender : C.muted}
            />
          </Pressable>
        ))}
      </SwipeSheet>
      <SwipeSheet
        visible={formattingOpen}
        onClose={() => setFormattingOpen(false)}
      >
        <Text style={styles.sheetEyebrow}>ОФОРМЛЕНИЕ ТЕКСТА</Text>
        <Text style={styles.sheetTitle}>Как показывать разметку</Text>
        <Text style={styles.sheetText}>
          Цветом выделяются части сообщений, которые отмечены в тексте.
        </Text>
        {[
          {
            key: "actions" as const,
            title: "Действия в *звёздочках*",
            description: "Например: *подходит ближе*",
          },
          {
            key: "thoughts" as const,
            title: "Мысли в <think>",
            description: "Внутренние мысли персонажа",
          },
          {
            key: "speech" as const,
            title: "Реплики в кавычках",
            description: "Прямая речь в «кавычках»",
          },
        ].map((option) => (
          <View key={option.key} style={styles.formattingRow}>
            <View style={styles.formattingCopy}>
              <Text style={styles.formattingTitle}>{option.title}</Text>
              <Text style={styles.formattingDescription}>
                {option.description}
              </Text>
            </View>
            <Switch
              value={textFormatting[option.key]}
              onValueChange={(value) =>
                onChangeTextFormatting({
                  ...textFormatting,
                  [option.key]: value,
                })
              }
              trackColor={{ false: "#324158", true: C.violet }}
              thumbColor={C.text}
            />
          </View>
        ))}
        <View style={styles.formattingPreview}>
          <FormattedMessageText
            text={
              "*Она улыбается.* <think>Стоит ответить спокойно.</think> «Я рядом.»"
            }
            formatting={textFormatting}
            style={styles.bubbleText}
          />
        </View>
      </SwipeSheet>
    </ScreenContainer>
  );
}

function SplashScreen() {
  return (
    <View style={styles.splash}>
      <View style={styles.splashGlyph}>
        <Text style={styles.splashStar}>✦</Text>
        <View style={styles.splashCore} />
      </View>
      <Text style={styles.splashTitle}>SoulExe</Text>
      <Text style={styles.splashStatus}>Initiating neural sequence…</Text>
      <View style={styles.progressTrack}>
        <View style={styles.progressFill} />
      </View>
    </View>
  );
}

function ConnectionWelcome({
  onFind,
  onDemo,
}: {
  onFind: () => void;
  onDemo: () => void;
}) {
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#020617]"
    >
      <View style={styles.connectionWelcome}>
        <View style={styles.connectionLogo}>
          <Text style={styles.splashStar}>✦</Text>
          <View style={styles.splashCore} />
        </View>
        <Text style={styles.connectionWordmark}>SoulExe</Text>
        <Text style={styles.connectionTitle}>Ваши персонажи — рядом</Text>
        <Text style={styles.connectionText}>
          Подключитесь к SoulExe на компьютере в одной Wi‑Fi сети и продолжайте
          разговоры с телефона.
        </Text>
        <View style={styles.connectionActions}>
          <Pressable
            onPress={onFind}
            style={({ pressed }) => [
              styles.primaryButton,
              pressed && styles.pressed,
            ]}
          >
            <MaterialIcons name="wifi-find" size={20} color="#2F116C" />
            <Text style={styles.primaryButtonText}>Найти SoulExe в Wi‑Fi</Text>
          </Pressable>
          <Pressable
            onPress={onDemo}
            style={({ pressed }) => [
              styles.secondaryButton,
              pressed && styles.pressed,
            ]}
          >
            <Text style={styles.secondaryButtonText}>Открыть демо-режим</Text>
          </Pressable>
        </View>
        <Text style={styles.connectionFootnote}>
          Демо-режим использует примерные данные и ничего не меняет на ПК.
        </Text>
      </View>
    </ScreenContainer>
  );
}

function ComputerPicker({
  onBack,
  onChoose,
}: {
  onBack: () => void;
  onChoose: (computer: ComputerChoice) => void;
}) {
  const [searching, setSearching] = useState(true);
  const [servers, setServers] = useState<DiscoveredSoulExeServer[]>([]);
  const [status, setStatus] = useState(
    "Ищем запущенные экземпляры в вашей Wi‑Fi сети…",
  );
  useSystemBack(onBack);
  const search = useCallback(async () => {
    setSearching(true);
    setServers([]);
    setStatus("Ищем запущенные экземпляры в вашей Wi‑Fi сети…");
    try {
      const found = await discoverSoulExeServers(setStatus);
      setServers(found);
      if (!found.length)
        setStatus(
          "SoulExe не найден. Проверьте, что ПК и телефон в одной Wi‑Fi сети.",
        );
    } catch (error) {
      setStatus(
        error instanceof Error
          ? error.message
          : "Не удалось выполнить поиск в сети.",
      );
    } finally {
      setSearching(false);
    }
  }, []);
  useEffect(() => {
    void search();
  }, [search]);
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <TopBar
        title="Выберите компьютер"
        onBack={onBack}
        rightIcon="refresh"
        onRightPress={() => void search()}
      />
      <View style={styles.connectionHeader}>
        <Text style={styles.eyebrow}>ЛОКАЛЬНАЯ СЕТЬ</Text>
        <Text style={styles.setupTitle}>SoulExe рядом</Text>
        <Text style={styles.setupText}>{status}</Text>
      </View>
      <View style={styles.computerList}>
        {searching ? (
          <View style={styles.searchingCard}>
            <ActivityIndicator color={C.lavender} />
            <Text style={styles.searchingText}>Поиск компьютеров…</Text>
          </View>
        ) : (
          servers.map((server) => (
            <Pressable
              key={server.baseUrl}
              onPress={() =>
                onChoose({ baseUrl: server.baseUrl, name: server.name })
              }
              style={({ pressed }) => [
                styles.computerCard,
                pressed && styles.pressed,
              ]}
            >
              <View style={styles.computerIcon}>
                <MaterialIcons name="computer" size={25} color={C.lavender} />
              </View>
              <View style={styles.typeCopy}>
                <Text style={styles.typeTitle}>{server.name}</Text>
                <Text style={styles.typeDescription}>
                  {server.baseUrl.replace(/^https?:\/\//, "")} · локальная сеть
                </Text>
              </View>
              <MaterialIcons name="chevron-right" size={23} color={C.muted} />
            </Pressable>
          ))
        )}
      </View>
      <View style={styles.connectionBottom}>
        <Pressable onPress={() => void search()} style={styles.secondaryButton}>
          <Text style={styles.secondaryButtonText}>Искать ещё раз</Text>
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

function LoginScreen({
  computer,
  onBack,
  onLogin,
}: {
  computer: ComputerChoice;
  onBack: () => void;
  onLogin: (username: string, password: string) => Promise<void>;
}) {
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  useSystemBack(onBack);
  const submit = async () => {
    if (!login.trim() || !password) {
      Alert.alert(
        "Введите логин и пароль",
        "Используйте данные входа из SoulExe Desktop.",
      );
      return;
    }
    setSubmitting(true);
    try {
      await onLogin(login.trim(), password);
    } catch (error) {
      Alert.alert(
        "Не удалось войти",
        error instanceof Error
          ? error.message
          : "Проверьте данные и повторите попытку.",
      );
    } finally {
      setSubmitting(false);
    }
  };
  return (
    <ScreenContainer
      edges={["top", "bottom", "left", "right"]}
      containerClassName="bg-[#051424]"
    >
      <KeyboardAvoidingView style={styles.flex} behavior="padding">
        <TopBar title="Подключение" onBack={onBack} />
        <ScrollView
          contentContainerStyle={styles.loginContent}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
        >
          <View style={styles.connectionHeader}>
            <Text style={styles.eyebrow}>ВЫБРАННЫЙ КОМПЬЮТЕР</Text>
            <Text style={styles.computerAddress}>{computer.name}</Text>
            <Text style={styles.setupText}>
              {computer.baseUrl.replace(/^https?:\/\//, "")} · введите данные
              SoulExe Desktop.
            </Text>
          </View>
          <Field
            label="Логин"
            value={login}
            onChangeText={setLogin}
            placeholder="Введите логин"
          />
          <View style={styles.field}>
            <Text style={styles.fieldLabel}>Пароль</Text>
            <View style={styles.passwordField}>
              <TextInput
                value={password}
                onChangeText={setPassword}
                placeholder="Введите пароль"
                placeholderTextColor="#68758A"
                style={styles.passwordInput}
                secureTextEntry={!passwordVisible}
              />
              <Pressable onPress={() => setPasswordVisible((value) => !value)}>
                <MaterialIcons
                  name={passwordVisible ? "visibility-off" : "visibility"}
                  size={21}
                  color={C.muted}
                />
              </Pressable>
            </View>
          </View>
          <Pressable
            disabled={submitting}
            onPress={() => void submit()}
            style={({ pressed }) => [
              styles.primaryButton,
              (pressed || submitting) && styles.pressed,
            ]}
          >
            <Text style={styles.primaryButtonText}>
              {submitting ? "Подключаемся…" : "Войти"}
            </Text>
            {submitting ? (
              <ActivityIndicator color="#2F116C" />
            ) : (
              <MaterialIcons name="arrow-forward" size={20} color="#2F116C" />
            )}
          </Pressable>
          <Pressable onPress={onBack} style={styles.secondaryButton}>
            <Text style={styles.secondaryButtonText}>
              Выбрать другой компьютер
            </Text>
          </Pressable>
        </ScrollView>
      </KeyboardAvoidingView>
    </ScreenContainer>
  );
}

export default function HomeScreen() {
  const [fontsLoaded] = useFonts({
    Inter: require("../../assets/fonts/InterVariable.ttf"),
    Manrope: require("../../assets/fonts/Manrope-VariableFont_wght.ttf"),
  });
  const [showSplash, setShowSplash] = useState(true);
  const [connectionStage, setConnectionStage] = useState<
    "welcome" | "computers" | "login" | null
  >("welcome");
  const [selectedComputer, setSelectedComputer] =
    useState<ComputerChoice | null>(null);
  const [session, setSession] = useState<SoulExeSession | null>(null);
  const [tab, setTab] = useState<"chat" | "library" | "more">("chat");
  const [characters, setCharacters] = useState(initialCharacters);
  const [personas, setPersonas] = useState(initialPersonas);
  const [promptPresets, setPromptPresets] = useState<SoulPromptPreset[]>([]);
  const [lorebooks, setLorebooks] = useState<SoulLorebookSummary[]>([]);
  const [conversationPreviews, setConversationPreviews] =
    useState<ConversationPreview[]>(conversations);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [conversation, setConversation] = useState<LibraryEntity | null>(null);
  const [libraryProfile, setLibraryProfile] = useState<EditorState | null>(
    null,
  );
  const [conversationParticipants, setConversationParticipants] = useState<
    LibraryEntity[]
  >([]);
  const [conversationMessages, setConversationMessages] = useState<
    Message[] | undefined
  >();
  const [activeConversationId, setActiveConversationId] = useState<
    string | undefined
  >();
  const [conversationTurnState, setConversationTurnState] =
    useState<SoulConversation["turnState"]>();
  const [conversationIsDemo, setConversationIsDemo] = useState(false);
  const [conversationTitle, setConversationTitle] = useState<string>();
  const [textFormatting, setTextFormatting] = useState<TextFormatting>(
    defaultTextFormatting,
  );
  const [chatBackground, setChatBackground] = useState<ChatBackgroundId>(
    defaultChatAppearance.chatBackground as ChatBackgroundId,
  );
  const [messageStyle, setMessageStyle] = useState<MessageStyleId>(
    defaultChatAppearance.messageStyle,
  );
  const [chatFontSize, setChatFontSize] = useState(
    defaultChatAppearance.fontSize,
  );
  const [newChatType, setNewChatType] = useState<
    "type" | "personal" | "group" | null
  >(null);
  const openConversationRef = useRef<(item: ConversationPreview) => void>(() => undefined);
  useSystemBack(() => {
    if (
      connectionStage !== null ||
      editor ||
      libraryProfile ||
      conversation ||
      newChatType
    )
      return false;
    if (tab !== "chat") {
      setTab("chat");
      return true;
    }
    return false;
  });
  const loadRemoteData = useCallback(async (activeSession: SoulExeSession) => {
    const api = new SoulExeApiClient(activeSession);
    const [
      remoteCharacters,
      remotePersonas,
      remoteConversations,
      remotePresets,
      remoteLorebooks,
    ] = await Promise.all([
      api.getCharacters(),
      api.getPersonas(),
      api.getConversations(1),
      api.getPromptPresets().catch(() => []),
      api.getLorebooks().catch(() => []),
    ]);
    const mappedCharacters = remoteCharacters.map(toCharacterEntity);
    setCharacters(mappedCharacters);
    setPersonas(remotePersonas.map(toPersonaEntity));
    setPromptPresets(remotePresets);
    setLorebooks(remoteLorebooks);
    setConversationPreviews(
      sortConversationPreviews(
        remoteConversations
          .map((conversation) =>
            toConversationPreview(conversation, mappedCharacters),
          )
          .filter(
            (conversation): conversation is ConversationPreview =>
              conversation !== null,
          ),
      ),
    );
  }, []);
  useEffect(() => {
    const timer = setTimeout(() => setShowSplash(false), 1450);
    void (async () => {
      const savedAppearance = await loadChatAppearance();
      setChatBackground(
        resolveChatBackground(savedAppearance.chatBackground).id,
      );
      setMessageStyle(
        MESSAGE_STYLES.some((item) => item.id === savedAppearance.messageStyle)
          ? savedAppearance.messageStyle
          : "glass",
      );
      setChatFontSize(
        Math.max(13, Math.min(21, savedAppearance.fontSize ?? 16)),
      );
      const stored = await loadSoulExeSession();
      if (!stored) return;
      try {
        await loadRemoteData(stored);
        setSession(stored);
        void startSoulExeForegroundService(stored).catch(() => undefined);
        setConversationIsDemo(false);
        setConnectionStage(null);
      } catch {
        // An expired session simply returns the user to the designed connection flow.
      }
    })();
    return () => clearTimeout(timer);
  }, [loadRemoteData]);
  const signIn = async (username: string, password: string) => {
    if (!selectedComputer)
      throw new Error("Сначала выберите компьютер SoulExe.");
    const nextSession = await SoulExeApiClient.login(
      selectedComputer.baseUrl,
      username,
      password,
    );
    await loadRemoteData(nextSession);
    await saveSoulExeSession(nextSession);
    setSession(nextSession);
    void startSoulExeForegroundService(nextSession).catch(() => undefined);
    setConversationIsDemo(false);
    setConnectionStage(null);
  };
  const signOut = async () => {
    await stopSoulExeForegroundService().catch(() => undefined);
    await clearSoulExeSession();
    setSession(null);
    setSelectedComputer(null);
    setConversation(null);
    setConversationParticipants([]);
    setConversationMessages(undefined);
    setConversationTurnState(undefined);
    setConversationTitle(undefined);
    setActiveConversationId(undefined);
    setConversationIsDemo(false);
    setConnectionStage("welcome");
  };
  const changeChatBackground = (next: ChatBackgroundId) => {
    setChatBackground(next);
    void saveChatAppearance({
      ...defaultChatAppearance,
      chatBackground: next,
      messageStyle,
      fontSize: chatFontSize,
    });
  };
  const changeMessageStyle = (next: MessageStyleId) => {
    setMessageStyle(next);
    void saveChatAppearance({
      ...defaultChatAppearance,
      chatBackground,
      messageStyle: next,
      fontSize: chatFontSize,
    });
  };
  const changeChatFontSize = (next: number) => {
    const safe = Math.max(13, Math.min(21, next));
    setChatFontSize(safe);
    void saveChatAppearance({
      ...defaultChatAppearance,
      chatBackground,
      messageStyle,
      fontSize: safe,
    });
  };
  const saveLibraryItem = async (
    item: LibraryEntity,
    kind: "character" | "persona",
    avatar?: {
      uri: string;
      fileName?: string | null;
      mimeType?: string | null;
    },
  ) => {
    try {
      if (session) {
        const api = new SoulExeApiClient(session);
        if (kind === "character") {
          const saved = characters.some((entry) => entry.id === item.id)
            ? await api.updateCharacter(item.id, {
                name: item.name,
                title: item.role,
                description: item.description,
                personality: item.personality,
                scenario: item.scenario,
                systemPrompt: item.systemPrompt,
                personalityExpressionLevel: item.personalityExpressionLevel,
                replyLanguage: item.replyLanguage,
                useRoleplayResponseFormatting:
                  item.useRoleplayResponseFormatting,
                defaultUserProfile: item.defaultUserProfile,
                defaultRelationshipContext: item.defaultRelationshipContext,
                exampleDialogue: item.exampleDialogue,
                selectedPromptPresetId: item.selectedPromptPresetId,
                lorebookIds: item.lorebookIds,
                cognitiveArchitectureEnabled: item.cognitiveArchitectureEnabled,
                soulMemoryEnabled: item.soulMemoryEnabled,
                soulMemoryPreset: item.soulMemoryPreset,
                soulMemoryIntervalMessages: item.soulMemoryIntervalMessages,
                autoSummaryEnabled: item.autoSummaryEnabled,
                autoSummaryIntervalMessages: item.autoSummaryIntervalMessages,
                proactiveMessagesEnabled: item.proactiveMessagesEnabled,
                proactiveQuietHoursEnabled: item.proactiveQuietHoursEnabled,
                proactiveQuietHoursStart: item.proactiveQuietHoursStart,
                proactiveQuietHoursEnd: item.proactiveQuietHoursEnd,
                realisticMessagingEnabled: item.realisticMessagingEnabled,
                selectedPersonaId: item.selectedPersonaId,
              })
            : await api.createCharacter({
                name: item.name,
                title: item.role,
                description: item.description,
                personality: item.personality,
                scenario: item.scenario,
                systemPrompt: item.systemPrompt,
                personalityExpressionLevel: item.personalityExpressionLevel,
                replyLanguage: item.replyLanguage,
                useRoleplayResponseFormatting:
                  item.useRoleplayResponseFormatting,
                defaultUserProfile: item.defaultUserProfile,
                defaultRelationshipContext: item.defaultRelationshipContext,
                exampleDialogue: item.exampleDialogue,
                selectedPromptPresetId: item.selectedPromptPresetId,
                lorebookIds: item.lorebookIds,
                cognitiveArchitectureEnabled: item.cognitiveArchitectureEnabled,
                soulMemoryEnabled: item.soulMemoryEnabled,
                soulMemoryPreset: item.soulMemoryPreset,
                soulMemoryIntervalMessages: item.soulMemoryIntervalMessages,
                autoSummaryEnabled: item.autoSummaryEnabled,
                autoSummaryIntervalMessages: item.autoSummaryIntervalMessages,
                proactiveMessagesEnabled: item.proactiveMessagesEnabled,
                proactiveQuietHoursEnabled: item.proactiveQuietHoursEnabled,
                proactiveQuietHoursStart: item.proactiveQuietHoursStart,
                proactiveQuietHoursEnd: item.proactiveQuietHoursEnd,
                realisticMessagingEnabled: item.realisticMessagingEnabled,
                selectedPersonaId: item.selectedPersonaId,
              });
          if (avatar) await api.uploadCharacterAvatar(saved.id, avatar);
        } else if (personas.some((entry) => entry.id === item.id)) {
          const saved = await api.updatePersona(item.id, {
            name: item.name,
            description: item.description,
            promptText: item.promptText,
          });
          if (avatar) await api.uploadPersonaAvatar(saved.id, avatar);
        } else {
          const saved = await api.createPersona({
            name: item.name,
            description: item.description,
            promptText: item.promptText,
          });
          if (avatar) await api.uploadPersonaAvatar(saved.id, avatar);
        }
        await loadRemoteData(session);
      } else if (kind === "character") {
        setCharacters((current) =>
          current.some((entry) => entry.id === item.id)
            ? current.map((entry) => (entry.id === item.id ? item : entry))
            : [...current, item],
        );
      } else {
        setPersonas((current) =>
          current.some((entry) => entry.id === item.id)
            ? current.map((entry) => (entry.id === item.id ? item : entry))
            : [...current, item],
        );
      }
      if (libraryProfile?.item?.id === item.id)
        setLibraryProfile({ kind, item });
      setEditor(null);
    } catch (error) {
      Alert.alert(
        "Не удалось сохранить",
        error instanceof Error ? error.message : "Повторите попытку.",
      );
    }
  };
  const openConversation = async (item: ConversationPreview) => {
    setConversation(item.character);
    setConversationTitle(item.title);
    setConversationParticipants(
      item.participants?.length ? item.participants : [item.character],
    );
    setConversationMessages(undefined);
    setActiveConversationId(item.source === "remote" ? item.id : undefined);
    setConversationTurnState(undefined);
    setConversationIsDemo(item.source === "demo");
    if (!session || item.source !== "remote") return;
    try {
      const detail = await new SoulExeApiClient(session).getConversation(
        item.id,
        80,
      );
      setConversationMessages(toConversationMessages(detail));
      setConversationTurnState(detail.turnState);
    } catch (error) {
      Alert.alert(
        "Не удалось загрузить сообщения",
        error instanceof Error
          ? error.message
          : "Откроем чат повторно после обновления связи.",
      );
    }
  };
  openConversationRef.current = (item) => void openConversation(item);
  useEffect(() => {
    const openConversationId = (conversationId: string) => {
      const target = conversationPreviews.find(
        (item) => item.source === "remote" && item.id === conversationId,
      );
      if (target) openConversationRef.current(target);
    };
    return subscribeToForegroundServiceLinks(openConversationId);
  }, [conversationPreviews, session]);
  const renameConversation = async (
    item: ConversationPreview,
    name: string,
  ) => {
    if (!session || item.source !== "remote") {
      setConversationPreviews((current) =>
        current.map((entry) =>
          entry.id === item.id ? { ...entry, title: name } : entry,
        ),
      );
      return;
    }
    const updated = await new SoulExeApiClient(session).conversationAction(
      item.id,
      { action: "rename", text: name },
    );
    const preview = toConversationPreview(updated, characters);
    if (preview) {
      setConversationPreviews((current) =>
        sortConversationPreviews(
          current.map((entry) => (entry.id === item.id ? preview : entry)),
        ),
      );
    }
    if (activeConversationId === item.id) setConversationTitle(updated.name);
  };
  const createConversation = async (
    selected: LibraryEntity[],
    details: NewConversationDetails,
  ) => {
    if (!selected.length) return;
    try {
      if (!session) {
        setConversation(selected[0]);
        setConversationTitle(details.name);
        setConversationParticipants(selected);
        setConversationMessages(undefined);
        setConversationIsDemo(true);
        setNewChatType(null);
        return;
      }
      const created = await new SoulExeApiClient(session).createConversation({
        characterIds: selected.map((character) => character.id),
        name: details.name,
        scenario: details.scenario?.trim() || undefined,
        location: details.location?.trim() || undefined,
        mood: details.mood?.trim() || undefined,
        goal: details.goal?.trim() || undefined,
        delaySeconds: details.delaySeconds,
        enforceContract: details.enforceContract,
        advanceAndAvoidRepetition: details.advanceAndAvoidRepetition,
      });
      const preview = toConversationPreview(created, characters);
      if (preview)
        setConversationPreviews((current) =>
          sortConversationPreviews([
            preview,
            ...current.filter((item) => item.id !== preview.id),
          ]),
        );
      setConversation(selected[0]);
      setConversationTitle(created.name?.trim() || details.name);
      setConversationParticipants(preview?.participants ?? selected);
      setConversationMessages(toConversationMessages(created));
      setActiveConversationId(created.id);
      setConversationTurnState(created.turnState);
      setConversationIsDemo(false);
      setNewChatType(null);
    } catch (error) {
      Alert.alert(
        "Не удалось создать разговор",
        error instanceof Error
          ? error.message
          : "Проверьте связь с SoulExe Desktop.",
      );
    }
  };
  if (!fontsLoaded || showSplash) return <SplashScreen />;
  if (connectionStage === "welcome")
    return (
      <ConnectionWelcome
        onFind={() => setConnectionStage("computers")}
        onDemo={() => {
          setSession(null);
          setConversationIsDemo(true);
          setConnectionStage(null);
        }}
      />
    );
  if (connectionStage === "computers")
    return (
      <ComputerPicker
        onBack={() => setConnectionStage("welcome")}
        onChoose={(computer) => {
          setSelectedComputer(computer);
          setConnectionStage("login");
        }}
      />
    );
  if (connectionStage === "login" && selectedComputer)
    return (
      <LoginScreen
        computer={selectedComputer}
        onBack={() => setConnectionStage("computers")}
        onLogin={signIn}
      />
    );
  if (editor)
    return (
      <EditorScreen
        state={editor}
        personas={personas}
        promptPresets={promptPresets}
        lorebooks={lorebooks}
        onClose={() => setEditor(null)}
        onSave={saveLibraryItem}
        onGeneratePersonaDescription={
          session
            ? async (idea) =>
                (
                  await new SoulExeApiClient(session).expandPersonaDescription(
                    idea,
                  )
                ).description
            : undefined
        }
        onGenerateEntity={
          session
            ? async (kind, idea) => {
                const api = new SoulExeApiClient(session);
                const generated =
                  kind === "character"
                    ? toCharacterEntity(await api.generateCharacter(idea))
                    : toPersonaEntity(await api.generatePersona(idea));
                await loadRemoteData(session);
                return generated;
              }
            : undefined
        }
        onExpandCharacterField={
          session
            ? async (characterId, field) => {
                const updated = await new SoulExeApiClient(
                  session,
                ).expandCharacterField(characterId, field);
                await loadRemoteData(session);
                return toCharacterEntity(updated);
              }
            : undefined
        }
      />
    );
  if (libraryProfile?.item)
    return (
      <CharacterProfile
        character={libraryProfile.item}
        onBack={() => setLibraryProfile(null)}
        onEdit={() => setEditor(libraryProfile)}
      />
    );
  if (conversation)
    return (
      <ConversationScreen
        character={conversation}
        personas={personas}
        conversationTitle={conversationTitle}
        textFormatting={textFormatting}
        chatBackground={chatBackground}
        messageStyle={messageStyle}
        fontSize={chatFontSize}
        initialMessages={conversationMessages}
        initialTurnState={conversationTurnState}
        session={session}
        conversationId={activeConversationId}
        isDemo={conversationIsDemo}
        onEditCharacter={(item) => setEditor({ kind: "character", item })}
        onRemoteConversation={(updated) => {
          setConversationMessages(toConversationMessages(updated));
          setConversationTurnState(updated.turnState);
          void loadRemoteData(session!);
        }}
        participants={
          conversationParticipants.length
            ? conversationParticipants
            : [conversation]
        }
        onBack={() => {
          setConversation(null);
          setConversationParticipants([]);
          setConversationMessages(undefined);
          setActiveConversationId(undefined);
          setConversationTurnState(undefined);
          setConversationTitle(undefined);
          setConversationIsDemo(false);
        }}
      />
    );
  if (newChatType === "type")
    return (
      <NewChatTypeScreen
        onBack={() => setNewChatType(null)}
        onChoose={setNewChatType}
      />
    );
  if (newChatType === "personal")
    return (
      <PersonalChatSetup
        characters={characters}
        onBack={() => setNewChatType("type")}
        onCreate={(entity) =>
          createConversation([entity], { name: `Разговор с ${entity.name}` })
        }
      />
    );
  if (newChatType === "group")
    return (
      <GroupChatSetup
        characters={characters}
        onBack={() => setNewChatType("type")}
        onCreate={createConversation}
      />
    );

  return (
    <View style={styles.root}>
      {tab === "chat" ? (
        <ConversationsScreen
          items={conversationPreviews}
          onOpenConversation={(item) => void openConversation(item)}
          onCreate={() => setNewChatType("type")}
          onRename={renameConversation}
        />
      ) : tab === "library" ? (
        <LibraryScreen
          characters={characters}
          personas={personas}
          onOpenCharacter={(item) =>
            setLibraryProfile({ kind: "character", item })
          }
          onOpenPersona={(item) => setLibraryProfile({ kind: "persona", item })}
          onEdit={(kind, item) => setEditor({ kind, item })}
          onCreate={(kind) => setEditor({ kind })}
        />
      ) : (
        <MoreScreen
          onBack={() => setTab("chat")}
          onLogout={signOut}
          textFormatting={textFormatting}
          onChangeTextFormatting={setTextFormatting}
          chatBackground={chatBackground}
          onChangeChatBackground={changeChatBackground}
          messageStyle={messageStyle}
          onChangeMessageStyle={changeMessageStyle}
          chatFontSize={chatFontSize}
          onChangeChatFontSize={changeChatFontSize}
        />
      )}
      <BottomNav active={tab} onChange={setTab} />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: C.navy },
  flex: { flex: 1 },
  pressed: { opacity: 0.72 },
  rowBetween: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  topBar: {
    height: 68,
    paddingHorizontal: 14,
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "rgba(22,36,54,0.92)",
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  iconButton: {
    minWidth: 44,
    minHeight: 44,
    alignItems: "center",
    justifyContent: "center",
  },
  topTitleWrap: { flex: 1, alignItems: "center" },
  topTitle: {
    fontFamily: "Manrope",
    fontSize: 19,
    fontWeight: "700",
    color: C.text,
  },
  topSubtitle: {
    fontFamily: "Inter",
    color: C.green,
    fontSize: 12,
    marginTop: 2,
  },
  conversationHeading: {
    paddingHorizontal: 18,
    paddingTop: 25,
    paddingBottom: 13,
  },
  libraryIntro: { paddingHorizontal: 18, paddingTop: 22, paddingBottom: 14 },
  sectionTitle: {
    fontFamily: "Manrope",
    fontSize: 27,
    color: C.text,
    fontWeight: "700",
    marginTop: 6,
  },
  introText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 14,
    lineHeight: 21,
    marginTop: 8,
    maxWidth: 340,
  },
  eyebrow: {
    fontFamily: "Manrope",
    letterSpacing: 1.7,
    fontSize: 11,
    color: C.muted,
    fontWeight: "600",
  },
  listHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    paddingBottom: 10,
  },
  listCount: { color: C.muted, fontFamily: "Inter", fontSize: 12 },
  listContent: { paddingHorizontal: 16, paddingTop: 14, paddingBottom: 112 },
  chatCard: {
    minHeight: 74,
    marginBottom: 10,
    borderRadius: 16,
    paddingHorizontal: 14,
    paddingVertical: 11,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
  },
  chatCopy: { flex: 1, paddingHorizontal: 13 },
  chatTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 16,
    fontWeight: "700",
    flex: 1,
    marginRight: 8,
  },
  chatPreview: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 13,
    marginTop: 4,
  },
  chatParameterPreview: {
    fontFamily: "Inter",
    color: C.lavender,
    fontSize: 11,
    marginTop: 5,
  },
  chatTypeLabel: {
    fontFamily: "Manrope",
    color: C.lavender,
    fontSize: 11,
    fontWeight: "700",
    marginTop: 7,
  },
  chatAvatarStack: {
    width: 72,
    height: 52,
    flexDirection: "row",
    alignItems: "center",
    paddingLeft: 1,
  },
  chatAvatarStackItem: {
    borderRadius: 28,
    borderWidth: 2,
    borderColor: C.card,
  },
  chatEditButton: {
    width: 34,
    height: 34,
    borderRadius: 11,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(139,92,246,0.10)",
    marginRight: 5,
  },
  chatMeta: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 11,
    marginLeft: 7,
  },
  time: { fontFamily: "Inter", color: C.muted, fontSize: 12 },
  statusRow: { flexDirection: "row", alignItems: "center", marginTop: 8 },
  statusDot: {
    width: 7,
    height: 7,
    borderRadius: 4,
    backgroundColor: C.green,
    marginRight: 6,
  },
  statusText: {
    color: C.green,
    fontFamily: "Manrope",
    fontSize: 11,
    fontWeight: "600",
  },
  segmented: {
    marginHorizontal: 16,
    marginBottom: 13,
    padding: 4,
    borderRadius: 14,
    backgroundColor: "#0B192A",
    flexDirection: "row",
    borderWidth: 1,
    borderColor: C.border,
  },
  segment: {
    flex: 1,
    minHeight: 42,
    borderRadius: 11,
    alignItems: "center",
    justifyContent: "center",
  },
  segmentActive: { backgroundColor: "#3A4660" },
  segmentText: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 13,
    fontWeight: "600",
  },
  segmentTextActive: { color: C.text },
  libraryCard: {
    minHeight: 94,
    marginBottom: 10,
    borderRadius: 16,
    paddingHorizontal: 14,
    paddingVertical: 13,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
  },
  avatar: {
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.24)",
  },
  avatarImage: { width: "100%", height: "100%" },
  avatarGlow: {
    position: "absolute",
    width: "100%",
    height: "100%",
    backgroundColor: "rgba(208,188,255,0.12)",
  },
  avatarGlyph: { color: "#F2F4FF", fontFamily: "Manrope", fontWeight: "600" },
  characterCopy: { flex: 1, paddingHorizontal: 13 },
  characterName: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 17,
    fontWeight: "700",
  },
  characterRole: {
    fontFamily: "Inter",
    color: C.lavender,
    fontSize: 12,
    marginTop: 2,
  },
  characterDescription: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 13,
    marginTop: 5,
    lineHeight: 18,
  },
  editButton: {
    width: 44,
    height: 44,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 13,
    backgroundColor: "rgba(139,92,246,0.13)",
  },
  fab: {
    position: "absolute",
    right: 18,
    bottom: 92,
    width: 58,
    height: 58,
    borderRadius: 19,
    backgroundColor: C.violet,
    alignItems: "center",
    justifyContent: "center",
    shadowColor: C.violet,
    shadowOpacity: 0.35,
    shadowRadius: 16,
    elevation: 8,
  },
  fabPressed: { transform: [{ scale: 0.96 }], opacity: 0.88 },
  bottomNav: {
    height: 78,
    paddingBottom: Platform.OS === "web" ? 8 : 16,
    paddingTop: 8,
    backgroundColor: "#162438",
    borderTopWidth: 1,
    borderTopColor: C.border,
    flexDirection: "row",
    justifyContent: "space-around",
  },
  navItem: {
    width: 100,
    alignItems: "center",
    justifyContent: "center",
    gap: 4,
  },
  navLabel: {
    fontFamily: "Manrope",
    fontSize: 11,
    color: C.muted,
    fontWeight: "600",
  },
  navLabelActive: { color: C.lavender },
  groupHeaderPanel: {
    paddingHorizontal: 16,
    paddingVertical: 10,
    backgroundColor: "#101F31",
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  groupParticipantRow: {
    minHeight: 46,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  groupParticipantAccent: { width: 5, height: 30, borderRadius: 3 },
  groupParticipantText: { flex: 1 },
  groupParticipantLegend: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 10,
    letterSpacing: 1.4,
    fontWeight: "700",
    marginBottom: 4,
  },
  groupParticipantImmutable: {
    color: "#738097",
    fontFamily: "Inter",
    fontSize: 11,
    marginBottom: 5,
  },
  groupHistoryPreview: {
    padding: 12,
    borderRadius: 13,
    backgroundColor: "rgba(139,92,246,0.08)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.14)",
    marginBottom: 8,
  },
  groupHistoryTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 14,
    fontWeight: "700",
  },
  groupHistoryMeta: {
    color: C.lavender,
    fontFamily: "Inter",
    fontSize: 11,
    marginTop: 4,
  },
  groupHistoryScenario: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    lineHeight: 18,
    marginTop: 8,
  },
  groupHistoryGoal: {
    color: C.text,
    fontFamily: "Inter",
    fontSize: 11,
    lineHeight: 16,
    marginTop: 7,
  },
  groupEditButton: {
    minHeight: 38,
    paddingHorizontal: 12,
    borderRadius: 12,
    backgroundColor: "rgba(139,92,246,0.12)",
    flexDirection: "row",
    alignItems: "center",
    gap: 7,
    marginBottom: 6,
  },
  groupEditText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  groupParticipantName: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 14,
    fontWeight: "700",
  },
  groupParticipantRole: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 11,
    marginTop: 2,
  },
  chatHeader: {
    height: 68,
    paddingHorizontal: 8,
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "rgba(22,36,54,0.96)",
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  groupHeaderAvatars: {
    width: 66,
    height: 46,
    flexDirection: "row",
    alignItems: "center",
  },
  groupHeaderAvatar: {
    borderRadius: 21,
    borderWidth: 2,
    borderColor: "#162436",
    overflow: "hidden",
  },
  groupHeaderAvatarOverlap: { marginLeft: -12 },
  chatHeaderCopy: { flex: 1, paddingLeft: 11 },
  chatName: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 18,
    fontWeight: "700",
  },
  headerPresence: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
    marginTop: 2,
  },
  typing: { fontFamily: "Inter", color: C.green, fontSize: 12 },
  headerTypingDots: {
    color: C.green,
    fontFamily: "Inter",
    fontSize: 13,
    letterSpacing: 1.5,
    marginTop: -2,
  },
  affinityPill: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
    paddingHorizontal: 10,
    paddingVertical: 7,
    borderRadius: 16,
    backgroundColor: "rgba(139,92,246,0.18)",
    marginRight: 4,
  },
  affinityText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  messageArea: { flex: 1, minHeight: 0, overflow: "hidden" },
  chatContent: {
    flexGrow: 1,
    justifyContent: "flex-end",
    paddingHorizontal: 16,
    paddingTop: 12,
    paddingBottom: 8,
  },
  // The history must consume only the remaining height. Without flex the
  // ScrollView grows with its messages and pushes the text field under tall
  // Xiaomi keyboards instead of shrinking above the real IME inset.
  messageList: { flex: 1, minHeight: 0, backgroundColor: "transparent" },
  chatTexture: { ...StyleSheet.absoluteFillObject, overflow: "hidden" },
  textureLineHorizontal: {
    position: "absolute",
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: "rgba(208,188,255,0.035)",
  },
  textureLineVertical: {
    position: "absolute",
    top: 0,
    bottom: 0,
    width: 1,
    backgroundColor: "rgba(208,188,255,0.035)",
  },
  textureWave: {
    position: "absolute",
    width: 520,
    height: 520,
    borderRadius: 260,
    borderWidth: 1,
    borderColor: "rgba(68,192,218,0.08)",
  },
  textureWaveTop: { top: -350, left: -210 },
  textureWaveBottom: { bottom: -390, right: -230 },
  textureStar: {
    position: "absolute",
    width: 3,
    height: 3,
    borderRadius: 2,
    backgroundColor: "#D0BCFF",
  },
  textureSparkle: {
    position: "absolute",
    width: 6,
    height: 6,
    borderRadius: 1,
    backgroundColor: "#F1B7FF",
    transform: [{ rotate: "45deg" }],
  },
  director: {
    alignItems: "center",
    marginVertical: 16,
    paddingHorizontal: 14,
    paddingVertical: 12,
    borderRadius: 16,
    backgroundColor: "rgba(139,92,246,0.07)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.13)",
  },
  directorBadge: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    marginBottom: 7,
  },
  directorBadgeText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 1.4,
  },
  directorText: {
    fontFamily: "Inter",
    fontStyle: "italic",
    color: "#C7C9D7",
    fontSize: 16,
    textAlign: "center",
    lineHeight: 24,
    width: "100%",
    paddingHorizontal: 18,
    paddingVertical: 16,
    marginVertical: 6,
  },
  messageActionText: { color: "#F6C76D", fontStyle: "italic" },
  messageThoughtText: { color: "#8ECCFF", fontStyle: "italic" },
  messageSpeechText: { color: "#F7DB8A" },
  directorLine: {
    width: 50,
    height: 1,
    backgroundColor: "rgba(208,188,255,0.35)",
    marginTop: 12,
  },
  messageRow: { width: "100%", alignItems: "flex-start", marginBottom: 12 },
  messageDateChip: {
    alignSelf: "center",
    marginTop: 5,
    marginBottom: 15,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 14,
    backgroundColor: "rgba(22,36,54,0.86)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.14)",
  },
  messageDateText: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 11,
    fontWeight: "700",
  },
  messageRowMine: { alignItems: "flex-end" },
  messageRowRight: { alignItems: "flex-end" },
  messageRowCenter: { alignItems: "center" },
  bubble: {
    maxWidth: "88%",
    paddingHorizontal: 16,
    paddingTop: 13,
    paddingBottom: 10,
    borderRadius: 17,
    borderWidth: 1,
    borderColor: C.border,
  },
  characterBubble: { backgroundColor: "#151F31", borderTopLeftRadius: 5 },
  characterBubbleAlt: {
    backgroundColor: "#1D2940",
    borderColor: "rgba(121,182,255,0.28)",
    borderTopRightRadius: 5,
  },
  personaBubble: {
    backgroundColor: "#40365F",
    borderColor: "rgba(208,188,255,0.30)",
  },
  userBubble: {
    backgroundColor: "#3B2A60",
    borderBottomRightRadius: 5,
    borderColor: "rgba(208,188,255,0.34)",
  },
  glassCharacterBubble: {
    backgroundColor: "rgba(24,36,55,0.88)",
    borderColor: "rgba(208,228,255,0.13)",
  },
  glassOwnBubble: {
    backgroundColor: "rgba(67,45,101,0.90)",
    borderColor: "rgba(208,188,255,0.30)",
  },
  contrastCharacterBubble: {
    backgroundColor: "#243149",
    borderColor: "#476183",
  },
  contrastOwnBubble: {
    backgroundColor: "#6940B5",
    borderColor: "#A88BEE",
  },
  softCharacterBubble: {
    backgroundColor: "#172437",
    borderColor: "transparent",
    borderRadius: 24,
  },
  softOwnBubble: {
    backgroundColor: "#382F4E",
    borderColor: "transparent",
    borderRadius: 24,
  },
  bubbleText: {
    fontFamily: "Inter",
    color: C.text,
    fontSize: 16,
    lineHeight: 24,
  },
  messageTime: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 11,
    textAlign: "right",
    marginTop: 7,
  },
  personaMessageAuthor: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 11,
    fontWeight: "700",
    marginBottom: 5,
  },
  typingIndicator: {
    flexDirection: "row",
    alignItems: "flex-end",
    gap: 8,
    marginBottom: 18,
  },
  typingName: {
    color: C.lavender,
    fontFamily: "Inter",
    fontSize: 11,
    marginBottom: 4,
  },
  typingBubble: {
    alignSelf: "flex-start",
    backgroundColor: C.card,
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderRadius: 16,
    marginBottom: 18,
  },
  typingDots: { color: C.lavender, letterSpacing: 4, fontSize: 16 },
  choiceLabel: {
    fontFamily: "Manrope",
    color: C.muted,
    letterSpacing: 1.4,
    fontSize: 10,
    fontWeight: "600",
    marginTop: 20,
    marginBottom: 10,
  },
  quickReplies: { gap: 8 },
  quickReply: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.28)",
    backgroundColor: "rgba(139,92,246,0.08)",
    paddingHorizontal: 14,
    paddingVertical: 12,
  },
  quickReplyPressed: { backgroundColor: "rgba(139,92,246,0.24)" },
  quickReplyText: { color: C.lavender, fontFamily: "Inter", fontSize: 14 },
  composerWrap: {
    paddingHorizontal: 13,
    paddingTop: 9,
    paddingBottom: 10,
    backgroundColor: "rgba(22,36,54,0.98)",
    borderTopWidth: 1,
    borderTopColor: C.border,
  },
  composerDock: {
    zIndex: 5,
  },
  jumpToLatestButton: {
    position: "absolute",
    right: 18,
    bottom: 166,
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: C.lavender,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.56)",
    shadowColor: "#000",
    shadowOpacity: 0.3,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 4 },
    elevation: 8,
  },
  composerToolbar: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginBottom: 9,
  },
  playButton: {
    width: 42,
    height: 42,
    borderRadius: 22,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.3)",
    alignItems: "center",
    justifyContent: "center",
  },
  playButtonRunning: {
    backgroundColor: C.green,
    borderColor: "rgba(110,255,203,0.85)",
    shadowColor: C.green,
    shadowOpacity: 0.55,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 0 },
    elevation: 7,
  },
  continueButtonBusy: {
    backgroundColor: "rgba(139,92,246,0.28)",
    borderColor: C.lavender,
  },
  controlButtonPressed: { transform: [{ scale: 0.9 }], opacity: 0.78 },
  turnTimer: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    minWidth: 46,
  },
  groupComposerControls: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  toolbarSpacer: { flex: 1 },
  continueButton: {
    width: 40,
    height: 42,
    paddingHorizontal: 0,
    borderRadius: 21,
    backgroundColor: "rgba(139,92,246,0.13)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.28)",
    alignItems: "center",
    justifyContent: "center",
  },
  continueButtonText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  modePill: {
    height: 42,
    paddingHorizontal: 15,
    borderRadius: 22,
    backgroundColor: "#3C3159",
    flexDirection: "row",
    alignItems: "center",
    gap: 5,
  },
  modeText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 14,
    fontWeight: "600",
  },
  composerIcon: {
    width: 44,
    height: 44,
    alignItems: "center",
    justifyContent: "center",
  },
  composer: {
    minHeight: 62,
    borderRadius: 31,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.23)",
    backgroundColor: "#152338",
    flexDirection: "row",
    alignItems: "center",
    paddingLeft: 19,
    paddingRight: 8,
  },
  input: {
    flex: 1,
    color: C.text,
    fontFamily: "Inter",
    fontSize: 15,
    maxHeight: 78,
    paddingVertical: 8,
  },
  sendButton: {
    width: 47,
    height: 47,
    borderRadius: 24,
    backgroundColor: C.lavender,
    alignItems: "center",
    justifyContent: "center",
  },
  editorContent: { padding: 20, paddingBottom: 28 },
  editorHero: { alignItems: "center", paddingVertical: 13 },
  editorKind: {
    fontFamily: "Manrope",
    letterSpacing: 1.8,
    color: C.lavender,
    fontSize: 11,
    fontWeight: "700",
    marginTop: 13,
  },
  field: { marginTop: 18 },
  memoryPresetSection: { marginTop: 18 },
  memoryPresetIntro: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    lineHeight: 18,
    marginTop: -2,
    marginBottom: 10,
  },
  memoryPresetList: { gap: 8 },
  memoryPresetCard: {
    minHeight: 76,
    paddingHorizontal: 13,
    paddingVertical: 12,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.18)",
    backgroundColor: "#081729",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 11,
  },
  memoryPresetCardActive: {
    borderColor: C.lavender,
    backgroundColor: "rgba(139,92,246,0.16)",
  },
  memoryPresetCopy: { flex: 1 },
  memoryPresetTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 13,
    fontWeight: "700",
  },
  memoryPresetTitleActive: { color: C.lavender },
  memoryPresetDescription: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    lineHeight: 17,
    marginTop: 4,
  },
  choiceSelector: {
    flexDirection: "row",
    gap: 7,
    padding: 4,
    borderRadius: 15,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.20)",
  },
  choiceSelectorItem: {
    flex: 1,
    minHeight: 42,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 11,
  },
  choiceSelectorItemActive: { backgroundColor: C.lavender },
  choiceSelectorText: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  choiceSelectorTextActive: { color: "#2F116C" },
  fieldLabel: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 14,
    fontWeight: "700",
    marginBottom: 8,
  },
  fieldInput: {
    minHeight: 52,
    borderRadius: 15,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.20)",
    backgroundColor: C.card,
    color: C.text,
    fontFamily: "Inter",
    fontSize: 15,
    paddingHorizontal: 15,
  },
  fieldInputMultiline: {
    minHeight: 118,
    textAlignVertical: "top",
    paddingTop: 14,
  },
  generateButton: {
    minHeight: 44,
    marginTop: 11,
    borderRadius: 13,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.28)",
    backgroundColor: "rgba(139,92,246,0.10)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
  },
  generateButtonText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 13,
    fontWeight: "700",
  },
  editorSection: {
    marginTop: 22,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: C.border,
    backgroundColor: "rgba(13,28,45,0.72)",
    padding: 14,
  },
  editorSectionTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 15,
    fontWeight: "700",
  },
  editorSectionHint: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    lineHeight: 17,
    marginTop: 4,
  },
  editorEmptyText: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 13,
    lineHeight: 19,
    marginTop: 14,
  },
  personaChoiceRow: { gap: 8, paddingTop: 13, paddingRight: 6 },
  personaChoice: {
    maxWidth: 150,
    minHeight: 43,
    paddingHorizontal: 10,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.18)",
    backgroundColor: "#081729",
    flexDirection: "row",
    alignItems: "center",
    gap: 7,
  },
  personaChoiceActive: { borderColor: C.lavender, backgroundColor: C.lavender },
  personaChoiceText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
    flexShrink: 1,
  },
  personaChoiceTextActive: { color: "#2F116C" },
  editorToggle: {
    minHeight: 62,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    borderBottomWidth: 1,
    borderBottomColor: "rgba(212,228,250,0.08)",
  },
  editorToggleCopy: { flex: 1, paddingVertical: 9 },
  editorToggleTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 13,
    fontWeight: "700",
  },
  editorToggleHint: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 11,
    lineHeight: 15,
    marginTop: 3,
  },
  editorHint: {
    flexDirection: "row",
    gap: 10,
    alignItems: "flex-start",
    backgroundColor: "rgba(139,92,246,0.10)",
    borderRadius: 15,
    padding: 14,
    marginTop: 20,
  },
  editorHintText: {
    flex: 1,
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 13,
    lineHeight: 19,
  },
  editorFooter: {
    paddingHorizontal: 18,
    paddingBottom: 12,
    paddingTop: 8,
    backgroundColor: "rgba(22,36,54,0.98)",
    borderTopWidth: 1,
    borderTopColor: C.border,
  },
  aiGeneratorCard: {
    borderRadius: 19,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.22)",
    backgroundColor: "rgba(139,92,246,0.09)",
    padding: 15,
    gap: 12,
  },
  aiGeneratorHeading: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 11,
  },
  aiIdeaInput: {
    minHeight: 90,
    borderRadius: 15,
    borderWidth: 1,
    borderColor: C.border,
    backgroundColor: "rgba(2,6,23,0.48)",
    color: C.text,
    fontFamily: "Inter",
    fontSize: 15,
    lineHeight: 21,
    paddingHorizontal: 14,
    paddingVertical: 12,
    textAlignVertical: "top",
  },
  fieldGenerateButton: {
    alignSelf: "flex-end",
    minHeight: 38,
    marginTop: -8,
    marginBottom: 2,
    paddingHorizontal: 12,
    borderRadius: 13,
    flexDirection: "row",
    alignItems: "center",
    gap: 7,
    backgroundColor: "rgba(139,92,246,0.10)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.16)",
  },
  fieldGenerateText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  selectedPresetDescription: {
    marginTop: 12,
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 13,
    lineHeight: 19,
    borderRadius: 13,
    backgroundColor: "rgba(2,6,23,0.34)",
    padding: 12,
  },
  moreContent: { padding: 18, paddingTop: 12, gap: 12, paddingBottom: 42 },
  settingCard: {
    borderRadius: 17,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    padding: 16,
    flexDirection: "row",
    alignItems: "center",
    gap: 13,
  },
  settingCopy: { flex: 1 },
  settingTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontWeight: "700",
    fontSize: 16,
  },
  settingDescription: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 13,
    marginTop: 4,
  },
  fontSizeCard: {
    borderRadius: 17,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    padding: 16,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  fontStepButton: {
    width: 42,
    height: 42,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(139,92,246,0.14)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.18)",
  },
  chatAppearancePreview: {
    minHeight: 220,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.18)",
    overflow: "hidden",
    padding: 16,
    gap: 12,
  },
  previewEyebrow: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 10,
    letterSpacing: 1.5,
    textAlign: "center",
    zIndex: 1,
  },
  previewBubble: {
    maxWidth: "82%",
    paddingHorizontal: 14,
    paddingVertical: 11,
    borderWidth: 1,
    borderRadius: 17,
    zIndex: 1,
  },
  previewBubbleLeft: { alignSelf: "flex-start", borderTopLeftRadius: 5 },
  previewBubbleRight: { alignSelf: "flex-end", borderBottomRightRadius: 5 },
  previewBubbleText: { color: C.text, fontFamily: "Inter" },
  formattingRow: {
    minHeight: 66,
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  formattingCopy: { flex: 1 },
  formattingTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 14,
    fontWeight: "700",
  },
  formattingDescription: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    marginTop: 3,
  },
  formattingPreview: {
    marginTop: 18,
    borderRadius: 15,
    backgroundColor: "#101C2C",
    borderWidth: 1,
    borderColor: C.border,
    padding: 15,
  },
  appearancePreview: {
    flexDirection: "row",
    alignItems: "center",
    padding: 16,
    borderRadius: 17,
    backgroundColor: "#111D2E",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.14)",
  },
  previewSwatch: {
    width: 62,
    height: 62,
    borderRadius: 18,
    backgroundColor: "#080C24",
    alignItems: "center",
    justifyContent: "center",
    marginRight: 14,
  },
  previewStar: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: "#392467",
    alignItems: "center",
    justifyContent: "center",
  },
  previewStarText: { color: C.lavender },
  previewCopy: { flex: 1 },
  previewTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontSize: 15,
    fontWeight: "700",
  },
  previewText: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 13,
    lineHeight: 18,
    marginTop: 4,
  },
  accountLabel: { marginTop: 16 },
  logoutButton: {
    minHeight: 54,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: "rgba(255,180,171,0.25)",
    backgroundColor: "rgba(147,0,10,0.12)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 9,
  },
  logoutText: {
    color: "#FFB4AB",
    fontFamily: "Manrope",
    fontWeight: "700",
    fontSize: 15,
  },
  aboutCard: {
    marginTop: 20,
    alignItems: "center",
    paddingVertical: 32,
    borderRadius: 20,
    backgroundColor: "rgba(139,92,246,0.08)",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.14)",
  },
  aboutLogo: { fontSize: 44, color: C.lavender },
  aboutTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 24,
    fontWeight: "700",
    marginTop: 8,
  },
  aboutText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 14,
    marginTop: 6,
  },
  aboutVersion: {
    fontFamily: "Inter",
    color: "#68758A",
    fontSize: 12,
    marginTop: 22,
  },
  appearanceOption: {
    minHeight: 52,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  appearanceOptionText: { color: C.text, fontFamily: "Inter", fontSize: 15 },
  backgroundOptionCopy: { flexDirection: "row", alignItems: "center", gap: 12 },
  backgroundSwatch: {
    width: 42,
    height: 42,
    borderRadius: 13,
    borderWidth: 2,
  },
  modalBackdrop: {
    flex: 1,
    backgroundColor: "rgba(0,0,0,0.66)",
    justifyContent: "flex-end",
  },
  modalKeyboard: { flex: 1 },
  sheet: {
    maxHeight: "88%",
    backgroundColor: "#172437",
    paddingHorizontal: 22,
    paddingTop: 12,
    paddingBottom: 28,
    borderTopLeftRadius: 28,
    borderTopRightRadius: 28,
    borderTopWidth: 1,
    borderColor: "rgba(208,188,255,0.22)",
  },
  sheetHandle: {
    width: 42,
    height: 4,
    borderRadius: 3,
    backgroundColor: "#6D7890",
    alignSelf: "center",
    marginBottom: 25,
  },
  sheetEyebrow: {
    fontFamily: "Manrope",
    color: C.lavender,
    letterSpacing: 1.6,
    fontSize: 11,
    fontWeight: "700",
    textAlign: "center",
  },
  sheetTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 22,
    fontWeight: "700",
    textAlign: "center",
    marginTop: 10,
  },
  sheetText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 15,
    lineHeight: 23,
    textAlign: "center",
    marginTop: 10,
    marginBottom: 20,
  },
  primaryButton: {
    minHeight: 54,
    borderRadius: 17,
    backgroundColor: C.lavender,
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "row",
    gap: 8,
  },
  primaryButtonText: {
    color: "#2F116C",
    fontFamily: "Manrope",
    fontSize: 16,
    fontWeight: "800",
  },
  secondaryButton: {
    minHeight: 52,
    alignItems: "center",
    justifyContent: "center",
  },
  secondaryButtonText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 15,
    fontWeight: "600",
  },
  inlineEditSheet: {
    margin: 22,
    padding: 22,
    borderRadius: 24,
    backgroundColor: "#172437",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.24)",
  },
  inlineEditActions: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 10,
    marginTop: 16,
  },
  resultSheet: {
    margin: 22,
    padding: 24,
    borderRadius: 25,
    backgroundColor: "#172437",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.24)",
  },
  resultIcon: {
    width: 54,
    height: 54,
    alignSelf: "center",
    borderRadius: 17,
    backgroundColor: "rgba(139,92,246,0.28)",
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 19,
  },
  connectionWelcome: {
    flex: 1,
    paddingHorizontal: 24,
    paddingTop: 58,
    paddingBottom: 28,
    justifyContent: "center",
  },
  connectionLogo: {
    width: 94,
    height: 94,
    borderRadius: 47,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.25)",
    alignItems: "center",
    justifyContent: "center",
    alignSelf: "center",
    backgroundColor: "rgba(139,92,246,0.10)",
    marginBottom: 18,
  },
  connectionWordmark: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 27,
    fontWeight: "700",
    textAlign: "center",
  },
  connectionTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 29,
    lineHeight: 36,
    fontWeight: "700",
    textAlign: "center",
    marginTop: 45,
  },
  connectionText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 15,
    lineHeight: 23,
    textAlign: "center",
    marginTop: 12,
  },
  connectionActions: { gap: 8, marginTop: 30 },
  connectionFootnote: {
    fontFamily: "Inter",
    color: "#68758A",
    fontSize: 11,
    lineHeight: 17,
    textAlign: "center",
    marginTop: 24,
  },
  connectionHeader: {
    paddingHorizontal: 20,
    paddingTop: 28,
    paddingBottom: 16,
  },
  computerList: { paddingHorizontal: 16, gap: 10 },
  searchingCard: {
    minHeight: 88,
    borderRadius: 17,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
  },
  searchingText: { fontFamily: "Inter", color: C.muted, fontSize: 14 },
  computerCard: {
    minHeight: 88,
    borderRadius: 17,
    padding: 15,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
  },
  computerIcon: {
    width: 52,
    height: 52,
    borderRadius: 16,
    backgroundColor: "rgba(139,92,246,0.18)",
    alignItems: "center",
    justifyContent: "center",
    marginRight: 13,
  },
  connectionBottom: { padding: 18, marginTop: "auto" },
  groupSettingsScroll: { maxHeight: 520, marginTop: 6 },
  loginContent: { paddingBottom: 28 },
  computerAddress: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 20,
    fontWeight: "700",
    marginTop: 8,
  },
  passwordField: {
    minHeight: 52,
    borderRadius: 15,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.20)",
    backgroundColor: C.card,
    paddingHorizontal: 15,
    flexDirection: "row",
    alignItems: "center",
  },
  passwordInput: { flex: 1, color: C.text, fontFamily: "Inter", fontSize: 15 },
  splash: {
    flex: 1,
    backgroundColor: C.navy,
    alignItems: "center",
    justifyContent: "center",
  },
  splashGlyph: {
    width: 96,
    height: 96,
    borderRadius: 48,
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.2)",
    alignItems: "center",
    justifyContent: "center",
    shadowColor: C.violet,
    shadowOpacity: 0.7,
    shadowRadius: 30,
    elevation: 15,
  },
  splashStar: { color: "#FFFFFF", fontSize: 43, zIndex: 2 },
  splashCore: {
    position: "absolute",
    width: 28,
    height: 28,
    backgroundColor: C.violet,
    transform: [{ rotate: "45deg" }],
    opacity: 0.95,
  },
  splashTitle: {
    fontFamily: "Manrope",
    fontSize: 36,
    color: C.text,
    fontWeight: "700",
    marginTop: 22,
  },
  splashStatus: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 16,
    marginTop: 58,
  },
  progressTrack: {
    position: "absolute",
    bottom: 30,
    width: "58%",
    height: 4,
    borderRadius: 3,
    backgroundColor: "#26374C",
    overflow: "hidden",
  },
  progressFill: { width: "62%", height: "100%", backgroundColor: C.lavender },
  newChatIntro: { paddingHorizontal: 20, paddingTop: 32, paddingBottom: 18 },
  newChatTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 28,
    fontWeight: "700",
    marginTop: 9,
  },
  newChatText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 15,
    lineHeight: 23,
    marginTop: 9,
    maxWidth: 330,
  },
  typeCards: { padding: 16, gap: 12 },
  typeCard: {
    minHeight: 112,
    borderRadius: 19,
    padding: 16,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
  },
  typeIcon: {
    width: 54,
    height: 54,
    borderRadius: 17,
    backgroundColor: "rgba(139,92,246,0.18)",
    alignItems: "center",
    justifyContent: "center",
    marginRight: 14,
  },
  typeCopy: { flex: 1 },
  typeTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 17,
    fontWeight: "700",
  },
  typeDescription: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 13,
    lineHeight: 19,
    marginTop: 6,
  },
  setupIntro: { paddingHorizontal: 19, paddingTop: 25, paddingBottom: 14 },
  setupTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 26,
    fontWeight: "700",
    marginTop: 7,
  },
  setupText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 14,
    lineHeight: 21,
    marginTop: 7,
  },
  selectCard: {
    minHeight: 78,
    marginBottom: 10,
    borderRadius: 16,
    paddingHorizontal: 14,
    paddingVertical: 12,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
    flexDirection: "row",
    alignItems: "center",
  },
  selectCardActive: {
    backgroundColor: "#26324A",
    borderColor: "rgba(208,188,255,0.4)",
  },
  setupFooter: {
    paddingHorizontal: 18,
    paddingTop: 9,
    paddingBottom: 14,
    backgroundColor: "rgba(22,36,54,0.98)",
    borderTopWidth: 1,
    borderTopColor: C.border,
  },
  selectedCount: {
    fontFamily: "Manrope",
    color: C.muted,
    fontSize: 12,
    textAlign: "center",
    marginBottom: 8,
  },
  disabledButton: { opacity: 0.45 },
  profileContent: { padding: 20, paddingBottom: 34 },
  profileHero: { alignItems: "center", paddingVertical: 18, marginBottom: 8 },
  profileName: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 27,
    fontWeight: "700",
    marginTop: 14,
  },
  profileRole: {
    fontFamily: "Inter",
    color: C.lavender,
    fontSize: 14,
    marginTop: 4,
  },
  profileSection: {
    paddingVertical: 17,
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  profileLabel: {
    fontFamily: "Manrope",
    color: C.muted,
    letterSpacing: 1.4,
    fontSize: 11,
    fontWeight: "700",
    marginBottom: 8,
  },
  profileValue: { fontFamily: "Inter", color: C.lavender, fontSize: 15 },
  profileBody: {
    fontFamily: "Inter",
    color: C.text,
    fontSize: 15,
    lineHeight: 23,
  },
  groupForm: { paddingBottom: 28 },
  formSectionLabel: {
    fontFamily: "Manrope",
    color: C.muted,
    letterSpacing: 1.4,
    fontSize: 11,
    fontWeight: "700",
    marginHorizontal: 20,
    marginTop: 24,
    marginBottom: 8,
  },
  groupParticipants: { paddingHorizontal: 16 },
  behaviorCard: {
    marginHorizontal: 18,
    borderRadius: 17,
    paddingHorizontal: 15,
    backgroundColor: C.card,
    borderWidth: 1,
    borderColor: C.border,
  },
  behaviorRow: {
    minHeight: 71,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  behaviorTitle: {
    fontFamily: "Manrope",
    color: C.text,
    fontSize: 14,
    fontWeight: "700",
  },
  behaviorText: {
    fontFamily: "Inter",
    color: C.muted,
    fontSize: 12,
    marginTop: 4,
  },
  delayPill: {
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 12,
    backgroundColor: "rgba(139,92,246,0.18)",
  },
  delayText: {
    fontFamily: "Manrope",
    color: C.lavender,
    fontSize: 12,
    fontWeight: "700",
  },
  delayButtons: {
    flexDirection: "row",
    gap: 8,
    paddingVertical: 12,
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  delayButton: {
    minWidth: 48,
    height: 34,
    borderRadius: 10,
    backgroundColor: "#162438",
    alignItems: "center",
    justifyContent: "center",
  },
  delayButtonActive: { backgroundColor: C.violet },
  delayButtonText: {
    color: C.muted,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  delayButtonTextActive: { color: C.text },
  authorModeButton: {
    height: 42,
    paddingHorizontal: 11,
    borderRadius: 21,
    backgroundColor: "#25334A",
    flexDirection: "row",
    alignItems: "center",
    gap: 5,
  },
  authorModeButtonActive: {
    backgroundColor: "#463467",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.35)",
  },
  directorButton: {
    width: 58,
    height: 42,
    borderRadius: 21,
    backgroundColor: "#25334A",
    alignItems: "center",
    justifyContent: "center",
  },
  directorButtonActive: {
    backgroundColor: "#463467",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.35)",
  },
  authorOption: {
    minHeight: 62,
    flexDirection: "row",
    alignItems: "center",
    gap: 11,
    borderBottomWidth: 1,
    borderBottomColor: C.border,
  },
  authorOptionIcon: {
    width: 42,
    height: 42,
    borderRadius: 21,
    backgroundColor: "rgba(139,92,246,0.16)",
    alignItems: "center",
    justifyContent: "center",
  },
  authorOptionCopy: { flex: 1 },
  authorOptionTitle: {
    color: C.text,
    fontFamily: "Manrope",
    fontWeight: "700",
    fontSize: 15,
  },
  authorOptionSubtitle: {
    color: C.muted,
    fontFamily: "Inter",
    fontSize: 12,
    marginTop: 3,
  },
  avatarAction: {
    marginTop: 10,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: 11,
    paddingVertical: 7,
    borderRadius: 12,
    backgroundColor: "rgba(139,92,246,0.12)",
  },
  avatarActionText: {
    color: C.lavender,
    fontFamily: "Manrope",
    fontSize: 12,
    fontWeight: "700",
  },
  nextTurnButton: {
    width: 40,
    height: 40,
    borderRadius: 20,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: "rgba(208,188,255,0.28)",
  },
});
