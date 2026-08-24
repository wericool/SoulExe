export type ServerHealth = {
  service: string;
  mobileDiscovery: boolean;
};

export type SoulExeSession = {
  baseUrl: string;
  session: string;
};

export type SoulCharacter = {
  id: string;
  name: string;
  title?: string;
  description?: string;
  personality?: string;
  scenario?: string;
  systemPrompt?: string;
  soulMemoryEnabled?: boolean;
  autoSummaryEnabled?: boolean;
  avatarUrl?: string | null;
};

export type SoulCharacterDraft = Pick<SoulCharacter, "name" | "title" | "description" | "personality" | "scenario" | "systemPrompt" | "soulMemoryEnabled" | "autoSummaryEnabled">;

export type SoulPersona = {
  id: string;
  name: string;
  description?: string;
  promptText?: string;
  avatarUrl?: string | null;
};

export type SoulChat = {
  id: string;
  name: string;
  updatedAt?: string;
};

export type ChatMessage = {
  id?: string;
  role: "user" | "assistant";
  author: string;
  content: string;
  createdAt: string;
  authorKind?: "user" | "persona" | "director";
  authorPersonaId?: string | null;
};

export type SoulSceneSummary = {
  id: string;
  name: string;
  status: "running" | "paused" | "finished";
  updatedAt?: string;
  nextTurnAt?: string | null;
  characterA?: SoulCharacter | null;
  characterB?: SoulCharacter | null;
};

export type SoulScene = SoulSceneSummary & {
  scenario?: string;
  location?: string;
  timeContext?: string;
  mood?: string;
  goal?: string;
  relationshipContext?: string;
  turnMode?: "alternate" | "manual";
  delaySeconds?: number;
  enforceSceneContract?: boolean;
  advanceSceneAndAvoidRepetition?: boolean;
  messages: Array<{
    kind: string;
    speakerId?: string | null;
    authorKind?: "user" | "persona" | "director";
    authorPersonaId?: string | null;
    author?: string;
    content: string;
    createdAt: string;
  }>;
};

export type SoulConversationParticipant = {
  id: string;
  kind: "User" | "Character" | "Director" | "System";
  displayName: string;
  characterId?: string | null;
  avatarUrl?: string | null;
  canGenerate: boolean;
  sortOrder: number;
};

export type SoulConversationMessage = {
  id: string;
  sequenceNumber: number;
  kind: "message" | "director" | "system";
  authorParticipantId?: string | null;
  authorKind?: "user" | "persona" | "director";
  authorPersonaId?: string | null;
  author: string;
  content: string;
  createdAt: string;
  editedAt?: string | null;
  variants: Array<{ id: string; label: string; content: string; createdAt: string }>;
  attachments: Array<{ id: string; mediaType: string; originalName: string; createdAt: string }>;
};

export type SoulConversation = {
  id: string;
  mode: "personal" | "group";
  source: string;
  name: string;
  isPinned: boolean;
  isArchived: boolean;
  summaryText: string;
  lastSummarizedSequence: number;
  createdAt: string;
  updatedAt: string;
  participants: SoulConversationParticipant[];
  messages: SoulConversationMessage[];
  context: {
    initialUserProfile?: string;
    initialRelationshipContext?: string;
    scenario?: string;
    location?: string;
    timeContext?: string;
    mood?: string;
    goal?: string;
    relationshipContext?: string;
  };
  turnState?: {
    status: "running" | "paused" | "finished";
    mode: "alternate" | "manual";
    nextParticipantId?: string | null;
    nextTurnAt?: string | null;
    delaySeconds: number;
    enforceContract: boolean;
    advanceAndAvoidRepetition: boolean;
  } | null;
};

export type SoulConversationPage = {
  items: SoulConversation[];
  nextCursor?: string | null;
};

export type SoulConversationPageOptions = {
  cursor?: string | null;
  limit?: number;
  take?: number;
};

export type SoulConversationAction = {
  action: "send" | "append" | "director" | "start" | "pause" | "finish" | "next" | "pin" | "unpin" | "rename" | "delete";
  text?: string;
  authorKind?: "user" | "persona" | "director";
  authorPersonaId?: string;
};

export type CreateSoulConversationRequest = {
  characterIds: string[];
  name: string;
  scenario?: string;
  location?: string;
  timeContext?: string;
  mood?: string;
  goal?: string;
  relationshipContext?: string;
  turnMode?: string;
  delaySeconds?: number;
  enforceContract?: boolean;
  advanceAndAvoidRepetition?: boolean;
};
export type UpdateSoulConversationRequest = Partial<CreateSoulConversationRequest>;

export type CreateSoulSceneRequest = {
  characterAId: string;
  characterBId: string;
  name: string;
  scenario?: string;
  location?: string;
  timeContext?: string;
  mood?: string;
  goal?: string;
  relationshipContext?: string;
  turnMode?: string;
  delaySeconds?: number;
  enforceSceneContract?: boolean;
  advanceSceneAndAvoidRepetition?: boolean;
};

export type UpdateSoulSceneRequest = Partial<CreateSoulSceneRequest>;

export type SoulAvatarUpload = {
  uri: string;
  fileName?: string | null;
  mimeType?: string | null;
};

export function normalizeServerUrl(value: string): string {
  const prepared = value.trim().replace(/\/+$/, "");
  if (!prepared) return "";
  return /^https?:\/\//i.test(prepared) ? prepared : `http://${prepared}`;
}

/** Removes prompt-engine and reasoning blocks that must never be rendered as scene dialogue. */
export function cleanSceneContent(value: string): string {
  return (value || "")
    .replace(/<think\b[^>]*>[\s\S]*?<\/think>/gi, "")
    .replace(/^\s*\[(?:SCENE STATE|RELATIONSHIP DYNAMICS|ACTIVE CHARACTER LORE|CURRENT SPEAKER|COUNTERPART|SCENE CONTRACT|SCENE PROGRESSION|MANDATORY PROGRESSION CHECK)[^\]]*\]\s*$/gim, "")
    .replace(/\[DIRECTOR EVENT\]\s*/gi, "")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

async function readJson<T>(response: Response): Promise<T> {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const message = typeof payload?.error === "string" ? payload.error : `Ошибка сервера (${response.status})`;
    throw new Error(message);
  }
  return payload as T;
}

export async function checkSoulExeServer(baseUrl: string, timeoutMs = 1000): Promise<ServerHealth> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const normalized = normalizeServerUrl(baseUrl);
    const response = await fetch(`${normalized}/api/health`, { signal: controller.signal });
    if (response.ok) {
      const health = await readJson<ServerHealth>(response);
      if (health.service !== "SoulExe") throw new Error("По этому адресу не найден SoulExe.");
      return health;
    }

    return readJson<ServerHealth>(response);
  } finally {
    clearTimeout(timeout);
  }
}

export class SoulExeApiClient {
  public constructor(private readonly session: SoulExeSession) {}

  private withAvatarSessionUrl(avatarUrl?: string | null): string | null | undefined {
    if (!avatarUrl) return avatarUrl;
    const absoluteAvatarUrl = avatarUrl.startsWith("http")
      ? avatarUrl
      : `${normalizeServerUrl(this.session.baseUrl)}${avatarUrl.startsWith("/") ? "" : "/"}${avatarUrl}`;
    const separator = absoluteAvatarUrl.includes("?") ? "&" : "?";
    return `${absoluteAvatarUrl}${separator}s=${encodeURIComponent(this.session.session)}`;
  }

  private withAvatarUrl(character?: SoulCharacter | null): SoulCharacter | null | undefined {
    if (!character || !character.avatarUrl) return character;
    return {
      ...character,
      // react-native Image cannot send the custom API header; the local server also accepts
      // the short-lived mobile session in a query string for protected image requests.
      avatarUrl: this.withAvatarSessionUrl(character.avatarUrl),
    };
  }

  private withConversationAvatarUrls(conversation: SoulConversation): SoulConversation {
    return {
      ...conversation,
      participants: conversation.participants.map((participant) => ({ ...participant, avatarUrl: this.withAvatarSessionUrl(participant.avatarUrl) })),
    };
  }

  private async request<T>(path: string, options: RequestInit = {}): Promise<T> {
    const response = await fetch(`${normalizeServerUrl(this.session.baseUrl)}${path}`, {
      ...options,
      cache: "no-store",
      headers: {
        Accept: "application/json",
        "X-SoulExe-Session": this.session.session,
        ...(options.headers ?? {}),
      },
    });
    return readJson<T>(response);
  }

  static async login(baseUrl: string, username: string, password: string): Promise<SoulExeSession> {
    const normalized = normalizeServerUrl(baseUrl);
    const response = await fetch(`${normalized}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ username, password }),
    });
    const data = await readJson<{ session: string }>(response);
    if (!data.session) throw new Error("SoulExe не вернул сессию входа.");
    return { baseUrl: normalized, session: data.session };
  }

  async getCharacters(signal?: AbortSignal) {
    const characters = await this.request<SoulCharacter[]>("/api/characters", { signal });
    return characters.map((character) => this.withAvatarUrl(character) as SoulCharacter);
  }

  getPersonas() {
    return this.request<SoulPersona[]>("/api/personas");
  }

  async getConversations(take?: number) {
    const suffix = take && take > 0 ? `?take=${Math.min(Math.max(Math.trunc(take), 1), 100)}` : "";
    const conversations = await this.request<SoulConversation[]>(`/api/conversations${suffix}`);
    return conversations.map((conversation) => this.withConversationAvatarUrls(conversation));
  }

  async getConversation(conversationId: string, take?: number) {
    const suffix = take && take > 0 ? `?take=${Math.min(Math.max(Math.trunc(take), 1), 100)}` : "";
    return this.withConversationAvatarUrls(await this.request<SoulConversation>(`/api/conversations/${conversationId}${suffix}`));
  }

  async getConversationPage(options: SoulConversationPageOptions = {}) {
    const params = new URLSearchParams();
    if (options.cursor) params.set("cursor", options.cursor);
    if (options.limit && options.limit > 0) params.set("limit", String(Math.min(Math.max(Math.trunc(options.limit), 1), 100)));
    if (options.take && options.take > 0) params.set("take", String(Math.min(Math.max(Math.trunc(options.take), 1), 100)));
    const suffix = params.size ? `?${params.toString()}` : "";
    const page = await this.request<SoulConversationPage>(`/api/conversations/page${suffix}`);
    return { ...page, items: page.items.map((conversation) => this.withConversationAvatarUrls(conversation)) };
  }

  async conversationAction(conversationId: string, action: SoulConversationAction) {
    return this.withConversationAvatarUrls(await this.request<SoulConversation>(`/api/conversations/${conversationId}/actions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(action),
    }));
  }

  async deleteConversation(conversationId: string) {
    await this.request<void>(`/api/conversations/${conversationId}/actions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "delete" }),
    });
  }

  async createConversation(request: CreateSoulConversationRequest) {
    return this.withConversationAvatarUrls(await this.request<SoulConversation>("/api/conversations", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }));
  }

  async updateConversation(conversationId: string, request: UpdateSoulConversationRequest) {
    return this.withConversationAvatarUrls(await this.request<SoulConversation>(`/api/conversations/${conversationId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }));
  }

  sendConversationMessage(conversationId: string, text: string, author: Pick<SoulConversationAction, "authorKind" | "authorPersonaId"> = {}) {
    return this.conversationAction(conversationId, { action: "send", text, ...author });
  }

  async createCharacter(draft: Pick<SoulCharacterDraft, "name"> & Partial<SoulCharacterDraft>) {
    return this.withAvatarUrl(await this.request<SoulCharacter>("/api/characters", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(draft),
    })) as SoulCharacter;
  }

  async generateCharacter(idea: string) {
    return this.withAvatarUrl(await this.request<SoulCharacter>("/api/characters/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ idea }),
    })) as SoulCharacter;
  }

  async updateCharacter(characterId: string, draft: Partial<SoulCharacterDraft>) {
    return this.withAvatarUrl(await this.request<SoulCharacter>(`/api/characters/${characterId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(draft),
    })) as SoulCharacter;
  }

  async uploadCharacterAvatar(characterId: string, asset: SoulAvatarUpload) {
    const extension = asset.fileName?.split(".").pop() || (asset.mimeType === "image/png" ? "png" : asset.mimeType === "image/webp" ? "webp" : "jpg");
    const form = new FormData();
    form.append("avatar", {
      uri: asset.uri,
      name: asset.fileName || `avatar.${extension}`,
      type: asset.mimeType || "image/jpeg",
    } as unknown as Blob);
    return this.withAvatarUrl(await this.request<SoulCharacter>(`/api/characters/${characterId}/avatar`, {
      method: "POST",
      body: form,
    })) as SoulCharacter;
  }

  getChats(characterId: string) {
    return this.request<SoulChat[]>(`/api/characters/${characterId}/chats`);
  }

  async getMessages(characterId: string, chatId: string, signal?: AbortSignal) {
    const messages = await this.request<Array<Omit<ChatMessage, "role"> & { role: "user" | "assistant" | "bot" | "system" }>>(
      `/api/characters/${characterId}/chats/${chatId}/messages?take=30`,
      { signal },
    );
    return messages.map((message): ChatMessage => ({
      ...message,
      // NetworkChatServer returns `bot` for an assistant reply. The mobile UI deliberately
      // uses only two visual sides: the user and the character.
      role: message.role === "user" ? "user" : "assistant",
    }));
  }

  createChat(characterId: string, name: string) {
    return this.request<SoulChat>(`/api/characters/${characterId}/chats`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    });
  }

  sendMessage(characterId: string, chatId: string, message: string) {
    return this.request<{ reply: string }>("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ characterId, chatId, message }),
    });
  }

  async getScenes() {
    const scenes = await this.request<SoulSceneSummary[]>("/api/scenes");
    return scenes.map((scene) => ({
      ...scene,
      characterA: this.withAvatarUrl(scene.characterA),
      characterB: this.withAvatarUrl(scene.characterB),
    }));
  }

  async getScene(sceneId: string, signal?: AbortSignal) {
    const scene = await this.request<SoulScene>(`/api/scenes/${sceneId}?take=30`, { signal });
    return this.withSceneAvatarUrls(scene);
  }

  async createScene(request: CreateSoulSceneRequest) {
    return this.withSceneAvatarUrls(await this.request<SoulScene>("/api/scenes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }));
  }

  async updateScene(sceneId: string, request: UpdateSoulSceneRequest) {
    return this.withSceneAvatarUrls(await this.request<SoulScene>(`/api/scenes/${sceneId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }));
  }

  private withSceneAvatarUrls(scene: SoulScene): SoulScene {
    return {
      ...scene,
      characterA: this.withAvatarUrl(scene.characterA),
      characterB: this.withAvatarUrl(scene.characterB),
      messages: (scene.messages ?? []).map((message) => ({ ...message, content: cleanSceneContent(message.content) })),
    };
  }

  async sceneAction(sceneId: string, action: "start" | "pause" | "next") {
    return this.withSceneAvatarUrls(await this.request<SoulScene>(`/api/scenes/${sceneId}/action`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action }),
    }));
  }

  async addDirectorEvent(sceneId: string, text: string) {
    return this.withSceneAvatarUrls(await this.request<SoulScene>(`/api/scenes/${sceneId}/director`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text }),
    }));
  }
}

/** Data contract shared by the real local-server client and the offline demonstration client. */
export type SoulExeApi = Pick<
  SoulExeApiClient,
  | "getCharacters"
  | "getPersonas"
  | "getConversations"
  | "getConversation"
  | "getConversationPage"
  | "conversationAction"
  | "deleteConversation"
  | "createConversation"
  | "updateConversation"
  | "sendConversationMessage"
  | "createCharacter"
  | "generateCharacter"
  | "updateCharacter"
  | "uploadCharacterAvatar"
>;
