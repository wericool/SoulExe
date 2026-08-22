using SoulTextWpf.Models;

namespace SoulTextWpf.ConversationChecks;

internal static class ConversationFixtures
{
    public static (SoulCharacter Character, SoulChat Chat) CreateDirectChat()
    {
        var character = new SoulCharacter
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "Надя",
            Description = "Бармен в уютном кафе.",
            Personality = "Вежливая, немного смущённая и внимательная.",
            Scenario = "Утреннее кафе в небольшом городе."
        };
        var chat = new SoulChat
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Name = "Кафе утром",
            InitialUserProfile = "Посетитель с книгой и сумкой.",
            InitialRelationshipContext = "Надя и посетитель знакомятся.",
            SummaryText = "Надя предложила гостю кофе и свежие булочки.",
            CreatedAt = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 22, 9, 4, 0, TimeSpan.Zero)
        };
        chat.Messages.Add(CreateMessage(1, SoulMessageRole.User, "Вы", "*Посетитель садится у стойки и открывает книгу.*"));
        chat.Messages.Add(CreateMessage(2, SoulMessageRole.Assistant, character.Name, "*Надя улыбается.* Доброе утро. Хотите кофе или булочку?"));
        character.Chats.Add(chat);
        character.CurrentChatId = chat.Id;
        return (character, chat);
    }

    public static (SoulCharacter First, SoulCharacter Second, SoulScene Scene) CreateScene()
    {
        var first = new SoulCharacter { Id = Guid.Parse("10000000-0000-0000-0000-000000000010"), Name = "Алиса", Personality = "Любознательная исследовательница." };
        var second = new SoulCharacter { Id = Guid.Parse("10000000-0000-0000-0000-000000000011"), Name = "Борис", Personality = "Спокойный проводник." };
        var scene = new SoulScene
        {
            Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Name = "Ночная экспедиция",
            CharacterAId = first.Id,
            CharacterBId = second.Id,
            Scenario = "Два путешественника ищут дорогу в горах.",
            Location = "Горный перевал",
            TimeContext = "Ночь",
            Goal = "Найти безопасный путь к убежищу.",
            RelationshipContext = "Участники доверяют друг другу, но устали.",
            TurnMode = "alternate",
            DelaySeconds = 10,
            Status = "paused",
            NextCharacterId = first.Id,
            SummaryText = "Алиса и Борис остановились у развилки во время метели.",
            CreatedAt = new DateTimeOffset(2026, 8, 22, 21, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 22, 21, 5, 0, TimeSpan.Zero)
        };
        scene.Messages.Add(new SoulSceneMessage { Id = Guid.Parse("40000000-0000-0000-0000-000000000001"), SequenceNumber = 1, Kind = SoulSceneMessageKind.Director, SpeakerName = "Режиссёр", Content = "*Поднимается сильный ветер.*", CreatedAt = scene.CreatedAt });
        scene.Messages.Add(new SoulSceneMessage { Id = Guid.Parse("40000000-0000-0000-0000-000000000002"), SequenceNumber = 2, Kind = SoulSceneMessageKind.Character, SpeakerCharacterId = first.Id, SpeakerName = first.Name, Content = "*Алиса поднимает фонарь.* Нам нужно выбрать тропу до метели.", CreatedAt = scene.CreatedAt.AddMinutes(2) });
        scene.Messages.Add(new SoulSceneMessage { Id = Guid.Parse("40000000-0000-0000-0000-000000000003"), SequenceNumber = 3, Kind = SoulSceneMessageKind.Character, SpeakerCharacterId = second.Id, SpeakerName = second.Name, Content = "Я вижу огонь ниже по склону. Идём осторожно.", CreatedAt = scene.CreatedAt.AddMinutes(5) });
        return (first, second, scene);
    }

    private static SoulMessage CreateMessage(int sequence, SoulMessageRole role, string author, string content)
    {
        var variant = new SoulMessageVariant { Id = Guid.NewGuid(), Label = "Основной", Content = content };
        return new SoulMessage
        {
            Id = Guid.NewGuid(),
            SequenceNumber = sequence,
            Role = role,
            AuthorName = author,
            CurrentVariantId = variant.Id,
            Variants = [variant],
            CreatedAt = new DateTimeOffset(2026, 8, 22, 9, sequence, 0, TimeSpan.Zero)
        };
    }
}
