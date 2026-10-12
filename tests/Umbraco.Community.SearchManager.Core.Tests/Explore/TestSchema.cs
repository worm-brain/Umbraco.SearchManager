using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

/// <summary>A schema shaped like Umbraco's ExternalIndex, with a few aliases.</summary>
internal static class TestSchema
{
    public static FieldSchema Content { get; } =
        new(
            [
                new IndexField("nodeName", FieldType.Text),
                new IndexField("nodeName_en-us", FieldType.Text),
                new IndexField("nodeName_da-dk", FieldType.Text),
                new IndexField("bodyText", FieldType.Text),
                new IndexField("__NodeTypeAlias", FieldType.Keyword),
                new IndexField("__Published", FieldType.Keyword),
                new IndexField("level", FieldType.Number),
                new IndexField("price", FieldType.Number),
                new IndexField("updateDate", FieldType.Date),
            ],
            [
                new FieldAlias("type", "__NodeTypeAlias", "Document type"),
                new FieldAlias("name", "nodeName", "Name"),
                new FieldAlias("updated", "updateDate", "Updated"),
                new FieldAlias("published", "__Published", "Published"),
                new FieldAlias("ghost", "noSuchField", "Not in this index"),
            ]
        );
}
