using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class GuardDisplayTextsTests
{
    [Fact]
    public void Test_purpose_uses_testing_texts()
    {
        GuardDisplayTexts texts = GuardDisplayTexts.For(GuardPurpose.Test);

        Assert.Equal("Тест забирает управление, клик для отмены: 10", texts.CountdownText(10));
        Assert.Equal("Идёт тестирование...", texts.Running);
        Assert.Equal("Остановить тестирование?", texts.ConfirmStop);
    }

    [Fact]
    public void Observe_windows_purpose_uses_window_observation_texts()
    {
        GuardDisplayTexts texts = GuardDisplayTexts.For(GuardPurpose.ObserveWindows);

        Assert.Equal("Изучение состояния окон, клик для отмены: 10", texts.CountdownText(10));
        Assert.Equal("Изучение состояния окон", texts.Running);
        Assert.Equal("Остановить изучение? Клик для остановки.", texts.ConfirmStop);
    }
}
