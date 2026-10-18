using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Tests;

public class DiagnosticDescriptorTests
{
	[Test]
	public async Task MessageFormats_DoNotStartWithALowercaseLetter()
	{
		Type diagnostics = typeof(AwaitenGenerator).Assembly.GetType("Awaiten.SourceGenerators.Diagnostics", true)!;
		DiagnosticDescriptor[] descriptors = diagnostics
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(DiagnosticDescriptor))
			.Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
			.ToArray();

		string[] lowercase = descriptors
			.Where(d => char.IsLower(d.MessageFormat.ToString()[0]))
			.Select(d => $"{d.Id}: {d.MessageFormat}")
			.ToArray();

		await That(descriptors).IsNotEmpty()
			.Because("the guard must actually see the descriptors of the Diagnostics class");
		await That(lowercase).IsEmpty()
			.Because("a message starts with an uppercase letter or a quoted placeholder, so messages of different ids read alike in the error list");
	}
}
