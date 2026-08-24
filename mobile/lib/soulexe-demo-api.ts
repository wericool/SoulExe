import type { ChatMessage, CreateSoulSceneRequest, SoulCharacter, SoulCharacterDraft, SoulChat, SoulConversation, SoulConversationAction, SoulConversationPageOptions, SoulExeApi, SoulExeApiClient, SoulPersona, SoulScene, SoulSceneSummary, UpdateSoulSceneRequest } from "@/lib/soulexe-api";

type SoulExeDemoApi = SoulExeApi & Pick<SoulExeApiClient,
  "getChats" | "getMessages" | "createChat" | "sendMessage" | "getScenes" | "getScene" |
  "createScene" | "updateScene" | "sceneAction" | "addDirectorEvent">;

const stamp = (minutesFromNow = 0) => new Date(Date.now() + minutesFromNow * 60_000).toISOString();
const turnStamp = (secondsFromNow: number) => new Date(Date.now() + secondsFromNow * 1_000).toISOString();
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

const initialCharacters: SoulCharacter[] = [
  {
    id: "demo-luna",
    name: "Луна",
    title: "Спокойная ночная собеседница",
    description: "Луна любит тихие разговоры, музыку и прогулки под дождём.",
    personality: "Внимательная, мягкая, наблюдательная. Отвечает тепло и образно.",
    scenario: "Вечернее кафе рядом с набережной.",
    systemPrompt: "Поддерживай живой, бережный разговор на русском языке.",
    soulMemoryEnabled: true,
    autoSummaryEnabled: true,
  },
  {
    id: "demo-mira",
    name: "Мира",
    title: "Искательница приключений",
    description: "Путешественница, которая собирает истории и старые карты.",
    personality: "Смелая, любознательная, с лёгким чувством юмора.",
    scenario: "Экспедиция в забытый приморский город.",
    systemPrompt: "Веди сюжет динамично, но уважай решения автора.",
    soulMemoryEnabled: true,
    autoSummaryEnabled: false,
  },
  {
    id: "demo-kai",
    name: "Кай",
    title: "Аналитик и друг",
    description: "Рациональный собеседник с добрым отношением к людям.",
    personality: "Собранный, прямой, иногда ироничный.",
    scenario: "Рабочая студия поздним вечером.",
    systemPrompt: "Помогай находить ясные решения и поддерживай диалог.",
  },
];

function assistantReply(characterName: string, message: string) {
  return `*${characterName} ненадолго задумывается, затем улыбается.*\n\n«Я тебя слышу. ${message.length > 54 ? "Давай разберём это спокойно и по шагам." : "Расскажи чуть подробнее — мне интересно продолжить."}»`;
}

export function createSoulExeDemoApi(): SoulExeDemoApi {
  let characters = copy(initialCharacters);
  const personas: SoulPersona[] = [
    { id: "demo-persona-anya", name: "Аня", description: "Тёплая и любознательная собеседница." },
    { id: "demo-persona-max", name: "Макс", description: "Спокойный наблюдатель." },
  ];
  const chats = new Map<string, SoulChat[]>([
    ["demo-luna", [{ id: "demo-chat-evening", name: "Вечер у набережной", updatedAt: stamp(-3) }]],
    ["demo-mira", [{ id: "demo-chat-journey", name: "Карта старого города", updatedAt: stamp(-42) }]],
    ["demo-kai", [{ id: "demo-chat-focus", name: "Планы на неделю", updatedAt: stamp(-180) }]],
  ]);
  const messages = new Map<string, ChatMessage[]>([
    ["demo-luna:demo-chat-evening", [
      { id: "1", role: "assistant", author: "Луна", content: "*Луна поправляет рукав и смотрит на отражения огней в воде.*\n\n«Сегодня город звучит совсем иначе, правда?»", createdAt: stamp(-26) },
      { id: "2", role: "user", author: "Вы", content: "Да, после дождя здесь особенно спокойно.", createdAt: stamp(-19) },
      { id: "3", role: "assistant", author: "Луна", content: "<think>Хочется сохранить это тихое настроение.</think>\n\n*Она кивает и делает глоток горячего чая.*\n\n«Тогда давай никуда не спешить.»", createdAt: stamp(-3) },
    ]],
    ["demo-mira:demo-chat-journey", [
      { id: "4", role: "assistant", author: "Мира", content: "*Мира разворачивает выцветшую карту на столе.*\n\n«Смотри: здесь отмечен путь к старому маяку.»", createdAt: stamp(-42) },
    ]],
    ["demo-kai:demo-chat-focus", [
      { id: "5", role: "assistant", author: "Кай", content: "«Начнём с одной небольшой задачи — так будет проще увидеть результат.»", createdAt: stamp(-180) },
    ]],
  ]);
  let scenes: SoulScene[] = [{
    id: "demo-scene-library",
    name: "Тишина старой библиотеки",
    status: "paused",
    updatedAt: stamp(-8),
    characterA: copy(initialCharacters[0]),
    characterB: copy(initialCharacters[1]),
    scenario: "Луна и Мира нашли записку между страницами старого атласа.",
    messages: [
      { kind: "dialogue", speakerId: "demo-luna", author: "Луна", content: "*Луна осторожно снимает печать с записки.*\n\n«Похоже, её оставили именно для нас.»", createdAt: stamp(-21) },
      { kind: "dialogue", speakerId: "demo-mira", author: "Мира", content: "«Тогда прочтём вместе. Такие находки редко бывают случайными.»", createdAt: stamp(-8) },
    ],
  }];

  const findCharacter = (characterId: string) => characters.find((character) => character.id === characterId);
  const messageKey = (characterId: string, chatId: string) => `${characterId}:${chatId}`;
  const findScene = (sceneId: string) => scenes.find((scene) => scene.id === sceneId);
  const getConversations = (): SoulConversation[] => [
    ...characters.flatMap((character) => (chats.get(character.id) || []).map((chat) => {
      const chatMessages = messages.get(messageKey(character.id, chat.id)) || [];
      return {
        id: chat.id, mode: "personal" as const, source: "Chat", name: chat.name, isPinned: false, isArchived: false,
        summaryText: "", lastSummarizedSequence: 0, createdAt: chat.updatedAt || stamp(), updatedAt: chat.updatedAt || stamp(),
        participants: [{ id: "user", kind: "User" as const, displayName: "Вы", canGenerate: false, sortOrder: 0 }, { id: character.id, kind: "Character" as const, displayName: character.name, characterId: character.id, avatarUrl: character.avatarUrl, canGenerate: true, sortOrder: 1 }],
        messages: chatMessages.map((message, index) => ({ id: message.id || `${chat.id}-${index}`, sequenceNumber: index + 1, kind: "message" as const, author: message.author, content: message.content, createdAt: message.createdAt, variants: [], attachments: [] })),
        context: { scenario: character.scenario }, turnState: null,
      };
    })),
    ...scenes.map((scene) => ({
      id: scene.id, mode: "group" as const, source: "Scene", name: scene.name, isPinned: false, isArchived: false,
      summaryText: "", lastSummarizedSequence: 0, createdAt: scene.updatedAt || stamp(), updatedAt: scene.updatedAt || stamp(),
      participants: [scene.characterA, scene.characterB].filter(Boolean).map((character, index) => ({ id: character!.id, kind: "Character" as const, displayName: character!.name, characterId: character!.id, avatarUrl: character!.avatarUrl, canGenerate: true, sortOrder: index })),
      messages: scene.messages.map((message, index) => ({ id: `${scene.id}-${index}`, sequenceNumber: index + 1, kind: message.kind === "director" ? "director" as const : "message" as const, author: message.author || "", content: message.content, createdAt: message.createdAt, variants: [], attachments: [] })),
      context: { scenario: scene.scenario, location: scene.location, timeContext: scene.timeContext, mood: scene.mood, goal: scene.goal, relationshipContext: scene.relationshipContext },
      turnState: { status: scene.status, mode: scene.turnMode === "manual" ? "manual" as const : "alternate" as const, nextTurnAt: scene.nextTurnAt, delaySeconds: scene.delaySeconds || 10, enforceContract: Boolean(scene.enforceSceneContract), advanceAndAvoidRepetition: Boolean(scene.advanceSceneAndAvoidRepetition) },
    })),
  ];

  return {
    async getCharacters() { return copy(characters); },
    async getPersonas() { return copy(personas); },
    async getConversations(take?: number) {
      const normalizedTake = take && take > 0 ? Math.min(Math.max(Math.trunc(take), 1), 100) : undefined;
      return copy(getConversations().map((conversation) => ({ ...conversation, messages: normalizedTake ? conversation.messages.slice(-normalizedTake) : conversation.messages })));
    },
    async getConversation(conversationId: string, take?: number) {
      const conversation = getConversations().find((item) => item.id === conversationId);
      if (!conversation) throw new Error("Разговор не найден.");
      const normalizedTake = take && take > 0 ? Math.min(Math.max(Math.trunc(take), 1), 100) : undefined;
      return copy({ ...conversation, messages: normalizedTake ? conversation.messages.slice(-normalizedTake) : conversation.messages });
    },
    async getConversationPage(options: SoulConversationPageOptions = {}) {
      const limit = options.limit && options.limit > 0 ? Math.min(Math.max(Math.trunc(options.limit), 1), 100) : 50;
      const offset = options.cursor && /^\d+$/.test(options.cursor) ? Number(options.cursor) : 0;
      const all = await this.getConversations(options.take);
      const ordered = [...all].sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt));
      const items = ordered.slice(offset, offset + limit);
      return copy({ items, nextCursor: offset + items.length < ordered.length ? String(offset + items.length) : null });
    },
    async conversationAction(conversationId: string, action: SoulConversationAction) {
      const target = getConversations().find((item) => item.id === conversationId);
      if (!target) throw new Error("Разговор не найден.");
      if (target.mode === "personal" && action.action === "send") {
        const owner = characters.find((character) => (chats.get(character.id) || []).some((chat) => chat.id === conversationId));
        if (!owner) throw new Error("Разговор не найден.");
        await this.sendMessage(owner.id, conversationId, action.text || "");
      } else if (target.mode === "group" && action.action === "director") {
        await this.addDirectorEvent(conversationId, action.text || "");
      } else if (target.mode === "group" && (action.action === "start" || action.action === "pause" || action.action === "next")) {
        await this.sceneAction(conversationId, action.action);
      } else {
        throw new Error("Это действие пока не поддерживается в демо-режиме.");
      }
      const conversation = getConversations().find((item) => item.id === conversationId);
      if (!conversation) throw new Error("Разговор не найден.");
      return copy(conversation);
    },
    async createConversation(request) {
      const ids = [...new Set(request.characterIds)];
      if (ids.length === 1) {
        await this.createChat(ids[0], request.name);
      } else if (ids.length === 2) {
        await this.createScene({
          characterAId: ids[0], characterBId: ids[1], name: request.name,
          scenario: request.scenario, location: request.location, timeContext: request.timeContext,
          mood: request.mood, goal: request.goal, relationshipContext: request.relationshipContext,
          turnMode: request.turnMode, delaySeconds: request.delaySeconds,
          enforceSceneContract: request.enforceContract,
          advanceSceneAndAvoidRepetition: request.advanceAndAvoidRepetition,
        });
      } else {
        throw new Error("Выберите одного или двух разных персонажей.");
      }
      const created = getConversations().find((conversation) =>
        conversation.mode === (ids.length === 1 ? "personal" : "group") && conversation.name === request.name);
      if (!created) throw new Error("Не удалось создать разговор.");
      return copy(created);
    },
    async updateConversation(conversationId, request) {
      const target = getConversations().find((conversation) => conversation.id === conversationId);
      if (!target) throw new Error("Разговор не найден.");
      if (target.mode === "personal") {
        for (const [characterId, values] of chats) {
          if (!values.some((chat) => chat.id === conversationId)) continue;
          chats.set(characterId, values.map((chat) => chat.id === conversationId ? { ...chat, name: request.name || chat.name, updatedAt: stamp() } : chat));
          break;
        }
      } else {
        await this.updateScene(conversationId, {
          characterAId: request.characterIds?.[0], characterBId: request.characterIds?.[1], name: request.name,
          scenario: request.scenario, location: request.location, timeContext: request.timeContext,
          mood: request.mood, goal: request.goal, relationshipContext: request.relationshipContext,
          turnMode: request.turnMode, delaySeconds: request.delaySeconds,
          enforceSceneContract: request.enforceContract,
          advanceSceneAndAvoidRepetition: request.advanceAndAvoidRepetition,
        });
      }
      const updated = getConversations().find((conversation) => conversation.id === conversationId);
      if (!updated) throw new Error("Разговор не найден.");
      return copy(updated);
    },
    async sendConversationMessage(conversationId: string, text: string, author = {}) {
      return this.conversationAction(conversationId, { action: "send", text, ...author });
    },
    async createCharacter(draft: Pick<SoulCharacterDraft, "name"> & Partial<SoulCharacterDraft>) {
      const character: SoulCharacter = { id: `demo-character-${Date.now()}`, name: draft.name, title: draft.title || "Новый персонаж", description: draft.description || "Демонстрационный персонаж", personality: draft.personality || "Характер можно изменить в редакторе.", scenario: draft.scenario || "", systemPrompt: draft.systemPrompt || "", soulMemoryEnabled: draft.soulMemoryEnabled, autoSummaryEnabled: draft.autoSummaryEnabled };
      characters = [character, ...characters]; chats.set(character.id, []); return copy(character);
    },
    async generateCharacter(idea: string) {
      return this.createCharacter({ name: "Астра", title: "Сгенерированный персонаж", description: `Создано по идее: ${idea}`, personality: "Творческая, внимательная, открытая к диалогу.", scenario: "Демо-сценарий для знакомства." });
    },
    async updateCharacter(characterId: string, draft: Partial<SoulCharacterDraft>) {
      const current = findCharacter(characterId); if (!current) throw new Error("Персонаж не найден.");
      const updated = { ...current, ...draft }; characters = characters.map((item) => item.id === characterId ? updated : item);
      scenes = scenes.map((scene) => ({ ...scene, characterA: scene.characterA?.id === characterId ? updated : scene.characterA, characterB: scene.characterB?.id === characterId ? updated : scene.characterB }));
      return copy(updated);
    },
    async uploadCharacterAvatar(characterId: string, asset) {
      const current = findCharacter(characterId); if (!current) throw new Error("Персонаж не найден.");
      const updated = { ...current, avatarUrl: asset.uri };
      characters = characters.map((item) => item.id === characterId ? updated : item);
      scenes = scenes.map((scene) => ({ ...scene, characterA: scene.characterA?.id === characterId ? updated : scene.characterA, characterB: scene.characterB?.id === characterId ? updated : scene.characterB }));
      return copy(updated);
    },
    async getChats(characterId: string) { return copy(chats.get(characterId) || []); },
    async getMessages(characterId: string, chatId: string) { return copy(messages.get(messageKey(characterId, chatId)) || []); },
    async createChat(characterId: string, name: string) {
      if (!findCharacter(characterId)) throw new Error("Персонаж не найден.");
      const chat = { id: `demo-chat-${Date.now()}`, name, updatedAt: stamp() }; chats.set(characterId, [chat, ...(chats.get(characterId) || [])]); messages.set(messageKey(characterId, chat.id), []); return copy(chat);
    },
    async sendMessage(characterId: string, chatId: string, message: string) {
      const character = findCharacter(characterId); if (!character) throw new Error("Персонаж не найден.");
      const key = messageKey(characterId, chatId); const reply = assistantReply(character.name, message); const sentAt = stamp();
      messages.set(key, [...(messages.get(key) || []), { id: `demo-user-${Date.now()}`, role: "user", author: "Вы", content: message, createdAt: sentAt }, { id: `demo-reply-${Date.now()}`, role: "assistant", author: character.name, content: reply, createdAt: stamp(1) }]);
      chats.set(characterId, (chats.get(characterId) || []).map((chat) => chat.id === chatId ? { ...chat, updatedAt: sentAt } : chat)); return { reply };
    },
    async getScenes() {
      return copy(scenes.map((scene) => ({
        id: scene.id,
        name: scene.name,
        status: scene.status,
        updatedAt: scene.updatedAt,
        nextTurnAt: scene.nextTurnAt,
        characterA: scene.characterA ?? null,
        characterB: scene.characterB ?? null,
      })));
    },
    async getScene(sceneId: string) { const scene = findScene(sceneId); if (!scene) throw new Error("Сцена не найдена."); return copy(scene); },
    async createScene(request: CreateSoulSceneRequest) {
      const characterA = findCharacter(request.characterAId); const characterB = findCharacter(request.characterBId); if (!characterA || !characterB) throw new Error("Выберите участников сцены.");
      const scene: SoulScene = { id: `demo-scene-${Date.now()}`, name: request.name, status: "paused", updatedAt: stamp(), characterA: copy(characterA), characterB: copy(characterB), scenario: request.scenario, location: request.location, timeContext: request.timeContext, mood: request.mood, goal: request.goal, relationshipContext: request.relationshipContext, turnMode: request.turnMode === "manual" ? "manual" : "alternate", delaySeconds: request.delaySeconds, enforceSceneContract: request.enforceSceneContract, advanceSceneAndAvoidRepetition: request.advanceSceneAndAvoidRepetition, messages: [{ kind: "director", author: "Режиссёр", content: request.scenario || "Новая демонстрационная сцена готова к запуску.", createdAt: stamp() }] }; scenes = [scene, ...scenes]; return copy(scene);
    },
    async updateScene(sceneId: string, request: UpdateSoulSceneRequest) {
      const current = findScene(sceneId); if (!current) throw new Error("Сцена не найдена.");
      const characterA = request.characterAId ? findCharacter(request.characterAId) : current.characterA;
      const characterB = request.characterBId ? findCharacter(request.characterBId) : current.characterB;
      if (!characterA || !characterB || characterA.id === characterB.id) throw new Error("Выберите двух разных участников сцены.");
      const updated: SoulScene = { ...current, ...request, characterA: copy(characterA), characterB: copy(characterB), turnMode: request.turnMode === "manual" ? "manual" : request.turnMode === "alternate" ? "alternate" : current.turnMode, updatedAt: stamp() };
      scenes = scenes.map((scene) => scene.id === sceneId ? updated : scene); return copy(updated);
    },
    async sceneAction(sceneId: string, action: "start" | "pause" | "next"): Promise<SoulScene> {
      const current = findScene(sceneId); if (!current) throw new Error("Сцена не найдена.");
      const running = action !== "pause";
      const updated: SoulScene = { ...current, status: running ? "running" : "paused", nextTurnAt: running && current.turnMode !== "manual" ? turnStamp(current.delaySeconds || 10) : null, updatedAt: stamp(), messages: [...current.messages] };
      if (action === "next") updated.messages.push({ kind: "dialogue", speakerId: current.characterA?.id, author: current.characterA?.name || "Участник", content: "*Персонаж делает шаг вперёд, развивая разговор.*\n\n«Кажется, теперь у нас появилась новая зацепка.»", createdAt: stamp() });
      scenes = scenes.map((scene) => scene.id === sceneId ? updated : scene); return copy(updated);
    },
    async addDirectorEvent(sceneId: string, text: string): Promise<SoulScene> {
      const current = findScene(sceneId); if (!current) throw new Error("Сцена не найдена.");
      const updated: SoulScene = { ...current, updatedAt: stamp(), messages: [...current.messages, { kind: "director", author: "Режиссёр", content: text, createdAt: stamp() }] }; scenes = scenes.map((scene) => scene.id === sceneId ? updated : scene); return copy(updated);
    },
  };
}
