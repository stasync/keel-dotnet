using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Keel.DependencyInjection.Internal
{
    internal static class InjectionTargetsCache
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<FieldInfo>> s_fieldCache = new();

        internal static IReadOnlyList<FieldInfo> GetFields(Type implementationType)
        {
            if (s_fieldCache.TryGetValue(implementationType, out var cachedResult))
                return cachedResult;

            var result = new List<FieldInfo>();

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var injectAttributeType = typeof(InjectAttribute);

            // GetFields() never returns private fields declared on base types, so walk the hierarchy.
            for (var type = implementationType; type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (Attribute.IsDefined(field, injectAttributeType))
                        result.Add(field);
                }
            }

            foreach (var property in implementationType.GetProperties(flags))
            {
                if (!Attribute.IsDefined(property, injectAttributeType))
                    continue;

                // The backing field belongs to the type that declares the property, which may be a base type.
                var backingField = property.DeclaringType.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (backingField != null)
                    result.Add(backingField);
                else
                    throw new InvalidOperationException($"Setter is not declared for property '{implementationType.FullName}.{property.Name}'.");
            }

            // Cached once complete, so a type that throws above throws every time.
            s_fieldCache.TryAdd(implementationType, result);
            return result;
        }
    }
}