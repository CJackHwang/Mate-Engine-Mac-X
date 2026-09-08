using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Pipeline.Compilation;
using UnityEngine;

namespace Unity.Pipeline.HotReload
{
    /// <summary>
    /// Main orchestrator for in-place hot reload processing.
    /// Handles the complete pipeline: source parsing -> transformation -> compilation.
    /// </summary>
    static class InPlaceReloadProcessor
    {
        /// <summary>
        /// Process a source file for in-place hot reload.
        /// Extracts [HotReloadWithOverrides] methods, transforms to static overrides, and compiles.
        /// DEADLOCK FIX: Detects main thread and uses synchronous processing when needed.
        /// </summary>
        /// <param name="sourceFilePath">Path to source file containing [HotReloadWithOverrides] methods</param>
        /// <param name="assemblyDir">Optional directory to save compiled assembly</param>
        /// <param name="pdb">Emit debug symbols mapped to the original source so breakpoints bind.</param>
        /// <returns>Compilation result with success/failure and diagnostic information</returns>
        public static Task<InPlaceReloadResult> ProcessSourceFileAsync(string sourceFilePath, string assemblyDir = null, bool pdb = false, bool useInterpreter = false)
        {
            // Runs synchronously on the calling (main) thread; returns a completed Task.
            return Task.FromResult(ProcessSourceFileOnMainThread(sourceFilePath, assemblyDir, pdb, useInterpreter));
        }

        /// <summary>
        /// Process source file on main thread synchronously to avoid deadlocks.
        /// </summary>
        /// <param name="sourceFilePath">Path to source file containing [HotReloadWithOverrides] methods.</param>
        /// <param name="assemblyDir">Optional directory to save compiled assembly.</param>
        /// <param name="pdb">Emit debug symbols mapped to the original source so breakpoints bind.</param>
        /// <param name="useInterpreter">Route the override through the interpreter backend instead of Assembly.Load.</param>
        /// <returns>Compilation result with success/failure and diagnostic information.</returns>
        public static InPlaceReloadResult ProcessSourceFileOnMainThread(string sourceFilePath, string assemblyDir = null, bool pdb = false, bool useInterpreter = false)
        {
            var result = new InPlaceReloadResult
            {
                SourceFilePath = sourceFilePath,
                Success = false
            };

            try
            {
                Debug.Log($"HotReload: Processing source file synchronously on main thread: {sourceFilePath}");

                // The interpreter backend needs #line directives when source-line errors are
                // enabled: its runtime errors resolve locations from the embedded PDB, which are
                // only meaningful mapped to the user's file.
                var lineInfo = useInterpreter && HotReloadCompiler.EmitSourceLineInfo;
                var prep = PrepareOverride(sourceFilePath, pdb || lineInfo, enforcePublicAccess: !useInterpreter);
                if (!prep.Success)
                {
                    result.ErrorMessage = prep.Error;
                    result.CompilationDiagnostics = prep.Diagnostics;
                    return result;
                }

                result.OriginalTypeName = prep.TypeName;
                result.ExtractedMethods = prep.MethodNames;
                result.UpToDateMethods = prep.UpToDateMethods;
                result.TransformedCode = prep.TransformedCode;

                // A method that matches the baseline again (the edit was undone) may still carry an
                // override from an earlier reload — drop it so the compiled body runs at full speed.
                foreach (var name in prep.UpToDateMethods)
                {
                    var id = $"{prep.TypeName}.{name}";
                    if (HotReloadRegistry.UnregisterMethodOverride(id))
                        result.RevertedMethods.Add(id);
                }

                if (prep.MethodNames.Count == 0)
                {
                    result.Success = true;
                    result.CompilationDiagnostics = prep.Diagnostics;
                    Debug.Log($"HotReload: {sourceFilePath} is up to date — all " +
                        $"{prep.UpToDateMethods.Count} [HotReload] method(s) match the compiled baseline" +
                        (result.RevertedMethods.Count > 0
                            ? $"; removed {result.RevertedMethods.Count} stale override(s)" : "") + ".");
                    return result;
                }

                var compilationResult = CompileTransformedCode(prep, assemblyDir, pdb, sourceFilePath, useInterpreter);

                result.Success = compilationResult.IsSuccess;
                result.AssemblyName = compilationResult.AssemblyName;
                result.RegisteredMethods = compilationResult.RegisteredMethods;
                result.CompilationDiagnostics = prep.Diagnostics.Concat(compilationResult.Diagnostics).ToList();

                if (result.Success)
                {
                    Debug.Log($"HotReload: In-place reload successful for {sourceFilePath} - {result.RegisteredMethods.Count} methods registered" +
                        (result.UpToDateMethods.Count > 0 ? $" ({result.UpToDateMethods.Count} up to date, left compiled)" : ""));
                }
                else
                {
                    result.ErrorMessage = compilationResult.ErrorDetails ?? "Compilation failed";
                    Debug.LogError($"HotReload: In-place reload compilation failed: {result.ErrorMessage}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Error processing source file {sourceFilePath}: {ex.Message}\n{ex.StackTrace}");

                result.ErrorMessage = $"Processing error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// What <see cref="PrepareOverride"/> hands back: the extracted type and surviving methods,
        /// the transformed source (null when everything was up to date), and the diagnostics
        /// gathered on the way. <see cref="Success"/> false means a hard failure with
        /// <see cref="Error"/> set.
        /// </summary>
        private sealed class OverridePreparation
        {
            public bool Success;
            public string Error;
            public string TypeName;
            public List<string> MethodNames;
            public List<string> UpToDateMethods = new List<string>();
            public string TransformedCode;
            public List<string> Diagnostics = new List<string>();
        }

        /// <summary>
        /// Shared prefix for in-place reload and push-to-player: read the file, extract [HotReload]
        /// methods, drop the ones still matching the captured <see cref="HotReloadBaseline"/> (they
        /// run correct AND faster compiled — <see cref="OverridePreparation.UpToDateMethods"/>
        /// reports them, and <paramref name="forceInclude"/> keeps named ones in anyway), reject
        /// non-public member access when the compiled backend will run the override
        /// (<paramref name="enforcePublicAccess"/>), and transform bodies into a static override
        /// class. When every method is up to date, succeeds with an empty
        /// <see cref="OverridePreparation.MethodNames"/> and a null
        /// <see cref="OverridePreparation.TransformedCode"/> — nothing to compile.
        /// </summary>
        private static OverridePreparation PrepareOverride(
            string sourceFilePath, bool pdb, bool enforcePublicAccess,
            string[] preprocessorSymbols = null, ICollection<string> forceInclude = null,
            bool useBaseline = true)
        {
            var prep = new OverridePreparation();

            var sourceCode = ReadSourceFile(sourceFilePath);
            if (string.IsNullOrEmpty(sourceCode))
            {
                prep.Error = $"Could not read source file: {sourceFilePath}";
                return prep;
            }

            var extraction = ExtractHotReloadableMethods(sourceCode, preprocessorSymbols);
            // Each skip entry carries its own reason (return-type gate, nested type, …).
            foreach (var skipped in extraction.SkippedMethods)
                prep.Diagnostics.Add($"[HotReload] {skipped} was skipped.");

            if (!extraction.HasMethods)
            {
                prep.Error = extraction.SkippedMethods.Count > 0
                    ? $"No reloadable [HotReload] methods in {sourceFilePath}. Skipped: " +
                      $"{string.Join(", ", extraction.SkippedMethods)}."
                    : $"No [HotReload] methods found in {sourceFilePath}";
                return prep;
            }

            // Skip methods whose declarations still match the captured baseline: their compiled
            // bodies are already what the source says, and the compiled body is strictly faster
            // than any override (the interpreter backend pays per call). No baseline captured
            // (returns null) = no filtering, the pre-baseline behavior.
            var unchanged = useBaseline
                ? HotReloadBaseline.GetUnchangedMethods(sourceFilePath, sourceCode, preprocessorSymbols)
                : null;
            if (unchanged != null && unchanged.Count > 0)
            {
                foreach (var name in extraction.Methods.Keys.ToList())
                {
                    if (!unchanged.Contains(name)) continue;
                    if (forceInclude != null && forceInclude.Contains(name)) continue;
                    extraction.Methods.Remove(name);
                    extraction.MethodSignatures.Remove(name);
                    prep.UpToDateMethods.Add(name);
                }

                if (extraction.Methods.Count == 0)
                {
                    prep.TypeName = extraction.TypeName;
                    prep.MethodNames = new List<string>();
                    prep.Success = true;
                    return prep;
                }
            }

            // Build one compilation + semantic model for the transform, so Roslyn's metadata bind
            // is paid once per reload (shared with the string TransformMethodBodies overload).
            var model = SourceCodeTransformer.BuildSemanticModel(
                sourceCode, extraction.TypeName, out var classDecl, preprocessorSymbols);
            if (classDecl == null)
            {
                prep.Error = $"Could not find class '{extraction.TypeName}' in source.";
                return prep;
            }

            // Only the interpreter backend compiles with relaxed accessibility (see
            // RoslynCompilationService.AllowNonPublicMemberAccess) and reaches non-public members at
            // runtime, via reflection. The Assembly.Load backend keeps standard access checks — a
            // non-public access would only surface later as a raw CS0122 — so reject it up front
            // with an actionable message.
            if (enforcePublicAccess)
            {
                var validation = AccessibilityValidator.ValidatePublicAccess(
                    model, classDecl, extraction.Methods, extraction.TypeName);
                if (!validation.IsValid)
                {
                    prep.Error = validation.GetFormattedErrorMessage();
                    return prep;
                }
            }

            // enforcePublicAccess doubles as the backend discriminator (true == Assembly.Load, see
            // the callers): only the interpreter needs interpolation lowered to string.Format —
            // the compiled backend keeps interpolations native.
            prep.TransformedCode = SourceCodeTransformer.TransformMethodBodies(
                model, classDecl, extraction.Methods, extraction.TypeName, extraction.MethodSignatures,
                emitLineDirectives: pdb, originalFilePath: sourceFilePath,
                rewriteInterpolations: !enforcePublicAccess);

            prep.TypeName = extraction.TypeName;
            prep.MethodNames = extraction.Methods.Keys.ToList();
            prep.Success = true;
            return prep;
        }

        /// <summary>
        /// Compile a source file's [HotReload] override(s) to raw assembly IL bytes without
        /// registering them locally — for pushing to a player that runs them through the
        /// interpreter. Editor-side only (needs Roslyn). <paramref name="useBaseline"/> false
        /// bypasses the <see cref="HotReloadBaseline"/> filter — the caller can't vouch that the
        /// target player was built from the baselined sources.
        /// </summary>
        internal static PushCompileResult CompileOverrideForPush(string sourceFilePath, string[] preprocessorSymbols = null,
            ICollection<string> forceInclude = null, bool useBaseline = true)
        {
            var r = new PushCompileResult();
            try
            {
                // When source-line errors are enabled, #line directives map each body to the
                // original file so the embedded PDB's sequence points — used by the player-side
                // interpreter for runtime error messages — carry the user's real line numbers.
                // enforcePublicAccess: false — pushed overrides run through the on-device
                // interpreter, which reaches non-public members by reflection.
                var prep = PrepareOverride(sourceFilePath, pdb: HotReloadCompiler.EmitSourceLineInfo,
                        enforcePublicAccess: false, preprocessorSymbols, forceInclude, useBaseline);
                if (!prep.Success)
                {
                    r.Error = prep.Error;
                    r.Diagnostics = prep.Diagnostics;
                    return r;
                }

                r.UpToDateMethods = prep.UpToDateMethods;
                if (prep.MethodNames.Count == 0)
                {
                    // Everything matches the baseline — a push would only replace compiled bodies
                    // with slower interpreted ones on the device.
                    r.Success = true;
                    r.TypeName = prep.TypeName;
                    r.Diagnostics = prep.Diagnostics;
                    return r;
                }

                var tempName = $"PushReload_{prep.TypeName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                if (!HotReloadCompiler.TryCompileOverrideBytes(prep.TransformedCode, tempName, out var il, out var compileDiagnostics, documentPath: sourceFilePath, preprocessorSymbols: preprocessorSymbols))
                {
                    r.Error = "Compilation failed";
                    r.Diagnostics = prep.Diagnostics.Concat(compileDiagnostics).ToList();
                    return r;
                }

                r.Success = true;
                r.TypeName = prep.TypeName;
                r.MethodNames = prep.MethodNames;
                r.IlBytes = il;
                r.Diagnostics = prep.Diagnostics.Concat(compileDiagnostics).ToList();
                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"Push compile error: {ex.Message}";
                return r;
            }
        }

        /// <summary>
        /// Check if a source file contains [HotReload] methods.
        /// Simple synchronous check to avoid async deadlocks in tests.
        /// </summary>
        /// <param name="sourceFilePath">Path to source file to check</param>
        /// <returns>True if file contains [HotReload] methods</returns>
        public static Task<bool> ContainsHotReloadableMethodsAsync(string sourceFilePath)
        {
            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    return Task.FromResult(false);
                }

                // Use synchronous read for simple attribute check to avoid deadlocks
                var sourceCode = File.ReadAllText(sourceFilePath);
                if (string.IsNullOrEmpty(sourceCode))
                {
                    return Task.FromResult(false);
                }

                // Quick check for the [HotReload] attribute. Prefix match (no closing bracket) so
                // combined attribute lists like [HotReload, DebugPanel("x")] still pass.
                var result = sourceCode.Contains("[HotReload");
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Error checking for [HotReload] methods in {sourceFilePath}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Read source file content synchronously (for main thread).
        /// </summary>
        private static string ReadSourceFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"HotReload: Source file not found: {filePath}");
                    return null;
                }

                return File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Error reading source file {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract [HotReloadWithOverrides] methods from source code.
        /// </summary>
        private static HotReloadableExtractionResult ExtractHotReloadableMethods(
            string sourceCode, string[] preprocessorSymbols = null)
        {
            var result = new HotReloadableExtractionResult();

            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode,
                    RoslynCompilationService.ProjectParseOptions(preprocessorSymbols));
                var root = syntaxTree.GetRoot();

                // Find the class containing [HotReload] methods.
                var classDeclaration = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(HasHotReloadAttribute));

                if (classDeclaration == null)
                {
                    return result;
                }

                result.TypeName = classDeclaration.Identifier.ValueText;

                // The transform lifts bodies into ONE override class for the top-level type chosen
                // above. Tagged methods anywhere else in the file — in a NESTED type (which the
                // weaver still weaves: Cecil's GetTypes() is recursive) or in a sibling top-level
                // type — would otherwise be dropped without a word, leaving a woven prologue that
                // never finds an override. Surface each one loudly; when nothing else is
                // reloadable, these become the reload's error.
                foreach (var method in root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>().Where(HasHotReloadAttribute))
                {
                    var owner = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    if (owner == null || owner == classDeclaration)
                        continue;
                    var ownerChain = string.Join(".", method.Ancestors()
                        .OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.ValueText));
                    result.SkippedMethods.Add(owner.Parent is TypeDeclarationSyntax
                        ? $"'{method.Identifier.ValueText}' (declared in nested type '{ownerChain}' — " +
                          "hot reload only supports methods declared directly in a top-level class)"
                        : $"'{method.Identifier.ValueText}' (declared in '{ownerChain}' — only the first " +
                          $"type with [HotReload] methods, '{result.TypeName}', is reloaded per file; split the file)");
                }

                var hotReloadableMethods = classDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where(HasHotReloadAttribute);

                foreach (var method in hotReloadableMethods)
                {
                    var methodName = method.Identifier.ValueText;

                    // Tagged methods must pass the same gate the weaver applies (void or
                    // IEnumerator returns; instance or static). The weaver never weaves a
                    // dispatch prologue into the others, so their override would register and
                    // then never run — reload would report success while silently no-opping.
                    if (!IsMethodLevelSupported(method, out var skipReason))
                    {
                        result.SkippedMethods.Add($"'{methodName}' ({skipReason})");
                        continue;
                    }
                    var methodBody = ExtractMethodBody(method);
                    var signature = ExtractMethodSignature(method);

                    if (methodBody != null)
                    {
                        result.Methods[methodName] = methodBody;
                        result.MethodSignatures[methodName] = signature;
                    }
                }

                Debug.Log($"HotReload: Extracted {result.Methods.Count} [HotReload] methods from class {result.TypeName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Error extracting [HotReload] methods: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Check if method has [HotReload] attribute. The unqualified name must match exactly: a
        /// suffix match also catches [OnHotReload] — the post-reload callback, not a reload target —
        /// and pushing that name makes the player-side registry lookup fail for the whole file.
        /// Internal so <see cref="HotReloadBaseline"/> hashes exactly the methods extraction picks.
        /// </summary>
        internal static bool HasHotReloadAttribute(MethodDeclarationSyntax method)
        {
            return method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => UnqualifiedAttributeName(a) == "HotReload");
        }

        /// <summary>
        /// Attribute name as the source wrote it, minus any namespace qualifier and the optional
        /// "Attribute" suffix — [Foo.BarAttribute] and [Bar] both yield "Bar".
        /// </summary>
        private static string UnqualifiedAttributeName(AttributeSyntax attr)
        {
            var name = attr.Name.ToString();
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
                name = name.Substring(lastDot + 1);
            if (name.EndsWith("Attribute"))
                name = name.Substring(0, name.Length - "Attribute".Length);
            return name;
        }

        /// <summary>
        /// Gate for explicitly-tagged [HotReload] methods, mirroring the skips the weaver applies
        /// (HotReloadInPlaceILPostProcessor.Process): unsupported return types get no dispatch
        /// prologue, so extracting them would produce an override that never runs. Instance and
        /// STATIC methods both weave (a static dispatches with a null instance). Coroutines
        /// (System.Collections.IEnumerator) are supported: the weaver routes them through the
        /// result dispatch, and the override's iterator — compiled or interpreted — is what
        /// StartCoroutine drives.
        /// </summary>
        private static bool IsMethodLevelSupported(MethodDeclarationSyntax method, out string reason)
        {
            var returnType = method.ReturnType.ToString();
            if (returnType != "void" &&
                returnType is not ("IEnumerator" or "System.Collections.IEnumerator"))
            {
                reason = $"returns {returnType} — void methods and IEnumerator coroutines only";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Extract method body content (excluding braces).
        /// null when the method has no block body at all (expression-bodied, abstract).
        /// </summary>
        private static string ExtractMethodBody(MethodDeclarationSyntax method)
        {
            if (method.Body == null)
                return null;

            var bodyText = method.Body.ToString().Trim();
            // Remove outer braces
            if (bodyText.StartsWith("{") && bodyText.EndsWith("}"))
            {
                bodyText = bodyText.Substring(1, bodyText.Length - 2).Trim();
            }
            return bodyText;
        }

        /// <summary>
        /// Extract method signature information.
        /// </summary>
        private static MethodSignatureInfo ExtractMethodSignature(MethodDeclarationSyntax method)
        {
            var signature = new MethodSignatureInfo
            {
                ReturnType = method.ReturnType.ToString()
            };

            foreach (var parameter in method.ParameterList.Parameters)
            {
                var paramInfo = new ParameterInfo
                {
                    Type = parameter.Type?.ToString() ?? "object",
                    Name = parameter.Identifier.ValueText,
                    HasDefaultValue = parameter.Default != null,
                    DefaultValue = parameter.Default?.Value?.ToString()
                };

                signature.Parameters.Add(paramInfo);
            }

            return signature;
        }

        /// <summary>
        /// Compile transformed code synchronously (for main thread).
        /// </summary>
        private static HotReloadCompileResult CompileTransformedCode(
            OverridePreparation prep,
            string assemblyDir,
            bool emitPdb,
            string documentPath,
            bool useInterpreter)
        {
            try
            {
                // Generate a temporary file name for the transformed code
                var tempFileName = $"InPlace_{prep.TypeName}_{DateTime.Now:yyyyMMdd_HHmmss}";

                return HotReloadCompiler.CompileSourceCodeOnMainThread(
                    prep.TransformedCode,
                    tempFileName,
                    assemblyDir,
                    emitPdb,
                    documentPath,
                    useInterpreter
                        ? new InterpreterOverrideSet
                        {
                            TypeName = prep.TypeName,
                            MethodNames = prep.MethodNames
                        }
                        : null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Error compiling transformed code for {prep.TypeName}: {ex.Message}");

                return HotReloadCompileResult.Failure(
                    "Compilation Error",
                    ex.Message,
                    0,
                    new List<string> { ex.ToString() });
            }
        }

        /// <summary>
        /// Result of extracting [HotReloadWithOverrides] methods from source code.
        /// </summary>
        private class HotReloadableExtractionResult
        {
            /// <summary>
            /// Name of the class containing [HotReloadWithOverrides] methods.
            /// </summary>
            public string TypeName { get; set; }

            /// <summary>
            /// Dictionary of method names to their extracted body code.
            /// </summary>
            public Dictionary<string, string> Methods { get; set; } = new Dictionary<string, string>();

            /// <summary>
            /// Dictionary of method names to their signature information.
            /// </summary>
            public Dictionary<string, MethodSignatureInfo> MethodSignatures { get; set; } = new Dictionary<string, MethodSignatureInfo>();

            /// <summary>
            /// Whether any [HotReloadWithOverrides] methods were found.
            /// </summary>
            public bool HasMethods => Methods.Count > 0;

            /// <summary>
            /// Explicitly-tagged methods that were rejected with the reason ("'Total' (returns
            /// int — void methods and IEnumerator coroutines only)"), so the caller can report
            /// them instead of failing opaquely.
            /// </summary>
            public List<string> SkippedMethods { get; } = new List<string>();
        }
    }

    /// <summary>
    /// Result of in-place hot reload processing.
    /// </summary>
    class InPlaceReloadResult
    {
        /// <summary>
        /// Path to the source file that was processed.
        /// </summary>
        public string SourceFilePath { get; set; }

        /// <summary>
        /// Whether the processing was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Name of the original type containing [HotReloadWithOverrides] methods.
        /// </summary>
        public string OriginalTypeName { get; set; }

        /// <summary>
        /// List of method names that were extracted and processed.
        /// </summary>
        public List<string> ExtractedMethods { get; set; } = new List<string>();

        /// <summary>
        /// Generated transformed code for hot reload assembly.
        /// </summary>
        public string TransformedCode { get; set; }

        /// <summary>
        /// Name of the compiled assembly (if successful).
        /// </summary>
        public string AssemblyName { get; set; }

        /// <summary>
        /// List of registered method IDs (if successful).
        /// </summary>
        public List<string> RegisteredMethods { get; set; } = new List<string>();

        /// <summary>
        /// [HotReload] method names skipped because they still match the compiled baseline —
        /// their compiled bodies keep running (no interpreter cost).
        /// </summary>
        internal List<string> UpToDateMethods { get; set; } = new List<string>();

        /// <summary>
        /// Method ids whose stale override was removed because the source matches the compiled
        /// baseline again (the edit was reverted).
        /// </summary>
        internal List<string> RevertedMethods { get; set; } = new List<string>();

        /// <summary>Every extracted method matched the baseline; nothing was compiled or registered.</summary>
        internal bool AllUpToDate => Success && ExtractedMethods.Count == 0 && UpToDateMethods.Count > 0;

        /// <summary>
        /// Error message if processing failed.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Compilation diagnostics (warnings, errors).
        /// </summary>
        public List<string> CompilationDiagnostics { get; set; } = new List<string>();

        /// <summary>
        /// Execution time in milliseconds.
        /// </summary>
        public long ExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// Result of compiling an override for pushing to a player (no local registration).
    /// </summary>
    internal class PushCompileResult
    {
        public bool Success { get; set; }
        public string TypeName { get; set; }
        public List<string> MethodNames { get; set; } = new List<string>();
        public byte[] IlBytes { get; set; }
        public string Error { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();

        /// <summary>[HotReload] method names skipped because they still match the compiled baseline.</summary>
        public List<string> UpToDateMethods { get; set; } = new List<string>();

        /// <summary>Every extracted method matched the baseline; there is nothing to push.</summary>
        public bool AllUpToDate => Success && MethodNames.Count == 0 && UpToDateMethods.Count > 0;
    }
}