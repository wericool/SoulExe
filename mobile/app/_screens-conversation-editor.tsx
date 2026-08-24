import { useEffect, useState } from "react";
import { Alert, Pressable, ScrollView, Text, View } from "react-native";

import { Button, Field } from "@/components/soul/ui";
import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import type { SoulCharacter, SoulConversation, SoulExeApi, SoulScene } from "@/lib/soulexe-api";

import { SceneCharacterPicker } from "./_components-chat";
import { styles } from "./_styles";

function MarkerToggleRow({ label, description, value, onChange }: { label: string; description: string; value: boolean; onChange: (value: boolean) => void }) {
  return <Pressable onPress={() => onChange(!value)} style={({ pressed }) => [styles.markerToggle, pressed && styles.markerTogglePressed]}><View style={{ flex: 1 }}><Text style={styles.markerToggleTitle}>{label}</Text><Text style={styles.markerToggleSubtitle}>{description}</Text></View><View style={[styles.markerToggleTrack, value && styles.markerToggleTrackOn]}><View style={[styles.markerToggleThumb, value && styles.markerToggleThumbOn]} /></View></Pressable>;
}

export function GroupConversationEditorScreen({ api, scene, onBack, onSaved }: { api: SoulExeApi; scene: SoulScene; onBack: () => void; onSaved: (conversation: SoulConversation) => void }) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [characterAId, setCharacterAId] = useState(scene.characterA?.id || "");
  const [characterBId, setCharacterBId] = useState(scene.characterB?.id || "");
  const [name, setName] = useState(scene.name);
  const [scenario, setScenario] = useState(scene.scenario || "");
  const [location, setLocation] = useState(scene.location || "");
  const [timeContext, setTimeContext] = useState(scene.timeContext || "");
  const [mood, setMood] = useState(scene.mood || "");
  const [goal, setGoal] = useState(scene.goal || "");
  const [relationshipContext, setRelationshipContext] = useState(scene.relationshipContext || "");
  const [turnMode, setTurnMode] = useState<"alternate" | "manual">(scene.turnMode === "manual" ? "manual" : "alternate");
  const [delaySeconds, setDelaySeconds] = useState(String(scene.delaySeconds ?? 10));
  const [enforceSceneContract, setEnforceSceneContract] = useState(scene.enforceSceneContract ?? true);
  const [advanceSceneAndAvoidRepetition, setAdvanceSceneAndAvoidRepetition] = useState(scene.advanceSceneAndAvoidRepetition ?? true);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.getCharacters().then(setCharacters).catch((error) => Alert.alert("Групповой разговор", error instanceof Error ? error.message : "Не удалось загрузить персонажей.")); }, [api]);

  const save = async () => {
    if (!characterAId || !characterBId || characterAId === characterBId) { Alert.alert("Групповой разговор", "Выберите двух разных участников."); return; }
    setBusy(true);
    try { onSaved(await api.updateConversation(scene.id, { characterIds: [characterAId, characterBId], name: name.trim() || "Групповой разговор", scenario: scenario.trim(), location: location.trim(), timeContext: timeContext.trim(), mood: mood.trim(), goal: goal.trim(), relationshipContext: relationshipContext.trim(), turnMode, delaySeconds: Math.max(0, Number(delaySeconds) || 0), enforceContract: enforceSceneContract, advanceAndAvoidRepetition: advanceSceneAndAvoidRepetition })); }
    catch (error) { Alert.alert("Групповой разговор", error instanceof Error ? error.message : "Ошибка сети"); }
    finally { setBusy(false); }
  };

  return <ScrollView contentContainerStyle={styles.newSceneScroll} keyboardShouldPersistTaps="handled"><MessengerThreadHeader title="Параметры группового разговора" subtitle="Изменения сохраняются на ПК" onBack={onBack} /><View style={styles.newSceneContent}><Field label="Название" value={name} onChangeText={setName} placeholder="Название разговора" /><SceneCharacterPicker label="Первый участник" characters={characters} selectedId={characterAId} excludeId={characterBId} onSelect={setCharacterAId} /><SceneCharacterPicker label="Второй участник" characters={characters} selectedId={characterBId} excludeId={characterAId} onSelect={setCharacterBId} /><Field label="Сценарий" value={scenario} onChangeText={setScenario} placeholder="Что происходит и с чего начинается сцена" multiline style={styles.largeField} /><Field label="Место" value={location} onChangeText={setLocation} placeholder="Место сцены" /><Field label="Время и контекст" value={timeContext} onChangeText={setTimeContext} placeholder="Время и текущая ситуация" /><Field label="Настроение" value={mood} onChangeText={setMood} placeholder="Настроение сцены" /><Field label="Цель сцены" value={goal} onChangeText={setGoal} placeholder="Что должно измениться или открыться" /><Field label="Отношения участников" value={relationshipContext} onChangeText={setRelationshipContext} placeholder="Общие отношения и контекст" multiline style={styles.largeField} /><Text style={styles.sceneOptionLabel}>РЕЖИМ ХОДОВ</Text><View style={styles.sceneTurnModeRow}><Button title="По очереди" variant={turnMode === "alternate" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("alternate")} /><Button title="Вручную" variant={turnMode === "manual" ? "primary" : "secondary"} style={{ flex: 1 }} onPress={() => setTurnMode("manual")} /></View><Field label="Пауза между репликами (сек.)" value={delaySeconds} onChangeText={setDelaySeconds} keyboardType="numeric" placeholder="10; 0 — вручную" /><MarkerToggleRow label="Соблюдать рамки разговора" description="Модель будет следовать заданному сценарию" value={enforceSceneContract} onChange={setEnforceSceneContract} /><MarkerToggleRow label="Развивать сюжет" description="Избегать повторений и двигать историю вперёд" value={advanceSceneAndAvoidRepetition} onChange={setAdvanceSceneAndAvoidRepetition} /><Button title={busy ? "Сохраняю…" : "Сохранить"} loading={busy} disabled={busy} onPress={() => void save()} style={{ marginTop: 16 }} /></View></ScrollView>;
}
