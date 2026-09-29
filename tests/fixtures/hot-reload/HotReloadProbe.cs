using System;
using System.Runtime.CompilerServices;
using UnityEngine;

public class HotReloadProbe : MonoBehaviour
{
    public int hp = 73;
    public int score = 19;
    public int value;
    public static int staticState = 47;
    [NonSerialized] public string transientState = "preserved";

    private void Update() { value = Calculate(3); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Calculate(int input) { return input * 2; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int NeverCalled(int input) { return input + 1; }
}
