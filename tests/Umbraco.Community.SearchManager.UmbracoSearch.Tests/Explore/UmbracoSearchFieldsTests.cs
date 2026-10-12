using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Explore;

public class UmbracoSearchFieldsTests
{
    [Theory]
    [InlineData("Field_title_texts", "title", FieldValues.Texts, null)]
    [InlineData("Field_Umb_Name_textsr1", "Umb_Name", FieldValues.TextsR1, null)]
    [InlineData("Field_price_decimals", "price", FieldValues.Decimals, null)]
    [InlineData("Field_title_texts_mobile", "title", FieldValues.Texts, "mobile")]
    public void TryParse_ProviderFieldName_ReadsNameKindAndSegment(
        string physical,
        string name,
        FieldValues values,
        string? segment
    )
    {
        // Act
        bool parsed = UmbracoSearchFields.TryParse(
            physical,
            out string? actualName,
            out FieldValues actualValues,
            out string? actualSegment
        );

        // Assert
        Assert.Equal(
            (true, name, values, segment),
            (parsed, actualName, actualValues, actualSegment)
        );
    }

    [Theory]
    [InlineData("Sys_Culture")]
    [InlineData("__NodeId")]
    [InlineData("__Query_Field_colour_keywords")]
    [InlineData("Field_title")]
    public void TryParse_OtherFieldName_IsNotAFilterField(string physical)
    {
        // Act
        bool parsed = UmbracoSearchFields.TryParse(physical, out _, out _, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void FromPhysicalNames_PropertyFields_AreTypedByTheirValues()
    {
        // Act
        FieldSchema schema = UmbracoSearchFields
            .FromPhysicalNames([
                "Field_title_texts",
                "Field_title_texts_mobile",
                "Field_count_integers",
                "Field_published_datetimeoffsets",
                "Sys_Culture",
            ])
            .Schema;

        // Assert
        Assert.Equal(
            (FieldType.Text, FieldType.Number, FieldType.Date, null),
            (
                schema.Resolve("title")?.Type,
                schema.Resolve("count")?.Type,
                schema.Resolve("published")?.Type,
                schema.Resolve("Sys_Culture")?.Type
            )
        );
    }

    [Fact]
    public void FromPhysicalNames_NoPhysicalIndex_StillOffersTheSystemFieldsAndAliases()
    {
        // Act
        FieldSchema schema = UmbracoSearchFields.FromPhysicalNames([]).Schema;

        // Assert
        Assert.Equal(
            ("Umb_ContentTypeId", FieldType.Keyword),
            (schema.Resolve("type")?.Name, schema.Resolve("type")!.Type)
        );
    }
}
