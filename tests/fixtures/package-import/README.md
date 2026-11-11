# Package import fixture

`small.unitypackage` is a 350-byte package exported by Unity 6000.3.28f1 on
Windows during Issue #559 verification. It contains
`Assets/Scenes/Generated/E2E/PackageE2E/actual.txt` with the text
`real Unity export`, plus its Unity metadata.

Use an isolated project when importing it. The source, hash, exact CLI results
and reproducible runner are documented in
[the verification evidence](../../../docs/verification/issue-559.md).
