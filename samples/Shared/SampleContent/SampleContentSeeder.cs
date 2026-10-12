using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace SearchManager.Samples.SampleContent;

/// <summary>
/// Gives an empty sample site enough content for every index to hold something worth searching:
/// four document types, two cultures (en-US and da-DK), published and unpublished pages, culture
/// drafts and products. Runs once, on the first start of a site whose content tree is empty, when
/// <c>SampleContent:Enabled</c> is true. Development only; never shipped.
/// </summary>
public sealed class SampleContentComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<
            UmbracoApplicationStartedNotification,
            SampleContentSeeder
        >();
}

/// <summary>Creates the sample languages, document types and content (see <see cref="SampleContentComposer"/>).</summary>
public sealed class SampleContentSeeder(
    IConfiguration configuration,
    IContentService contentService,
    IContentTypeService contentTypeService,
    IDataTypeService dataTypeService,
    ILanguageService languageService,
    IShortStringHelper shortStringHelper,
    ILogger<SampleContentSeeder> logger
) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private static readonly string[] Topics =
    [
        "Umbraco",
        "Examine",
        "search",
        "Lucene",
        "content modelling",
        "the Delivery API",
        "load balancing",
        "block lists",
        "upgrades",
        "accessibility",
        "performance",
        "media",
    ];

    private static readonly string[] Authors = ["Priya Shah", "Sam Okafor", "Lena Berg"];

    /// <inheritdoc />
    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification,
        CancellationToken cancellationToken
    )
    {
        if (
            !configuration.GetValue<bool>("SampleContent:Enabled")
            || contentService.GetRootContent().Any()
        )
        {
            return;
        }

        logger.LogInformation("Seeding sample content for Search Manager");
        Guid user = Constants.Security.SuperUserKey;

        if (await languageService.GetAsync("da-DK") is null)
        {
            await languageService.CreateAsync(new Language("da-DK", "Danish (Denmark)"), user);
        }

        IDataType textstring =
            await dataTypeService.GetAsync(Constants.DataTypes.Guids.TextstringGuid)
            ?? throw new InvalidOperationException("Textstring data type missing");
        IDataType textarea =
            await dataTypeService.GetAsync(Constants.DataTypes.Guids.TextareaGuid)
            ?? throw new InvalidOperationException("Textarea data type missing");

        IContentType home = await CreateTypeAsync(
            "home",
            "Home",
            variant: true,
            user,
            (textarea, "bodyText")
        );
        IContentType page = await CreateTypeAsync(
            "contentPage",
            "Content page",
            variant: true,
            user,
            (textarea, "bodyText"),
            (textstring, "metaDescription")
        );
        IContentType blog = await CreateTypeAsync(
            "blogPost",
            "Blog post",
            variant: false,
            user,
            (textarea, "bodyText"),
            (textstring, "metaDescription"),
            (textstring, "author")
        );
        IContentType product = await CreateTypeAsync(
            "product",
            "Product",
            variant: false,
            user,
            (textarea, "bodyText"),
            (textstring, "price"),
            (textstring, "sku")
        );

        IContent root = contentService.Create("Home", Constants.System.Root, home);
        root.SetCultureName("Home", "en-US");
        root.SetCultureName("Hjem", "da-DK");
        root.SetValue("bodyText", "Websites, training and support for Umbraco.", "en-US");
        root.SetValue("bodyText", "Hjemmesider, kurser og support til Umbraco.", "da-DK");
        contentService.Save(root);
        contentService.Publish(root, ["en-US", "da-DK"]);

        // Content pages: every third has a published Danish variant, every fourth a Danish draft.
        for (int i = 0; i < 24; i++)
        {
            string topic = Topics[i % Topics.Length];
            IContent item = contentService.Create($"Guide to {topic} {i + 1}", root, page.Alias);
            item.SetCultureName($"Guide to {topic} {i + 1}", "en-US");
            item.SetValue(
                "bodyText",
                $"Everything our Umbraco team knows about {topic}, part {i + 1}.",
                "en-US"
            );
            item.SetValue("metaDescription", $"A guide to {topic}.", "en-US");
            var cultures = new List<string> { "en-US" };
            if (i % 3 == 0 || i % 4 == 0)
            {
                item.SetCultureName($"Guide til {topic} {i + 1}", "da-DK");
                item.SetValue("bodyText", $"Alt om {topic}, del {i + 1}.", "da-DK");
                if (i % 3 == 0)
                {
                    cultures.Add("da-DK");
                }
            }

            contentService.Save(item);
            contentService.Publish(item, [.. cultures]);
        }

        // Blog posts: invariant; every seventh is left unpublished, and some lack a meta description.
        IContent blogRoot = contentService.Create("Blog", root, page.Alias);
        blogRoot.SetCultureName("Blog", "en-US");
        contentService.Save(blogRoot);
        contentService.Publish(blogRoot, ["en-US"]);
        for (int i = 0; i < 120; i++)
        {
            string topic = Topics[i % Topics.Length];
            IContent post = contentService.Create(
                $"Notes on {topic} #{i + 1}",
                blogRoot,
                blog.Alias
            );
            post.SetValue(
                "bodyText",
                $"Post {i + 1}: what we learned about {topic} while building Umbraco sites."
            );
            if (i % 5 != 0)
            {
                post.SetValue("metaDescription", $"Notes on {topic}.");
            }

            post.SetValue("author", Authors[i % Authors.Length]);
            contentService.Save(post);
            if (i % 7 != 0)
            {
                contentService.Publish(post, ["*"]);
            }
        }

        // Products: invariant, under a shop page; ProductIndex holds only these.
        IContent shop = contentService.Create("Shop", root, page.Alias);
        shop.SetCultureName("Shop", "en-US");
        contentService.Save(shop);
        contentService.Publish(shop, ["en-US"]);
        for (int i = 0; i < 60; i++)
        {
            IContent item = contentService.Create($"Umbraco product {i + 1}", shop, product.Alias);
            item.SetValue(
                "bodyText",
                $"Product {i + 1} for people who love {Topics[i % Topics.Length]}."
            );
            item.SetValue(
                "price",
                ((i % 9) * 5 + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            item.SetValue("sku", $"SKU-{1000 + i}");
            contentService.Save(item);
            contentService.Publish(item, ["*"]);
        }

        logger.LogInformation("Seeded sample content for Search Manager");
    }

    private async Task<IContentType> CreateTypeAsync(
        string alias,
        string name,
        bool variant,
        Guid user,
        params (IDataType DataType, string Alias)[] properties
    )
    {
        var type = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = alias,
            Name = name,
            Icon = "icon-document",
            AllowedAsRoot = true,
            Variations = variant ? ContentVariation.Culture : ContentVariation.Nothing,
        };

        foreach ((IDataType dataType, string propertyAlias) in properties)
        {
            type.AddPropertyType(
                new PropertyType(shortStringHelper, dataType, propertyAlias)
                {
                    Name = propertyAlias,
                    Variations =
                        variant && propertyAlias != "price"
                            ? ContentVariation.Culture
                            : ContentVariation.Nothing,
                },
                "content",
                "Content"
            );
        }

        await contentTypeService.CreateAsync(type, user);
        return type;
    }
}
