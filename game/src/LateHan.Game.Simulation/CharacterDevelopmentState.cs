using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public sealed record ExperienceMemory(
    GameDate Date,
    AbilityDomain Domain,
    int Amount,
    string Description,
    string? RelatedCharacterId = null,
    string? RelatedOrganizationId = null);

public sealed record CharacterDevelopmentState(
    string CharacterId,
    Abilities Abilities,
    int CommandExperience,
    int MartialExperience,
    int StrategyExperience,
    int AdministrationExperience,
    int DiplomacyExperience,
    int LearningExperience,
    IReadOnlyList<ExperienceMemory> RecentExperiences)
{
    public const int ExperiencePerAbilityPoint = 10;

    public int TotalExperience =>
        CommandExperience +
        MartialExperience +
        StrategyExperience +
        AdministrationExperience +
        DiplomacyExperience +
        LearningExperience;

    public static CharacterDevelopmentState Create(string characterId, Abilities abilities) => new(
        characterId,
        abilities,
        0,
        0,
        0,
        0,
        0,
        0,
        []);

    public int ExperienceFor(AbilityDomain domain) => domain switch
    {
        AbilityDomain.Command => CommandExperience,
        AbilityDomain.Martial => MartialExperience,
        AbilityDomain.Strategy => StrategyExperience,
        AbilityDomain.Administration => AdministrationExperience,
        AbilityDomain.Diplomacy => DiplomacyExperience,
        _ => LearningExperience,
    };

    public int ProgressFor(AbilityDomain domain) => ExperienceFor(domain) % ExperiencePerAbilityPoint;

    public CharacterDevelopmentState Gain(
        GameDate date,
        AbilityDomain domain,
        int amount,
        string description,
        string? relatedCharacterId = null,
        string? relatedOrganizationId = null)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "经历增长必须为正数。");
        }

        var before = ExperienceFor(domain);
        var after = before + amount;
        var abilityIncrease = (after / ExperiencePerAbilityPoint) - (before / ExperiencePerAbilityPoint);
        var memory = new ExperienceMemory(
            date,
            domain,
            amount,
            description,
            relatedCharacterId,
            relatedOrganizationId);
        var memories = RecentExperiences
            .Append(memory)
            .TakeLast(12)
            .ToArray();
        var updated = domain switch
        {
            AbilityDomain.Command => this with { CommandExperience = after },
            AbilityDomain.Martial => this with { MartialExperience = after },
            AbilityDomain.Strategy => this with { StrategyExperience = after },
            AbilityDomain.Administration => this with { AdministrationExperience = after },
            AbilityDomain.Diplomacy => this with { DiplomacyExperience = after },
            _ => this with { LearningExperience = after },
        };
        return updated with
        {
            Abilities = updated.Abilities.Improve(domain, abilityIncrease),
            RecentExperiences = memories,
        };
    }
}

public sealed record CharacterRelationshipState(
    string FirstCharacterId,
    string SecondCharacterId,
    int Favor,
    int Trust,
    int Obligation,
    int SharedExperiences,
    GameDate LastChangedOn,
    string LastReason)
{
    public static string KeyFor(string firstCharacterId, string secondCharacterId) =>
        string.Compare(firstCharacterId, secondCharacterId, StringComparison.Ordinal) < 0
            ? $"{firstCharacterId}|{secondCharacterId}"
            : $"{secondCharacterId}|{firstCharacterId}";

    public static CharacterRelationshipState Create(
        string firstCharacterId,
        string secondCharacterId,
        GameDate date,
        string reason)
    {
        var ordered = new[] { firstCharacterId, secondCharacterId }
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new CharacterRelationshipState(ordered[0], ordered[1], 0, 0, 0, 0, date, reason);
    }

    public bool Involves(string characterId) =>
        FirstCharacterId == characterId || SecondCharacterId == characterId;

    public string OtherCharacterId(string characterId) => FirstCharacterId == characterId
        ? SecondCharacterId
        : FirstCharacterId;

    public CharacterRelationshipState Improve(
        int favor,
        int trust,
        int obligation,
        GameDate date,
        string reason) => this with
        {
            Favor = Math.Clamp(Favor + favor, -100, 100),
            Trust = Math.Clamp(Trust + trust, -100, 100),
            Obligation = Math.Clamp(Obligation + obligation, -100, 100),
            SharedExperiences = SharedExperiences + 1,
            LastChangedOn = date,
            LastReason = reason,
        };
}
