namespace ScriSelfStudio.BookArchitecture.Tests;

using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Персонажи, их арки и шаги сюжетных линий (ТЗ п. 27, 35; ADR 010).
/// </summary>
public sealed class CharactersAndPlotLinesTests
{
    private const string ProjectPath = @"C:\Проекты\Рукопись.scri";

    [Fact]
    public async Task New_character_gets_the_canonical_arc_steps()
    {
        var context = await CreateContextAsync();

        var saved = await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" });

        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        var character = Assert.Single(context.Architecture.GetCharacters());
        Assert.Equal(CharacterArcStage.DefaultStages.Count, character.ArcStages.Count);
        Assert.Equal("Начальное состояние", character.ArcStages[0].Title);
        Assert.Equal("Финальное состояние", character.ArcStages[^1].Title);
    }

    [Fact]
    public async Task Character_card_fields_are_saved()
    {
        var context = await CreateContextAsync();
        var created = await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" });
        var character = created.Require().Characters[0];

        var updated = await context.Architecture.SaveCharacterAsync(character with
        {
            Age = "25 лет",
            Role = CharacterRole.Protagonist,
            FalseBelief = "Деньги решают всё",
            InternalNeed = "Научиться доверять",
            ArcType = CharacterArcType.Negative,
            Tags = ["маг", "предатель"],
            DraftStatus = CharacterDraftStatus.Draft,
        });

        Assert.True(updated.IsSuccess, updated.Error?.TechnicalDetails);
        var saved = Assert.Single(context.Architecture.GetCharacters());
        Assert.Equal("25 лет", saved.Age);
        Assert.Equal(CharacterRole.Protagonist, saved.Role);
        Assert.Equal("Деньги решают всё", saved.FalseBelief);
        Assert.Equal(CharacterArcType.Negative, saved.ArcType);
        Assert.Equal(2, saved.Tags.Count);
        Assert.Equal(CharacterDraftStatus.Draft, saved.DraftStatus);
    }

    [Fact]
    public async Task New_character_without_arc_steps_keeps_steps_and_applies_card_fields()
    {
        var context = await CreateContextAsync();

        var saved = await context.Architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Анна",
            Role = CharacterRole.Protagonist,
            ExternalGoal = "Найти источник магии",
        });

        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        var character = Assert.Single(context.Architecture.GetCharacters());
        Assert.Equal(CharacterRole.Protagonist, character.Role);
        Assert.Equal("Найти источник магии", character.ExternalGoal);
        Assert.Equal(CharacterArcStage.DefaultStages.Count, character.ArcStages.Count);
    }

    [Fact]
    public async Task Character_names_stay_unique()
    {
        var context = await CreateContextAsync();
        await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" });

        var duplicate = await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" });

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.EntityAlreadyExists, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Step_links_a_character_and_a_step_of_his_arc()
    {
        var context = await CreateContextAsync();
        var character = (await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" }))
            .Require().Characters[0];
        var step = character.ArcStages[0];

        var saved = await context.Architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Линия Анны",
            ColorHex = "#4A7DFF",
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Знакомство",
                    Order = 0,
                    Participants = [new StepParticipantDto
                    {
                        CharacterId = character.Id,
                        CharacterArcStageId = step.Id,
                    }],
                },
                new PlotLineStepDto { Title = "Развязка", Order = 1, IsFinal = true },
            ],
        });

        Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        var line = Assert.Single(context.Architecture.GetPlotLines());
        Assert.Equal("Линия Анны", line.Name);
        Assert.Equal(2, line.Steps.Count);
        Assert.True(line.Steps[1].IsFinal);

        var participant = Assert.Single(line.Steps[0].Participants);
        Assert.Equal(character.Id, participant.CharacterId);
        Assert.Equal(step.Id, participant.CharacterArcStageId);
    }

    [Fact]
    public async Task Step_participant_must_be_an_existing_character()
    {
        var context = await CreateContextAsync();

        var saved = await context.Architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Линия",
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Этап",
                    Order = 0,
                    Participants = [new StepParticipantDto { CharacterId = Guid.NewGuid() }],
                },
            ],
        });

        Assert.False(saved.IsSuccess);
        Assert.Equal(ErrorCode.EntityNotFound, saved.Error!.Code);
    }

    [Fact]
    public async Task Deleting_a_character_removes_him_from_steps()
    {
        var context = await CreateContextAsync();
        var character = (await context.Architecture.SaveCharacterAsync(new CharacterDto { Name = "Анна" }))
            .Require().Characters[0];

        await context.Architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Линия",
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Этап",
                    Order = 0,
                    Participants = [new StepParticipantDto { CharacterId = character.Id }],
                },
            ],
        });

        var deleted = await context.Architecture.DeleteCharacterAsync(character.Id);

        Assert.True(deleted.IsSuccess, deleted.Error?.TechnicalDetails);
        Assert.Empty(context.Architecture.GetCharacters());
        Assert.Empty(context.Architecture.GetPlotLines()[0].Steps[0].Participants);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var clock = new FakeClock();
        var projects = new ProjectService(new InMemoryProjectStore(), new InMemoryRecoveryStore(clock), new EventBus(), clock);
        var architecture = new BookArchitectureService(projects, clock);
        var created = await projects.CreateAsync(new CreateProjectRequest
        {
            FilePath = ProjectPath,
            Name = "Рукопись",
        });

        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        return new TestContext(projects, architecture);
    }

    private sealed record TestContext(ProjectService Projects, BookArchitectureService Architecture);
}
