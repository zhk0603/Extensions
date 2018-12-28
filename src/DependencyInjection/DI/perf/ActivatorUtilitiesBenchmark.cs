// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;

namespace Microsoft.Extensions.DependencyInjection.Performance
{
    public class ActivatorUtilitiesBenchmark
    {
        private ServiceProvider _serviceProvider;
        private ObjectFactory _factory;
        private ObjectFactory _factoryAllDi;
        private object[] _factoryArguments;
        private object[] _factoryAllDiArguments;

        [Params(true, false)]
        public bool UseActivatorFactory { get; set; } = false;

        [GlobalSetup]
        public void SetUp()
        {
            var collection = new ServiceCollection();
            collection.AddTransient<TypeToBeActivated>();
            collection.AddSingleton<DependencyA>();
            collection.AddSingleton<DependencyB>();
            collection.AddSingleton<DependencyC>();
            collection.AddTransient<TypeToBeActivated>();

            _serviceProvider = collection.BuildServiceProvider();
            var serviceProviderForActivator = UseActivatorFactory ? _serviceProvider : null;
            _factory = ActivatorUtilities.CreateFactory(serviceProviderForActivator, typeof(TypeToBeActivated), new Type[] { typeof(DependencyB), typeof(DependencyC) });
            _factoryAllDi = ActivatorUtilities.CreateFactory(serviceProviderForActivator, typeof(TypeToBeActivated), new Type[0]);
            _factoryArguments = new object[] { new DependencyB(), new DependencyC() };
            _factoryAllDiArguments = new object[] {  };
        }

        [Benchmark]
        public void ServiceProvider()
        {
           _serviceProvider.GetService<TypeToBeActivated>();
        }

        [Benchmark]
        public void Factory()
        {
            _ = (TypeToBeActivated)_factory(_serviceProvider, _factoryArguments);
        }

        [Benchmark]
        public void Factory3ArgumentsFromDI()
        {
            _ = (TypeToBeActivated)_factoryAllDi(_serviceProvider, _factoryAllDiArguments);
        }

        [Benchmark]
        public void CreateInstance()
        {
            ActivatorUtilities.CreateInstance<TypeToBeActivated>(_serviceProvider, _factoryArguments);
        }

        public class TypeToBeActivated
        {
            public TypeToBeActivated(DependencyA a, DependencyB b, DependencyC c)
            {
            }
        }

        public class DependencyA {}
        public class DependencyB {}
        public class DependencyC {}
    }
}
