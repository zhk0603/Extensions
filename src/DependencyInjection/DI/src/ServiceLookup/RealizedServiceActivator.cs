// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class RealizedServiceActivator: IServiceActivator
    {
        private readonly Func<ServiceProviderEngineScope, object> _func;

        public RealizedServiceActivator(Func<ServiceProviderEngineScope, object> func)
        {
            _func = func;
        }

        public object GetService(IServiceProvider provider)
        {
            if (provider is ServiceProvider serviceProvider)
            {
                return _func((ServiceProviderEngineScope)serviceProvider._engine.RootScope);
            }
            return _func((ServiceProviderEngineScope)provider);

        }
    }
}
