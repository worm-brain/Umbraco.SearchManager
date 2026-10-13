using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

/// <summary>
/// Reads explanations Lucene 4.8 (DefaultSimilarity) printed for queries the Explore compiler
/// writes, captured from a real index; <c>ExamineExploreSourceExplainTests</c> pins that Lucene
/// still prints them this way.
/// </summary>
public class ScoreExplanationReaderTests
{
    private const string WordMatch = """
        0.2393851 = (MATCH) sum of:
          0.2393851 = (MATCH) weight(nodeName:umbraco in 0) [DefaultSimilarity], result of:
            0.2393851 = score(doc=0,freq=1), product of:
              0.29744622 = queryWeight, product of:
                1.287682 = idf(docFreq=2, maxDocs=4)
                0.23099354 = queryNorm
              0.8048013 = fieldWeight in 0, product of:
                1 = tf(freq=1), with freq of:
                  1 = termFreq=1
                1.287682 = idf(docFreq=2, maxDocs=4)
                0.625 = fieldNorm(doc=0)
        """;

    private const string PhraseMatch = """
        0.64182776 = (MATCH) sum of:
          0.64182776 = (MATCH) weight(nodeName:"getting started" in 1) [DefaultSimilarity], result of:
            0.64182776 = score(doc=1,freq=1), product of:
              0.37907383 = queryWeight, product of:
                3.3862944 = idf(), sum of:
                  1.6931472 = idf(docFreq=1, maxDocs=4)
                  1.6931472 = idf(docFreq=1, maxDocs=4)
                0.11194356 = queryNorm
              1.6931472 = fieldWeight in 1, product of:
                1 = tf(freq=1), with freq of:
                  1 = phraseFreq=1
                3.3862944 = idf(), sum of:
                  1.6931472 = idf(docFreq=1, maxDocs=4)
                  1.6931472 = idf(docFreq=1, maxDocs=4)
                0.5 = fieldNorm(doc=1)
        """;

    private const string PrefixMatch = """
        0.5 = (MATCH) sum of:
          0.5 = (MATCH) ConstantScore(nodeName:hood*), product of:
            1 = boost
            0.5 = queryNorm
        """;

    private const string WordAndType = """
        0.83965117 = (MATCH) sum of:
          0.22294064 = (MATCH) sum of:
            0.22294064 = (MATCH) weight(nodeName:umbraco in 0) [DefaultSimilarity], result of:
              0.22294064 = score(doc=0,freq=1), product of:
                0.27701327 = queryWeight, product of:
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.21512553 = queryNorm
                0.8048013 = fieldWeight in 0, product of:
                  1 = tf(freq=1), with freq of:
                    1 = termFreq=1
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.625 = fieldNorm(doc=0)
          0.61671054 = (MATCH) weight(__NodeTypeAlias:blogpost in 0) [DefaultSimilarity], result of:
            0.61671054 = score(doc=0,freq=1), product of:
              0.3642392 = queryWeight, product of:
                1.6931472 = idf(docFreq=1, maxDocs=4)
                0.21512553 = queryNorm
              1.6931472 = fieldWeight in 0, product of:
                1 = tf(freq=1), with freq of:
                  1 = termFreq=1
                1.6931472 = idf(docFreq=1, maxDocs=4)
                1 = fieldNorm(doc=0)
        """;

    private const string WordAndLevel = """
        0.45831028 = (MATCH) sum of:
          0.23324327 = (MATCH) sum of:
            0.23324327 = (MATCH) weight(nodeName:umbraco in 0) [DefaultSimilarity], result of:
              0.23324327 = score(doc=0,freq=1), product of:
                0.28981474 = queryWeight, product of:
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.225067 = queryNorm
                0.8048013 = fieldWeight in 0, product of:
                  1 = tf(freq=1), with freq of:
                    1 = termFreq=1
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.625 = fieldNorm(doc=0)
          0.225067 = (MATCH) ConstantScore(level:[2 TO 2]), product of:
            1 = boost
            0.225067 = queryNorm
        """;

    private const string TypeOnly = """
        1.6931472 = (MATCH) weight(__NodeTypeAlias:blogpost in 0) [DefaultSimilarity], result of:
          1.6931472 = fieldWeight in 0, product of:
            1 = tf(freq=1), with freq of:
              1 = termFreq=1
            1.6931472 = idf(docFreq=1, maxDocs=4)
            1 = fieldNorm(doc=0)
        """;

    private const string OneOfTwoWords = """
        0.06664242 = (MATCH) product of:
          0.13328484 = (MATCH) sum of:
            0.13328484 = (MATCH) sum of:
              0.13328484 = (MATCH) weight(nodeName:umbraco in 1) [DefaultSimilarity], result of:
                0.13328484 = score(doc=1,freq=1), product of:
                  0.20701514 = queryWeight, product of:
                    1.287682 = idf(docFreq=2, maxDocs=4)
                    0.16076572 = queryNorm
                  0.643841 = fieldWeight in 1, product of:
                    1 = tf(freq=1), with freq of:
                      1 = termFreq=1
                    1.287682 = idf(docFreq=2, maxDocs=4)
                    0.5 = fieldNorm(doc=1)
          0.5 = coord(1/2)
        """;

    private const string Fuzzy = """
        1.058217 = (MATCH) weight(nodeName:hoodie^0.6 in 2) [DefaultSimilarity], result of:
          1.058217 = fieldWeight in 2, product of:
            1 = tf(freq=1), with freq of:
              1 = termFreq=1
            1.6931472 = idf(docFreq=1, maxDocs=4)
            0.625 = fieldNorm(doc=2)
        """;

    private const string MatchAll = """
        1 = (MATCH) MatchAllDocsQuery, product of:
          1 = queryNorm
        """;

    private const string RequiredClauseMissed = """
        0 = (NON-MATCH) Failure to meet condition(s) of required/prohibited clause(s)
          0.18659462 = (MATCH) sum of:
            0.18659462 = (MATCH) weight(nodeName:umbraco in 1) [DefaultSimilarity], result of:
              0.18659462 = score(doc=1,freq=1), product of:
                0.28981474 = queryWeight, product of:
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.225067 = queryNorm
                0.643841 = fieldWeight in 1, product of:
                  1 = tf(freq=1), with freq of:
                    1 = termFreq=1
                  1.287682 = idf(docFreq=2, maxDocs=4)
                  0.5 = fieldNorm(doc=1)
          0 = no match on required clause (ConstantScore(level:[2 TO 2]))
            0 = (NON-MATCH) ConstantScore(level:[2 TO 2]) doesn't match id 1
        """;

    [Fact]
    public void Read_WordMatch_ReadsEveryTfIdfFactor()
    {
        // Act
        ExplainNode match = Assert.Single(Read(WordMatch).Children);

        // Assert
        Assert.Equal(
            (
                ExplainNodeKind.TermMatch,
                "nodeName",
                "umbraco",
                new ExplainFactors(
                    Freq: 1,
                    Tf: 1,
                    Idf: 1.287682f,
                    DocFreq: 2,
                    MaxDocs: 4,
                    FieldNorm: 0.625f,
                    QueryNorm: 0.23099354f
                )
            ),
            (match.Kind, match.Field, match.Term, match.Factors)
        );
    }

    [Fact]
    public void Read_WordMatch_RootIsTheSumWithTheScore()
    {
        // Act
        ExplainNode root = Read(WordMatch);

        // Assert
        Assert.Equal((ExplainNodeKind.Sum, 0.2393851f), (root.Kind, root.Value));
    }

    [Fact]
    public void Read_Phrase_IsAPhraseMatchWithTheSummedIdf()
    {
        // Act
        ExplainNode match = Assert.Single(Read(PhraseMatch).Children);

        // Assert
        Assert.Equal(
            (ExplainNodeKind.PhraseMatch, "getting started", 1f, 3.3862944f),
            (match.Kind, match.Term, match.Factors?.Freq, match.Factors?.Idf)
        );
    }

    [Fact]
    public void Read_PrefixOnATextField_IsAConstant()
    {
        // Act
        ExplainNode match = Assert.Single(Read(PrefixMatch).Children);

        // Assert
        Assert.Equal(
            (ExplainNodeKind.Constant, "nodeName", "hood*", 1f, 0.5f),
            (match.Kind, match.Field, match.Term, match.Factors?.Boost, match.Factors?.QueryNorm)
        );
    }

    [Fact]
    public void Read_KeywordClause_IsAFilterPointingAtItsChip()
    {
        // Arrange: the excluded type chip is not the one that matched.
        Filter[] chips =
        [
            new(FilterKind.Text, null, FilterOperator.Contains, "umbraco"),
            new(FilterKind.Field, "type", FilterOperator.Is, "product", Exclude: true),
            new(FilterKind.Field, "type", FilterOperator.Is, "blogPost"),
        ];

        // Act
        ExplainNode filter = Read(WordAndType, chips).Children[1];

        // Assert
        Assert.Equal(
            (ExplainNodeKind.Filter, "__NodeTypeAlias", "blogpost", (int?)2, 0.61671054f),
            (filter.Kind, filter.Field, filter.Term, filter.FilterIndex, filter.Value)
        );
    }

    [Fact]
    public void Read_KeywordClause_KeepsTheMatchUnderTheFilter()
    {
        // Act
        ExplainNode filter = Read(WordAndType).Children[1];

        // Assert
        Assert.Equal(ExplainNodeKind.TermMatch, Assert.Single(filter.Children).Kind);
    }

    [Fact]
    public void Read_TextClauseBesideAFilter_StaysASum()
    {
        // Act
        ExplainNode words = Read(WordAndType).Children[0];

        // Assert
        Assert.Equal(ExplainNodeKind.Sum, words.Kind);
    }

    [Fact]
    public void Read_NumberRange_IsAFilterPointingAtItsChip()
    {
        // Arrange
        Filter[] chips =
        [
            new(FilterKind.Text, null, FilterOperator.Contains, "umbraco"),
            new(FilterKind.Field, "level", FilterOperator.Equals, "2"),
        ];

        // Act
        ExplainNode filter = Read(WordAndLevel, chips).Children[1];

        // Assert
        Assert.Equal(
            (ExplainNodeKind.Filter, "level", "[2 TO 2]", (int?)1),
            (filter.Kind, filter.Field, filter.Term, filter.FilterIndex)
        );
    }

    [Fact]
    public void Read_SkippedChipOnTheField_IsNotTheFiltersChip()
    {
        // Arrange: chip 0 was skipped by the compiler, so chip 1 is the one that ran.
        Filter[] chips =
        [
            new(FilterKind.Field, "level", FilterOperator.Equals, "two"),
            new(FilterKind.Field, "level", FilterOperator.Equals, "2"),
        ];

        // Act
        ExplainNode filter = ScoreExplanationReader
            .Read(LuceneExplanationText.Parse(WordAndLevel), TestSchema.Content, chips, null, [0])
            .Children[1];

        // Assert
        Assert.Equal(1, filter.FilterIndex);
    }

    [Fact]
    public void Read_KeywordQueryAlone_TheRootIsTheFilter()
    {
        // Act
        ExplainNode root = Read(TypeOnly);

        // Assert
        Assert.Equal((ExplainNodeKind.Filter, (int?)null), (root.Kind, root.FilterIndex));
    }

    [Fact]
    public void Read_OneOfTwoOptionalWords_IsACoordOfOneInTwo()
    {
        // Act
        ExplainNode root = Read(OneOfTwoWords);

        // Assert
        Assert.Equal(
            (ExplainNodeKind.Coord, 1, 2, 0.06664242f),
            (root.Kind, root.Factors?.Matched, root.Factors?.Of, root.Value)
        );
    }

    [Fact]
    public void Read_OneOfTwoOptionalWords_TheCoordWrapsTheSum()
    {
        // Act
        ExplainNode root = Read(OneOfTwoWords);

        // Assert
        Assert.Equal(ExplainNodeKind.Sum, Assert.Single(root.Children).Kind);
    }

    [Fact]
    public void Read_FuzzyTerm_ReadsTheBoostOutOfTheTerm()
    {
        // Act
        ExplainNode root = Read(Fuzzy);

        // Assert
        Assert.Equal(("hoodie", 0.6f), (root.Term, root.Factors?.Boost));
    }

    [Fact]
    public void Read_MatchAll_IsMatchAllWithTheQueryNorm()
    {
        // Act
        ExplainNode root = Read(MatchAll);

        // Assert
        Assert.Equal((ExplainNodeKind.MatchAll, 1f), (root.Kind, root.Factors?.QueryNorm));
    }

    [Fact]
    public void Read_MissedRequiredClause_IsANoMatchNamingTheClause()
    {
        // Act
        ExplainNode root = Read(RequiredClauseMissed);

        // Assert
        Assert.Equal(
            (ExplainNodeKind.NoMatch, ExplainNodeKind.NoMatch),
            (root.Kind, root.Children[1].Kind)
        );
    }

    [Fact]
    public void Read_UnknownDescription_IsOtherWithTheDescription()
    {
        // Arrange
        var raw = new RawExplanation(
            2,
            "custom similarity, product of:",
            true,
            [new RawExplanation(2, "something new", true, [])]
        );

        // Act
        ExplainNode root = ScoreExplanationReader.Read(raw, TestSchema.Content, [], null);

        // Assert
        Assert.Equal(
            (ExplainNodeKind.Other, "custom similarity, product of:", ExplainNodeKind.Other),
            (root.Kind, root.Description, root.Children[0].Kind)
        );
    }

    [Fact]
    public void MustNotMatch_Chips_ListsTheProhibitedOnesThatRan()
    {
        // Arrange: excluded chips and "is missing" prohibit; an excluded "is missing" requires a
        // value; chip 4 was skipped.
        Filter[] chips =
        [
            new(FilterKind.Text, null, FilterOperator.Contains, "umbraco"),
            new(FilterKind.Field, "type", FilterOperator.Is, "product", Exclude: true),
            new(FilterKind.Field, "price", FilterOperator.Missing, ""),
            new(FilterKind.Field, "level", FilterOperator.Missing, "", Exclude: true),
            new(FilterKind.Field, "colour", FilterOperator.Is, "red", Exclude: true),
        ];

        // Act
        IReadOnlyList<int> prohibited = ScoreExplanationReader.MustNotMatch(chips, [4]);

        // Assert
        Assert.Equal([1, 2], prohibited);
    }

    private static ExplainNode Read(string text, IReadOnlyList<Filter>? chips = null) =>
        ScoreExplanationReader.Read(
            LuceneExplanationText.Parse(text),
            TestSchema.Content,
            chips ?? [],
            null
        );
}
