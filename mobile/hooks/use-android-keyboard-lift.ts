import { useEffect, useRef, useState } from "react";
import { Keyboard, Platform, useWindowDimensions } from "react-native";

export function useAndroidKeyboardLift() {
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
