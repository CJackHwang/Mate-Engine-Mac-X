using System.Runtime.CompilerServices;

// Lets the EditMode test assembly use internal test helpers (PipelineClient, AttachByPathFixture, ...)
// now that this assembly's test types are internal rather than public (PVP-200-1).
[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Editor")]
