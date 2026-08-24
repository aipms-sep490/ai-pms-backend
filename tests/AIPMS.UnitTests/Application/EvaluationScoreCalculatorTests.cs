using AIPMS.Application.Features.Evaluations;
using Xunit;
namespace AIPMS.UnitTests.Application;
public sealed class EvaluationScoreCalculatorTests
{
 [Fact] public void Calculates_weighted_total_deterministically(){var c=new EvaluationScoreCalculator();var x=new[]{new ScoreInput(8,10,40),new ScoreInput(9,10,30),new ScoreInput(7,10,30)};Assert.Equal(8m,c.Calculate(x));Assert.Equal(c.Calculate(x),c.Calculate(x));}
 [Fact] public void Rounds_away_from_zero_to_two_decimals(){var c=new EvaluationScoreCalculator();Assert.Equal(8.01m,c.Calculate([new ScoreInput(80.125m,100m,100m)]));}
}
