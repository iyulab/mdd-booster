using MddBooster.Core.Types;

namespace MddBooster.Tests.Types;

/// <summary>
/// The digits a <c>decimal</c> field keeps — one rule every target reads (the SQL column, the C# validation, the form's
/// step), so a column and the rule guarding writes to it cannot disagree.
/// </summary>
public class DecimalDigitsOfTests
{
    [Fact]
    public void Declared_one_declared_and_bare_decimals_keep_their_digits()
    {
        Assert.Equal((18, 4), M3lPrimitives.DecimalDigitsOf(["18", "4"]));
        Assert.Equal((10, 0), M3lPrimitives.DecimalDigitsOf(["10"]));
        Assert.Equal((18, 2), M3lPrimitives.DecimalDigitsOf(null));
        Assert.Equal((18, 2), M3lPrimitives.DecimalDigitsOf([]));
    }
}
