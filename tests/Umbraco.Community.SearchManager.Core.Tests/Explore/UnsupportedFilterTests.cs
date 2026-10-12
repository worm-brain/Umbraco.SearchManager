using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class UnsupportedFilterTests
{
    private static readonly UnsupportedFilter[] Rules =
    [
        new(FilterKind.Text, [], [], true, "No excluded text"),
        new(FilterKind.Field, [FieldType.Text], [FilterOperator.Phrase], null, "No phrase"),
        new(FilterKind.Field, [], [FilterOperator.Missing], null, "No missing"),
    ];

    public static TheoryData<Filter, FieldType?, string?> Cases =>
        new()
        {
            {
                new(FilterKind.Text, null, FilterOperator.Contains, "a", Exclude: true),
                null,
                "No excluded text"
            },
            { new(FilterKind.Text, null, FilterOperator.Contains, "a"), null, null },
            {
                new(FilterKind.Field, "title", FilterOperator.Phrase, "a b"),
                FieldType.Text,
                "No phrase"
            },
            { new(FilterKind.Field, "tag", FilterOperator.Phrase, "a b"), FieldType.Keyword, null },
            {
                new(FilterKind.Field, "level", FilterOperator.Missing, ""),
                FieldType.Number,
                "No missing"
            },
            { new(FilterKind.Field, "level", FilterOperator.AtLeast, "2"), FieldType.Number, null },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FirstReason_Chip_ReturnsTheFirstMatchingRulesReason(
        Filter filter,
        FieldType? type,
        string? expected
    )
    {
        // Act
        string? reason = UnsupportedFilter.FirstReason(Rules, filter, type);

        // Assert
        Assert.Equal(expected, reason);
    }
}
