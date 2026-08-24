import { MaterialIcons } from "@expo/vector-icons";
import { StatusBar } from "expo-status-bar";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ActivityIndicator, FlatList, KeyboardAvoidingView, Platform, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { Button, Field, Screen } from "@/components/soul/ui";
import { checkSoulExeServer, normalizeServerUrl, SoulExeApiClient, type SoulExeApi, type SoulExeSession } from "@/lib/soulexe-api";
import { createSoulExeDemoApi } from "@/lib/soulexe-demo-api";
import { discoverSoulExeServers, type DiscoveredSoulExeServer } from "@/lib/soulexe-discovery";
import { clearSoulExeSession, defaultChatAppearance, loadChatAppearance, loadSoulExeSession, saveChatAppearance, saveSoulExeSession, type ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";

import type { TabKey } from "./_types";
import { styles } from "./_styles";
import { CharactersScreen } from "./_screens-characters";
import { SettingsScreen } from "./_screens-settings";
import { ChatsScreen } from "./_screens-chats";
export default function SoulExeMobile() {
  const [session, setSession] = useState<SoulExeSession | null>(null);
  const [booting, setBooting] = useState(true);
  const [tab, setTab] = useState<TabKey>("chats");
  const [demoMode, setDemoMode] = useState(false);
  const [appearance, setAppearance] = useState<ChatAppearanceSettings>(defaultChatAppearance);
  const demoApi = useMemo(() => createSoulExeDemoApi(), []);
  const liveApi = useMemo(() => (session ? new SoulExeApiClient(session) : null), [session]);

  useEffect(() => {
    Promise.all([loadSoulExeSession(), loadChatAppearance()])
      .then(([nextSession, nextAppearance]) => {
        setSession(nextSession);
        setAppearance(nextAppearance);
      })
      .catch(() => {
        // Keep defaults — user will see connection screen
      })
      .finally(() => setBooting(false));
  }, []);

  const updateAppearance = useCallback((changes: Partial<ChatAppearanceSettings>) => {
    setAppearance((current) => {
      const next = { ...current, ...changes };
      void saveChatAppearance(next);
      return next;
    });
  }, []);

  if (booting) {
    return <SplashScreen />;
  }

  if (!session && !demoMode) {
    return (
      <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
        <StatusBar style="light" />
        <ConnectionScreen
          onConnected={async (next) => {
            await saveSoulExeSession(next);
            setSession(next);
            setDemoMode(false);
            setTab("chats");
          }}
          onEnterDemo={() => {
            setDemoMode(true);
            setTab("chats");
          }}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
      <StatusBar style="light" />
      <ConnectedApp
        api={demoMode ? demoApi : liveApi!}
        isDemo={demoMode}
        appearance={appearance}
        onAppearanceChange={updateAppearance}
        tab={tab}
        onTabChange={setTab}
        onLogout={async () => {
          if (demoMode) {
            setDemoMode(false);
            return;
          }
          await clearSoulExeSession();
          setSession(null);
        }}
      />
    </SafeAreaView>
  );
}

function SplashScreen() {
  return (
    <SafeAreaView style={styles.root} edges={["top", "left", "right"]}>
      <StatusBar style="light" />
      <View style={styles.boot}>
        <View style={styles.logoMark}>
          <MaterialIcons name="auto-awesome" size={28} color={colors.text} />
        </View>
        <Text style={styles.bootTitle}>SoulExe</Text>
        <ActivityIndicator color={colors.accentHover} style={{ marginTop: 18 }} />
      </View>
    </SafeAreaView>
  );
}

function ConnectionScreen({
  onConnected,
  onEnterDemo,
}: {
  onConnected: (session: SoulExeSession) => Promise<void>;
  onEnterDemo: () => void;
}) {
  const [serverUrl, setServerUrl] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [status, setStatus] = useState("Найдите SoulExe в Wi-Fi или введите адрес вручную.");
  const [servers, setServers] = useState<DiscoveredSoulExeServer[]>([]);
  const [busy, setBusy] = useState(false);
  const [step, setStep] = useState<"start" | "servers" | "login">("start");

  const discover = async () => {
    setBusy(true);
    setServers([]);
    setStep("servers");
    try {
      const found = await discoverSoulExeServers(setStatus);
      setServers(found);
      if (!found.length) setStatus("SoulExe в сети не найден. Проверьте, что мобильный доступ включён на ПК.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Не удалось начать поиск.");
    } finally {
      setBusy(false);
    }
  };

  const selectServer = (server: DiscoveredSoulExeServer) => {
    setServerUrl(server.baseUrl);
    setStatus(`Вход в SoulExe по адресу ${server.baseUrl}`);
    setStep("login");
  };

  const connect = async () => {
    const baseUrl = normalizeServerUrl(serverUrl);
    if (!baseUrl || !username || !password) {
      setStatus("Укажите адрес, логин и пароль из «Мобильный доступ» на ПК.");
      return;
    }
    setBusy(true);
    setStatus("Проверяю сервер и вхожу…");
    try {
      await checkSoulExeServer(baseUrl);
      await onConnected(await SoulExeApiClient.login(baseUrl, username, password));
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Подключение не удалось.");
    } finally {
      setBusy(false);
    }
  };

  if (step === "servers")
    return (
      <View style={styles.connectPlain}>
        <View style={styles.connectPlainBrand}>
          <View style={styles.authOrb}>
            <MaterialIcons name="wifi-find" size={34} color={colors.text} />
          </View>
          <Text style={styles.connectPlainTitle}>Выберите компьютер</Text>
          <Text style={styles.connectPlainText}>{status}</Text>
        </View>
        <FlatList
          style={styles.serverPickerList}
          data={servers}
          keyExtractor={(item) => item.baseUrl}
          ListEmptyComponent={
            busy ? (
              <ActivityIndicator color={colors.accentHover} style={{ marginTop: 24 }} />
            ) : (
              <Text style={styles.connectEmptyText}>Компьютеры пока не найдены.</Text>
            )
          }
          renderItem={({ item }) => (
            <Pressable
              onPress={() => selectServer(item)}
              style={({ pressed }) => [styles.serverPickerRow, pressed && styles.serverPickerRowPressed]}
            >
              <MaterialIcons name="desktop-windows" size={22} color={colors.accentHover} />
              <View style={{ flex: 1 }}>
                <Text style={styles.serverTitle}>{item.name || "SoulExe на ПК"}</Text>
                <Text style={styles.serverUrl}>{item.baseUrl}</Text>
              </View>
              <MaterialIcons name="chevron-right" size={22} color={colors.dim} />
            </Pressable>
          )}
          ListFooterComponent={
            <Button
              title={busy ? "Ищу…" : "Искать ещё раз"}
              variant="secondary"
              icon="refresh"
              disabled={busy}
              onPress={discover}
              style={{ marginTop: 20 }}
            />
          }
        />
        <Pressable onPress={() => setStep("start")} style={styles.connectBack}>
          <Text style={styles.connectBackText}>Назад</Text>
        </Pressable>
      </View>
    );

  if (step === "login")
    return (
      <KeyboardAvoidingView
        style={styles.grow}
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        keyboardVerticalOffset={0}
      >
        <ScrollView
          contentContainerStyle={[styles.connectPlain, styles.connectPlainLogin]}
          keyboardDismissMode="interactive"
          keyboardShouldPersistTaps="handled"
        >
          <View style={styles.connectPlainBrand}>
            <View style={styles.authOrb}>
              <MaterialIcons name="lock-outline" size={34} color={colors.text} />
            </View>
            <Text style={styles.connectPlainTitle}>Вход в SoulExe</Text>
            <Text style={styles.connectPlainText}>{serverUrl}</Text>
          </View>
          <View style={styles.loginFields}>
            <Field
              label="Логин"
              value={username}
              onChangeText={setUsername}
              autoCapitalize="none"
              autoCorrect={false}
              placeholder="Как в SoulExe на ПК"
            />
            <Field
              label="Пароль"
              value={password}
              onChangeText={setPassword}
              secureTextEntry
              placeholder="Пароль мобильного доступа"
              onSubmitEditing={connect}
            />
            <Button
              title={busy ? "Вход…" : "Войти"}
              icon="login"
              disabled={busy}
              loading={busy}
              onPress={connect}
            />
            <Text style={styles.connectStatus}>{status}</Text>
          </View>
          <Pressable onPress={() => setStep("servers")} style={styles.connectBack}>
            <Text style={styles.connectBackText}>Выбрать другой компьютер</Text>
          </Pressable>
        </ScrollView>
      </KeyboardAvoidingView>
    );

  return (
    <View style={styles.connectPlain}>
      <View style={styles.connectPlainBrand}>
        <View style={styles.authOrb}>
          <MaterialIcons name="forum" size={34} color={colors.text} />
        </View>
        <Text style={styles.authProductName}>SoulExe Mobile</Text>
        <Text style={styles.connectPlainTitle}>Ваши персонажи — рядом</Text>
        <Text style={styles.connectPlainText}>
          Подключитесь к SoulExe на компьютере или сначала посмотрите приложение в демо-режиме.
        </Text>
      </View>
      <View style={styles.connectActions}>
        <Button
          title="Найти SoulExe в Wi-Fi"
          icon="wifi-find"
          disabled={busy}
          loading={busy}
          onPress={discover}
        />
        <Button
          title="Открыть демо-режим"
          icon="play-circle-outline"
          variant="secondary"
          disabled={busy}
          onPress={onEnterDemo}
        />
      </View>
    </View>
  );
}

function ConnectedApp({
  api,
  isDemo,
  appearance,
  onAppearanceChange,
  tab,
  onTabChange,
  onLogout,
}: {
  api: SoulExeApi;
  isDemo: boolean;
  appearance: ChatAppearanceSettings;
  onAppearanceChange: (changes: Partial<ChatAppearanceSettings>) => void;
  tab: TabKey;
  onTabChange: (tab: TabKey) => void;
  onLogout: () => Promise<void>;
}) {
  const [threadOpen, setThreadOpen] = useState(false);
  const changeTab = (next: TabKey) => {
    setThreadOpen(false);
    onTabChange(next);
  };

  return (
    <Screen>
      <View style={styles.content}>
        <View style={[styles.tabPage, tab !== "chats" && styles.tabPageHidden]}>
          <ChatsScreen
            api={api}
            appearance={appearance}
            isVisible={tab === "chats"}
            onThreadChange={setThreadOpen}
          />
        </View>
        {tab === "characters" ? <CharactersScreen api={api} /> : null}
        {tab === "settings" ? (
          <SettingsScreen
            baseUrl={isDemo ? "Автономная демонстрация" : "Подключение к SoulExe на ПК"}
            isDemo={isDemo}
            appearance={appearance}
            onAppearanceChange={onAppearanceChange}
            onLogout={onLogout}
          />
        ) : null}
      </View>
      {!threadOpen ? (
        <View style={styles.tabBar}>
          <TabButton
            icon="chat-bubble-outline"
            label="Разговоры"
            active={tab === "chats"}
            onPress={() => changeTab("chats")}
          />
          <TabButton
            icon="people-outline"
            label="Персонажи"
            active={tab === "characters"}
            onPress={() => changeTab("characters")}
          />
          <TabButton
            icon="settings"
            label="Ещё"
            active={tab === "settings"}
            onPress={() => changeTab("settings")}
          />
        </View>
      ) : null}
    </Screen>
  );
}

function TabButton({
  icon,
  label,
  active,
  onPress,
}: {
  icon: keyof typeof MaterialIcons.glyphMap;
  label: string;
  active: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      style={[styles.tabButton, active && styles.tabButtonActive]}
    >
      <MaterialIcons name={icon} size={22} color={active ? colors.accentHover : colors.muted} />
      <Text style={[styles.tabLabel, active && styles.tabLabelActive]}>{label}</Text>
    </Pressable>
  );
}
