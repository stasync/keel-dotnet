#pragma warning disable CS0414 // Field is assigned but its value is never used
using Keel.DependencyInjection.Factories;
using Keel.DependencyInjection.Interface;

namespace Keel.DependencyInjection.Tests
{
    public class ErrorHandlingTests
    {
        [Fact]
        public void ProvideUnregisteredInterface_ReturnsNull()
        {
            var binder = new ScopeBinder();
            binder.BindToDefaultImplementation<IFirstService>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            // Request interface that was never bound
            var unregisteredService = rootScope.Provide<ISecondService>();
            Assert.Null(unregisteredService);
        }

        [Fact]
        public void DuplicateBinding_ThrowsInvalidOperationException()
        {
            var binder = new ScopeBinder();
            binder.Bind<IFirstService, FirstService>();

            // Attempting to bind the same interface again should throw
            Assert.Throws<InvalidOperationException>(() =>
                binder.Bind<IFirstService, AlternativeFirstService>());
        }

        [Fact]
        public void BindingConflictingBehaviors_ThrowsInvalidOperationException()
        {
            var binder = new ScopeBinder();
            binder.Bind<IFirstService, FirstService>();

            // Same interface, same implementation, but different behavior should throw
            Assert.Throws<InvalidOperationException>(() =>
                binder.Bind<IFirstService, FirstService>(ImplementationBehaviour.Transient));
        }

        [Fact]
        public void ProvideAfterDisposal_ThrowsObjectDisposedException()
        {
            var binder = new ScopeBinder();
            binder.BindToDefaultImplementation<IFirstService>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            // Dispose the scope
            rootScope.Dispose();

            // Attempting to provide after disposal should throw
            Assert.Throws<ObjectDisposedException>(() => rootScope.Provide<IFirstService>());
        }

        [Fact]
        public void BindInvalidImplementationType_ThrowsInvalidOperationException()
        {
            var binder = new ScopeBinder();

            // Attempting to bind an interface as implementation should throw
            Assert.Throws<InvalidOperationException>(() =>
                binder.Bind<IFirstService, ISecondService>());
        }

        [Fact]
        public void ProvideAfterFactoryThrows_ScopeKeepsWorking()
        {
            var binder = new ScopeBinder();
            binder.Bind<IFirstService, FirstService>(() => throw new InvalidOperationException("Factory failed."));
            binder.Bind<IHealthyService, HealthyService>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            var exception = Assert.Throws<InvalidOperationException>(() => rootScope.Provide<IFirstService>());
            Assert.Equal("Factory failed.", exception.Message);

            // The failed resolve must not leave the scope stuck in its resolving state.
            Assert.NotNull(rootScope.Provide<IHealthyService>());
        }

        [Fact]
        public void FailedBinding_LeavesBinderUnchanged()
        {
            var binder = new ScopeBinder();

            // An abstract implementation is rejected...
            Assert.Throws<InvalidOperationException>(() =>
                binder.Bind<IFirstService, AbstractFirstService>());

            // ...without leaving the interface half registered, so it can still be bound.
            binder.Bind<IFirstService, FirstService>();
            var rootScope = new RootScope(binder.ToDependencyMap());
            Assert.IsType<FirstService>(rootScope.Provide<IFirstService>());
        }

        [Fact]
        public void BindWithNonFactoryType_MessageNamesInstanceFactory()
        {
            var binder = new ScopeBinder();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                binder.Bind(typeof(IFirstService), typeof(FirstService), typeof(string), ImplementationBehaviour.Singleton));

            Assert.Contains(nameof(InstanceFactory), exception.Message);
        }

        [Fact]
        public void InjectIntoPropertyWithoutBackingField_ThrowsEveryTime()
        {
            var binder = new ScopeBinder();
            binder.BindToSelf<ServiceWithManualProperty>();
            var dependencyMap = binder.ToDependencyMap();

            Assert.Throws<InvalidOperationException>(() => new RootScope(dependencyMap));

            // The failed lookup must not be cached as if it had succeeded.
            Assert.Throws<InvalidOperationException>(() => new RootScope(dependencyMap));
        }

        private sealed class FirstService : IFirstService;

        private sealed class AlternativeFirstService : IFirstService;

        private abstract class AbstractFirstService : IFirstService;

        private sealed class HealthyService : IHealthyService;

        private sealed class ServiceWithManualProperty
        {
            [Inject]
            public IFirstService Service
            {
                get => null;
                set { }
            }
        }

        private sealed class RecursiveService : IRecursiveService
        {
            [Inject] private readonly Scope _scope = null;

            [Inject] private IFirstService _firstService = null;

            public RecursiveService()
            {
                // This will be null during injection phase, but we trigger in OnResolved
            }

            public void OnResolved()
            {
                // This should throw - can't call Provide during resolution
                _scope.Provide<ISecondService>();
            }
        }

        [DefaultImplementation(typeof(FirstService))]
        private interface IFirstService;

        private interface ISecondService : IFirstService;

        private interface IHealthyService;

        [DefaultImplementation(typeof(RecursiveService))]
        private interface IRecursiveService : IScopeListener;
    }
}