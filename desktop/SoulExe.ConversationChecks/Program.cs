using SoulTextWpf.ConversationChecks;
using SoulTextWpf.Models;
using SoulTextWpf.Services;
using SoulTextWpf.ViewModels;

var failures = new List<string>();
void Expect(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

var (character, chat) = ConversationFixtures.CreateDirectChat();
Expect(character.Chats.Single().Id == chat.Id, "Обычный чат должен оставаться связан с персонажем.");
Expect(chat.Messages.Count == 2, "Фикстура обычного чата должна содержать две реплики.");
Expect(chat.Messages.Select(message => message.SequenceNumber).SequenceEqual([1, 2]), "Номера сообщений обычного чата должны быть последовательными.");
Expect(chat.Messages.All(message => message.Variants.Any(variant => variant.Id == message.CurrentVariantId)), "Каждое сообщение обычного чата должно иметь текущий вариант.");

var (first, second, scene) = ConversationFixtures.CreateScene();
Expect(scene.CharacterAId == first.Id && scene.CharacterBId == second.Id, "Сцена должна хранить обоих участников.");
Expect(scene.Messages.Count == 3, "Фикстура сцены должна содержать режиссёрское событие и две реплики.");
Expect(scene.Messages.Count(message => message.Kind == SoulSceneMessageKind.Director) == 1, "Режиссёрское событие не должно потеряться.");
Expect(scene.Messages.Select(message => message.SequenceNumber).SequenceEqual([1, 2, 3]), "Номера сценических сообщений должны быть последовательными.");

var root = new SoulDataRoot { Characters = [character, first, second], Scenes = [scene] };
var reader = new ConversationReadService();
var conversations = reader.ReadAll(root);
var direct = conversations.Single(conversation => conversation.Id == chat.Id);
var sceneConversation = conversations.Single(conversation => conversation.Id == scene.Id);
Expect(conversations.Count == 2, "Единый read model должен вернуть один чат и одну сцену.");
Expect(direct.Kind == ConversationKind.Direct && direct.Participants.Count == 2, "Обычный чат должен стать разговором с пользователем и персонажем.");
Expect(direct.Messages.Count == chat.Messages.Count, "Адаптер не должен терять сообщения обычного чата.");
Expect(direct.Messages.All(message => message.Variants.Count > 0), "Адаптер должен сохранить варианты ответа обычного чата.");
Expect(sceneConversation.Kind == ConversationKind.Scene && sceneConversation.Participants.Count == 3, "Сцена должна стать разговором с двумя персонажами и режиссёром.");
Expect(sceneConversation.Messages.Count == scene.Messages.Count, "Адаптер не должен терять сообщения сцены.");
Expect(sceneConversation.Messages.Count(message => message.Kind == ConversationMessageKind.DirectorEvent) == 1, "Адаптер должен сохранить режиссёрское событие.");
Expect(sceneConversation.TurnState?.NextParticipantId == first.Id, "Следующий участник сцены должен отображаться в общей модели.");

var directThread = new ConversationThreadPresentationViewModel(direct, root.Characters);
var sceneThread = new ConversationThreadPresentationViewModel(sceneConversation, root.Characters);
Expect(directThread.Messages.Count == direct.Messages.Count, "Общее представление не должно терять сообщения обычного чата.");
Expect(directThread.Messages[0].ShowDateSeparator, "Первая реплика общего представления должна открывать дату-разделитель.");
Expect(directThread.Messages.Any(message => message.IsOutgoing), "В обычном чате пользовательская реплика должна быть выровнена как исходящая.");
Expect(sceneThread.Messages.Count == sceneConversation.Messages.Count, "Общее представление не должно терять сообщения сцены.");
Expect(sceneThread.Messages.Count(message => message.IsDirector) == 1, "Режиссёрское событие должно иметь особый тип в общем представлении.");
Expect(sceneThread.Messages.Any(message => message.IsOutgoing), "Первая реплика сцены должна сохранять правило выравнивания участника A.");

var history = new[] { "старое", "актуальное" };
var selectedHistory = ConversationContextWindow.TakeLatestThatFits(history, 5, value => value);
Expect(selectedHistory.SequenceEqual(["актуальное"]), "Общее окно контекста должно сохранять последнюю подходящую реплику.");
Expect(ConversationTurnPolicy.CanScheduleAutomaticTurn("running", "alternate", 5), "Автоматическая сцена с задержкой от пяти секунд должна планироваться.");
Expect(!ConversationTurnPolicy.CanScheduleAutomaticTurn("paused", "alternate", 10), "Пауза не должна планировать автоматический ход.");
Expect(ConversationTurnPolicy.NextStatusAfterGeneratedTurn("manual") == "paused", "Ручная сцена должна останавливаться после сгенерированного хода.");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Conversation fixture checks failed:");
    foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("Conversation fixture checks passed.");
return 0;
