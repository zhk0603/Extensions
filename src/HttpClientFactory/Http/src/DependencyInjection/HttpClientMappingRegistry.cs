// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.DependencyInjection
{
    // Internal tracking for HTTP Client configuration. This is used to prevent some common mistakes
    // that are easy to make with HTTP Client registration.
    //
    // See: https://github.com/aspnet/Extensions/issues/519
    // See: https://github.com/aspnet/Extensions/issues/960
    internal class HttpClientMappingRegistry
    {
        private static readonly ConditionalWeakTable<IServiceCollection, HttpClientMappingRegistry> _table = new ConditionalWeakTable<IServiceCollection, HttpClientMappingRegistry>();

        public static HttpClientMappingRegistry Get(IServiceCollection services)
        {
            return _table.GetOrCreateValue(services);
        }

        public Dictionary<Type, string> TypedClientRegistrations { get; } = new Dictionary<Type, string>();

        public Dictionary<string, Type> NamedClientRegistrations { get; } = new Dictionary<string, Type>();
    }
}
