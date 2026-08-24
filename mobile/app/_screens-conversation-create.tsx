import { MaterialIcons } from "@expo/vector-icons";
import { useEffect, useState } from "react";
import { Alert, FlatList, Pressable, ScrollView, Text, View } from "react-native";

import { Avatar, Button, Field } from "@/components/soul/ui";
import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import type { SoulCharacter, SoulExeApi } from "@/lib/soulexe-api";
import { colors } from "@/lib/theme";

import { SceneCharacterPicker } from "./_components-chat";
import { styles } from "./_styles";

export function NewConversationChoiceScreen({
  onBack,
  onChat,
  onScene,
}: {
  onBack: () => void;
  onChat: () => void;
  onScene: () => void;
}) {
  return (
    <View style={styles.grow}>
      <MessengerThreadHeader title="Создать" subtitle="Выберите тип переписки" onBack={onBack} />
      <View style={styles.creationChoiceList}>
        <Pressable onPress={onChat} style={({ pressed }) => [styles.creationChoice, pressed && styles.creationChoicePressed]}>
          <View style={styles.creationChoiceIcon}><MaterialIcons name="chat-bubble-outline" size={23} color={colors.accentHover} /></View>
          <View style={{ flex: 1 }}><Text style={styles.creationChoiceTitle}>Личный разговор</Text><Text style={styles.creationChoiceSubtitle}>Один персонаж и выбранный вами автор сообщений</Text></View>
          <MaterialIcons name="chevron-right" size={22} color={colors.dim} />
        </Pressable>
        <Pressable onPress={onScene} style={({ pressed }) => [styles.creationChoice, pressed && styles.creationChoicePressed]}>
          <View style={styles.creationChoiceIcon}><MaterialIcons name="auto-awesome" size={23} color={colors.accentHover} /></View>
          <View style={{ flex: 1 }}><Text style={styles.creationChoiceTitle}>Групповой разговор</Text><Text style={styles.creationChoiceSubtitle}>Два персонажа, режиссёр и правила развития истории</Text></View>
          <MaterialIcons name="chevron-right" size={22} color={colors.dim} />
        </Pressable>
      </View>
    </View>
  );
}

export function NewSceneScreen({
  api,
  onBack,
  onCreated,
}: {
  api: SoulExeApi;
  onBack: () => void;
  onCreated: () => void;
}) {
  const [characters, setCharacters] = useState<SoulCharacter[]>([]);
  const [characterAId, setCharacterAId] = useState("");
  const [characterBId, setCharacterBId] = useState("");
  const [name, setName] = useState("Новый групповой разговор");
  const [scenario, setScenario] = useState("");
  const [location, setLocation] = useState("");
  const [timeContext, setTimeContext] = useState("");
  const [mood, setMood] = useState("");
  const [goal, setGoal] = useState("");
  const [relationshipContext, setRelationshipContext] = useState("");
  const [turnMode, setTurnMode] = useState<"alternate" | "manual">("alternate");
  const [delaySeconds, setDelaySeconds] = useState("10");
  const [enforceSceneContract, setEnforceSceneContract] = useState(true);
  const [advanceSceneAndAvoidRepetition, setAdvanceSceneAndAvoidRepetition] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api
      .getCharacters()
      .then((items) => {
        setCharacters(items);
        setCharacterAId(items[0]?.id || "");
        setCharacterBId(items[1]?.id || items[0]?.id || "");
      })
      .catch((error) =>
        Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети"),
      );
  }, [api]);

  const create = async () => {
    if (!characterAId || !characterBId || characterAId === characterBId) {
      Alert.alert("Сцена", "Выберите двух разных участников.");
      return;
    }
    setBusy(true);
    try {
      await api.createConversation({
        characterIds: [characterAId, characterBId],
        name: name.trim() || "Новый групповой разговор",
        scenario: scenario.trim(),
        location: location.trim(),
        timeContext: timeContext.trim(),
        mood: mood.trim(),
        goal: goal.trim(),
        relationshipContext: relationshipContext.trim(),
        turnMode,
        delaySeconds: Math.max(0, Number(delaySeconds) || 0),
        enforceContract: enforceSceneContract,
        advanceAndAvoidRepetition: advanceSceneAndAvoidRepetition,
      });
      onCreated();
    } catch (error) {
      Alert.alert("Сцена", error instanceof Error ? error.message : "Ошибка сети");
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView contentContainerStyle={styles.newSceneScroll} keyboardShouldPersistTaps="handled">
      <MessengerThreadHeader title="Новый групповой разговор" subtitle="Настройте участников и ход истории" onBack={onBack} />
      <View style={styles.newSceneContent}>
        <Field label="Название" value={name} onChangeText={setName} placeholder="Например, Тайна старого маяка" />
        <SceneCharacterPicker label="Первый участник" characters={characters} selectedId={characterAId} excludeId={characterBId} onSelect={setCharacterAId} />
        <SceneCharacterPicker label="Второй участник" characters={characters} selectedId={characterBId} excludeId={characterAId} onSelect={setCharacterBId} />
        <Field label="Контекст" value={scenario} onChangeText={setScenario} placeholder="Что происходит и с чего начинается разговор" multiline style={styles.largeField} />
        <Field label="Место" value={location} onChangeText={setLocation} placeholder="Например, заброшенный маяк у моря" />
        <Field label="Время и контекст" value={timeContext} onChangeText={setTimeContext} placeholder="Например, поздний вечер после шторма" />
        <Field label="Настроение" value={mood} onChangeText={setMood} placeholder="Например, тревожное, но тёплое" />
        <Field label="Цель разговора" value={goal} onChangeText={setGoal} placeholder="Что должно измениться или открыться" />
        <Field label="Отношения участников" value={relationshipContext} onChangeText={setRelationshipContext} placeholder="Например, союзники, но ещё не доверяют друг другу" multiline style={styles.largeField} />
        <Text style={styles.sceneOptionLabel}>РЕЖИМ ХОДОВ</Text>
        <View style={styles.sceneTurnModeRow}>
          <Button
            title="По очереди"
            variant={turnMode === "alternate" ? undefined : "secondary"}
            onPress={() => setTurnMode("alternate")}
            style={{ flex: 1 }}
          />
          <Button
            title="Вручную"
            variant={turnMode === "manual" ? undefined : "secondary"}
            onPress={() => setTurnMode("manual")}
            style={{ flex: 1 }}
          />
        </View>
        <Field label="Задержка (сек)" value={delaySeconds} onChangeText={setDelaySeconds} placeholder="10" keyboardType="numeric" />
        <View style={styles.markerToggle}>
          <Pressable onPress={() => setEnforceSceneContract(!enforceSceneContract)} style={styles.markerTogglePressed}>
            <View>
              <Text style={styles.markerToggleTitle}>Соблюдать сценарий</Text>
              <Text style={styles.markerToggleSubtitle}>Модель будет следовать заданному контексту</Text>
            </View>
          </Pressable>
          <Pressable
            onPress={() => setEnforceSceneContract(!enforceSceneContract)}
            style={[styles.markerToggleTrack, enforceSceneContract && styles.markerToggleTrackOn]}
          >
            <View style={[styles.markerToggleThumb, enforceSceneContract && styles.markerToggleThumbOn]} />
          </Pressable>
        </View>
        <View style={styles.markerToggle}>
          <Pressable onPress={() => setAdvanceSceneAndAvoidRepetition(!advanceSceneAndAvoidRepetition)} style={styles.markerTogglePressed}>
            <View>
              <Text style={styles.markerToggleTitle}>Развивать сюжет</Text>
              <Text style={styles.markerToggleSubtitle}>Избегать повторений и двигать историю вперёд</Text>
            </View>
          </Pressable>
          <Pressable
            onPress={() => setAdvanceSceneAndAvoidRepetition(!advanceSceneAndAvoidRepetition)}
            style={[styles.markerToggleTrack, advanceSceneAndAvoidRepetition && styles.markerToggleTrackOn]}
          >
            <View style={[styles.markerToggleThumb, advanceSceneAndAvoidRepetition && styles.markerToggleThumbOn]} />
          </Pressable>
        </View>
        <Button title={busy ? "Создаю…" : "Создать групповой разговор"} icon="auto-awesome" disabled={busy || !characterAId || !characterBId || characterAId === characterBId} loading={busy} onPress={create} style={{ marginTop: 8 }} />
      </View>
    </ScrollView>
  );
}

export function NewChatScreen({
  characters,
  characterId,
  name,
  busy,
  onCharacterChange,
  onNameChange,
  onBack,
  onCreate,
}: {
  characters: SoulCharacter[];
  characterId: string;
  name: string;
  busy: boolean;
  onCharacterChange: (id: string) => void;
  onNameChange: (value: string) => void;
  onBack: () => void;
  onCreate: () => void;
}) {
  return (
    <View style={styles.grow}>
      <MessengerThreadHeader title="Новый личный разговор" subtitle="Выберите персонажа и название" onBack={onBack} />
      <FlatList
        data={characters}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.newChatList}
        ListHeaderComponent={
          <View>
            <Field label="Название разговора" value={name} onChangeText={onNameChange} placeholder="Например, Вечер в кафе" />
            <Text style={styles.selectorLabel}>ПЕРСОНАЖ</Text>
          </View>
        }
        renderItem={({ item }) => (
          <Pressable
            onPress={() => onCharacterChange(item.id)}
            style={[styles.choiceRow, item.id === characterId && styles.choiceRowActive]}
          >
            <Avatar character={item} size={42} />
            <View style={{ flex: 1 }}>
              <Text style={styles.characterName}>{item.name}</Text>
              <Text numberOfLines={1} style={styles.chatMeta}>
                {item.title || item.description || "Персонаж"}
              </Text>
            </View>
            {item.id === characterId ? (
              <MaterialIcons name="check-circle" size={22} color={colors.accentHover} />
            ) : null}
          </Pressable>
        )}
        ListFooterComponent={
          <Button
            title={busy ? "Создаю…" : "Создать разговор"}
            icon="chat"
            disabled={busy || !characterId}
            loading={busy}
            onPress={onCreate}
            style={{ marginTop: 16 }}
          />
        }
      />
    </View>
  );
}
