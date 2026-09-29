# Editor evaluation compiler dependencies

These unmodified, pinned **netstandard2.0** binaries are downloaded from the
official NuGet flat-container URLs recorded in `dependencies.json`. The manifest
records both package and extracted DLL SHA-256 hashes. Extract the indicated DLL
from `lib/netstandard2.0/` to reproduce an update. Preserve the Editor-only plugin
importer metadata; these libraries must never enter a player build.

Roslyn Common/CSharp 4.8.0, Immutable/Metadata/CodePages 7.0.0 and Unsafe 6.0.0
are distributed under the included MIT licenses. Original package `.nuspec`
and third-party notices are retained. Roslyn source revision:
`e091728607ca0fc9efca55ccfb3e59259c6b5a0a`.

The supported Unity 6 runtime supplies System.Memory, System.Buffers,
System.Numerics.Vectors and System.Threading.Tasks.Extensions through its public
Mono/.NET Standard framework assemblies and facades. Do not add duplicate NuGet
implementations of these framework types. Microsoft.CodeAnalysis.Analyzers is a
build-time dependency and is not needed to execute the compiler. There is no
reference to Unity's private compiler/tooling directories.

Validation: Unity 6000.4.11f1 Editor tests compile and execute snippets through
the package assemblies. Re-run these tests when changing Unity or Roslyn versions.
