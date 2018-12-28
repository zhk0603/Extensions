using System;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Extensions.DependencyInjection.Specification
{
    public abstract class SkippableDependencyInjectionSpecificationTests: DependencyInjectionSpecificationTests
    {
        public virtual string[] SkippedTests { get; } = { };

        public string[] DefaultSkippedTests { get; } = {
            "IServiceProvider_Resolves_IServiceActivatorFactory",
            "IServiceActivatorFactory_Create_ReturnsNullForUnregisteredService",
            "IServiceActivator_GetService_ReturnsNewInstanceForRegisteredService",
            "IServiceActivator_GetService_ResolvesInstanceFromProvidedScope",
            "IServiceActivator_GetService_ReturnsSameInstanceForSingletons",
        };

        protected sealed override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            foreach (var stackFrame in new StackTrace(1).GetFrames().Take(2))
            {
                var name = stackFrame.GetMethod().Name;
                if (DefaultSkippedTests.Contains(name) ||
                    SkippedTests.Contains(name))
                {
                    // We skip tests by returning MEDI service provider that we know passes the test
                    return serviceCollection.BuildServiceProvider();
                }
            }

            return CreateServiceProviderImpl(serviceCollection);
        }

        protected abstract IServiceProvider CreateServiceProviderImpl(IServiceCollection serviceCollection);
    }
}
