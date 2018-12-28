// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection.Specification.Fakes;
using Xunit;

namespace Microsoft.Extensions.DependencyInjection.Specification
{
    public abstract partial class DependencyInjectionSpecificationTests
    {
        [Fact]
        public void IServiceProvider_Resolves_IServiceActivatorFactory()
        {
            var serviceProvider = CreateServiceProvider(new TestServiceCollection());
            Assert.NotNull(serviceProvider.GetService(typeof(IServiceActivatorFactory)));
        }

        [Fact]
        public void IServiceActivatorFactory_Create_ReturnsNullForUnregisteredService()
        {
            var serviceProvider = CreateServiceProvider(new TestServiceCollection());
            var activatorFactory = serviceProvider.GetService<IServiceActivatorFactory>();

            Assert.Null(activatorFactory.Create(typeof(IFakeService)));
        }

        [Fact]
        public void IServiceActivator_GetService_ReturnsNewInstanceForRegisteredService()
        {
            var collection = new TestServiceCollection();
            collection.AddTransient<IFakeService, FakeService>();

            var serviceProvider = CreateServiceProvider(collection);
            var activatorFactory = serviceProvider.GetService<IServiceActivatorFactory>();
            var activator = activatorFactory.Create(typeof(IFakeService));

            var fakeService1 = Assert.IsType<FakeService>(activator.GetService(serviceProvider));
            var fakeService2 = Assert.IsType<FakeService>(activator.GetService(serviceProvider));

            Assert.NotEqual(fakeService1, fakeService2);
        }

        [Fact]
        public void IServiceActivator_GetService_ResolvesInstanceFromProvidedScope()
        {
            var collection = new TestServiceCollection();
            collection.AddScoped<IFakeService, FakeService>();

            var serviceProvider = CreateServiceProvider(collection);
            var activatorFactory = serviceProvider.GetService<IServiceActivatorFactory>();
            var activator = activatorFactory.Create(typeof(IFakeService));

            var fakeService1 = Assert.IsType<FakeService>(activator.GetService(serviceProvider));

            FakeService fakeService2;
            FakeService fakeService3;

            using (var scope = serviceProvider.CreateScope())
            {
                fakeService2 = Assert.IsType<FakeService>(activator.GetService(scope.ServiceProvider));
                fakeService3 = Assert.IsType<FakeService>(activator.GetService(scope.ServiceProvider));
            }

            Assert.NotEqual(fakeService1, fakeService2);
            Assert.Equal(fakeService2, fakeService3);
        }

        [Fact]
        public void IServiceActivator_GetService_ReturnsSameInstanceForSingletons()
        {
            var collection = new TestServiceCollection();
            collection.AddSingleton<IFakeService, FakeService>();

            var serviceProvider = CreateServiceProvider(collection);
            var activatorFactory = serviceProvider.GetService<IServiceActivatorFactory>();
            var activator = activatorFactory.Create(typeof(IFakeService));

            var fakeService1 = Assert.IsType<FakeService>(activator.GetService(serviceProvider));

            FakeService fakeService2;
            FakeService fakeService3;

            using (var scope = serviceProvider.CreateScope())
            {
                fakeService2 = Assert.IsType<FakeService>(activator.GetService(scope.ServiceProvider));
                fakeService3 = Assert.IsType<FakeService>(activator.GetService(scope.ServiceProvider));
            }

            Assert.Equal(fakeService1, fakeService2);
            Assert.Equal(fakeService2, fakeService3);
        }



        //private void ValidateLifetimeBehavior(Func<Type, object> resolver, Type serviceType, ServiceLifetime expectedLifetime)
        //{
        //    var rootResolution1 = resolver(serviceType);
        //    object scopeResolution1;
        //    object scopeResolution2;
        //    object scopeResolution3;

        //    using (var scope = resolver(typeof(IServiceScopeFactory).CreateScope())
        //    {
        //        scopeResolution1 = scope.ServiceProvider.GetService(serviceType);
        //        scopeResolution2 = scope.ServiceProvider.GetService(serviceType);
        //    }

        //    using (var scope = serviceProvider.CreateScope())
        //    {
        //        scopeResolution3 = scope.ServiceProvider.GetService(serviceType);
        //    }

        //    var rootResolution2 = serviceProvider.GetService(serviceType);

        //    switch (expectedLifetime)
        //    {
        //        case ServiceLifetime.Singleton:
        //            Assert.Equal(rootResolution1, rootResolution2);
        //            Assert.Equal(rootResolution1, scopeResolution1);
        //            Assert.Equal(scopeResolution1, scopeResolution2);
        //            Assert.Equal(scopeResolution2, scopeResolution3);
        //            break;
        //        case ServiceLifetime.Scoped:
        //            Assert.Equal(rootResolution1, rootResolution2);
        //            Assert.Equal(scopeResolution1, scopeResolution2);
        //            Assert.NotEqual(rootResolution1, scopeResolution1);
        //            Assert.NotEqual(scopeResolution2, scopeResolution3);
        //            break;
        //        case ServiceLifetime.Transient:
        //            Assert.NotEqual(rootResolution1, rootResolution2);
        //            Assert.NotEqual(rootResolution1, scopeResolution1);
        //            Assert.NotEqual(scopeResolution1, scopeResolution2);
        //            Assert.NotEqual(scopeResolution2, scopeResolution3);
        //            break;
        //        default:
        //            throw new ArgumentOutOfRangeException(nameof(expectedLifetime), expectedLifetime, null);
        //    }
        //}
    }
}
