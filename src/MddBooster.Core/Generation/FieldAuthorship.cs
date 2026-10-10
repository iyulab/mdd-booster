using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Types;

namespace MddBooster.Core.Generation;

/// <summary>
/// Who supplies a stored field's value — the one question every target with a write or an authoring surface asks, so
/// the registration, the forms, the field schema and the entity metadata cannot answer it differently.
/// </summary>
/// <remarks>
/// <para>
/// <c>@system</c> on a field says the application's own code writes it: a state a transition action moves, the user who
/// performed an action, a key a storage service returned. The value is stored and read like any other; what the
/// declaration removes is the two ways a caller could set it directly — a control on a generated form, and a property
/// in a generic write. It is the same word an enum value already carries (<c>@system</c>: written by the system, never
/// picked by a person), one grain up.
/// </para>
/// <para>
/// It is a different statement from <c>@immutable</c> (a caller supplies the value once, at creation) and from
/// <c>@internal</c> (the field is kept off the read surface altogether).
/// </para>
/// </remarks>
public static class FieldAuthorship
{
    /// <summary>The model declares the field <c>@system</c>.</summary>
    public static bool IsDeclaredSystem(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return FieldAttributes.Has(field, "system");
    }

    /// <summary>
    /// Nobody enters the value: the model declares it <c>@system</c>, or it is the row version the database engine sets.
    /// </summary>
    public static bool IsSystemWritten(FieldNode field)
        => IsDeclaredSystem(field) || M3lPrimitives.IsRowVersion(field);
}
