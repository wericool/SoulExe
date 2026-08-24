import { MaterialIcons } from "@expo/vector-icons";
import { type ReactNode } from "react";
import { Pressable, TextInput, View } from "react-native";

import { colors } from "@/lib/theme";

import { styles } from "./_styles";

export function ConversationMessageComposer({ value, onChangeText, placeholder, leftAction, onSend, sendDisabled, rightActions, authorPicker, onFocus }: {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  leftAction?: { icon: keyof typeof MaterialIcons.glyphMap; onPress: () => void; disabled?: boolean; accessibilityLabel: string };
  onSend?: () => void;
  sendDisabled?: boolean;
  rightActions?: { icon: keyof typeof MaterialIcons.glyphMap; onPress: () => void; disabled?: boolean; primary?: boolean; accessibilityLabel: string }[];
  authorPicker?: ReactNode;
  onFocus?: () => void;
}) {
  const resolvedRightActions = rightActions ?? (onSend ? [{ icon: "arrow-upward" as const, onPress: onSend, disabled: sendDisabled || !value.trim(), primary: true, accessibilityLabel: "Отправить" }] : []);
  return <View>{authorPicker}<View style={styles.sceneComposer}>
    {leftAction ? (<Pressable onPress={leftAction.onPress} disabled={leftAction.disabled} style={({ pressed }) => [styles.composerAction, (pressed || leftAction.disabled) && styles.composerActionPressed]}><MaterialIcons name={leftAction.icon} size={22} color={colors.muted} /></Pressable>) : null}
    <TextInput value={value} onChangeText={onChangeText} onFocus={onFocus} placeholder={placeholder} placeholderTextColor={colors.dim} multiline maxLength={8000} textAlignVertical="center" style={styles.sceneComposerInput} />
    {resolvedRightActions.map((action, index) => <Pressable key={`ra-${index}`} onPress={action.onPress} disabled={action.disabled} style={({ pressed }) => [styles.composerAction, action.primary && styles.composerActionPrimary, (pressed || action.disabled) && styles.composerActionPressed]}><MaterialIcons name={action.icon} size={22} color={action.primary ? colors.text : colors.muted} /></Pressable>)}
  </View></View>;
}
