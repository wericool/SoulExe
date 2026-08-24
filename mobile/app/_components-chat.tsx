import { MaterialIcons } from "@expo/vector-icons";
import type { ReactNode } from "react";
import { Pressable, ScrollView, Text, TextInput, View } from "react-native";
import type { ChatMessage, SoulCharacter, SoulPersona } from "@/lib/soulexe-api";
import type { ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";
import { formatTime } from "./_utils";
import { styles } from "./_styles";
import { Avatar } from "@/components/soul/ui";

export function FormattedMessageText({
  content,
  mine = false,
  appearance,
}: {
  content: string;
  mine?: boolean;
  appearance: ChatAppearanceSettings;
}) {
  const parts = content.split(/(<think\b[^>]*>[\s\S]*?<\/think>|\*[^*\n]+\*|«[^»\n]+»|"[^"\n]+")/gi);
  return (
    <Text style={[styles.messageText, mine && styles.messageTextMine]}>
      {parts.map((part, index) => {
        if (/^<think\b/i.test(part))
          return (
            <Text key={index} style={[styles.messageThought, { color: appearance.thoughtColor }]}>
              {appearance.stripThoughtMarkers ? part.replace(/<\/?think\b[^>]*>/gi, "") : part}
            </Text>
          );
        if (/^\*/.test(part))
          return (
            <Text key={index} style={[styles.messageAction, { color: appearance.actionColor }]}>
              {appearance.stripActionMarkers ? part.slice(1, -1) : part}
            </Text>
          );
        if (/^(«|\")/.test(part))
          return (
            <Text key={index} style={[styles.messageSpeech, { color: appearance.speechColor }]}>
              {appearance.stripSpeechMarkers ? part.slice(1, -1) : part}
            </Text>
          );
        return <Text key={index}>{part}</Text>;
      })}
    </Text>
  );
}

export function MessageBubble({
  message,
  appearance,
}: {
  message: ChatMessage;
  appearance: ChatAppearanceSettings;
}) {
  const mine = message.role === "user";
  return (
    <View style={[styles.bubbleRow, mine && styles.bubbleRowMine]}>
      <View style={[styles.bubble, mine ? styles.bubbleMine : styles.bubbleTheirs]}>
        {(!mine || message.authorKind !== "user") && message.author ? (
          <Text style={styles.messageAuthor}>{message.author}</Text>
        ) : null}
        <FormattedMessageText content={message.content} mine={mine} appearance={appearance} />
        <Text style={[styles.messageTime, mine && styles.messageTimeMine]}>
          {formatTime(message.createdAt)}
        </Text>
      </View>
    </View>
  );
}

export function MessageComposer({
  value,
  onChangeText,
  placeholder,
  leftAction,
  onSend,
  sendDisabled,
  rightActions,
  authorPicker,
}: {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  leftAction?: {
    icon: keyof typeof MaterialIcons.glyphMap;
    onPress: () => void;
    disabled?: boolean;
    accessibilityLabel?: string;
  };
  onSend?: () => void;
  sendDisabled?: boolean;
  rightActions?: {
    icon: keyof typeof MaterialIcons.glyphMap;
    onPress: () => void;
    disabled?: boolean;
    primary?: boolean;
    accessibilityLabel?: string;
  }[];
  authorPicker?: ReactNode;
}) {
  const resolvedRightActions = rightActions ?? (onSend ? [{
    icon: "arrow-upward" as const,
    onPress: onSend,
    disabled: sendDisabled || !value.trim(),
    primary: true,
    accessibilityLabel: "Отправить",
  }] : []);
  return (
    <View>
      {authorPicker}
      <View style={styles.sceneComposer}>
      {leftAction ? (
        <Pressable
          onPress={leftAction.onPress}
          disabled={leftAction.disabled}
          style={({ pressed }) => [
            styles.composerAction,
            (pressed || leftAction.disabled) && styles.composerActionPressed,
          ]}
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
        accessibilityLabel={placeholder}
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
          accessibilityLabel={action.accessibilityLabel}
        >
          <MaterialIcons
            name={action.icon}
            size={22}
            color={action.primary ? colors.text : colors.muted}
          />
        </Pressable>
      ))}
      </View>
    </View>
  );
}

export function ComposerAuthorPicker({ personas, value, onChange }: { personas: SoulPersona[]; value: { kind: "user" | "persona" | "director"; personaId?: string }; onChange: (value: { kind: "user" | "persona" | "director"; personaId?: string }) => void }) {
  const choices: Array<{ key: string; label: string; icon: keyof typeof MaterialIcons.glyphMap; personaId?: string; persona?: SoulPersona }> = [{ key: "user", label: "Вы", icon: "person-outline" }, ...personas.map((persona) => ({ key: `persona:${persona.id}`, label: persona.name, icon: "face" as const, personaId: persona.id, persona })), { key: "director", label: "Режиссёр", icon: "movie-creation" }];
  const activeKey = value.kind === "persona" ? `persona:${value.personaId}` : value.kind;
  return <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 7, paddingHorizontal: 12, paddingTop: 8, paddingBottom: 4 }}>
    {choices.map((choice) => <Pressable key={choice.key} onPress={() => onChange(choice.personaId ? { kind: "persona", personaId: choice.personaId } : { kind: choice.key as "user" | "director" })} style={({ pressed }) => [{ flexDirection: "row", alignItems: "center", gap: 4, borderWidth: 1, borderColor: activeKey === choice.key ? colors.accentHover : colors.border, backgroundColor: activeKey === choice.key ? colors.accentSoft : colors.surface0, borderRadius: 14, paddingHorizontal: 10, paddingVertical: 6, opacity: pressed ? 0.75 : 1 }]}>{choice.persona ? <Avatar character={choice.persona} size={16} /> : <MaterialIcons name={choice.icon} size={15} color={activeKey === choice.key ? colors.accentHover : colors.muted} />}<Text numberOfLines={1} style={{ color: activeKey === choice.key ? colors.text : colors.muted, fontSize: 12, fontWeight: "600", maxWidth: 120 }}>{choice.label}</Text></Pressable>)}
  </ScrollView>;
}

export function SceneCharacterPicker({
  label,
  characters,
  selectedId,
  excludeId,
  onSelect,
}: {
  label: string;
  characters: SoulCharacter[];
  selectedId: string;
  excludeId?: string;
  onSelect: (id: string) => void;
}) {
  const selected = characters.find((c) => c.id === selectedId);
  return (
    <View>
      <Text style={styles.sceneOptionLabel}>{label}</Text>
      {selected ? (
        <Pressable
          onPress={() => onSelect(selectedId)}
          style={({ pressed }) => [styles.scenePickerRow, pressed && styles.scenePickerRowPressed]}
        >
          <Avatar character={selected} size={34} />
          <View style={{ flex: 1 }}>
            <Text style={styles.scenePickerName}>{selected.name}</Text>
            <Text style={styles.scenePickerSubtitle}>{selected.title || "Персонаж"}</Text>
          </View>
          <MaterialIcons name="expand-more" size={20} color={colors.dim} />
        </Pressable>
      ) : (
        <Pressable
          onPress={() => onSelect(characters.find((c) => c.id !== excludeId)?.id || "")}
          style={({ pressed }) => [styles.scenePickerRow, pressed && styles.scenePickerRowPressed]}
        >
          <View style={styles.scenePickerEmptyAvatar}>
            <MaterialIcons name="person-outline" size={18} color={colors.dim} />
          </View>
          <Text style={styles.scenePickerSubtitle}>Выберите</Text>
        </Pressable>
      )}
    </View>
  );
}
