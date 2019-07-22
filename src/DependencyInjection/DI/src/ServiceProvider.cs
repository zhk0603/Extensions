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
    public sealed class ServiceProvider : IServiceProvider, IDisposable, IServiceProviderEngineCallback
#if DISPOSE_ASYNC
        , IAsyncDisposable
#endif
    {
        private readonly IEnumerable<ServiceDescriptor> _serviceDescriptors;

        private readonly IServiceProviderEngine _engine;

        private readonly CallSiteValidator _callSiteValidator;

        internal ServiceProvider(IEnumerable<ServiceDescriptor> serviceDescriptors, ServiceProviderOptions options)
        {
            _serviceDescriptors = serviceDescriptors;
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
                    _engine = new DynamicServiceProviderEngine(serviceDescriptors, callback);
#else
                    if (RuntimeFeature.IsSupported("IsDynamicCodeCompiled"))
                    {
                        _engine = new DynamicServiceProviderEngine(serviceDescriptors, callback);
                    }
                    else
                    {
                        // Don't try to compile Expressions/IL if they are going to get interpreted
                        _engine = new RuntimeServiceProviderEngine(serviceDescriptors, callback);
                    }
#endif
                    break;
                case ServiceProviderMode.Dynamic:
                    _engine = new DynamicServiceProviderEngine(serviceDescriptors, callback);
                    break;
                case ServiceProviderMode.Runtime:
                    _engine = new RuntimeServiceProviderEngine(serviceDescriptors, callback);
                    break;
#if IL_EMIT
                case ServiceProviderMode.ILEmit:
                    _engine = new ILEmitServiceProviderEngine(serviceDescriptors, callback);
                    break;
#endif
                case ServiceProviderMode.Expressions:
                    _engine = new ExpressionsServiceProviderEngine(serviceDescriptors, callback);
                    break;
                default:
                    throw new NotSupportedException(nameof(options.Mode));
            }

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

        class CecilResolverBuilder
        {
            private readonly ServiceDescriptor[] _descriptors;

            public CecilResolverBuilder(ServiceDescriptor[] descriptors)
            {
                _descriptors = descriptors;
            }

            public AssemblyDefinition Build()
            {
                var context = new CecilResolverBuilderContext()
                {
                    AssemblyDefinition = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("DI", new Version(1, 0, 0, 0)), "mail", ModuleKind.Dll),

                };

                var assemblyDefinition = context.AssemblyDefinition;
                var serviceDescriptors = _descriptors.ToArray();

                var f = new CallSiteFactory(serviceDescriptors);
                f.Add(typeof(IServiceProvider), new ServiceProviderCallSite());
                f.Add(typeof(IServiceScopeFactory), new ServiceScopeFactoryCallSite());

                TypeDefinition rootScope = new TypeDefinition("DI", "RootScope", TypeAttributes.Class);
                TypeDefinition scope = new TypeDefinition("DI", "Scope", TypeAttributes.Class);

                assemblyDefinition.MainModule.Types.Add(rootScope);

                var v = assemblyDefinition.MainModule.ImportReference(typeof(void));
                var o = assemblyDefinition.MainModule.ImportReference(typeof(object));
                var t = assemblyDefinition.MainModule.ImportReference(typeof(Type));
                var gtt = assemblyDefinition.MainModule.ImportReference(t.Resolve().Methods.Single(m => m.Name == "GetTypeFromHandle"));
                var teq = assemblyDefinition.MainModule.ImportReference(t.Resolve().Methods.Single(m => m.Name == "op_Equality"));

                var sd = assemblyDefinition.MainModule.ImportReference(typeof(ServiceDescriptor));
                var sdf = assemblyDefinition.MainModule.ImportReference(sd.Resolve().Properties.Single(p => p.Name == nameof(ServiceDescriptor.ImplementationFactory)).GetMethod);
                var sdi = assemblyDefinition.MainModule.ImportReference(sd.Resolve().Properties.Single(p => p.Name == nameof(ServiceDescriptor.ImplementationInstance)).GetMethod);

                MethodDefinition initMethod = new MethodDefinition("SetGlobals", MethodAttributes.Public, v);
                var parameterDefinition = new ParameterDefinition("descriptors", ParameterAttributes.None, sd.MakeArrayType());
                initMethod.Parameters.Add(parameterDefinition);
                var setGlobalsBuilder = initMethod.Body.GetILProcessor();

                int i = 0;
                foreach (var descriptor in serviceDescriptors)
                {
                    if (descriptor.ImplementationFactory != null || descriptor.ImplementationInstance != null)
                    {
                        var field = new FieldDefinition("constant" + i, FieldAttributes.Private, o);

                        setGlobalsBuilder.Emit(OpCodes.Ldarg_0);

                        setGlobalsBuilder.Emit(OpCodes.Ldarg, parameterDefinition);
                        setGlobalsBuilder.Emit(OpCodes.Ldc_I4, i);
                        setGlobalsBuilder.Emit(OpCodes.Ldelem_Ref);

                        if (descriptor.ImplementationFactory != null)
                        {
                            setGlobalsBuilder.Emit(OpCodes.Callvirt, sdf);
                        }
                        else
                        {
                            setGlobalsBuilder.Emit(OpCodes.Callvirt, sdi);
                        }

                        setGlobalsBuilder.Emit(OpCodes.Stfld, field);
                        rootScope.Fields.Add(field);
                    }

                    i++;
                }
                setGlobalsBuilder.Emit(OpCodes.Ret);

                rootScope.Methods.Add(initMethod);

                MethodDefinition resolveMethod = new MethodDefinition("Resolve", MethodAttributes.Public, o);
                var resolveTypeParameter = new ParameterDefinition("type", ParameterAttributes.None, t);
                resolveMethod.Parameters.Add(resolveTypeParameter);
                rootScope.Methods.Add(resolveMethod);
                var resolveMethodBuilder = resolveMethod.Body.GetILProcessor();

                foreach (var descriptor in serviceDescriptors)
                {
                    if (!descriptor.ServiceType.IsGenericType || descriptor.ServiceType.IsConstructedGenericType)
                    {
                        var callsite = f.GetCallSite(descriptor.ServiceType, new CallSiteChain());
                        var imported = assemblyDefinition.MainModule.ImportReference(callsite.ServiceType);

                        resolveMethodBuilder.Emit(OpCodes.Ldtoken, imported);
                        resolveMethodBuilder.Emit(OpCodes.Call, gtt);

                        resolveMethodBuilder.Emit(OpCodes.Ldarg, resolveTypeParameter);
                        resolveMethodBuilder.Emit(OpCodes.Call, teq);
                        var jumpPlaceholder = Instruction.Create(OpCodes.Nop);
                        resolveMethodBuilder.Append(jumpPlaceholder);
                        // Put resolve code here

                        EmitResolver(resolveMethodBuilder, callsite, o);

                        EmitCWL(resolveMethodBuilder, "Resolving " + callsite.ServiceType);

                        var nop = Instruction.Create(OpCodes.Nop);
                        // Adjust jump
                        resolveMethodBuilder.Replace(jumpPlaceholder, Instruction.Create(OpCodes.Brfalse, nop));
                        resolveMethodBuilder.Append(nop);
                    }
                }

                resolveMethodBuilder.Emit(OpCodes.Ldnull);
                resolveMethodBuilder.Emit(OpCodes.Ret);
            }
            internal class CecilResolverBuilderContext
            {
                public AssemblyDefinition AssemblyDefinition { get; set; }
            }
        }
        private MethodDefinition EmitResolver(TypeDefinition type, ServiceCallSite callsite)
        {
            return new MethodDefinition("Resolve_" + callsite.ServiceType.Name, MethodAttributes.Private, );
        }

        void EmitCWL(ILProcessor p, string text)
        {
            var console = p.Body.Method.Module.ImportReference(typeof(Console));
            var cwl = p.Body.Method.Module.ImportReference(console
                .Resolve().Methods
                .First(m => m.Name == "WriteLine" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.MetadataType == MetadataType.String));
            p.Emit(OpCodes.Ldstr, text);
            p.Emit(OpCodes.Call, cwl);
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
