namespace FlatRatScamper;

internal sealed class SprintStamina
{
    private const double SprintSeconds = 10;
    private const double RecoveryDelay = 2;
    private const double RefillSeconds = 3;

    private double remaining = SprintSeconds;
    private double timeWithoutShift;

    internal float Fraction => (float)(remaining / SprintSeconds);

    internal bool Update(bool shiftHeld, bool moving, float elapsed)
    {
        if (shiftHeld)
        {
            timeWithoutShift = 0;
            if (!moving || remaining <= 0) return false;
            remaining = Math.Max(0, remaining - elapsed);
            return true;
        }

        double previouslyRecovering = Math.Max(0, timeWithoutShift - RecoveryDelay);
        timeWithoutShift = Math.Min(RecoveryDelay + RefillSeconds, timeWithoutShift + elapsed);
        double recovered = Math.Max(0, timeWithoutShift - RecoveryDelay) - previouslyRecovering;
        remaining = Math.Min(SprintSeconds, remaining + recovered * SprintSeconds / RefillSeconds);
        return false;
    }
}
