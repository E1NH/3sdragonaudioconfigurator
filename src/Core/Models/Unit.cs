namespace DragonOS.AudioConfigurator.Core.Models;

/// <summary>
/// Represents the absence of a meaningful return value in a result-oriented context.
/// Used as the type parameter for <see cref="OperationResult{T}"/> when an operation
/// succeeds but produces no data — the functional equivalent of <c>void</c>.
/// </summary>
/// <remarks>
/// C# lacks a built-in unit type. This struct fills that gap so that all service
/// methods can return a uniform <c>OperationResult&lt;T&gt;</c> regardless of whether
/// they produce a value, simplifying caller code and error propagation.
/// </remarks>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The single canonical instance of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = default;

    /// <inheritdoc/>
    public bool Equals(Unit other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override string ToString() => "()";

    /// <summary>All <see cref="Unit"/> values are equal.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>All <see cref="Unit"/> values are equal.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
