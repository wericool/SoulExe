import { MaterialIcons } from "@expo/vector-icons";
import type { ReactNode } from "react";
import { ActivityIndicator, Image, Pressable, Text, TextInput, View } from "react-native";
import type { StyleProp, TextInputProps, ViewStyle } from "react-native";

import { colors, radii, space } from "@/lib/theme";

export function Screen({ children }: { children: ReactNode }) {
  return <View style={{ flex: 1, backgroundColor: colors.background }}>{children}</View>;
}

export function Card({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  return <View style={[{ marginHorizontal: space.lg, padding: 14, borderRadius: radii.lg, backgroundColor: colors.panel, borderWidth: 1, borderColor: colors.border }, style]}>{children}</View>;
}

export function PageHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return <View style={{ paddingHorizontal: space.lg, paddingTop: 18, paddingBottom: 14, gap: 3 }}><Text style={{ color: colors.text, fontSize: 24, fontWeight: "800" }}>{title}</Text>{subtitle ? <Text style={{ color: colors.muted, fontSize: 13 }}>{subtitle}</Text> : null}</View>;
}

export function StatusPill({ text, tone = "muted" }: { text: string; tone?: "success" | "muted" | "danger" | "accent" }) {
  const toneColor = tone === "success" ? colors.online : tone === "danger" ? "#FF6978" : tone === "accent" ? colors.accentHover : colors.muted;
  return <View style={{ alignSelf: "flex-start", flexDirection: "row", alignItems: "center", gap: 6, paddingHorizontal: 9, paddingVertical: 5, borderRadius: radii.pill, backgroundColor: colors.elevated }}><View style={{ width: 7, height: 7, borderRadius: 4, backgroundColor: toneColor }} /><Text style={{ color: toneColor, fontWeight: "700", fontSize: 11 }}>{text}</Text></View>;
}

export function Button({ title, onPress, disabled, loading, icon, variant = "primary", style }: { title: string; onPress: () => void; disabled?: boolean; loading?: boolean; icon?: keyof typeof MaterialIcons.glyphMap; variant?: "primary" | "secondary" | "danger"; style?: StyleProp<ViewStyle> }) {
  const backgroundColor = variant === "primary" ? colors.accent : variant === "danger" ? "#B8394A" : colors.elevated;
  const textColor = variant === "secondary" ? colors.text : "#FFFFFF";
  return <Pressable disabled={disabled || loading} onPress={onPress} style={({ pressed }) => [{ minHeight: 46, paddingHorizontal: 14, borderRadius: radii.md, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 8, backgroundColor, opacity: disabled || loading ? 0.5 : pressed ? 0.76 : 1 }, style]}>{loading ? <ActivityIndicator size="small" color={textColor} /> : icon ? <MaterialIcons name={icon} size={19} color={textColor} /> : null}<Text style={{ color: textColor, fontSize: 14, fontWeight: "800" }}>{title}</Text></Pressable>;
}

export function Field({ label, style, multiline, ...props }: TextInputProps & { label: string; style?: TextInputProps["style"] }) {
  return <View style={{ gap: 6 }}><Text style={{ color: colors.dim, fontSize: 10, fontWeight: "800", letterSpacing: 0.8 }}>{label}</Text><TextInput {...props} multiline={multiline} placeholderTextColor={colors.dim} textAlignVertical={multiline ? "top" : "center"} style={[{ minHeight: multiline ? 92 : 48, paddingHorizontal: 13, paddingVertical: 11, borderRadius: radii.md, color: colors.text, backgroundColor: colors.input, borderWidth: 1, borderColor: colors.border, fontSize: 15 }, style]} /></View>;
}

export function EmptyState({ icon, title, caption }: { icon: keyof typeof MaterialIcons.glyphMap; title: string; caption: string }) {
  return <View style={{ alignItems: "center", paddingHorizontal: 32, paddingTop: 48, gap: 8 }}><MaterialIcons name={icon} size={34} color={colors.dim} /><Text style={{ color: colors.text, fontSize: 16, fontWeight: "800" }}>{title}</Text><Text style={{ color: colors.muted, fontSize: 13, textAlign: "center", lineHeight: 19 }}>{caption}</Text></View>;
}

export function Avatar({ character, size = 40 }: { character?: { name: string; avatarUrl?: string | null } | null; size?: number }) {
  const initials = (character?.name || "?").trim().split(/\s+/).slice(0, 2).map((part) => part[0]).join("").toUpperCase();
  const base = { width: size, height: size, borderRadius: size / 2, alignItems: "center" as const, justifyContent: "center" as const, backgroundColor: colors.accentSoft, overflow: "hidden" as const };
  return character?.avatarUrl ? <Image source={{ uri: character.avatarUrl }} style={base} /> : <View style={base}><Text style={{ color: colors.accentHover, fontSize: Math.max(11, size * 0.34), fontWeight: "800" }}>{initials}</Text></View>;
}
