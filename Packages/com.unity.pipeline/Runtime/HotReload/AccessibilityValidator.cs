using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

namespace Unity.Pipeline.HotReload
{
    /// <summary>
    /// Accessibility gate for [HotReload] method bodies on the compiled (Assembly.Load) backend.
    ///
    /// The compiled backend cannot use non-public members of the target type: even if the override
    /// compiled with Roslyn's access checks relaxed, Unity's Mono enforces accessibility when
    /// JIT-ing the loaded override IL and does not honor <c>[assembly: IgnoresAccessChecksTo]</c>
    /// (verified on Mono 6.13) — the access would throw (e.g. <c>FieldAccessException</c>) on first
    /// dispatch and silently fall back to the original body. Compiled-backend overrides therefore
    /// compile with standard access checks (see
    /// <c>RoslynCompilationService.AllowNonPublicMemberAccess</c>, interpreter-only), and this
    /// validator runs up front — only when the compiled backend will execute the override — to turn
    /// the eventual raw CS0122 into a clear reload error. The interpreter backend reaches
    /// non-public members via reflection and must not be gated.
    /// </summary>
    static class AccessibilityValidator
    {
        /// <summary>
        /// Check that every [HotReload] method body only accesses public instance members of its
        /// declaring type. Runs over an already-built semantic model + class declaration, so the
        /// in-place reload path shares one compilation between validation and transformation
        /// instead of building two.
        /// </summary>
        /// <param name="model">The semantic model built over the source containing the declaring type.</param>
        /// <param name="classDecl">The declaring type's class declaration within <paramref name="model"/>.</param>
        /// <param name="methodBodies">Map of method name to body source, for the methods being validated.</param>
        /// <param name="originalTypeName">Name of the declaring type to validate against.</param>
        /// <returns>The validation result, including any violations found.</returns>
        public static AccessibilityValidationResult ValidatePublicAccess(
            SemanticModel model,
            ClassDeclarationSyntax classDecl,
            Dictionary<string, string> methodBodies,
            string originalTypeName)
        {
            var result = new AccessibilityValidationResult
            {
                IsValid = true,
                Violations = new List<AccessibilityViolation>()
            };

            try
            {
                var classSymbol = model.GetDeclaredSymbol(classDecl);

                foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
                {
                    var methodName = method.Identifier.ValueText;
                    if (!methodBodies.ContainsKey(methodName) || method.Body == null)
                        continue;

                    var seen = new HashSet<string>();
                    foreach (var name in method.Body.DescendantNodes().OfType<SimpleNameSyntax>())
                    {
                        if (IsMemberName(name))
                            continue;

                        var symbol = model.GetSymbolInfo(name).Symbol;
                        if (!IsInstanceMemberOf(symbol, classSymbol))
                            continue;

                        if (symbol.DeclaredAccessibility != Accessibility.Public && seen.Add(symbol.Name))
                        {
                            result.Violations.Add(new AccessibilityViolation
                            {
                                MemberName = symbol.Name,
                                MethodName = methodName,
                                AccessLevel = symbol.DeclaredAccessibility,
                                ViolationType = AccessibilityViolationType.PrivateAccess,
                                ErrorMessage = $"Cannot access non-public member '{symbol.Name}' " +
                                    $"({symbol.DeclaredAccessibility}) in [HotReload] method '{methodName}'",
                                Suggestion = $"Make '{symbol.Name}' public in {originalTypeName}, or reload " +
                                    "through the interpreter backend (reload_file_editor_interpreter), which " +
                                    "reaches non-public members. The compiled backend loads the override " +
                                    "as a separate assembly and Mono enforces accessibility at dispatch."
                            });
                        }
                    }
                }

                result.IsValid = result.Violations.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"HotReload: Accessibility validation error: {ex.Message}");
                return new AccessibilityValidationResult
                {
                    IsValid = false,
                    ValidationError = ex.Message,
                    Violations = new List<AccessibilityViolation>()
                };
            }
        }

        /// <summary>True if the name is the right-hand member name of an access (foo.Bar -> Bar).</summary>
        private static bool IsMemberName(SimpleNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                return true;
            if (node.Parent is QualifiedNameSyntax)
                return true;
            if (node.Parent is MemberBindingExpressionSyntax)
                return true;
            return false;
        }

        /// <summary>True if the symbol is an instance field/property/method/event of the type or a base.</summary>
        private static bool IsInstanceMemberOf(ISymbol symbol, INamedTypeSymbol type)
        {
            if (symbol == null || symbol.IsStatic)
                return false;

            switch (symbol.Kind)
            {
                case SymbolKind.Field:
                case SymbolKind.Property:
                case SymbolKind.Method:
                case SymbolKind.Event:
                    break;
                default:
                    return false;
            }

            for (var t = type; t != null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t, symbol.ContainingType))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Result of accessibility validation for hot reload methods.
    /// </summary>
    class AccessibilityValidationResult
    {
        /// <summary>True when no accessibility violations were found.</summary>
        public bool IsValid { get; set; }
        /// <summary>Violations found, if any.</summary>
        public List<AccessibilityViolation> Violations { get; set; } = new List<AccessibilityViolation>();
        /// <summary>Set when validation itself could not run (e.g. a parse failure), rather than a violation being found.</summary>
        public string ValidationError { get; set; }

        /// <summary>Human-readable summary of <see cref="ValidationError"/> or all <see cref="Violations"/>.</summary>
        public string GetFormattedErrorMessage()
        {
            if (!string.IsNullOrEmpty(ValidationError))
                return $"HotReload Validation Error: {ValidationError}";

            if (Violations.Count == 0)
                return "All member access is valid for hot reload.";

            var errorMessage = $"HotReload Validation Failed: {Violations.Count} accessibility violation(s) found\n\n";
            for (int i = 0; i < Violations.Count; i++)
            {
                var violation = Violations[i];
                errorMessage += $"{i + 1}. Method '{violation.MethodName}': {violation.ErrorMessage}\n";
                errorMessage += $"   → Suggestion: {violation.Suggestion}\n";
                if (i < Violations.Count - 1)
                    errorMessage += "\n";
            }

            errorMessage += "\nFix these accessibility issues and run reload_file again.";
            return errorMessage;
        }
    }

    /// <summary>
    /// Information about a specific accessibility violation in hot reload code.
    /// </summary>
    class AccessibilityViolation
    {
        /// <summary>Name of the non-public member that was accessed.</summary>
        public string MemberName { get; set; }
        /// <summary>Name of the [HotReload] method containing the access.</summary>
        public string MethodName { get; set; }
        /// <summary>The member's actual accessibility.</summary>
        public Accessibility AccessLevel { get; set; }
        /// <summary>Category of violation.</summary>
        public AccessibilityViolationType ViolationType { get; set; }
        /// <summary>Human-readable description of the violation.</summary>
        public string ErrorMessage { get; set; }
        /// <summary>Suggested fix.</summary>
        public string Suggestion { get; set; }
    }

    /// <summary>
    /// Types of accessibility violations that can occur in hot reload methods.
    /// </summary>
    enum AccessibilityViolationType
    {
        /// <summary>A private member was accessed.</summary>
        PrivateAccess,
        /// <summary>An internal member was accessed.</summary>
        InternalAccess,
        /// <summary>A protected member was accessed.</summary>
        ProtectedAccess,
        /// <summary>The source could not be parsed to determine accessibility.</summary>
        ParseError
    }
}
