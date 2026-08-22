export type ServerHealth = {
  service: string;
  mobileDiscovery: boolean;
};

export type SoulTextSession = {
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
    author?: string;
    content: string;
    createdAt: string;
  }>;
};

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

export async function checkSoulTextServer(baseUrl: string, timeoutMs = 1000): Promise<ServerHealth> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const normalized = normalizeServerUrl(baseUrl);
    const response = await fetch(`${normalized}/api/health`, { signal: controller.signal });
    if (response.ok) {
      const health = await readJson<ServerHealth>(response);
      if (health.service !== "SoulExe" && health.service !== "SoulText") throw new Error("По этому адресу не найден SoulExe.");
      return health;
    }

    // Совместимость с Windows-версиями, выпущенными до /api/health: защищённый
    // endpoint сцен отвечает 401 именно у мобильного локального сервера SoulText.
    if (response.status === 404) {
      const legacyResponse = await fetch(`${normalized}/api/scenes`, { signal: controller.signal });
      const legacyPayload = await legacyResponse.json().catch(() => ({}));
      if (legacyResponse.status === 401 && typeof legacyPayload?.error === "string" && legacyPayload.error.includes("мобильн")) {
        return { service: "SoulExe", mobileDiscovery: false };
      }
    }

    return readJson<ServerHealth>(response);
  } finally {
    clearTimeout(timeout);
  }
}

export class SoulTextApi {
  public constructor(private readonly session: SoulTextSession) {}

  private withAvatarUrl(character?: SoulCharacter | null): SoulCharacter | null | undefined {
    if (!character || !character.avatarUrl) return character;
    const absoluteAvatarUrl = character.avatarUrl.startsWith("http")
      ? character.avatarUrl
      : `${normalizeServerUrl(this.session.baseUrl)}${character.avatarUrl.startsWith("/") ? "" : "/"}${character.avatarUrl}`;
    const separator = absoluteAvatarUrl.includes("?") ? "&" : "?";
    return {
      ...character,
      // react-native Image cannot send the custom API header; the local server also accepts
      // the short-lived mobile session in a query string for protected image requests.
      avatarUrl: `${absoluteAvatarUrl}${separator}s=${encodeURIComponent(this.session.session)}`,
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

  static async login(baseUrl: string, username: string, password: string): Promise<SoulTextSession> {
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

  async getCharacters() {
    const characters = await this.request<SoulCharacter[]>("/api/characters");
    return characters.map((character) => this.withAvatarUrl(character) as SoulCharacter);
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

  async getMessages(characterId: string, chatId: string) {
    const messages = await this.request<Array<Omit<ChatMessage, "role"> & { role: "user" | "assistant" | "bot" | "system" }>>(
      `/api/characters/${characterId}/chats/${chatId}/messages?take=30`,
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

  async getScene(sceneId: string) {
    const scene = await this.request<SoulScene>(`/api/scenes/${sceneId}?take=30`);
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
  SoulTextApi,
  | "getCharacters"
  | "createCharacter"
  | "generateCharacter"
  | "updateCharacter"
  | "uploadCharacterAvatar"
  | "getChats"
  | "getMessages"
  | "createChat"
  | "sendMessage"
  | "getScenes"
  | "getScene"
  | "createScene"
  | "updateScene"
  | "sceneAction"
  | "addDirectorEvent"
>;
