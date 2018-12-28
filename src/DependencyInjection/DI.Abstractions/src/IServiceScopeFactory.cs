// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Resolves a predefined instance from provided IServiceProvider
    /// </summary>
    public interface IServiceActivator
    {
        /// <summary>
        ///
        /// </summary>
        object GetService(IServiceProvider provider);
    }

    internal class DefaultServiceActivator: IServiceActivator
    {
        private readonly Type _serviceType;

        public DefaultServiceActivator(Type serviceType)
        {
            _serviceType = serviceType;
        }

        public object GetService(IServiceProvider provider)
        {
            return provider.GetService(_serviceType);
        }
    }

    /// <summary>
    ///
    /// </summary>
    public interface IServiceActivatorFactory
    {
        /// <summary>
        ///
        /// </summary>
        IServiceActivator Create(Type serviceType);
    }

    /// <summary>
    /// A factory for creating instances of <see cref="IServiceScope"/>, which is used to create
    /// services within a scope.
    /// </summary>
    public interface IServiceScopeFactory
    {
        /// <summary>
        /// Create an <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope"/> which
        /// contains an <see cref="System.IServiceProvider"/> used to resolve dependencies from a
        /// newly created scope.
        /// </summary>
        /// <returns>
        /// An <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope"/> controlling the
        /// lifetime of the scope. Once this is disposed, any scoped services that have been resolved
        /// from the <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope.ServiceProvider"/>
        /// will also be disposed.
        /// </returns>
        IServiceScope CreateScope();
    }
}
