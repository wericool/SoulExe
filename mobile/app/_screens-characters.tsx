import { MaterialIcons } from "@expo/vector-icons";
import * as ImagePicker from "expo-image-picker";
import { useCallback, useEffect, useState } from "react";
import { Alert, BackHandler, FlatList, Pressable, ScrollView, Text, View } from "react-native";

import { Avatar, Button, EmptyState, Field, PageHeader } from "@/components/soul/ui";
import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import type { SoulCharacter, SoulCharacterDraft, SoulExeApi } from "@/lib/soulexe-api";
import { colors } from "@/lib/theme";
import { styles } from "./_styles";

export function CharactersScreen({ api }: { api: SoulExeApi }) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [busy, setBusy] = useState(false);
  const [active, setActive] = useState<SoulCharacter>();
  const [creating, setCreating] = useState(false);
  const load = useCallback(async (quiet = false) => {
    if (!quiet) setBusy(true);
    try {
      const fresh = await api.getCharacters();
      setCharacters(fresh);
      setActive((current) => current ? fresh.find((item) => item.id === current.id) ?? current : current);
    } finally { if (!quiet) setBusy(false); }
  }, [api]);
  useEffect(() => { load().catch((error) => Alert.alert("Персонажи", error instanceof Error ? error.message : "Ошибка сети")); }, [load]);
  useEffect(() => { const timer = setInterval(() => { void load(true).catch(() => undefined); }, 3000); return () => clearInterval(timer); }, [load]);
  useEffect(() => {
    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      if (creating) { setCreating(false); return true; }
      if (active) { setActive(undefined); return true; }
      return false;
    });
    return () => subscription.remove();
  }, [active, creating]);
  if (creating) return <CharacterCreateScreen api={api} onBack={() => setCreating(false)} onCreated={(character) => { setCharacters((current) => [character, ...current]); setCreating(false); setActive(character); }} />;
  if (active) return <CharacterEditorScreen api={api} character={active} onBack={() => { setActive(undefined); void load(); }} onSaved={(character) => { setActive(character); setCharacters((current) => current.map((item) => item.id === character.id ? character : item)); }} />;
  return <View style={styles.grow}><FlatList data={characters} keyExtractor={(item) => item.id} contentContainerStyle={styles.characterListWithFab} refreshing={busy} onRefresh={load} ListEmptyComponent={busy ? <View /> : <EmptyState icon="groups" title="Пусто" caption="Создайте персонажа кнопкой внизу." />} renderItem={({ item }) => <Pressable onPress={() => setActive(item)} style={styles.characterCard}><Avatar character={item} size={48} /><View style={{ flex: 1 }}><Text style={styles.characterName}>{item.name}</Text><Text style={styles.chatMeta} numberOfLines={2}>{item.title || item.description || "Карточка персонажа"}</Text></View><MaterialIcons name="edit" size={19} color={colors.dim} /></Pressable>} /><FloatingCreateButton icon="person-add" onPress={() => setCreating(true)} accessibilityLabel="Создать персонажа" /></View>;
}

function FloatingCreateButton({ icon, onPress, accessibilityLabel }: { icon: keyof typeof MaterialIcons.glyphMap; onPress: () => void; accessibilityLabel: string }) {
  return <Pressable accessibilityRole="button" accessibilityLabel={accessibilityLabel} onPress={onPress} style={({ pressed }) => [styles.floatingCreate, pressed && styles.floatingCreatePressed]}><MaterialIcons name={icon} size={25} color={colors.text} /></Pressable>;
}

function CharacterCreateScreen({ api, onBack, onCreated }: { api: SoulExeApi; onBack: () => void; onCreated: (character: SoulCharacter) => void }) {
  const [mode, setMode] = useState<"manual" | "generate">("manual");
  const [name, setName] = useState("");
  const [idea, setIdea] = useState("");
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    if (mode === "manual" && !name.trim()) { Alert.alert("Персонаж", "Укажите имя."); return; }
    if (mode === "generate" && !idea.trim()) { Alert.alert("Персонаж", "Опишите персонажа для генерации."); return; }
    setBusy(true);
    try { onCreated(mode === "manual" ? await api.createCharacter({ name: name.trim() }) : await api.generateCharacter(idea.trim())); }
    catch (error) { Alert.alert("Персонаж", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.editorScroll}><MessengerThreadHeader title="Новый персонаж" subtitle="Создайте сами или заполните через ИИ" onBack={onBack} /><View style={styles.modeRow}><Button title="Вручную" variant={mode === "manual" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setMode("manual")} /><Button title="Сгенерировать" variant={mode === "generate" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setMode("generate")} /></View>{mode === "manual" ? <Field label="Имя" value={name} onChangeText={setName} placeholder="Имя персонажа" /> : <Field label="Идея" value={idea} onChangeText={setIdea} placeholder="Кратко опишите персонажа по-русски" multiline style={styles.largeField} />}<Button title={busy ? "Создаю…" : mode === "manual" ? "Создать персонажа" : "Сгенерировать персонажа"} loading={busy} disabled={busy} onPress={() => void submit()} style={{ marginTop: 16 }} /></ScrollView>;
}

export function CharacterEditorScreen({ api, character, onBack, onSaved }: { api: SoulExeApi; character: SoulCharacter; onBack: () => void; onSaved: (character: SoulCharacter) => void }) {
  const [draft, setDraft] = useState<SoulCharacterDraft>({ name: character.name, title: character.title || "", description: character.description || "", personality: character.personality || "", scenario: character.scenario || "", systemPrompt: character.systemPrompt || "", soulMemoryEnabled: character.soulMemoryEnabled, autoSummaryEnabled: character.autoSummaryEnabled });
  const [busy, setBusy] = useState(false);
  const update = <K extends keyof SoulCharacterDraft>(key: K, value: SoulCharacterDraft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const save = async () => { if (!draft.name.trim()) { Alert.alert("Персонаж", "Имя обязательно."); return; } setBusy(true); try { onSaved(await api.updateCharacter(character.id, draft)); } catch (error) { Alert.alert("Персонаж", error instanceof Error ? error.message : "Ошибка сети"); } finally { setBusy(false); } };
  const chooseAvatar = async () => {
    try {
      const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ImagePicker.MediaTypeOptions.Images, allowsEditing: true, aspect: [1, 1], quality: 0.85 });
      if (result.canceled || !result.assets[0]) return;
      setBusy(true);
      onSaved(await api.uploadCharacterAvatar(character.id, result.assets[0]));
    } catch (error) { Alert.alert("Аватар", error instanceof Error ? error.message : "Не удалось сохранить аватар."); }
    finally { setBusy(false); }
  };
  return <ScrollView contentContainerStyle={styles.editorScroll} keyboardShouldPersistTaps="handled"><MessengerThreadHeader title="Профиль персонажа" subtitle="Изменения сохраняются на ПК" character={character} onBack={onBack} /><View style={styles.profileHero}><Avatar character={character} size={84} /><Text style={styles.profileName}>{character.name}</Text><Button title="Изменить фото" icon="photo-library" variant="secondary" disabled={busy} onPress={() => void chooseAvatar()} style={styles.avatarUploadButton} /></View><Field label="Имя" value={draft.name} onChangeText={(value) => update("name", value)} /><Field label="Подзаголовок" value={draft.title || ""} onChangeText={(value) => update("title", value)} /><Field label="Описание" value={draft.description || ""} onChangeText={(value) => update("description", value)} multiline style={styles.largeField} /><Field label="Личность" value={draft.personality || ""} onChangeText={(value) => update("personality", value)} multiline style={styles.largeField} /><Field label="Сценарий" value={draft.scenario || ""} onChangeText={(value) => update("scenario", value)} multiline style={styles.largeField} /><Field label="Системный промпт" value={draft.systemPrompt || ""} onChangeText={(value) => update("systemPrompt", value)} multiline style={styles.largeField} /><Button title={busy ? "Сохраняю…" : "Сохранить"} loading={busy} disabled={busy} onPress={() => void save()} style={{ marginTop: 16 }} /></ScrollView>;
}

export function CharacterProfilePreview({ character, chatName, onBack, onEdit }: { character: SoulCharacter; chatName?: string; onBack: () => void; onEdit: () => void }) {
  return <ScrollView contentContainerStyle={styles.profileScroll}><MessengerThreadHeader title="Профиль" onBack={onBack} onEdit={onEdit} /><View style={styles.profileHero}><Avatar character={character} size={96} /><Text style={styles.profileName}>{character.name}</Text><Text style={styles.profileTitle}>{character.title || "Персонаж SoulExe"}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ДИАЛОГ</Text><Text style={styles.profileValue}>{chatName || "Без названия"}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ОПИСАНИЕ</Text><Text style={styles.profileValue}>{character.description || "Описание ещё не заполнено."}</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>ЛИЧНОСТЬ</Text><Text style={styles.profileValue}>{character.personality || "Черты личности ещё не заполнены."}</Text></View></ScrollView>;
}
