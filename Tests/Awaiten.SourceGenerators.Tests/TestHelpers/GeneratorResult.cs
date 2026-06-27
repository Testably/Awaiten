using System.Collections.Generic;

namespace Awaiten.SourceGenerators.Tests.TestHelpers;

public record GeneratorResult(Dictionary<string, string> Sources, string[] Diagnostics);
