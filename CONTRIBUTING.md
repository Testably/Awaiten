# Contributor guide

## Pull requests

**Pull requests are welcome!**  
Please include a clear description of the changes you have made with your request; the title should follow
the [conventional commits](https://www.conventionalcommits.org/en/v1.0.0/) guideline.
All code should be covered by unit tests and comply with the coding guideline in this project.

## Building and testing

`dotnet build Awaiten.slnx` builds everything; `./build.ps1 UnitTests` (or `./build.sh UnitTests`) runs the tests
like the CI.

The tests run on [TUnit](https://tunit.dev) as Microsoft.Testing.Platform executables, which `global.json` selects for
`dotnet test`:

```shell
dotnet test --project Tests/Awaiten.Tests -f net10.0
dotnet test --project Tests/Awaiten.Tests -f net10.0 --treenode-filter "/*/*/ScanTests/*"
```
