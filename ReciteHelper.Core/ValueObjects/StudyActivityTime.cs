using System.Text.Json.Serialization;

namespace ReciteHelper.Core.ValueObjects;

public readonly record struct StudyActivityTime(double ForegroundSeconds, double ActiveSeconds)
{
    [JsonIgnore]
    public double? Efficiency => ForegroundSeconds > 0
        ? Math.Clamp(ActiveSeconds / ForegroundSeconds, 0, 1) : null;

    public static StudyActivityTime operator +(StudyActivityTime left, StudyActivityTime right)
        => new(left.ForegroundSeconds + right.ForegroundSeconds, left.ActiveSeconds + right.ActiveSeconds);

    public static StudyActivityTime FromInterval(double elapsedSeconds, double activeRemainingSeconds)
        => elapsedSeconds > 0 && elapsedSeconds <= 5
            ? new(elapsedSeconds, Math.Clamp(activeRemainingSeconds, 0, elapsedSeconds))
            : default;
}
