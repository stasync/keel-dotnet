namespace Keel.DependencyInjection.Tests
{
    /// <summary>
    /// Type.GetFields never returns private fields declared on a base type, and an auto-property's backing field is
    /// always private to the type that declares the property - so injection has to walk the type hierarchy to find them.
    /// </summary>
    public class BaseClassInjectionTests
    {
        [Fact]
        public void Provide_InjectsPrivateFieldOfBaseClass()
        {
            var binder = new ScopeBinder();
            binder.Bind<IDependency, Dependency>();
            binder.BindToSelf<DerivedFromPrivateField>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            var service = rootScope.Provide<DerivedFromPrivateField>();

            Assert.NotNull(service.BaseDependency);
        }

        [Fact]
        public void Provide_InjectsAutoPropertyOfBaseClass()
        {
            var binder = new ScopeBinder();
            binder.Bind<IDependency, Dependency>();
            binder.BindToSelf<DerivedFromAutoProperty>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            var service = rootScope.Provide<DerivedFromAutoProperty>();

            Assert.NotNull(service.BaseDependency);
        }

        [Fact]
        public void Provide_InjectsEveryLevelOfHierarchy()
        {
            var binder = new ScopeBinder();
            binder.Bind<IDependency, Dependency>();
            binder.BindToSelf<DerivedWithOwnField>();
            var rootScope = new RootScope(binder.ToDependencyMap());

            var service = rootScope.Provide<DerivedWithOwnField>();

            Assert.NotNull(service.BaseDependency);
            Assert.NotNull(service.OwnDependency);
        }

        private sealed class Dependency : IDependency;

        private class BaseWithPrivateField
        {
            [Inject] private readonly IDependency _baseDependency = null;

            public IDependency BaseDependency => _baseDependency;
        }

        private sealed class DerivedFromPrivateField : BaseWithPrivateField;

        private sealed class DerivedWithOwnField : BaseWithPrivateField
        {
            [Inject] private readonly IDependency _ownDependency = null;

            public IDependency OwnDependency => _ownDependency;
        }

        private class BaseWithAutoProperty
        {
            [Inject]
            public IDependency BaseDependency { get; } = null;
        }

        private sealed class DerivedFromAutoProperty : BaseWithAutoProperty;

        private interface IDependency;
    }
}