using SoulExe.Models;

namespace SoulExe.ConversationChecks;

internal static class ConversationFixtures
{
    public static (SoulCharacter Character, ConversationSnapshot Conversation) CreateDirectChat()
    {
        var character = new SoulCharacter { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "Надя", Description = "Бармен в уютном кафе.", Personality = "Вежливая, немного смущённая и внимательная.", Scenario = "Утреннее кафе в небольшом городе." };
        var createdAt = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var characterParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var conversation = new ConversationSnapshot
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), Kind = ConversationKind.Direct, Source = ConversationSource.CharacterChat,
            Name = "Кафе утром", SummaryText = "Надя предложила гостю кофе и свежие булочки.", CreatedAt = createdAt, UpdatedAt = createdAt.AddMinutes(4),
            Participants = [new(userId, ConversationParticipantKind.User, "Вы", null, false, 0), new(characterParticipantId, ConversationParticipantKind.Character, character.Name, character.Id, true, 1)],
            Context = new ConversationContextSnapshot { InitialUserProfile = "Посетитель с книгой и сумкой.", InitialRelationshipContext = "Надя и посетитель знакомятся.", SummaryDirectives = "Сохранять изменения доверия между участниками.", Memory = new SoulMemoryBundle() },
            Messages = [Message(1, userId, SoulMessageAuthorKind.User, "Вы", "*Посетитель садится у стойки и открывает книгу.*", createdAt), Message(2, characterParticipantId, SoulMessageAuthorKind.User, character.Name, "*Надя улыбается.* Доброе утро. Хотите кофе или булочку?", createdAt.AddMinutes(1))]
        };
        character.CurrentChatId = conversation.Id;
        return (character, conversation);
    }

    public static (SoulCharacter First, SoulCharacter Second, ConversationSnapshot Conversation) CreateScene()
    {
        var first = new SoulCharacter { Id = Guid.Parse("10000000-0000-0000-0000-000000000010"), Name = "Алиса", Personality = "Любознательная исследовательница." };
        var second = new SoulCharacter { Id = Guid.Parse("10000000-0000-0000-0000-000000000011"), Name = "Борис", Personality = "Спокойный проводник." };
        var createdAt = new DateTimeOffset(2026, 8, 22, 21, 0, 0, TimeSpan.Zero);
        var firstParticipant = Guid.Parse("30000000-0000-0000-0000-000000000010");
        var secondParticipant = Guid.Parse("30000000-0000-0000-0000-000000000011");
        var director = Guid.Parse("30000000-0000-0000-0000-000000000012");
        var conversation = new ConversationSnapshot
        {
            Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), Kind = ConversationKind.Scene, Source = ConversationSource.RootScene,
            Name = "Ночная экспедиция", SummaryText = "Алиса и Борис остановились у развилки во время метели.", CreatedAt = createdAt, UpdatedAt = createdAt.AddMinutes(5),
            Participants = [new(firstParticipant, ConversationParticipantKind.Character, first.Name, first.Id, true, 0), new(secondParticipant, ConversationParticipantKind.Character, second.Name, second.Id, true, 1), new(Guid.Parse("00000000-0000-0000-0000-000000000001"), ConversationParticipantKind.User, "Вы", null, false, 2), new(director, ConversationParticipantKind.Director, "Режиссёр", null, false, 3)],
            Context = new ConversationContextSnapshot { Scenario = "Два путешественника ищут дорогу в горах.", Location = "Горный перевал", TimeContext = "Ночь", Goal = "Найти безопасный путь к убежищу.", RelationshipContext = "Участники доверяют друг другу, но устали." },
            TurnState = new ConversationTurnState("paused", "alternate", firstParticipant, null, 10, true, true),
            Messages = [Message(1, firstParticipant, SoulMessageAuthorKind.User, first.Name, "*Алиса поднимает фонарь.* Нам нужно выбрать тропу до метели.", createdAt), Message(2, director, SoulMessageAuthorKind.Director, "Режиссёр", "*Поднимается сильный ветер.*", createdAt.AddMinutes(2), ConversationMessageKind.DirectorEvent), Message(3, secondParticipant, SoulMessageAuthorKind.User, second.Name, "Я вижу огонь ниже по склону. Идём осторожно.", createdAt.AddMinutes(5))]
        };
        return (first, second, conversation);
    }

    public static ConversationMessageSnapshot Message(int sequence, Guid? participantId, SoulMessageAuthorKind authorKind, string author, string content, DateTimeOffset createdAt, ConversationMessageKind kind = ConversationMessageKind.Message)
    {
        var variant = new ConversationMessageVariantSnapshot(Guid.NewGuid(), "Основной", content, createdAt);
        return new ConversationMessageSnapshot { Id = Guid.NewGuid(), SequenceNumber = sequence, Kind = kind, AuthorParticipantId = participantId, AuthorKind = authorKind, AuthorName = author, Content = content, CreatedAt = createdAt, SelectedVariantId = variant.Id, Variants = [variant] };
    }
}
