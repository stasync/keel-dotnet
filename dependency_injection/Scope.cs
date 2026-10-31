using Keel.DependencyInjection.Diagnostics;
using Keel.DependencyInjection.Interface;
using Keel.DependencyInjection.Internal;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Keel.DependencyInjection
{
    public class Scope : IDisposable, IEnumerable<Scope>
    {
        // Type maps.
        private readonly ScopeDependencyMap _dependencyMap;

        // Key - implementation type.
        private readonly Dictionary<Type, object> _instances = new();

        // Child scope.
        private readonly List<Scope> _childScopes = new();

        // Root scope.
        private readonly RootScope _rootScope;

        // Parent scope.
        private readonly Scope _parentScope;

        // If the scope dispose function was called.
        private bool _disposed;

        protected Scope(Scope parentScope, RootScope rootScope, ScopeDependencyMap scopeDependencyMap)
        {
            scopeDependencyMap ??= new ScopeDependencyMap();

            CircularDependencyDetector.Validate(scopeDependencyMap);

            _dependencyMap = scopeDependencyMap;
            _parentScope = parentScope;
            _rootScope = rootScope ?? this as RootScope;
        }

        public TInterface Provide<TInterface>() where TInterface : class =>
            Provide(typeof(TInterface)) as TInterface;

        public object Provide(Type interfaceType)
        {
            EnsureValid();

            if (_rootScope.DependencyResolvingContext != null)
                throw new InvalidOperationException($"Unable to provide implementation for '{interfaceType.FullName}' while dependency tree is the resolving context. Use '{nameof(IScopeListener)}.{nameof(IScopeListener.OnResolved)}' instead.");

            using (var resolvingContext = new DependencyResolvingContext())
            {
                _rootScope.DependencyResolvingContext = resolvingContext;

                object instance;
                try
                {
                    // Calling provide (recursive) to create an instance and resolve all the dependencies.
                    instance = ProvideDependency(interfaceType);
                }
                finally
                {
                    // Clean up the resolving context, so the user is able to resolve from the 'OnResolved' function.
                    // Also when resolving throws - otherwise the scope would refuse every later call as a nested one.
                    _rootScope.DependencyResolvingContext = null;
                }

                // Look for all instances that may listen for OnResolved callback.
                foreach (var contextInstance in resolvingContext.GetAllContextInstances())
                {
                    if (contextInstance is IScopeListener instanceAsService)
                        instanceAsService.OnResolved();
                }

                return instance;
            }
        }

        private object ProvideDependency(Type interfaceType)
        {
            if (_rootScope.DependencyResolvingContext == null)
                throw new InvalidOperationException($"'{nameof(ProvideDependency)}' is not called in a valid context.");

            // Check if interface is declared in the current scope.
            // If not - trying to provide the data from the parent scope.
            if (!_dependencyMap.InterfaceToImplementationMap.TryGetValue(interfaceType, out var implementationType))
                return _parentScope?.ProvideDependency(interfaceType);

            // Sanity check.
            if (!_dependencyMap.Factories.TryGetValue(implementationType, out var factory))
                throw new InvalidOperationException($"Factory is not registered for implementation '{implementationType.FullName}'.");

            // Sanity check.
            if (!_dependencyMap.ImplementationBehaviourTypeMap.TryGetValue(implementationType, out var behaviourType))
                throw new InvalidOperationException($"Behaviour is not registered for implementation '{implementationType.FullName}'.");

            // Process different behaviors.
            switch (behaviourType)
            {
                // For Transient behavior, just produce and resolve new instance.
                case ImplementationBehaviour.Transient:
                    return ProduceAndResolve();

                // For Singleton behavior, trying to access an existing instance, create a new one if it's not yet created.
                case ImplementationBehaviour.Singleton:
                    {
                        // Create new singleton instance, if it's not yet created.
                        if (!_instances.TryGetValue(implementationType, out var instance))
                            _instances.Add(implementationType, instance = ProduceAndResolve());

                        return instance;
                    }
                default:
                    return null;
            }

            // Local function - provides and actual dependency instance.
            object ProduceAndResolve()
            {
                // Creation pipeline should probably be replaced to this:
                // https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=net-5.0
                // https://stackoverflow.com/Questions/2420193/how-to-avoid-dependency-injection-constructor-madness
                var instance = factory.Produce();
                if (instance == null)
                    throw new InvalidOperationException($"Factory produced null for implementation '{implementationType.FullName}'.");

                // Resolve the instance.
                var instanceType = instance.GetType();
                foreach (var field in InjectionTargetsCache.GetFields(instanceType))
                {
                    var dependency = field.FieldType.IsAssignableFrom(typeof(Scope)) ? this : ProvideDependency(field.FieldType);
                    field.SetValue(instance, dependency);
                }

                // Register new instance in the context.
                _rootScope.DependencyResolvingContext.AddNewInstance(instance);
                return instance;
            }
        }

        public Scope CreateChildScope(ScopeDependencyMap scopeDependencyMap)
        {
            EnsureValid();

            var childScope = new Scope(parentScope: this, rootScope: _rootScope, scopeDependencyMap);
            _childScopes.Add(childScope);
            return childScope;
        }

        /// <summary>
        /// A debug only method.
        /// </summary>
        /// <returns>All instances that were resolved within the scope.</returns>
        public IEnumerable<object> EnumerateActiveInstances()
        {
            EnsureValid();
            return _instances.Values;
        }

        public IEnumerator<Scope> GetEnumerator()
        {
            EnsureValid();
            return _childScopes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        protected void EnsureValid()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        public virtual void Dispose()
        {
            if (_disposed)
                return;

            // Marked first, so the children disposed below don't remove themselves from the list being iterated.
            _disposed = true;

            foreach (var childScope in _childScopes)
                childScope.Dispose();

            _childScopes.Clear();
            _parentScope?.OnChildScopeDisposed(this);
        }

        private void OnChildScopeDisposed(Scope childScope)
        {
            // A parent that is being disposed itself clears the whole list anyway.
            if (!_disposed)
                _childScopes.Remove(childScope);
        }
    }
}