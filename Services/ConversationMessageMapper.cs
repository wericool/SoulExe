using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Maps canonical conversation messages to the desktop presentation model.</summary>
public static class ConversationMessageMapper
{
    public static SoulMessage ToPersonalMessage(ConversationSnapshot conversation, ConversationMessageSnapshot message)
    {
        var participant = conversation.FindParticipant(message.AuthorParticipantId);
        var role = message.Kind is ConversationMessageKind.SystemEvent or ConversationMessageKind.DirectorEvent
            ? SoulMessageRole.System
            : participant?.Kind == ConversationParticipantKind.Character ? SoulMessageRole.Assistant : SoulMessageRole.User;
        var variants = message.Variants.Select(value => new SoulMessageVariant
        {
            Id = value.Id,
            Label = value.Label,
            Content = value.Content,
            CreatedAt = value.CreatedAt
        }).ToList();
        if (variants.Count == 0)
            variants.Add(new SoulMessageVariant { Content = message.Content, CreatedAt = message.CreatedAt });
        var selected = message.SelectedVariantId is { } selectedId && variants.Any(value => value.Id == selectedId)
            ? selectedId
            : variants[0].Id;
        return new SoulMessage
        {
            Id = message.Id,
            SequenceNumber = message.SequenceNumber,
            Role = role,
            AuthorKind = message.AuthorKind,
            AuthorPersonaId = message.AuthorPersonaId,
            AuthorName = message.AuthorName,
            AuthorAvatarPath = message.AuthorAvatarPath,
            CurrentVariantId = selected,
            Variants = variants,
            Attachments = message.Attachments.Select(value => new SoulAttachment
            {
                Id = value.Id,
                MediaType = value.MediaType,
                LocalPath = value.LocalPath,
                OriginalName = value.OriginalName,
                CreatedAt = value.CreatedAt
            }).ToList(),
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt
        };
    }
}
