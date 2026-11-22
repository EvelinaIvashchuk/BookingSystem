using BookingSystem.Ai;
using FluentAssertions;

namespace BookingSystem.Tests.Ai;

public class ReplyFormatterTests
{
    [Theory]
    [InlineData("Натисніть **Підтвердити** протягом 15 хв.", "Натисніть Підтвердити протягом 15 хв.")]
    [InlineData("__Важливо__: депозит 10 000 ₴", "Важливо: депозит 10 000 ₴")]
    [InlineData("### Депозит\nSUV — 10 000 ₴", "Депозит\nSUV — 10 000 ₴")]
    [InlineData("* Camry\n* Sienna", "- Camry\n- Sienna")]
    [InlineData("- вже простий список", "- вже простий список")]
    [InlineData("Ціна 2 * 3 = 6 ₴", "Ціна 2 * 3 = 6 ₴")]
    public void ToPlainText_StripsCommonMarkdown(string input, string expected)
    {
        ReplyFormatter.ToPlainText(input.Replace("\\n", "\n")).Should().Be(expected.Replace("\\n", "\n"));
    }
}
