using System.Collections;
using UnityEngine;

// COMPILED TWINS for the transform tests' source-string fixtures. The transformer decides
// "is this whole type NEW?" by looking the class up in the compilation's references
// (SourceCodeTransformer.FindCompiledType) — a type with no compiled twin gets every method
// co-emitted and its receiver parameter degraded to `object` (it exists in no reference).
// The tests' fixtures describe the NORMAL live-edit flow, where the type IS compiled in the
// running build, so their twins must exist here — with the same method names, or those
// methods would classify as newly added instead of overrides.
//
// Nothing instantiates these; only their metadata matters.
class Demo : MonoBehaviour
{
    public float speed = 1f;
    void Tick() { }
}

class CoDemo : MonoBehaviour
{
    public int step;
    IEnumerator Run() { yield return null; }
}
