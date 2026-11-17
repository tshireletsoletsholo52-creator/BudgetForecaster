using BudgetForecaster.Core;

namespace BudgetForecaster.Tests;

public class CategoryRulesTests
{
    [Fact]
    public void NormalizeName_TrimsAndCollapsesSpaces()
    {
        Assert.Equal("Eating out", CategoryRules.NormalizeName("  Eating    out  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeName_BlankIsRejected(string? raw)
    {
        Assert.Throws<CategoryValidationException>(() => CategoryRules.NormalizeName(raw));
    }

    [Fact]
    public void NormalizeName_ExactlyMaxLength_IsAccepted()
    {
        Assert.Equal(CategoryRules.MaxNameLength, CategoryRules.NormalizeName(new string('a', CategoryRules.MaxNameLength)).Length);
    }

    [Fact]
    public void NormalizeName_OverMaxLength_IsRejected()
    {
        Assert.Throws<CategoryValidationException>(() => CategoryRules.NormalizeName(new string('a', CategoryRules.MaxNameLength + 1)));
    }
}
