using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Features.Diagnose.Nodes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

public class NodeFactsReaderTests
{
    private static readonly Guid Key = Guid.Parse("c8addc1f-0a30-4bd9-849d-243ae8ab24a2");

    private readonly IEntityService _entities = Substitute.For<IEntityService>();
    private readonly IEntitySearchService _search = Substitute.For<IEntitySearchService>();
    private readonly IContentService _content = Substitute.For<IContentService>();
    private readonly IPublicAccessService _publicAccess = Substitute.For<IPublicAccessService>();
    private readonly NodeFactsReader _reader;

    public NodeFactsReaderTests()
    {
        IEntitySlim guide = Substitute.For<IEntitySlim>();
        guide.Id.Returns(1061);
        guide.Key.Returns(Key);
        guide.Name.Returns("Guide");
        _entities.Get(1061, UmbracoObjectTypes.Document).Returns(guide);
        _entities.Get(Key, UmbracoObjectTypes.Document).Returns(guide);
        _search
            .Search(UmbracoObjectTypes.Document, "gui", 0, 10)
            .Returns(new PagedModel<IEntitySlim>(1, [guide]));
        _content.GetById(Arg.Any<Guid>()).Returns((IContent?)null);
        _reader = new NodeFactsReader(_entities, _search, _content, _publicAccess);
    }

    [Theory]
    [InlineData("1061")]
    [InlineData("c8addc1f-0a30-4bd9-849d-243ae8ab24a2")]
    [InlineData(" gui ")]
    public void Find_IdKeyOrName_FindsTheDocument(string query)
    {
        // Act
        IReadOnlyList<NodeCandidate> candidates = _reader.Find(query, 10);

        // Assert
        Assert.Equal(1061, Assert.Single(candidates).Id);
    }

    [Fact]
    public void Find_BlankQuery_FindsNothing()
    {
        // Act
        IReadOnlyList<NodeCandidate> candidates = _reader.Find("  ", 10);

        // Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public void Read_VariantDocument_ReadsEachCulture()
    {
        // Arrange
        IContent content = Substitute.For<IContent>();
        content.Key.Returns(Key);
        content.Name.Returns("Guide");
        content.Path.Returns("-1,1061");
        content.ContentType.Variations.Returns(ContentVariation.Culture);
        content.ContentType.Alias.Returns("contentPage");
        content.AvailableCultures.Returns(["en-US", "da-DK"]);
        content.IsCulturePublished("en-US").Returns(true);
        content.IsCultureEdited("da-DK").Returns(true);
        _content.GetById(Key).Returns(content);

        // Act
        NodeFacts? facts = _reader.Read(Key);

        // Assert
        Assert.Equal(
            "en-US:True:False|da-DK:False:True",
            string.Join("|", facts!.Cultures.Select(c => $"{c.Iso}:{c.Published}:{c.Draft}"))
        );
    }

    [Fact]
    public void Read_UnknownKey_IsNull()
    {
        // Act
        NodeFacts? facts = _reader.Read(Guid.NewGuid());

        // Assert
        Assert.Null(facts);
    }
}
