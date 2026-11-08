namespace ScriSelfStudio.Presentation.Characters;

using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Presentation.Localization;

public sealed record CharacterProfileField(string Label, string Value);

/// <summary>Памятка автору сцены: факты карточки и только назначенные этапы личной арки.</summary>
public static class CharacterSceneProfile
{
    public static IReadOnlyList<CharacterProfileField> Fields(CharacterDto character) =>
        new (string Label, string? Value)[]
        {
            ("Внешность", character.Appearance), ("Темперамент", character.Temperament),
            ("Сильные стороны", character.Strengths), ("Слабости", character.Weaknesses),
            ("Привычки", character.Habits), ("Речь", character.Speech),
            ("Частые фразы", character.SpeechHabits), ("Цитаты", character.Quotes),
        }.Where(field => !string.IsNullOrWhiteSpace(field.Value))
        .Select(field => new CharacterProfileField(Loc.T(field.Label), field.Value!)).ToList();

    public static IReadOnlyList<CharacterArcStageDto> ArcStages(
        CharacterDto character, IEnumerable<PlotLineDto> lines, Guid sceneId)
    {
        var ids = lines.SelectMany(line => line.Steps)
            .Where(step => step.SceneIds.Contains(sceneId))
            .SelectMany(step => step.Participants)
            .Where(member => member.CharacterId == character.Id)
            .Select(member => member.CharacterArcStageId).ToHashSet();
        return character.ArcStages.Where(stage => ids.Contains(stage.Id)).OrderBy(stage => stage.Order).ToList();
    }
}
