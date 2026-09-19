using System.Runtime.CompilerServices;

// Commands are internal - only CanvasStore creates them - so the tests can assert on them directly.
[assembly: InternalsVisibleTo("Ben.Canvas.Tests")]
