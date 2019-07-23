// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.ServiceLookup;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The default IServiceProvider.
    /// </summary>
    public sealed partial class ServiceProvider : IServiceProvider, IDisposable, IServiceProviderEngineCallback
#if DISPOSE_ASYNC
        , IAsyncDisposable
#endif
    {
        private readonly IEnumerable<ServiceDescriptor> _serviceDescriptors;

        private readonly IServiceProviderEngine _engine;

        private CallSiteValidator _callSiteValidator;

        internal ServiceProvider(IServiceProviderEngine engine)
        {
            _engine = engine;

        }
        internal ServiceProvider(IEnumerable<ServiceDescriptor> serviceDescriptors, ServiceProviderOptions options)
        {
            _serviceDescriptors = serviceDescriptors;
            _engine = CreateEngine(serviceDescriptors, options);

            if (options.ValidateOnBuild)
            {
                List<Exception> exceptions = null;
                foreach (var serviceDescriptor in serviceDescriptors)
                {
                    try
                    {
                        _engine.ValidateService(serviceDescriptor);
                    }
                    catch (Exception e)
                    {
                        exceptions = exceptions ?? new List<Exception>();
                        exceptions.Add(e);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException("Some services are not able to be constructed", exceptions.ToArray());
                }
            }
        }

        private IServiceProviderEngine CreateEngine(IEnumerable<ServiceDescriptor> serviceDescriptors, ServiceProviderOptions options)
        {
            IServiceProviderEngineCallback callback = null;
            if (options.ValidateScopes)
            {
                callback = this;
                _callSiteValidator = new CallSiteValidator();
            }

            switch (options.Mode)
            {
                case ServiceProviderMode.Default:
#if !NETCOREAPP
                    return new DynamicServiceProviderEngine(serviceDescriptors, callback);
#else
                    if (RuntimeFeature.IsSupported("IsDynamicCodeCompiled"))
                    {
                        return new DynamicServiceProviderEngine(serviceDescriptors, callback);
                    }
                    else
                    {
                        // Don't try to compile Expressions/IL if they are going to get interpreted
                        return new RuntimeServiceProviderEngine(serviceDescriptors, callback);
                    }
#endif
                case ServiceProviderMode.Dynamic:
                    return new DynamicServiceProviderEngine(serviceDescriptors, callback);
                case ServiceProviderMode.Runtime:
                    return new RuntimeServiceProviderEngine(serviceDescriptors, callback);
#if IL_EMIT
                case ServiceProviderMode.ILEmit:
                    return new ILEmitServiceProviderEngine(serviceDescriptors, callback);
#endif
                case ServiceProviderMode.Expressions:
                    return new ExpressionsServiceProviderEngine(serviceDescriptors, callback);
                default:
                    throw new NotSupportedException(nameof(options.Mode));
            }
        }

        /// <summary>
        /// Gets the service object of the specified type.
        /// </summary>
        /// <param name="serviceType">The type of the service to get.</param>
        /// <returns>The service that was produced.</returns>
        public object GetService(Type serviceType) => _engine.GetService(serviceType);

        /// <inheritdoc />
        public void Dispose()
        {
            _engine.Dispose();
        }

        public void EmitIT(string file)
        {
            new CecilResolverBuilder(_serviceDescriptors.ToArray()).Build().Write(file);
        }

        void IServiceProviderEngineCallback.OnCreate(ServiceCallSite callSite)
        {
            _callSiteValidator.ValidateCallSite(callSite);
        }

        void IServiceProviderEngineCallback.OnResolve(Type serviceType, IServiceScope scope)
        {
            _callSiteValidator.ValidateResolution(serviceType, scope, _engine.RootScope);
        }

#if DISPOSE_ASYNC
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return _engine.DisposeAsync();
        }
#endif
    }
}
