import { MaterialIcons } from "@expo/vector-icons";
import { Pressable, ScrollView, Text, View } from "react-native";

import { Button, Card, PageHeader, StatusPill } from "@/components/soul/ui";
import { defaultChatAppearance, type ChatAppearanceSettings } from "@/lib/soulexe-storage";
import { colors } from "@/lib/theme";
import { Alert } from "react-native";
import { styles } from "./_styles";

const markupPalette = ["#C8A6FF", "#73B7FF", "#FFD18A", "#FF9EBE", "#79D8B1", "#FFB86B", "#C2D2FF", "#F2A6FF"];

function MarkupColorRow({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return <View style={styles.markupSetting}><Text style={styles.markupSettingLabel}>{label}</Text><View style={styles.colorSwatches}>{markupPalette.map((color) => <Pressable key={color} onPress={() => onChange(color)} style={[styles.colorSwatch, { backgroundColor: color }, value === color && styles.colorSwatchSelected]}><MaterialIcons name="check" size={13} color="#0B0D15" /></Pressable>)}</View></View>;
}

function MarkerToggleRow({ label, description, value, onChange }: { label: string; description: string; value: boolean; onChange: (value: boolean) => void }) {
  return <Pressable onPress={() => onChange(!value)} style={({ pressed }) => [styles.markerToggle, pressed && styles.markerTogglePressed]}><View style={{ flex: 1 }}><Text style={styles.markerToggleTitle}>{label}</Text><Text style={styles.markerToggleSubtitle}>{description}</Text></View><View style={[styles.markerToggleTrack, value && styles.markerToggleTrackOn]}><View style={[styles.markerToggleThumb, value && styles.markerToggleThumbOn]} /></View></Pressable>;
}

function MarkupAppearanceSettings({ appearance, onChange }: { appearance: ChatAppearanceSettings; onChange: (changes: Partial<ChatAppearanceSettings>) => void }) {
  return <Card style={{ gap: 12, marginBottom: 12 }}><Text style={styles.settingTitle}>Оформление сообщений</Text><Text style={styles.helper}>Цвета применяются сразу к чатам и сценам и сохраняются на этом устройстве.</Text><MarkupColorRow label="Действия *…*" value={appearance.actionColor} onChange={(actionColor) => onChange({ actionColor })} /><MarkupColorRow label="Мысли &lt;think&gt;…&lt;/think&gt;" value={appearance.thoughtColor} onChange={(thoughtColor) => onChange({ thoughtColor })} /><MarkupColorRow label={'Речь «…» / "…"'} value={appearance.speechColor} onChange={(speechColor) => onChange({ speechColor })} /><View style={styles.markupDivider} /><Text style={styles.markupGroupTitle}>Очистка визуальных маркеров</Text><MarkerToggleRow label="Убирать звёздочки" description="Показывать действие без символов *…*" value={appearance.stripActionMarkers} onChange={(stripActionMarkers) => onChange({ stripActionMarkers })} /><MarkerToggleRow label="Убирать теги мыслей" description="Скрывать &lt;think&gt; и &lt;/think&gt;" value={appearance.stripThoughtMarkers} onChange={(stripThoughtMarkers) => onChange({ stripThoughtMarkers })} /><MarkerToggleRow label="Убирать кавычки речи" description={'Показывать реплики без «…» и "…"'} value={appearance.stripSpeechMarkers} onChange={(stripSpeechMarkers) => onChange({ stripSpeechMarkers })} /><View style={styles.markupPreview}><Text style={styles.markupPreviewLabel}>ПРЕДПРОСМОТР</Text></View></Card>;
}

export function SettingsScreen({ baseUrl, isDemo, appearance, onAppearanceChange, onLogout }: { baseUrl: string; isDemo: boolean; appearance: ChatAppearanceSettings; onAppearanceChange: (changes: Partial<ChatAppearanceSettings>) => void; onLogout: () => Promise<void> }) {
  return <ScrollView contentContainerStyle={{ paddingBottom: 24 }}><PageHeader title="Настройки" subtitle={isDemo ? "Автономный просмотр интерфейса" : "Подключение и сессия"} /><Card style={{ gap: 8, marginBottom: 12 }}><Text style={styles.settingTitle}>{isDemo ? "Режим работы" : "Текущий сервер"}</Text><Text style={styles.helper}>{baseUrl}</Text><StatusPill text={isDemo ? "Демонстрация" : "Локальная сеть"} tone={isDemo ? "accent" : "success"} /></Card><MarkupAppearanceSettings appearance={appearance} onChange={onAppearanceChange} /><Card style={{ gap: 10 }}><Text style={styles.settingTitle}>{isDemo ? "Демо-режим" : "Сессия"}</Text><Text style={styles.helper}>{isDemo ? "Это пример данных: чаты, сцены, создание и редактирование персонажей работают только внутри приложения и не меняют данные на ПК." : "Выход сбросит только вход на телефоне. Чаты, сцены и модели на ПК не удаляются."}</Text><Button title={isDemo ? "Завершить демо" : "Сменить ПК / выйти"} variant="danger" icon="logout" onPress={() => Alert.alert(isDemo ? "Завершить демо?" : "Выйти?", isDemo ? "Вы вернётесь к экрану подключения." : "Потребуется снова ввести адрес и пароль.", [{ text: "Отмена", style: "cancel" }, { text: isDemo ? "Завершить" : "Выйти", style: "destructive", onPress: () => void onLogout() }])} /></Card></ScrollView>;
}
