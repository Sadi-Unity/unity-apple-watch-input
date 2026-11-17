using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

// Nothing in a user's project references this assembly directly at first; the
// entry point is a [RuntimeInitializeOnLoadMethod]. Without this attribute the
// UnityLinker never processes the assembly and IL2CPP silently drops it from
// the player build.
[assembly: AlwaysLinkAssembly]
[assembly: InternalsVisibleTo("com.sadiakdemir.watch-input.tests.editor")]
