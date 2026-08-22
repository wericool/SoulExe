import { MaterialIcons } from "@expo/vector-icons";
import type { ReactNode } from "react";
import {
  ActivityIndicator,
  Image,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  type TextInputProps,
  View,
  type ViewStyle,
} from "react-native";

import { colors, radii, space, typography } from "@/lib/theme";
import type { SoulCharacter } from "@/lib/soultext-api";

export function Screen({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  return <View style={[styles.screen, style]}>{children}</View>;
}

export function Button({
  title,
  onPress,
  variant = "primary",
  disabled = false,
  icon,
  loading = false,
  style,
}: {
  title: string;
  onPress: () => void;
  variant?: "primary" | "secondary" | "danger" | "ghost" | "accentSoft";
  disabled?: boolean;
  loading?: boolean;
  icon?: keyof typeof MaterialIcons.glyphMap;
  style?: ViewStyle;
}) {
  const bg =
    variant === "danger"
      ? colors.dangerBg
      : variant === "secondary"
        ? colors.elevated
        : variant === "ghost"
          ? "transparent"
          : variant === "accentSoft"
            ? colors.accentSoft
            : colors.accent;

  return (
    <Pressable
      disabled={disabled || loading}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        { backgroundColor: bg },
        variant === "ghost" && styles.buttonGhost,
        variant === "secondary" && styles.buttonSecondary,
        (pressed || disabled || loading) && styles.buttonPressed,
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={colors.text} size="small" />
      ) : (
        <>
          {icon ? <MaterialIcons name={icon} size={18} color={colors.text} /> : null}
          <Text style={styles.buttonText}>{title}</Text>
        </>
      )}
    </Pressable>
  );
}

export function IconButton({
  icon,
  onPress,
  disabled,
  variant = "secondary",
  accessibilityLabel,
}: {
  icon: keyof typeof MaterialIcons.glyphMap;
  onPress: () => void;
  disabled?: boolean;
  variant?: "primary" | "secondary";
  accessibilityLabel?: string;
}) {
  return (
    <Pressable
      accessibilityLabel={accessibilityLabel}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.iconButton,
        { backgroundColor: variant === "primary" ? colors.accent : colors.elevated },
        pressed && styles.buttonPressed,
        disabled && styles.buttonPressed,
      ]}
    >
      <MaterialIcons name={icon} size={20} color={colors.text} />
    </Pressable>
  );
}

export function Field({
  label,
  containerStyle,
  ...props
}: TextInputProps & { label?: string; containerStyle?: ViewStyle }) {
  return (
    <View style={[styles.fieldWrap, containerStyle]}>
      {label ? <Text style={styles.fieldLabel}>{label}</Text> : null}
      <TextInput
        placeholderTextColor={colors.dim}
        {...props}
        style={[styles.input, props.multiline && styles.inputMultiline, props.style]}
      />
    </View>
  );
}

export function Avatar({ character, size = 40 }: { character?: SoulCharacter | null; size?: number }) {
  const initial = character?.name?.slice(0, 1).toUpperCase() || "?";
  if (character?.avatarUrl) {
    return (
      <Image
        source={{ uri: character.avatarUrl }}
        resizeMode="cover"
        style={{ width: size, height: size, borderRadius: size / 2, backgroundColor: colors.card, flexShrink: 0 }}
      />
    );
  }
  return (
    <View style={[styles.avatarFallback, { width: size, height: size, borderRadius: size / 2 }]}>
      <Text style={[styles.avatarText, { fontSize: size * 0.38 }]}>{initial}</Text>
    </View>
  );
}

export function EmptyState({
  title,
  caption,
  icon = "inbox",
}: {
  title: string;
  caption: string;
  icon?: keyof typeof MaterialIcons.glyphMap;
}) {
  return (
    <View style={styles.empty}>
      <View style={styles.emptyIcon}>
        <MaterialIcons name={icon} size={28} color={colors.accentHover} />
      </View>
      <Text style={styles.emptyTitle}>{title}</Text>
      <Text style={styles.emptyCaption}>{caption}</Text>
    </View>
  );
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  return <View style={[styles.card, style]}>{children}</View>;
}

export function StatusPill({
  text,
  tone = "muted",
}: {
  text: string;
  tone?: "muted" | "success" | "danger" | "accent";
}) {
  const map = {
    muted: { bg: colors.elevated, fg: colors.muted },
    success: { bg: colors.success, fg: colors.successText },
    danger: { bg: colors.dangerBg, fg: colors.danger },
    accent: { bg: colors.accentSoft, fg: colors.accentHover },
  }[tone];
  return (
    <View style={[styles.pill, { backgroundColor: map.bg }]}>
      <Text style={[styles.pillText, { color: map.fg }]}>{text}</Text>
    </View>
  );
}

export function PageHeader({
  title,
  subtitle,
  right,
}: {
  title: string;
  subtitle?: string;
  right?: ReactNode;
}) {
  return (
    <View style={styles.pageHeader}>
      <View style={{ flex: 1 }}>
        <Text style={styles.pageTitle}>{title}</Text>
        {subtitle ? <Text style={styles.pageSubtitle}>{subtitle}</Text> : null}
      </View>
      {right}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  button: { minHeight: 48, borderRadius: radii.md, paddingHorizontal: 16, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 8 },
  buttonGhost: { borderWidth: 1, borderColor: colors.borderStrong },
  buttonSecondary: { borderWidth: 1, borderColor: colors.border },
  buttonPressed: { opacity: 0.72 },
  buttonText: { color: colors.text, fontSize: 14, fontWeight: "700" },
  iconButton: { width: 44, height: 44, flexShrink: 0, borderRadius: radii.md, alignItems: "center", justifyContent: "center", borderWidth: 1, borderColor: colors.borderStrong, overflow: "hidden" },
  fieldWrap: { gap: 6 },
  fieldLabel: { ...typography.label, color: colors.muted, textTransform: "uppercase" },
  input: { minHeight: 48, borderRadius: radii.md, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.input, color: colors.text, paddingHorizontal: 14, paddingVertical: 12, fontSize: 15 },
  inputMultiline: { minHeight: 88, textAlignVertical: "top" },
  avatarFallback: { alignItems: "center", justifyContent: "center", backgroundColor: colors.accentBlue },
  avatarText: { color: colors.text, fontWeight: "800" },
  empty: { flex: 1, alignItems: "center", justifyContent: "center", paddingHorizontal: 36, gap: 10 },
  emptyIcon: { width: 56, height: 56, borderRadius: 18, backgroundColor: colors.accentSoft, alignItems: "center", justifyContent: "center", marginBottom: 4 },
  emptyTitle: { ...typography.section, color: colors.text, textAlign: "center" },
  emptyCaption: { ...typography.body, color: colors.muted, textAlign: "center" },
  card: { backgroundColor: colors.panel, borderColor: colors.hairline, borderWidth: StyleSheet.hairlineWidth, borderRadius: radii.lg, padding: space.lg },
  pill: { paddingHorizontal: 10, paddingVertical: 4, borderRadius: radii.pill },
  pillText: { fontSize: 11, fontWeight: "700" },
  pageHeader: { flexDirection: "row", alignItems: "center", gap: 12, marginBottom: 12, paddingTop: 4 },
  pageTitle: { ...typography.title, color: colors.text },
  pageSubtitle: { ...typography.caption, color: colors.muted, marginTop: 2 },
});
