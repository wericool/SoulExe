import { MaterialIcons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";

import { Avatar, IconButton, StatusPill } from "@/components/soul/ui";
import { colors, layout, radii, typography } from "@/lib/theme";
import type { SoulCharacter } from "@/lib/soultext-api";

function formatListTime(value?: string) {
  if (!value) return "";
  try {
    const date = new Date(value);
    const now = new Date();
    if (date.toDateString() === now.toDateString()) {
      return date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
    }
    const yesterday = new Date(now);
    yesterday.setDate(now.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) return "вчера";
    return date.toLocaleDateString("ru-RU", { day: "2-digit", month: "2-digit" });
  } catch {
    return "";
  }
}

/** Telegram-style dialog row: avatar | title+time / preview */
export function MessengerRow({
  title,
  subtitle,
  updatedAt,
  character,
  sceneCharacters,
  status,
  unread,
  onPress,
}: {
  title: string;
  subtitle: string;
  updatedAt?: string;
  character?: SoulCharacter | null;
  sceneCharacters?: [SoulCharacter | null | undefined, SoulCharacter | null | undefined];
  status?: "running" | "paused" | "finished";
  unread?: number;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      android_ripple={{ color: "rgba(255,255,255,0.06)" }}
      style={({ pressed }) => [styles.row, pressed && styles.pressed]}
    >
      {sceneCharacters ? (
        <View style={styles.stack}>
          <Avatar character={sceneCharacters[0]} size={40} />
          <View style={styles.stackSecond}>
            <Avatar character={sceneCharacters[1]} size={40} />
          </View>
        </View>
      ) : (
        <Avatar character={character} size={layout.avatarList} />
      )}

      <View style={styles.body}>
        <View style={styles.topLine}>
          <Text numberOfLines={1} style={styles.title}>
            {title}
          </Text>
          <Text style={styles.time}>{formatListTime(updatedAt)}</Text>
        </View>
        <View style={styles.bottomLine}>
          <Text numberOfLines={1} style={styles.subtitle}>
            {subtitle || "Нет сообщений"}
          </Text>
          {status ? (
            <View
              style={[
                styles.statusDot,
                status === "running" && styles.statusRunning,
                status === "paused" && styles.statusPaused,
                status === "finished" && styles.statusFinished,
              ]}
            />
          ) : null}
          {unread && unread > 0 ? (
            <View style={styles.badge}>
              <Text style={styles.badgeText}>{unread > 99 ? "99+" : String(unread)}</Text>
            </View>
          ) : null}
        </View>
      </View>
    </Pressable>
  );
}

export function MessengerThreadHeader({
  title,
  subtitle,
  character,
  onBack,
  onTitlePress,
  onEdit,
  timer,
  status,
}: {
  title: string;
  subtitle?: string;
  character?: SoulCharacter | null;
  onBack: () => void;
  onTitlePress?: () => void;
  onEdit?: () => void;
  timer?: string;
  status?: { text: string; tone: "success" | "muted" | "danger" | "accent" };
}) {
  return (
    <View style={styles.threadHeader}>
      <Pressable onPress={onBack} style={styles.headerIcon} accessibilityLabel="Назад">
        <MaterialIcons name="arrow-back" size={24} color={colors.text} />
      </Pressable>
      {character ? <Avatar character={character} size={layout.avatarHeader} /> : null}
      <Pressable onPress={onTitlePress} disabled={!onTitlePress} style={styles.headerCopy}>
        <Text numberOfLines={1} style={styles.headerTitle}>
          {title}
        </Text>
        {subtitle ? (
          <Text numberOfLines={1} style={styles.headerSubtitle}>
            {subtitle}
          </Text>
        ) : null}
      </Pressable>
      {timer ? <Text style={styles.timer}>{timer}</Text> : null}
      {status ? <StatusPill text={status.text} tone={status.tone} /> : null}
      {onEdit ? (
        <Pressable onPress={onEdit} style={styles.headerIcon} accessibilityLabel="Ещё">
          <MaterialIcons name="more-vert" size={22} color={colors.muted} />
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    height: layout.chatRowHeight,
    paddingHorizontal: 12,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    backgroundColor: colors.background,
  },
  pressed: { backgroundColor: colors.panel },
  body: { flex: 1, minWidth: 0, paddingVertical: 12, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.hairline },
  topLine: { flexDirection: "row", alignItems: "center", gap: 8 },
  bottomLine: { flexDirection: "row", alignItems: "center", gap: 8, marginTop: 3 },
  title: { ...typography.chatTitle, color: colors.text, flex: 1 },
  time: { ...typography.time, color: colors.dim },
  subtitle: { ...typography.chatPreview, color: colors.muted, flex: 1 },
  statusDot: { width: 8, height: 8, borderRadius: 4 },
  statusRunning: { backgroundColor: colors.online },
  statusPaused: { backgroundColor: colors.accentHover },
  statusFinished: { backgroundColor: colors.dim },
  badge: {
    minWidth: 20,
    height: 20,
    borderRadius: 10,
    paddingHorizontal: 6,
    backgroundColor: colors.badge,
    alignItems: "center",
    justifyContent: "center",
  },
  badgeText: { color: "#fff", fontSize: 12, fontWeight: "700" },
  stack: { width: 58, height: 54, flexDirection: "row", alignItems: "center" },
  stackSecond: { marginLeft: -16 },
  threadHeader: {
    height: layout.threadHeaderHeight,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingHorizontal: 4,
    backgroundColor: colors.panel,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: colors.hairline,
  },
  headerIcon: { width: 44, height: 44, alignItems: "center", justifyContent: "center" },
  headerCopy: { flex: 1, minWidth: 0 },
  headerTitle: { color: colors.text, fontSize: 16, fontWeight: "600" },
  headerSubtitle: { color: colors.muted, fontSize: 13, marginTop: 1 },
  timer: { color: colors.accentHover, fontSize: 12, fontWeight: "700", fontVariant: ["tabular-nums"] },
});
