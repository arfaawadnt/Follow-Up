using System.Reflection;

namespace FollowUp.Domain.Common;

/// <summary>
/// Rich, type-safe enumeration base (a "smart enum"). Each value is a singleton carrying a stable
/// integer <see cref="Id"/> and a persistence-friendly string <see cref="Name"/>. Subclasses may add
/// behaviour (e.g. legal state transitions) — something a plain C# <c>enum</c> cannot express.
/// </summary>
public abstract class Enumeration : IComparable
{
    protected Enumeration(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }

    /// <summary>Stable string persisted to the database and used across the API/CHECK constraints.</summary>
    public string Name { get; }

    public override string ToString() => Name;

    public static IReadOnlyCollection<T> GetAll<T>() where T : Enumeration =>
        typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(f => f.GetValue(null))
            .OfType<T>()
            .ToArray();

    public static T FromName<T>(string name) where T : Enumeration =>
        GetAll<T>().FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"'{name}' is not a valid {typeof(T).Name}.");

    public static T FromId<T>(int id) where T : Enumeration =>
        GetAll<T>().FirstOrDefault(e => e.Id == id)
        ?? throw new DomainException($"'{id}' is not a valid {typeof(T).Name} id.");

    public static bool TryFromName<T>(string name, out T? value) where T : Enumeration
    {
        value = GetAll<T>().FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return value is not null;
    }

    public override bool Equals(object? obj) =>
        obj is Enumeration other && GetType() == other.GetType() && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public int CompareTo(object? obj) => Id.CompareTo(((Enumeration)obj!).Id);

    public static bool operator ==(Enumeration? a, Enumeration? b) => Equals(a, b);

    public static bool operator !=(Enumeration? a, Enumeration? b) => !Equals(a, b);
}
