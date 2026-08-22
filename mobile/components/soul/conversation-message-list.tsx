import { type ReactNode, type RefObject } from "react";
import {
  FlatList,
  type NativeScrollEvent,
  type NativeSyntheticEvent,
  type StyleProp,
  StyleSheet,
  Text,
  View,
  type ViewStyle,
} from "react-native";

import { colors } from "@/lib/theme";

export type ConversationListMessage = { createdAt?: string };

function dayKey(value?: string) {
  const date = value ? new Date(value) : new Date(0);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function dayLabel(value?: string) {
  const date = value ? new Date(value) : new Date();
  if (Number.isNaN(date.getTime())) return "Сообщения";
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (dayKey(value) === dayKey(today.toISOString())) return "Сегодня";
  if (dayKey(value) === dayKey(yesterday.toISOString())) return "Вчера";
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: date.getFullYear() === today.getFullYear() ? undefined : "numeric" }).format(date);
}

function ConversationDateDivider({ value }: { value?: string }) {
  return <View style={styles.divider}><View style={styles.dividerLine} /><Text style={styles.dividerText}>{dayLabel(value)}</Text><View style={styles.dividerLine} /></View>;
}

export function ConversationMessageList<T extends ConversationListMessage>({
  messages,
  listRef,
  keyExtractor,
  renderMessage,
  style,
  contentContainerStyle,
  initialNumToRender,
  maxToRenderPerBatch,
  windowSize = 7,
  onContentSizeChange,
  onScroll,
  onScrollToIndexFailed,
  footer,
  empty,
}: {
  messages: T[];
  listRef: RefObject<FlatList<T> | null>;
  keyExtractor: (item: T, index: number) => string;
  renderMessage: (item: T, index: number) => ReactNode;
  style?: StyleProp<ViewStyle>;
  contentContainerStyle?: StyleProp<ViewStyle>;
  initialNumToRender?: number;
  maxToRenderPerBatch?: number;
  windowSize?: number;
  onContentSizeChange?: () => void;
  onScroll?: (event: NativeSyntheticEvent<NativeScrollEvent>) => void;
  onScrollToIndexFailed?: () => void;
  footer?: ReactNode;
  empty?: ReactNode;
}) {
  return <FlatList
    ref={listRef}
    style={style}
    keyboardDismissMode="interactive"
    keyboardShouldPersistTaps="handled"
    data={messages}
    keyExtractor={keyExtractor}
    contentContainerStyle={contentContainerStyle}
    initialNumToRender={initialNumToRender ?? Math.max(messages.length, 1)}
    maxToRenderPerBatch={maxToRenderPerBatch ?? Math.max(messages.length, 1)}
    windowSize={windowSize}
    onContentSizeChange={onContentSizeChange}
    onScroll={onScroll}
    scrollEventThrottle={16}
    renderItem={({ item, index }) => <>{index === 0 || dayKey(messages[index - 1]?.createdAt) !== dayKey(item.createdAt) ? <ConversationDateDivider value={item.createdAt} /> : null}{renderMessage(item, index)}</>}
    ListFooterComponent={footer ? () => <>{footer}</> : null}
    ListEmptyComponent={empty ? () => <>{empty}</> : null}
    onScrollToIndexFailed={onScrollToIndexFailed}
  />;
}

const styles = StyleSheet.create({
  divider: { flexDirection: "row", alignItems: "center", gap: 9, paddingHorizontal: 18, paddingTop: 15, paddingBottom: 9 },
  dividerLine: { flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: colors.hairline },
  dividerText: { color: colors.dim, fontSize: 12, fontWeight: "600" },
});
