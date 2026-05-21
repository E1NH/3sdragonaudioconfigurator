namespace DragonOS.AudioConfigurator.Core.Models;

/// <summary>
/// A snapshot of progress for a long-running service operation, designed to be
/// surfaced through <see cref="IProgress{T}"/> callbacks to the UI layer.
/// </summary>
/// <param name="Message">
/// A short, present-tense description of the current sub-step (e.g. "Downloading driver...").
/// </param>
/// <param name="PercentComplete">
/// A value in the range [0, 100] representing overall completion of the enclosing operation.
/// Use <c>-1</c> to signal indeterminate progress (maps to <c>IsIndeterminate</c> in the UI).
/// </param>
public sealed record ProgressReport(string Message, double PercentComplete)
{
    /// <summary>
    /// Convenience factory for indeterminate progress — use when total work is unknown
    /// (e.g. waiting for a process to exit).
    /// </summary>
    /// <param name="message">The message to display.</param>
    public static ProgressReport Indeterminate(string message) => new(message, -1);

    /// <summary>
    /// <see langword="true"/> if <see cref="PercentComplete"/> is <c>-1</c>,
    /// meaning total work cannot be estimated.
    /// </summary>
    public bool IsIndeterminate => PercentComplete < 0;
}
