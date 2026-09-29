using System;
using UnityCliBridge.HotReload;
using Xunit;
public class PreviewValidationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void CompilationProofRequiresAssemblySuccessUnlessExplicitCleanBuild(bool cleanBuild, bool assemblySucceeded, bool expected)
    {
        var completed = new System.Collections.Generic.HashSet<string> { "Unrelated" };
        if (assemblySucceeded) completed.Add("Assembly-CSharp");
        Assert.Equal(expected, CompilationProof.CanCommit(cleanBuild, completed, "Assembly-CSharp"));
    }
    const string Baseline = "class Probe { int value; void Update() { value = 1; } int Read() { return value; } }";
    [Fact] public void BodyChangeIsIdentifiedAndInstrumented()
    {
        var change = PreviewValidation.Compare(Baseline, Baseline.Replace("value = 1", "value = 2"), "transaction");
        Assert.Equal(new[] { "Update" }, change.Methods);
        Assert.Contains("Observe(\"transaction\", \"Update\")", change.InstrumentedSource);
    }
    [Fact] public void TriviaOnlyChangesAreNoOp() => Assert.Empty(PreviewValidation.Compare(Baseline, "// comment\n" + Baseline, "t").Methods);
    [Fact] public void InternalCommentsAreNoOp() => Assert.Empty(PreviewValidation.Compare(Baseline, Baseline.Replace("int value;", "int /* trivia */ value;"), "t").Methods);
    [Fact] public void NamespaceMapsToCompiledType()
    {
        var source = "namespace Outer { namespace Inner { " + Baseline + " } }";
        Assert.Equal("Outer.Inner.Probe", PreviewValidation.Compare(source, source, "t").TypeName);
    }
    [Fact] public void AllChangedMethodsReceiveAnObservation()
    {
        var change = PreviewValidation.Compare(Baseline, Baseline.Replace("value = 1", "value = 2").Replace("return value", "return value + 1"), "t");
        Assert.Equal(new[] { "Update", "Read" }, change.Methods);
        Assert.Contains("Observe(\"t\", \"Read\")", change.InstrumentedSource);
    }
    [Theory]
    [InlineData("class Probe { int other; void Update() { } }")]
    [InlineData("class Probe { int value; void Update(int x) {} int Read() { return value; } }")]
    [InlineData("class Probe { int value; void Update() { value = ; } int Read() { return value; } }")]
    public void StructuralAndSyntaxChangesAreRejected(string source) => Assert.ThrowsAny<InvalidOperationException>(() => PreviewValidation.Compare(Baseline, source, "t"));
    [Fact] public void SyntaxFailureHasSpecificCode()
    {
        var error = Assert.ThrowsAny<InvalidOperationException>(() => PreviewValidation.Compare(Baseline, "class {", "t"));
        Assert.Equal("HOT_RELOAD_SYNTAX_ERROR", error.Data["code"]);
    }
    [Fact] public void CompilationProofRejectsBackdatedSourceChange()
    {
        var source = System.IO.Path.GetTempFileName();
        var assembly = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(source, Baseline);
            System.IO.File.WriteAllText(assembly, "compiled assembly");
            var proof = CompilationProof.Capture(new[] { source });
            var assemblyHash = CompilationProof.HashFile(assembly);
            Assert.True(CompilationProof.Matches(proof, source, assembly, assemblyHash));
            System.IO.File.WriteAllText(source, Baseline.Replace("int value", "long value"));
            System.IO.File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-10));
            Assert.False(CompilationProof.Matches(proof, source, assembly, assemblyHash));
        }
        finally { System.IO.File.Delete(source); System.IO.File.Delete(assembly); }
    }
    [Theory]
    [InlineData("partial class Probe { void Update() {} }")]
    [InlineData("class Probe<T> { void Update() {} }")]
    [InlineData("class Probe { void OnScriptHotReload() {} }")]
    [InlineData("class Probe { async void Update() {} }")]
    [InlineData("class Probe { void Update() { void Local() {} } }")]
    [InlineData("class Probe { System.Collections.IEnumerator Update() { yield return null; } }")]
    [InlineData("class Probe { int Value => 1; }")]
    [InlineData("class Probe { void Update() { System.Action a = () => {}; } }")]
    [InlineData("class Probe { void Update() {} void Update(int arg) {} }")]
    [InlineData("class Probe { Probe() {} void Update() {} }")]
    [InlineData("class Probe { class Nested {} }")]
    [InlineData("#if UNITY_EDITOR\nclass Probe { void Update() {} }\n#endif")]
    [InlineData("class Probe { void Update() {} } class Another {}")]
    public void UnsupportedBaselineIsRejected(string source) => Assert.ThrowsAny<InvalidOperationException>(() => PreviewValidation.Compare(source, source, "t"));
    [Fact] public void ObservationsAreTransactionScoped()
    {
        var observations = new ExecutionObservations("current", new[] { "Update", "Read" });
        observations.Observe("old", "Update"); Assert.Empty(observations.Observed());
        observations.Observe("current", "Update"); Assert.Equal(new[] { "Read" }, observations.Unverified());
        observations.Observe("current", "Read"); Assert.Empty(observations.Unverified());
        observations.Observe("current", "Unknown"); Assert.Equal(2, observations.Observed().Length);
    }
}
