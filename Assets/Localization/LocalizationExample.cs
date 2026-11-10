public static class LocalizationExample
{
    public static string RussianItemCount() =>
        new Strings("ru").ItemsCount(count: 3, name: "Анна");
}
