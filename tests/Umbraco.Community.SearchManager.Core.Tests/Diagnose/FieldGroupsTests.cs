using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Core.Tests.Diagnose;

public class FieldGroupsTests
{
    private static readonly HashSet<string> Fields = new(
        [
            "nodeName",
            "nodeName_en-us",
            "__Published",
            "__Published_da-dk",
            "__Raw_bodyText",
            "__IndexType",
            "title_old",
            "bodyText",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly string[] Cultures = ["en-US", "da-DK"];

    public static TheoryData<string, FieldGroup, string?> Cases =>
        new()
        {
            { "nodeName", FieldGroup.Field, null },
            { "nodeName_en-us", FieldGroup.CultureVariant, "en-us" },
            { "__Published_da-dk", FieldGroup.CultureVariant, "da-dk" },
            { "__Raw_bodyText", FieldGroup.RawCopy, null },
            { "__IndexType", FieldGroup.System, null },
            // A suffix that is not a site culture, or whose base is not a field, stays a field.
            { "title_old", FieldGroup.Field, null },
            { "bodyText_fr-fr", FieldGroup.Field, null },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_FieldName_ReturnsGroupAndCulture(
        string name,
        FieldGroup group,
        string? culture
    )
    {
        // Act
        (FieldGroup Group, string? Culture) result = FieldGroups.Classify(name, Fields, Cultures);

        // Assert
        Assert.Equal((group, culture), result);
    }

    [Fact]
    public void Classify_CultureSuffixWithoutItsBaseField_IsAField()
    {
        // Arrange
        var fields = new HashSet<string>(["teaser_en-us"], StringComparer.OrdinalIgnoreCase);

        // Act
        (FieldGroup group, _) = FieldGroups.Classify("teaser_en-us", fields, Cultures);

        // Assert
        Assert.Equal(FieldGroup.Field, group);
    }
}
