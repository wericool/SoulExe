import { afterEach, describe, expect, it, vi } from "vitest";

import { checkSoulTextServer, cleanSceneContent, normalizeServerUrl, SoulTextApi } from "../lib/soultext-api";
import { sortConversationRows, toConversationListRow } from "../lib/conversation-adapter";

describe("normalizeServerUrl", () => {
  it("добавляет HTTP и удаляет лишний завершающий слеш", () => {
    expect(normalizeServerUrl("192.168.1.34:8000/")).toBe("http://192.168.1.34:8000");
  });

  it("сохраняет HTTPS и не меняет корректный URL", () => {
    expect(normalizeServerUrl("https://soultext.local:8000")).toBe("https://soultext.local:8000");
  });

  it("возвращает пустую строку для пустого значения", () => {
    expect(normalizeServerUrl("   ")).toBe("");
  });
});

describe("checkSoulTextServer", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("распознаёт старый мобильный сервер по защищённому endpoint сцен", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("{}", { status: 404 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ error: "Выполните вход в мобильный SoulExe." }), { status: 401 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(checkSoulTextServer("192.168.1.34:8000", 100)).resolves.toEqual({ service: "SoulExe", mobileDiscovery: false });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});

describe("cleanSceneContent", () => {
  it("скрывает служебные теги и блоки рассуждений, оставляя ролевой текст", () => {
    expect(cleanSceneContent("<think>служебное рассуждение</think>\n[DIRECTOR EVENT] *Аня смотрит в окно.*\nПривет.")).toBe("*Аня смотрит в окно.*\nПривет.");
  });
});

describe("общий адаптер разговоров", () => {
  it("преобразует сцену в единую строку с участниками и последней репликой", () => {
    const row = toConversationListRow({
      id: "scene-1", kind: "scene", source: "Scene", name: "Ночная прогулка", isPinned: false, isArchived: false,
      summaryText: "", lastSummarizedSequence: 0, createdAt: "2026-08-20T00:00:00Z", updatedAt: "2026-08-21T00:00:00Z",
      participants: [
        { id: "a", kind: "Character", displayName: "Аня", canGenerate: true, sortOrder: 0 },
        { id: "b", kind: "Character", displayName: "Мира", canGenerate: true, sortOrder: 1 },
      ],
      messages: [{ id: "m", sequenceNumber: 1, kind: "message", author: "Аня", content: "*Смотрит на город.*", createdAt: "2026-08-21T00:00:00Z", variants: [], attachments: [] }],
      context: {}, turnState: { status: "running", mode: "alternate", delaySeconds: 10, enforceContract: true, advanceAndAvoidRepetition: true },
    });
    expect(row).toMatchObject({ kind: "scene", title: "Ночная прогулка", subtitle: "Аня · Мира", preview: "*Смотрит на город.*", isRunning: true });
    expect(sortConversationRows([row])[0].id).toBe("scene-1");
  });
});

describe("SoulTextApi: чаты и сцены", () => {
  const api = new SoulTextApi({ baseUrl: "http://192.168.1.34:8000", session: "test-session" });

  afterEach(() => vi.unstubAllGlobals());

  it("загружает последние 30 сообщений чата и отображает серверную роль bot как ответ персонажа", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([
      { role: "user", author: "Вы", content: "Привет", createdAt: "2026-08-21T00:00:00Z" },
      { role: "bot", author: "Полина", content: "Привет!", createdAt: "2026-08-21T00:00:01Z" },
    ]), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(api.getMessages("char-1", "chat-1")).resolves.toMatchObject([
      { role: "user", content: "Привет" },
      { role: "assistant", author: "Полина", content: "Привет!" },
    ]);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://192.168.1.34:8000/api/characters/char-1/chats/chat-1/messages?take=30",
      expect.objectContaining({ headers: expect.objectContaining({ "X-SoulExe-Session": "test-session" }) }),
    );
  });

  it("отправляет сообщение обычного чата в маршрут /api/chat и передаёт идентификаторы", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ reply: "Ответ" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await api.sendMessage("char-1", "chat-1", "Привет");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://192.168.1.34:8000/api/chat",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ characterId: "char-1", chatId: "chat-1", message: "Привет" }) }),
    );
  });

  it("загружает единый список разговоров через совместимый endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([
      { id: "chat-1", kind: "direct", name: "Диалог", participants: [{ id: "char-1", kind: "Character", displayName: "Полина", avatarUrl: "/api/characters/char-1/avatar?v=2" }], messages: [] },
      { id: "scene-1", kind: "scene", name: "Сцена", participants: [], messages: [{ id: "event-1", kind: "director", content: "Событие" }] },
    ]), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const conversations = await api.getConversations(1);

    expect(conversations.map((conversation) => conversation.kind)).toEqual(["direct", "scene"]);
    expect(conversations[1].messages[0].kind).toBe("director");
    expect(conversations[0].participants[0].avatarUrl).toBe("http://192.168.1.34:8000/api/characters/char-1/avatar?v=2&s=test-session");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://192.168.1.34:8000/api/conversations?take=1",
      expect.objectContaining({ headers: expect.objectContaining({ "X-SoulExe-Session": "test-session" }) }),
    );
  });

  it("загружает страницу разговоров с limit, take и непрозрачным курсором", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], nextCursor: "opaque-cursor" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(api.getConversationPage({ cursor: "before", limit: 2, take: 1 })).resolves.toEqual({ items: [], nextCursor: "opaque-cursor" });
    expect(fetchMock).toHaveBeenCalledWith(
      "http://192.168.1.34:8000/api/conversations/page?cursor=before&limit=2&take=1",
      expect.objectContaining({ headers: expect.objectContaining({ "X-SoulExe-Session": "test-session" }) }),
    );
  });

  it("отправляет выбранный аватар как multipart-данные в локальный SoulExe", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "char-1", name: "Полина", avatarUrl: "/api/characters/char-1/avatar?v=2" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await api.uploadCharacterAvatar("char-1", { uri: "file:///avatar.jpg", fileName: "avatar.jpg", mimeType: "image/jpeg" });

    expect(result.avatarUrl).toContain("/api/characters/char-1/avatar?v=2");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://192.168.1.34:8000/api/characters/char-1/avatar",
      expect.objectContaining({ method: "POST", body: expect.any(FormData), headers: expect.objectContaining({ "X-SoulExe-Session": "test-session" }) }),
    );
  });

  it("загружает сцену и отправляет действия и режиссёрские события в правильные маршруты", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: "scene-1", name: "Сцена", status: "running", nextTurnAt: "2026-08-22T16:00:00Z", messages: [] }), { status: 200 }))
      .mockResolvedValueOnce(new Response("{}", { status: 200 }))
      .mockResolvedValueOnce(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const scene = await api.getScene("scene-1");
    await api.sceneAction("scene-1", "next");
    await api.addDirectorEvent("scene-1", "В комнате погас свет.");

    expect(scene).toMatchObject({ nextTurnAt: "2026-08-22T16:00:00Z" });

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://192.168.1.34:8000/api/scenes/scene-1?take=30",
      "http://192.168.1.34:8000/api/scenes/scene-1/action",
      "http://192.168.1.34:8000/api/scenes/scene-1/director",
    ]);
    expect(fetchMock.mock.calls[1][1]).toMatchObject({ method: "POST", body: JSON.stringify({ action: "next" }) });
    expect(fetchMock.mock.calls[2][1]).toMatchObject({ method: "POST", body: JSON.stringify({ text: "В комнате погас свет." }) });
  });
});
