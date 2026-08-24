namespace SoulExe.ViewModels;

/// <summary>Names of scene-related properties that usually refresh together after a scene reload.</summary>
public static class SceneUiNotifications
{
    public static readonly string[] SelectionChanged =
    [
        nameof(MainViewModel.SelectedGroupConversation),
        nameof(MainViewModel.IsSceneSelected),
        nameof(MainViewModel.IsSceneConversationVisible),
        nameof(MainViewModel.SelectedSceneCharacterA),
        nameof(MainViewModel.SelectedSceneCharacterB),
        nameof(MainViewModel.SceneParticipants),
        nameof(MainViewModel.SceneParticipantNames),
        nameof(MainViewModel.SceneNextSpeakerName),
        nameof(MainViewModel.SceneLastMessageLabel),
        nameof(MainViewModel.SceneStartPauseText),
        nameof(MainViewModel.IsSceneFinished)
    ];

    public static readonly string[] SelectionWithoutLastMessage =
    [
        nameof(MainViewModel.SelectedGroupConversation),
        nameof(MainViewModel.IsSceneSelected),
        nameof(MainViewModel.IsSceneConversationVisible),
        nameof(MainViewModel.SelectedSceneCharacterA),
        nameof(MainViewModel.SelectedSceneCharacterB),
        nameof(MainViewModel.SceneParticipants),
        nameof(MainViewModel.SceneParticipantNames),
        nameof(MainViewModel.SceneNextSpeakerName),
        nameof(MainViewModel.SceneStartPauseText),
        nameof(MainViewModel.IsSceneFinished)
    ];
}
