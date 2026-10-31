using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Keel.DependencyInjection.Internal
{
    internal static class InjectionTargetsCache
    {
        private const BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly ConcurrentDictionary<Type, IReadOnlyList<FieldInfo>> s_fieldCache = new();

        // Only a complete result is cached: if collecting throws, nothing is stored and the next call throws again.
        internal static IReadOnlyList<FieldInfo> GetFields(Type implementationType) =>
            s_fieldCache.GetOrAdd(implementationType, CollectFields);

        private static IReadOnlyList<FieldInfo> CollectFields(Type implementationType)
        {
            var result = new List<FieldInfo>();
            var injectAttributeType = typeof(InjectAttribute);

            // GetFields() never returns private fields declared on base types, and an auto-property's backing field is
            // always private to the type that declares the property - so walk the hierarchy.
            for (var type = implementationType; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(FieldFlags))
                {
                    if (Attribute.IsDefined(field, injectAttributeType))
                        result.Add(field);
                }

                foreach (var property in type.GetProperties(FieldFlags))
                {
                    if (!Attribute.IsDefined(property, injectAttributeType))
                        continue;

                    var backingField = type.GetField($"<{property.Name}>k__BackingField", FieldFlags);
                    if (backingField != null)
                        result.Add(backingField);
                    else
                        throw new InvalidOperationException($"Setter is not declared for property '{type.FullName}.{property.Name}'.");
                }
            }

            return result;
        }
    }
}