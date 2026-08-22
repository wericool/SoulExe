/**
 * ChatActivityEnterView — React Native port of Telegram Android enter-bar concepts
 * (inspired by org.telegram.ui.Components.ChatActivityEnterView, GPL-2.0+).
 *
 * Kept behaviors:
 * - Growing multiline field (min ~48, max ~120)
 * - Send button enabled only when text is non-empty
 * - Circular send control on the right
 * - Optional leading action (attach / format)
 * - Single continuous bar, no heavy bordered "blocks"
 */
import { MaterialIcons } from "@expo/vector-icons";
import { useMemo, useState } from "react";
import {
  Pressable,
  StyleSheet,
  TextInput,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { colors, layout, radii } from "@/lib/theme";

export type EnterAction = {
  icon: keyof typeof MaterialIcons.glyphMap;
  onPress: () => void;
  disabled?: boolean;
  accessibilityLabel?: string;
  primary?: boolean;
};

type Props = {
  value: string;
  onChangeText: (value: string) => void;
  placeholder?: string;
  leftAction?: EnterAction;
  onSend: () => void;
  sendDisabled?: boolean;
  style?: StyleProp<ViewStyle>;
};

export function ChatActivityEnterView({
  value,
  onChangeText,
  placeholder = "Сообщение",
  leftAction,
  onSend,
  sendDisabled,
  style,
}: Props) {
  const [inputHeight, setInputHeight] = useState<number>(layout.enterMinHeight);
  const canSend = value.trim().length > 0 && !sendDisabled;
  const isTall = inputHeight >= layout.enterMaxHeight - 8;

  const sendStyle = useMemo(
    () => [styles.send, canSend ? styles.sendActive : styles.sendIdle],
    [canSend],
  );

  return (
    <View style={[styles.wrap, style]}>
      <View style={styles.bar}>
        {leftAction ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={leftAction.accessibilityLabel}
            disabled={leftAction.disabled}
            onPress={leftAction.onPress}
            style={({ pressed }) => [styles.sideBtn, (pressed || leftAction.disabled) && styles.pressed]}
          >
            <MaterialIcons name={leftAction.icon} size={24} color={colors.muted} />
          </Pressable>
        ) : null}

        <View style={styles.fieldShell}>
          <TextInput
            value={value}
            onChangeText={onChangeText}
            placeholder={placeholder}
            placeholderTextColor={colors.dim}
            multiline
            maxLength={8000}
            scrollEnabled={isTall}
            textAlignVertical="center"
            style={[styles.field, { height: inputHeight }]}
            onContentSizeChange={({ nativeEvent }) => {
              const next = Math.max(
                layout.enterMinHeight,
                Math.min(layout.enterMaxHeight, Math.ceil(nativeEvent.contentSize.height) + 18),
              );
              setInputHeight(next);
            }}
          />
        </View>

        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Отправить"
          disabled={!canSend}
          onPress={onSend}
          style={({ pressed }) => [...sendStyle, pressed && canSend && styles.pressed]}
        >
          <MaterialIcons name="send" size={20} color={canSend ? "#FFFFFF" : colors.dim} />
        </Pressable>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    paddingHorizontal: 6,
    paddingTop: 6,
    paddingBottom: 6,
    backgroundColor: colors.panel,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.hairline,
  },
  bar: {
    flexDirection: "row",
    alignItems: "flex-end",
    gap: 4,
    minHeight: layout.enterMinHeight,
  },
  sideBtn: {
    width: layout.enterButton,
    height: layout.enterButton,
    borderRadius: layout.enterButton / 2,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 4,
  },
  fieldShell: {
    flex: 1,
    minHeight: layout.enterMinHeight,
    borderRadius: radii.enter,
    backgroundColor: colors.input,
    paddingHorizontal: 14,
    justifyContent: "center",
  },
  field: {
    color: colors.text,
    fontSize: 16,
    lineHeight: 21,
    paddingTop: 10,
    paddingBottom: 10,
    margin: 0,
  },
  send: {
    width: layout.enterButton,
    height: layout.enterButton,
    borderRadius: layout.enterButton / 2,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 4,
  },
  sendActive: {
    backgroundColor: colors.send,
  },
  sendIdle: {
    backgroundColor: colors.elevated,
  },
  pressed: { opacity: 0.75 },
});
