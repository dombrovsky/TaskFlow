#if !NET9_0_OR_GREATER
namespace System.Runtime.CompilerServices;

// Compiler-recognized metadata for consumers using C# 13+, including netstandard2.0/net8.0.
// Keep internal: this is a compiler compatibility shim, not a TaskFlow API.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
#endif