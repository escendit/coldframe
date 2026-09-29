namespace Coldframe.Contracts.Events;

/// <summary>
/// Converts one schema version of an event to the next (AD-21).
/// </summary>
/// <remarks>
/// <typeparamref name="TFrom"/> and <typeparamref name="TTo"/> carry the same
/// <see cref="EventTypeAttribute.Alias"/>, and the schema version of <typeparamref name="TTo"/> is
/// exactly one above that of <typeparamref name="TFrom"/>. The journal reads an old row as
/// <typeparamref name="TFrom"/> and runs every upcaster in the chain up to the newest version.
/// An upcaster is pure: it reads nothing but its argument, and never the clock.
/// </remarks>
/// <typeparam name="TFrom">The older contract.</typeparam>
/// <typeparam name="TTo">The contract one schema version newer.</typeparam>
public interface IEventUpcaster<in TFrom, out TTo>
    where TFrom : class
    where TTo : class
{
    /// <summary>
    /// Returns the event as the newer contract.
    /// </summary>
    TTo Upcast(TFrom source);
}
