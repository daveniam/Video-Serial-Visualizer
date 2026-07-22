namespace VideoSerialVisualizer.Helpers;

public static class TimeFormatter
{
    public static string Format(double ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return time.Hours > 0
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }
}
