import { MaterialIcons } from "@expo/vector-icons";
import { ScrollView, Text, View } from "react-native";

import { Avatar } from "@/components/soul/ui";
import { MessengerThreadHeader } from "@/components/soul/messenger-elements";
import type { SoulScene, SoulSceneSummary } from "@/lib/soulexe-api";
import { colors } from "@/lib/theme";

import { styles } from "./_styles";
import { statusLabel } from "./_utils";

export function GroupConversationProfile({ scene, onBack, onEdit }: { scene: SoulSceneSummary | SoulScene; onBack: () => void; onEdit: () => void }) {
  const messageCount = "messages" in scene ? scene.messages.length : 0;
  const full = "messages" in scene ? scene : undefined;
  const details = [["Место", full?.location], ["Время и контекст", full?.timeContext], ["Настроение", full?.mood], ["Цель сцены", full?.goal], ["Отношения участников", full?.relationshipContext]].filter(([, value]) => Boolean(value));
  return <ScrollView contentContainerStyle={styles.profileScroll}><MessengerThreadHeader title="О разговоре" onBack={onBack} onEdit={onEdit} /><View style={styles.profileHero}><View style={styles.sceneProfileEmblem}><MaterialIcons name="auto-awesome" size={38} color={colors.accentHover} /></View><Text style={styles.profileName}>{scene.name}</Text><Text style={styles.profileTitle}>{statusLabel(scene.status)} · {messageCount} реплик</Text></View><View style={styles.profileCard}><Text style={styles.profileLabel}>УЧАСТНИКИ</Text>{scene.characterA ? <View style={styles.sceneParticipantRow}><Avatar character={scene.characterA} size={38} /><View><Text style={styles.sceneParticipantName}>{scene.characterA.name}</Text><Text style={styles.chatMeta}>{scene.characterA.title || "Персонаж"}</Text></View></View> : null}{scene.characterB ? <View style={styles.sceneParticipantRow}><Avatar character={scene.characterB} size={38} /><View><Text style={styles.sceneParticipantName}>{scene.characterB.name}</Text><Text style={styles.chatMeta}>{scene.characterB.title || "Персонаж"}</Text></View></View> : null}</View><View style={styles.profileCard}><Text style={styles.profileLabel}>КОНТЕКСТ</Text><Text style={styles.profileValue}>{full?.scenario || "Контекст пока не задан."}</Text></View>{details.length ? <View style={styles.profileCard}><Text style={styles.profileLabel}>ПАРАМЕТРЫ РАЗГОВОРА</Text>{details.map(([label, value]) => <View key={label} style={styles.sceneDetailRow}><Text style={styles.sceneDetailLabel}>{label}</Text><Text style={styles.profileValue}>{value}</Text></View>)}</View> : null}<View style={styles.profileCard}><Text style={styles.profileLabel}>РЕЖИМ И СОСТОЯНИЕ</Text><Text style={styles.profileValue}>{full?.turnMode === "manual" ? "Ручная очередность ходов" : "Автоматическая очередность ходов"}{full?.delaySeconds ? ` · пауза ${full.delaySeconds} сек.` : ""}</Text></View></ScrollView>;
}
