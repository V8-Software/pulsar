using System.Globalization;

namespace TestGiveMeSpace.Core;

public sealed record GuardDisplayTexts(
    string CountdownFormat,
    string Running,
    string ConfirmStop)
{
    public string CountdownText(int seconds)
        => string.Format(CultureInfo.InvariantCulture, CountdownFormat, seconds);

    public static GuardDisplayTexts For(GuardPurpose purpose)
        => purpose switch
        {
            GuardPurpose.ObserveWindows => new(
                "Изучение состояния окон, клик для отмены: {0}",
                "Изучение состояния окон",
                "Остановить изучение? Клик для остановки."),
            _ => new(
                "Тест забирает управление, клик для отмены: {0}",
                "Идёт тестирование...",
                "Остановить тестирование?"),
        };
}
