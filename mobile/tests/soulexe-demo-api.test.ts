import { describe, expect, it } from "vitest";

import { createSoulExeDemoApi } from "../lib/soulexe-demo-api";

describe("SoulExe demo API", () => {
  it("открывает автономные образцы персонажей, чатов и сообщений", async () => {
    const api = createSoulExeDemoApi();
    const characters = await api.getCharacters();
    const chats = await api.getChats(characters[0].id);
    const messages = await api.getMessages(characters[0].id, chats[0].id);

    expect(characters).toHaveLength(3);
    expect(chats[0].name).toBe("Вечер у набережной");
    expect(messages.at(-1)).toMatchObject({ author: "Луна", role: "assistant" });
  });

  it("добавляет локальный ответ персонажа без сетевого запроса", async () => {
    const api = createSoulExeDemoApi();
    const [character] = await api.getCharacters();
    const [chat] = await api.getChats(character.id);

    await expect(api.sendMessage(character.id, chat.id, "Как прошёл день?")).resolves.toMatchObject({ reply: expect.stringContaining("Луна") });
    await expect(api.getMessages(character.id, chat.id)).resolves.toHaveLength(5);
  });

  it("создаёт и развивает демонстрационную сцену", async () => {
    const api = createSoulExeDemoApi();
    const characters = await api.getCharacters();
    const scene = await api.createScene({ characterAId: characters[0].id, characterBId: characters[1].id, name: "Тестовая сцена" });
    const updated = await api.sceneAction(scene.id, "next");

    expect(updated.status).toBe("running");
    expect(updated.messages).toHaveLength(2);
  });

  it("сохраняет параметры существующей демонстрационной сцены", async () => {
    const api = createSoulExeDemoApi();
    const [scene] = await api.getScenes();
    const characters = await api.getCharacters();
    const updated = await api.updateScene(scene.id, { name: "Новый план", location: "Маяк", turnMode: "manual", delaySeconds: 0, characterAId: characters[2].id, characterBId: characters[1].id });

    expect(updated).toMatchObject({ name: "Новый план", location: "Маяк", turnMode: "manual", delaySeconds: 0, characterA: { id: characters[2].id }, characterB: { id: characters[1].id } });
  });
});
