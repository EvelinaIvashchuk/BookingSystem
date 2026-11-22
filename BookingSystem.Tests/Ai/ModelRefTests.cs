using BookingSystem.Ai;
using FluentAssertions;

namespace BookingSystem.Tests.Ai;

public class ModelRefTests
{
    [Theory]
    [InlineData("groq:llama-3.3-70b-versatile", "groq", "llama-3.3-70b-versatile")]
    [InlineData("groq:openai/gpt-oss-120b", "groq", "openai/gpt-oss-120b")]
    [InlineData(" Gemini : gemini-3.8-flash ", "gemini", "gemini-3.8-flash")]
    public void Parse_SplitsOnFirstColon(string id, string provider, string model)
    {
        var parsed = ModelRef.Parse(id);

        parsed.Provider.Should().Be(provider);
        parsed.Model.Should().Be(model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("llama")]
    [InlineData(":llama")]
    [InlineData("groq:")]
    public void Parse_RejectsMalformedIds(string id)
    {
        var act = () => ModelRef.Parse(id);

        act.Should().Throw<FormatException>();
    }
}
