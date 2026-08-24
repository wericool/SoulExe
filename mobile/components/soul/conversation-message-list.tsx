import type { ReactNode, RefObject } from "react";
import { FlatList, Text, View } from "react-native";
import type { FlatListProps, StyleProp, ViewStyle } from "react-native";

type MessageListProps<T> = {
  messages: T[];
  listRef?: RefObject<FlatList<T> | null>;
  renderMessage: (item: T) => ReactNode;
  footer?: ReactNode;
  empty?: ReactNode;
  style?: StyleProp<ViewStyle>;
  contentContainerStyle?: FlatListProps<T>["contentContainerStyle"];
  keyExtractor: (item: T, index: number) => string;
} & Pick<FlatListProps<T>, "initialNumToRender" | "maxToRenderPerBatch" | "windowSize" | "onContentSizeChange" | "onScroll" | "onScrollToIndexFailed">;

export function ConversationMessageList<T>({ messages, listRef, renderMessage, footer, empty, contentContainerStyle, ...props }: MessageListProps<T>) {
  return <FlatList ref={listRef} data={messages} renderItem={({ item }) => <>{renderMessage(item)}</>} ListFooterComponent={footer ? <View>{footer}</View> : null} ListEmptyComponent={empty ? <View>{empty}</View> : <Text />} contentContainerStyle={contentContainerStyle} {...props} />;
}
