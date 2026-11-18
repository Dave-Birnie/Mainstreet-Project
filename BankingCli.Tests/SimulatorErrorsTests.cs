using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks the plain-English explanations of the simulator's error codes.</summary>
public class SimulatorErrorsTests
{
    [Theory]
    [InlineData("ERROR INSUFFICIENT_FUNDS | savings=$6.00",
        "Savings only has $6.00, which isn't enough. Try a smaller amount, or move money into savings first. Nothing has been changed.")]
    [InlineData("ERROR SAME_ACCOUNT",
        "A transfer has to be between two different accounts. Name two different accounts, such as \"transfer savings chequing 5\". Nothing has been changed.")]
    [InlineData("ERROR INVALID_AMOUNT",
        "That amount isn't valid. Use a number above zero with at most two decimal places, such as 5 or 5.25, without a $ sign. Nothing has been changed.")]
    [InlineData("ERROR UNKNOWN_COMMAND",
        "I don't recognize that command. Type help to see the commands you can use.")]
    [InlineData("ERROR INVALID_ARGUMENTS",
        "That command has missing or extra words. Type help to see the format of each command, such as \"deposit savings 10\".")]
    public void Each_error_says_what_went_wrong_and_what_to_do(string simulatorResult, string expected) =>
        Assert.Equal(expected, SimulatorErrors.Explain(simulatorResult));

    [Fact]
    public void Successful_results_have_no_explanation() =>
        Assert.Null(SimulatorErrors.Explain("DEPOSIT $10.00 TO savings | savings=$10.00"));

    [Fact]
    public void The_model_gets_the_factual_meaning_of_a_code() =>
        Assert.Equal("A transfer must be between two different accounts.", SimulatorErrors.MeaningFor("SAME_ACCOUNT"));
}
