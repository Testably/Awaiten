using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	// A resolution key is a compile-time constant carried on [Key]/[FromKey]. It is stored internally as a
	// canonical, kind-prefixed string so that constants of different kinds never unify: the enum PaymentProvider.A,
	// the string "A" and a typeof(A) encode distinctly. The prefixes also fence user keys off from the synthetic
	// resolution keys (__ctx:/__dec:), which is what makes their "unlikely to collide" guard an actual guarantee.
	// The encoding is internal: it is decoded back to the user-written literal for emission (KeyLiteral) and to the
	// user-written form for diagnostics (KeyDisplay), so a string-keyed container's generated code is unchanged.
	private const string StringKeyPrefix = "str|";
	private const string EnumKeyPrefix = "enum|";
	private const string TypeKeyPrefix = "type|";

	/// <summary>
	///     Encodes a <c>[Key]</c>/<c>[FromKey]</c> constant into its canonical internal key, or <see langword="null" />
	///     when the argument is absent, null, or an unsupported constant kind (a numeric/char/bool constant is treated
	///     as no key, exactly as a non-string key was before typed keys were supported). Strings, enum values and
	///     <c>typeof(...)</c> are supported.
	/// </summary>
	private static string? EncodeKeyConstant(TypedConstant constant)
	{
		if (constant.IsNull)
		{
			return null;
		}

		switch (constant.Kind)
		{
			case TypedConstantKind.Primitive when constant.Value is string value:
				return StringKeyPrefix + value;
			case TypedConstantKind.Enum when constant.Type is INamedTypeSymbol enumType:
				string enumTypeName = enumType.ToDisplayString(FullyQualified);
				string memberName = EnumMemberName(enumType, constant.Value);
				return $"{EnumKeyPrefix}{enumTypeName}|{constant.Value}|{memberName}";
			case TypedConstantKind.Type when constant.Value is INamedTypeSymbol typeValue:
				return TypeKeyPrefix + typeValue.ToDisplayString(FullyQualified);
			default:
				return null;
		}
	}

	/// <summary>
	///     Reads the <c>Key</c> named argument of a lifetime attribute as its canonical internal key (see
	///     <see cref="EncodeKeyConstant" />), or <see langword="null" /> when unset.
	/// </summary>
	private static string? NamedKeyArgument(AttributeData attribute)
	{
		foreach (System.Collections.Generic.KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Key")
			{
				return EncodeKeyConstant(argument.Value);
			}
		}

		return null;
	}

	// The simple member name of the enum constant with the given underlying value, or "" for a value with no named
	// member (e.g. an unnamed flags combination). Included in the encoding so diagnostics and emission can render the
	// user-written PaymentProvider.Stripe. Two aliased members with the same underlying value resolve to the first and
	// so unify into one key, which is correct: they are Equals-equal at runtime and select the same registration.
	private static string EnumMemberName(INamedTypeSymbol enumType, object? value)
	{
		foreach (ISymbol member in enumType.GetMembers())
		{
			if (member is IFieldSymbol { HasConstantValue: true, IsConst: true, } field && Equals(field.ConstantValue, value))
			{
				return field.Name;
			}
		}

		return string.Empty;
	}

	// A key constant that is present (non-null) but of a type EncodeKeyConstant does not support (a numeric, char or
	// bool constant): it would otherwise be silently treated as no key, so the read sites report AWT170 instead.
	private static bool IsUnsupportedKeyConstant(TypedConstant constant)
		=> !constant.IsNull && EncodeKeyConstant(constant) is null;

	private static string KeyConstantTypeDisplay(TypedConstant constant)
		=> constant.Type is { } type ? Display(type.ToDisplayString(FullyQualified)) : "?";

	/// <summary>Reports AWT170 when a <c>[FromKey]</c> carries a constant whose type is not a supported key type.</summary>
	private static void ReportUnsupportedFromKey(ImmutableArray<AttributeData> attributes, LocationInfo? location, string ownerDisplay, List<DiagnosticInfo> diagnostics)
	{
		if (TryGetAwaitenAttribute(attributes, "FromKeyAttribute", out AttributeData? attribute)
		    && attribute!.ConstructorArguments.Length == 1
		    && IsUnsupportedKeyConstant(attribute.ConstructorArguments[0]))
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.UnsupportedKeyType, location,
				new EquatableArray<string>([ownerDisplay, KeyConstantTypeDisplay(attribute.ConstructorArguments[0]),])));
		}
	}

	/// <summary>Reports AWT170 when a lifetime attribute's <c>Key</c> argument is a constant of an unsupported key type.</summary>
	private static void ReportUnsupportedRegistrationKey(AttributeData attribute, LocationInfo? location, string ownerDisplay, List<DiagnosticInfo> diagnostics)
	{
		foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
		{
			if (argument.Key == "Key" && IsUnsupportedKeyConstant(argument.Value))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.UnsupportedKeyType, location,
					new EquatableArray<string>([ownerDisplay, KeyConstantTypeDisplay(argument.Value),])));
			}
		}
	}

	/// <summary>Whether an internal key is a user-written key rather than a synthetic (<c>__ctx:</c>/<c>__dec:</c>) one.</summary>
	private static bool IsUserKey(string? key)
		=> key is not null
		   && (key.StartsWith(StringKeyPrefix, StringComparison.Ordinal)
		       || key.StartsWith(EnumKeyPrefix, StringComparison.Ordinal)
		       || key.StartsWith(TypeKeyPrefix, StringComparison.Ordinal));

	/// <summary>The fully-qualified enum type of an enum-kind key, or <see langword="null" /> for any other key kind.</summary>
	private static string? EnumKeyType(string key)
		=> key.StartsWith(EnumKeyPrefix, StringComparison.Ordinal)
			? key.Substring(EnumKeyPrefix.Length, key.IndexOf('|', EnumKeyPrefix.Length) - EnumKeyPrefix.Length)
			: null;

	/// <summary>Whether an internal key is a string key.</summary>
	private static bool IsStringKey(string key) => key.StartsWith(StringKeyPrefix, StringComparison.Ordinal);

	/// <summary>
	///     Decodes an internal key back to the C# literal expression that produced it: a quoted string, an enum member
	///     access (or a cast for an unnamed value), or a <c>typeof(...)</c>. Used to emit dictionary key literals and
	///     the object key forwarded to an external keyed provider.
	/// </summary>
	internal static string KeyLiteral(string key)
	{
		if (key.StartsWith(StringKeyPrefix, StringComparison.Ordinal))
		{
			return SymbolDisplay.FormatLiteral(key.Substring(StringKeyPrefix.Length), quote: true);
		}

		if (key.StartsWith(TypeKeyPrefix, StringComparison.Ordinal))
		{
			return $"typeof({key.Substring(TypeKeyPrefix.Length)})";
		}

		if (key.StartsWith(EnumKeyPrefix, StringComparison.Ordinal))
		{
			(string enumType, string value, string member) = DecodeEnumKey(key);
			return member.Length > 0 ? $"{enumType}.{member}" : $"({enumType})({value})";
		}

		// A synthetic key is never emitted as a literal; fall back to a string literal defensively.
		return SymbolDisplay.FormatLiteral(key, quote: true);
	}

	/// <summary>
	///     Renders an internal key in the user-written form for diagnostics: the bare string, the enum member access
	///     (or the underlying value for an unnamed value), or <c>typeof(...)</c>.
	/// </summary>
	private static string KeyDisplay(string key)
	{
		if (key.StartsWith(StringKeyPrefix, StringComparison.Ordinal))
		{
			return key.Substring(StringKeyPrefix.Length);
		}

		if (key.StartsWith(TypeKeyPrefix, StringComparison.Ordinal))
		{
			return $"typeof({Display(key.Substring(TypeKeyPrefix.Length))})";
		}

		if (key.StartsWith(EnumKeyPrefix, StringComparison.Ordinal))
		{
			(string enumType, string value, string member) = DecodeEnumKey(key);
			return $"{Display(enumType)}.{(member.Length > 0 ? member : value)}";
		}

		return key;
	}

	// Splits an enum|<fully-qualified-type>|<underlying-value>|<member-name> key. Neither the type, the value nor the
	// member name contains a '|', so a bounded split is unambiguous.
	private static (string EnumType, string Value, string Member) DecodeEnumKey(string key)
	{
		string[] parts = key.Split('|');
		return (parts[1], parts[2], parts.Length > 3 ? parts[3] : string.Empty);
	}

	/// <summary>
	///     The C# key type to synthesize a keyed dictionary under for a requested key type symbol: the <c>string</c>
	///     keyword for <c>System.String</c> (byte-identical to the pre-typed-keys emission), the fully-qualified name
	///     for an enum, or <see langword="null" /> for any other type (which stays AWT159-unsupported).
	/// </summary>
	private static string? SupportedDictionaryKeyType(ITypeSymbol keyType)
	{
		if (keyType.SpecialType == SpecialType.System_String)
		{
			return "string";
		}

		return keyType.TypeKind == TypeKind.Enum ? keyType.ToDisplayString(FullyQualified) : null;
	}

	/// <summary>
	///     The C# key type stored on a keyed-collection dependency for emission: the supported form (<c>string</c> or a
	///     fully-qualified enum) when the requested key type is supported, otherwise its plain fully-qualified name so
	///     an (AWT159-rejected) unsupported request still emits well-formed code rather than crashing the emitter.
	/// </summary>
	private static string DictionaryKeyTypeDisplay(ITypeSymbol keyType)
		=> SupportedDictionaryKeyType(keyType) ?? keyType.ToDisplayString(FullyQualified);

	/// <summary>
	///     The C# key type of the by-type dictionary synthesized from a service's keyed members: the <c>string</c>
	///     keyword when every member has a string key, the fully-qualified enum type when every member is a constant of
	///     that one enum, or <see langword="null" /> when the members mix key kinds (no coherent by-type dictionary, so
	///     the by-type dispatch entry is skipped; an injection request is caught as AWT159 at the consumer).
	/// </summary>
	private static string? DictionaryKeyType(KeyedMember[] members)
	{
		if (members.Length > 0 && members.All(member => IsStringKey(member.Key)))
		{
			return "string";
		}

		string? enumType = EnumKeyType(members[0].Key);
		return enumType is not null && members.All(member => EnumKeyType(member.Key) == enumType) ? enumType : null;
	}
}
