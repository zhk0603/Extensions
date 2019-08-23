// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Microsoft.JSInterop.Infrastructure
{
    using InvokeMethod = Func<object, object[], object>;

    internal static partial class JSInvokableCache
    {
        private static readonly ConcurrentDictionary<AssemblyKey, Dictionary<string, (InvokeMethod, Type[])>> _cachedMethodsByAssembly
            = new ConcurrentDictionary<AssemblyKey, Dictionary<string, (InvokeMethod, Type[])>>();

        private static readonly ConcurrentDictionary<Type, Dictionary<string, (InvokeMethod, Type[])>> _cachedMethodsByType
            = new ConcurrentDictionary<Type, Dictionary<string, (InvokeMethod, Type[])>>();

        public static (InvokeMethod, Type[]) GetCachedMethodInfo(in DotNetInvocationInfo invocationInfo, IDotNetObjectReference objectReference)
        {
            var methodIdentifier = invocationInfo.MethodIdentifier;
            (InvokeMethod, Type[]) cachedMethodInfo;

            if (objectReference is null)
            {
                var assemblyKey = new AssemblyKey(invocationInfo.AssemblyName);
                cachedMethodInfo = GetCachedMethodInfo(assemblyKey, methodIdentifier);
            }
            else
            {
                cachedMethodInfo = GetCachedMethodInfo(objectReference, methodIdentifier, invocationInfo.DotNetObjectId);
            }

            return cachedMethodInfo;
        }

        private static (InvokeMethod, Type[]) GetCachedMethodInfo(IDotNetObjectReference objectReference, string methodIdentifier, long objectId)
        {
            Debug.Assert(objectReference != null);

            if (string.IsNullOrWhiteSpace(methodIdentifier))
            {
                throw new ArgumentException("Cannot be null, empty, or whitespace.", nameof(methodIdentifier));
            }

            var type = objectReference.Value.GetType();
            if (!_cachedMethodsByType.TryGetValue(type, out var cachedMethods))
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                var dictionary = new Dictionary<string, (InvokeMethod, Type[])>();

                foreach (var method in methods)
                {
                    var customAttribute = method.GetCustomAttribute<JSInvokableAttribute>(inherit: false);
                    if (customAttribute == null)
                    {
                        continue;
                    }

                    var identifier = customAttribute.Identifier ?? method.Name;

                    var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

                    if (dictionary.ContainsKey(identifier))
                    {
                        throw new InvalidOperationException($"The type with Id '{objectId}' contains more than one " +
                            $"[JSInvokable] method with identifier '{methodIdentifier}'. All [JSInvokable] methods within the same " +
                            $"scope must have unique identifiers. You can pass a custom identifier as a parameter to " +
                            $"the [JSInvokable] attribute.");
                    }

                    var invoker = CreateInvoker(method);

                    dictionary.Add(identifier, (invoker, parameterTypes));
                }

                cachedMethods = dictionary;
                _cachedMethodsByType.TryAdd(type, cachedMethods);
            }


            if (!cachedMethods.TryGetValue(methodIdentifier, out var result))
            {
                throw new ArgumentException($"DotNetObjectReference with id '{objectId}' does not contain a public method with [{nameof(JSInvokableAttribute)}(\"{methodIdentifier}\")].");
            }

            return result;
        }

        private static (InvokeMethod, Type[]) GetCachedMethodInfo(AssemblyKey assemblyKey, string methodIdentifier)
        {
            if (string.IsNullOrWhiteSpace(assemblyKey.AssemblyName))
            {
                throw new ArgumentException("Cannot be null, empty, or whitespace.", nameof(assemblyKey.AssemblyName));
            }

            if (string.IsNullOrWhiteSpace(methodIdentifier))
            {
                throw new ArgumentException("Cannot be null, empty, or whitespace.", nameof(methodIdentifier));
            }

            var assemblyMethods = _cachedMethodsByAssembly.GetOrAdd(assemblyKey, ScanAssemblyForCallableMethods);
            if (assemblyMethods.TryGetValue(methodIdentifier, out var result))
            {
                return result;
            }
            else
            {
                throw new ArgumentException($"The assembly '{assemblyKey.AssemblyName}' does not contain a public method with [{nameof(JSInvokableAttribute)}(\"{methodIdentifier}\")].");
            }
        }

        private static Dictionary<string, (InvokeMethod, Type[])> ScanAssemblyForCallableMethods(AssemblyKey assemblyKey)
        {
            // TODO: Consider looking first for assembly-level attributes (i.e., if there are any,
            // only use those) to avoid scanning, especially for framework assemblies.
            var result = new Dictionary<string, (InvokeMethod, Type[])>(StringComparer.Ordinal);
            var invokableMethods = GetRequiredLoadedAssembly(assemblyKey)
                .GetExportedTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly |
                    BindingFlags.Static))
                .Where(method => method.IsDefined(typeof(JSInvokableAttribute), inherit: false));
            foreach (var method in invokableMethods)
            {
                var identifier = method.GetCustomAttribute<JSInvokableAttribute>(inherit: false).Identifier ?? method.Name;
                var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

                try
                {
                    result.Add(identifier, (CreateInvoker(method), parameterTypes));
                }
                catch (ArgumentException)
                {
                    if (result.ContainsKey(identifier))
                    {
                        throw new InvalidOperationException($"The assembly '{assemblyKey.AssemblyName}' contains more than one " +
                            $"[JSInvokable] method with identifier '{identifier}'. All [JSInvokable] methods within the same " +
                            $"assembly must have different identifiers. You can pass a custom identifier as a parameter to " +
                            $"the [JSInvokable] attribute.");
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return result;
        }

        private static InvokeMethod CreateInvoker(MethodInfo method)
        {
            var count = 0;
            InvokeMethod invokeMethod = null;
            return (object instance, object[] parameters) =>
            {
                if (invokeMethod != null)
                {
                    return invokeMethod(instance, parameters);
                }

                if (count++ == 0)
                {
                    // Use reflection if this method is only ever called once.
                    return method.Invoke(instance, parameters);
                }

                // If it's called more often, create a delegate.
                // The assignment will result in safe races.
                invokeMethod = (InvokeMethod)method.CreateDelegate(typeof(InvokeMethod));
                return invokeMethod(instance, parameters);
            };
        }

        private static Assembly GetRequiredLoadedAssembly(AssemblyKey assemblyKey)
        {
            // We don't want to load assemblies on demand here, because we don't necessarily trust
            // "assemblyName" to be something the developer intended to load. So only pick from the
            // set of already-loaded assemblies.
            // In some edge cases this might force developers to explicitly call something on the
            // target assembly (from .NET) before they can invoke its allowed methods from JS.
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Using LastOrDefault to workaround for https://github.com/dotnet/arcade/issues/2816.
            // In most ordinary scenarios, we wouldn't have two instances of the same Assembly in the AppDomain
            // so this doesn't change the outcome.
            var assembly = loadedAssemblies.LastOrDefault(a => new AssemblyKey(a).Equals(assemblyKey));

            return assembly
                ?? throw new ArgumentException($"There is no loaded assembly with the name '{assemblyKey.AssemblyName}'.");
        }

        private readonly struct AssemblyKey : IEquatable<AssemblyKey>
        {
            public AssemblyKey(Assembly assembly)
            {
                Assembly = assembly;
                AssemblyName = assembly.GetName().Name;
            }

            public AssemblyKey(string assemblyName)
            {
                Assembly = null;
                AssemblyName = assemblyName;
            }

            public Assembly Assembly { get; }

            public string AssemblyName { get; }

            public bool Equals(AssemblyKey other)
            {
                if (Assembly != null && other.Assembly != null)
                {
                    return Assembly == other.Assembly;
                }

                return AssemblyName.Equals(other.AssemblyName, StringComparison.Ordinal);
            }

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(AssemblyName);
        }

    }
}
