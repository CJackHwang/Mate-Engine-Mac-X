using System;

namespace Unity.Pipeline.HotReload
{
    /// <summary>
    /// Marks a method as hot reloadable. The method can be overridden at runtime
    /// through the hot reload system without triggering Unity domain reload.
    ///
    /// Belongs to the helper (separate override file) workflow, together with
    /// <see cref="HotReloadOverrideMethodAttribute"/>. For the in-place workflow use
    /// <see cref="HotReloadAttribute"/> instead.
    ///
    /// Pattern A: Attribute-Based Method Override
    /// - Individual methods can be surgically replaced
    /// - Hot reload methods must have same signature + instance parameter
    /// - May access members of any accessibility (private/protected/internal included)
    /// - Best for: Quick gameplay tweaks, formula adjustments
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HotReloadWithOverridesAttribute : Attribute
    {
        /// <summary>
        /// Optional identifier for this hot reloadable method.
        /// If not specified, uses TypeName.MethodName format.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Whether this method requires main thread execution.
        /// Defaults to true for Unity API safety.
        /// </summary>
        public bool RequireMainThread { get; set; } = true;
    }

    /// <summary>
    /// Marks a method for the in-place hot reload workflow: the method body is
    /// edited directly in the original source file and applied via the <c>reload_file</c> command.
    /// Bodies may touch members of any accessibility, including
    /// private/protected/internal ones of the target type.
    ///
    /// This attribute is exclusive to the in-place workflow and is independent of
    /// <see cref="HotReloadWithOverridesAttribute"/> / <see cref="HotReloadOverrideMethodAttribute"/>, which
    /// belong to the helper (separate override file) workflow.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HotReloadAttribute : Attribute
    {
        /// <summary>
        /// Optional identifier for this in-place hot reloadable method.
        /// If not specified, uses TypeName.MethodName format.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Whether this method requires main thread execution.
        /// Defaults to true for Unity API safety.
        /// </summary>
        public bool RequireMainThread { get; set; } = true;
    }

    /// <summary>
    /// Marks a parameterless instance method to be invoked after a hot reload is applied to its
    /// declaring component. Fires once per <c>reload_file</c> (and <c>reload_file_override</c>) that
    /// binds at least one override on the type, on every live instance — a place to re-initialize or
    /// refresh state when the code is swapped (analogous to a domain-reload-free OnEnable). The method
    /// runs on the main thread; exceptions are logged, not propagated. Only UnityEngine.Object-derived
    /// types are supported (instances are discovered via FindObjectsByType).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [JetBrains.Annotations.MeansImplicitUse]
    public class OnHotReloadAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a static method as a hot reload override for a specific method.
    /// Used in hot reload files to define replacement logic for [HotReloadWithOverrides] methods.
    ///
    /// Method signature must match original method plus instance parameter as first argument.
    /// Example: void Update() becomes static void Update(TargetType instance)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HotReloadOverrideMethodAttribute : Attribute
    {
        /// <summary>
        /// Target method identifier in format "TypeName.MethodName"
        /// Must match a method marked with [HotReloadWithOverrides].
        /// </summary>
        public string TargetMethodId { get; }

        /// <summary>
        /// Optional description of what this hot reload does.
        /// Useful for debugging and hot reload management.
        /// </summary>
        public string Description { get; set; }

        /// <summary>Mark a method as the hot-reload override for the given target method.</summary>
        /// <param name="targetMethodId">Target method identifier, "TypeName.MethodName".</param>
        public HotReloadOverrideMethodAttribute(string targetMethodId)
        {
            TargetMethodId = targetMethodId ?? throw new ArgumentNullException(nameof(targetMethodId));
        }
    }

    /// <summary>
    /// Marks a component class as hot reloadable via complete behavior replacement.
    /// The component becomes a proxy that delegates to hot reload implementations.
    ///
    /// Pattern C: Component Override
    /// - Complete component behavior replacement via interfaces
    /// - Maximum flexibility for major behavioral experiments
    /// - Pattern C wins when multiple patterns apply to same component
    /// - Best for: Major AI changes, experimental features
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class HotReloadableComponentAttribute : Attribute
    {
        /// <summary>
        /// Optional identifier for this hot reloadable component.
        /// If not specified, uses the class name.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Interface type that hot reload implementations must implement.
        /// If not specified, generates interface automatically.
        /// </summary>
        public Type InterfaceType { get; set; }
    }

    /// <summary>
    /// Marks a class as a hot reload implementation for a specific component.
    /// Used in hot reload files to provide complete component behavior replacement.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class HotReloadComponentAttribute : Attribute
    {
        /// <summary>
        /// Target component type to override.
        /// Must be a type marked with [HotReloadableComponent].
        /// </summary>
        public Type TargetType { get; }

        /// <summary>
        /// Optional description of what this hot reload implementation does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>Mark a class as the hot-reload implementation for the given component type.</summary>
        /// <param name="targetType">Target component type to override.</param>
        public HotReloadComponentAttribute(Type targetType)
        {
            TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        }
    }

    /// <summary>
    /// Marks a static method as a hot reload target for Pattern B explicit wrapper system.
    /// Used with HotReloadMethod&lt;T&gt; wrapper for type-safe delegate hot reload.
    ///
    /// Pattern B: Explicit Wrapper
    /// - HotReloadMethod&lt;T&gt; wrapper with type-safe delegate calls
    /// - Built-in fallback logic, zero method signature changes
    /// - Explicit opt-in with performance-optimized direct calls
    /// - Best for: Performance-critical paths, type-safe hot reload
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HotReloadTargetAttribute : Attribute
    {
        /// <summary>
        /// Unique identifier that matches HotReloadMethod&lt;T&gt; wrapper registration.
        /// This ID connects the wrapper to the hot reload implementation.
        /// </summary>
        public string TargetId { get; }

        /// <summary>
        /// Optional description of what this hot reload target does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>Mark a static method as a Pattern B hot-reload target.</summary>
        /// <param name="targetId">Unique identifier matching a HotReloadMethod&lt;T&gt; wrapper registration.</param>
        public HotReloadTargetAttribute(string targetId)
        {
            TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        }
    }
}