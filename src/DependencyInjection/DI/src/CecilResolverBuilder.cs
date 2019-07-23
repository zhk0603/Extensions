// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.ServiceLookup;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Microsoft.Extensions.DependencyInjection
{
    class CecilResolverBuilder : CallSiteVisitor<CecilResolverBuilder.CecilResolverMethodBuilderContext, object>
    {
        private static readonly MethodAttributes PublicCtorAttributes =
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
        private readonly ServiceDescriptor[] _descriptors;

        public CecilResolverBuilder(ServiceDescriptor[] descriptors)
        {
            _descriptors = descriptors;
        }

        private static string FormatTypeName(TypeReference type)
        {
            if (type.IsGenericInstance)
            {
                var genericInstance = (GenericInstanceType)type;
                return $"{type.Name.Substring(0, type.Name.IndexOf('`'))}_{string.Join("_", genericInstance.GenericArguments.Select(p => FormatTypeName(p)).ToArray())}_";
            }
            return type.Name;
        }

        public AssemblyDefinition Build()
        {
            var context = new CecilResolverBuilderContext()
            {
                AssemblyDefinition = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("DI", new Version(1, 0, 0, 0)), "mail", ModuleKind.Dll),

            };

            var assemblyDefinition = context.AssemblyDefinition;
            var serviceDescriptors = _descriptors.ToArray();

            var ignoreChecksAttribute = EmitIgnoresAccessChecksToAttribute(context);

            assemblyDefinition.MainModule.Types.Add(ignoreChecksAttribute);

            var f = new CallSiteFactory(serviceDescriptors);
            f.Add(typeof(IServiceProvider), new ServiceProviderCallSite());
            f.Add(typeof(IServiceScopeFactory), new ServiceScopeFactoryCallSite());

            TypeDefinition rootScope = new TypeDefinition("DI", "RootScope", TypeAttributes.Class, context.BaseEngineTypeReference);

            assemblyDefinition.MainModule.Types.Add(rootScope);
            context.RootScopeTypeDefinition = rootScope;

            var sd = assemblyDefinition.MainModule.ImportReference(typeof(ServiceDescriptor));
            var sdf = assemblyDefinition.MainModule.ImportReference(sd.Resolve().Properties.Single(p => p.Name == nameof(ServiceDescriptor.ImplementationFactory)).GetMethod);
            var sdi = assemblyDefinition.MainModule.ImportReference(sd.Resolve().Properties.Single(p => p.Name == nameof(ServiceDescriptor.ImplementationInstance)).GetMethod);

            MethodDefinition initMethod = new MethodDefinition(".ctor", PublicCtorAttributes, context.AssemblyDefinition.MainModule.TypeSystem.Void);
            var parameterDefinition = new ParameterDefinition("descriptors", ParameterAttributes.None, sd.MakeArrayType());
            initMethod.Parameters.Add(parameterDefinition);
            var setGlobalsBuilder = initMethod.Body.GetILProcessor();

            int i = 0;
            foreach (var descriptor in serviceDescriptors)
            {
                if (descriptor.ImplementationFactory != null || descriptor.ImplementationInstance != null)
                {
                    var field = new FieldDefinition("constant" + i, FieldAttributes.Private, context.ObjectTypeReference);

                    setGlobalsBuilder.Emit(OpCodes.Ldarg_0);

                    setGlobalsBuilder.Emit(OpCodes.Ldarg, parameterDefinition);
                    setGlobalsBuilder.Emit(OpCodes.Ldc_I4, i);
                    setGlobalsBuilder.Emit(OpCodes.Ldelem_Ref);

                    setGlobalsBuilder.Emit(OpCodes.Callvirt, descriptor.ImplementationFactory != null ? sdf: sdi);

                    setGlobalsBuilder.Emit(OpCodes.Stfld, field);
                    rootScope.Fields.Add(field);
                    context.DescriptorConstants.Add(descriptor, field);
                }

                i++;
            }
            setGlobalsBuilder.Emit(OpCodes.Ret);

            rootScope.Methods.Add(initMethod);

            MethodDefinition resolveMethod = new MethodDefinition("GetService", MethodAttributes.Public| MethodAttributes.Virtual, context.ObjectTypeReference);
            var resolveTypeParameter = new ParameterDefinition("type", ParameterAttributes.None, context.TypeTypeReference);
            resolveMethod.Parameters.Add(resolveTypeParameter);
            rootScope.Methods.Add(resolveMethod);
            var resolveMethodBuilder = resolveMethod.Body.GetILProcessor();

            foreach (var descriptor in serviceDescriptors)
            {
                if (!descriptor.ServiceType.IsGenericType || descriptor.ServiceType.IsConstructedGenericType)
                {
                    context.ResolvedServices.Enqueue(descriptor.ServiceType);
                }
            }

            while (context.ResolvedServices.Any())
            {
                var service = context.ResolvedServices.Dequeue();

                if (context.Resolvers.ContainsKey(service))
                {
                    continue;
                }

                var callsite = f.GetCallSite(service, new CallSiteChain());
                var imported = assemblyDefinition.MainModule.ImportReference(callsite.ServiceType);

                resolveMethodBuilder.Emit(OpCodes.Ldtoken, imported);
                resolveMethodBuilder.Emit(OpCodes.Call, context.Type_GetTypeFromHandleMethodReference);

                resolveMethodBuilder.Emit(OpCodes.Ldarg, resolveTypeParameter);
                resolveMethodBuilder.Emit(OpCodes.Call, context.Type_OpEqualityMethodReference);
                var jumpPlaceholder = Instruction.Create(OpCodes.Nop);
                resolveMethodBuilder.Append(jumpPlaceholder);
                // Put resolve code here

                var resolver = EmitResolver(context, callsite);
                context.Resolvers.Add(service, resolver);

                rootScope.Methods.Add(resolver);

                EmitCWL(context, resolveMethodBuilder, "Resolving " + callsite.ServiceType);

                resolveMethodBuilder.Emit(OpCodes.Ldarg_0);

                resolveMethodBuilder.Emit(OpCodes.Callvirt, resolver);
                resolveMethodBuilder.Emit(OpCodes.Ret);

                var nop = Instruction.Create(OpCodes.Nop);
                // Adjust jump
                resolveMethodBuilder.Replace(jumpPlaceholder, Instruction.Create(OpCodes.Brfalse, nop));
                resolveMethodBuilder.Append(nop);
            }

            resolveMethodBuilder.Emit(OpCodes.Ldnull);
            resolveMethodBuilder.Emit(OpCodes.Ret);

            foreach (var aref in context.AssemblyDefinition.MainModule.AssemblyReferences)
            {

                var a = new CustomAttribute(ignoreChecksAttribute.GetConstructors().Single());
                a.ConstructorArguments.Add(new CustomAttributeArgument(context.AssemblyDefinition.MainModule.TypeSystem.String, aref.Name));
                assemblyDefinition.CustomAttributes.Add(a);
            }

            return assemblyDefinition;
        }

        protected override object VisitCallSiteMain(ServiceCallSite callSite, CecilResolverMethodBuilderContext argument)
        {
            return base.VisitCallSiteMain(callSite, argument);
        }


        public class CecilResolverBuilderContext
        {
            private TypeReference _objecttypeReference;
            private TypeReference _consoleReferenc;
            private MethodReference _consoleWriteLineReference;
            private TypeReference _typeTypeReference;
            private MethodReference _typeOpEqualityMethodReference;
            private MethodReference _typeGetTypeFromHandleMethodReference;
            private TypeReference _arrayTypeReference;

            public AssemblyDefinition AssemblyDefinition { get; set; }

            public TypeDefinition RootScopeTypeDefinition { get; set; }

            public Dictionary<ServiceCacheKey, FieldDefinition> SingletonCaches { get; set; } = new Dictionary<ServiceCacheKey, FieldDefinition>();

            public Dictionary<ServiceDescriptor, FieldDefinition> DescriptorConstants { get; set; } = new Dictionary<ServiceDescriptor, FieldDefinition>();

            public Queue<Type> ResolvedServices { get; set; } = new Queue<Type>();

            public Dictionary<Type, MethodDefinition> Resolvers { get; set; } = new Dictionary<Type, MethodDefinition>();

            public TypeReference BaseEngineTypeReference => _arrayTypeReference ??= AssemblyDefinition.MainModule.ImportReference(typeof(NiceServiceProviderEngine));

            public TypeReference ArrayTypeReference => _arrayTypeReference ??= AssemblyDefinition.MainModule.ImportReference(typeof(Type));

            public MethodReference ArrayEmptyMethodReference => _typeGetTypeFromHandleMethodReference ??= AssemblyDefinition.MainModule.ImportReference(
   TypeTypeReference.Resolve().Methods.Single(m => m.Name == "Empty"));

            public TypeReference TypeTypeReference => _typeTypeReference ??= AssemblyDefinition.MainModule.ImportReference(typeof(Type));
            public MethodReference Type_OpEqualityMethodReference => _typeOpEqualityMethodReference ??= AssemblyDefinition.MainModule.ImportReference(
                TypeTypeReference.Resolve().Methods.Single(m => m.Name == "op_Equality"));
            public MethodReference Type_GetTypeFromHandleMethodReference => _typeGetTypeFromHandleMethodReference ??= AssemblyDefinition.MainModule.ImportReference(
               TypeTypeReference.Resolve().Methods.Single(m => m.Name == "GetTypeFromHandle"));
            public TypeReference ObjectTypeReference => _objecttypeReference ??= AssemblyDefinition.MainModule.ImportReference(typeof(object));
            public TypeReference ConsoleTypeReference => _consoleReferenc ??= AssemblyDefinition.MainModule.ImportReference(typeof(Console));
            public MethodReference ConsoleWriteLineMethodReference => _consoleWriteLineReference ??= AssemblyDefinition.MainModule.ImportReference(
                ConsoleTypeReference.Resolve().Methods
                .First(m => m.Name == "WriteLine" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.MetadataType == MetadataType.String)
                );
        }

        private TypeDefinition EmitIgnoresAccessChecksToAttribute(CecilResolverBuilderContext context)
        {
            var type = new TypeDefinition("System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute", TypeAttributes.Class);
            var field = new FieldDefinition("_assemblyName", FieldAttributes.Private, context.AssemblyDefinition.MainModule.TypeSystem.String);

            type.Fields.Add(field);

            var property = new PropertyDefinition("AssemblyName", PropertyAttributes.None, context.AssemblyDefinition.MainModule.TypeSystem.String);
            MethodAttributes getMethodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot;
            var methodName = "get_" + property.Name;
            var getter = new MethodDefinition(methodName, getMethodAttributes, property.PropertyType)
            {
                IsGetter = true,
                Body = { InitLocals = true },
            };

            property.GetMethod = getter;
            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);

            type.Properties.Add(property);

            var param = new ParameterDefinition("name", ParameterAttributes.None, context.AssemblyDefinition.MainModule.TypeSystem.String);
            var ctor = new MethodDefinition(".ctor", PublicCtorAttributes, context.AssemblyDefinition.MainModule.TypeSystem.Void);
            ctor.Parameters.Add(param);

            var ctorIL = ctor.Body.GetILProcessor();
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_1);
            ctorIL.Emit(OpCodes.Stfld, field);
            ctorIL.Emit(OpCodes.Ret);

            type.Methods.Add(ctor);
            type.Methods.Add(getter);

            return type;
        }

        private MethodDefinition EmitResolver(CecilResolverBuilderContext context, ServiceCallSite callsite)
        {
            var imported = context.AssemblyDefinition.MainModule.ImportReference(callsite.ServiceType);
            var method = new MethodDefinition("Resolve_" + FormatTypeName(imported), MethodAttributes.Private, imported);
            var methodBuilderContext = new CecilResolverMethodBuilderContext()
            {
                GlobalContext = context,
                ResolverMethod = method,
                ILProcessor = method.Body.GetILProcessor()
            };

            VisitCallSite(callsite, methodBuilderContext);

            methodBuilderContext.ILProcessor.Emit(OpCodes.Ret);
            return method;
        }

        void EmitCWL(CecilResolverBuilderContext context, ILProcessor p, string text)
        {
            p.Emit(OpCodes.Ldstr, text);
            p.Emit(OpCodes.Call, context.ConsoleWriteLineMethodReference);
        }

        protected override object VisitConstructor(ConstructorCallSite constructorCallSite, CecilResolverMethodBuilderContext argument)
        {
            foreach (var parameterCallSite in constructorCallSite.ParameterCallSites)
            {
                VisitCallSite(parameterCallSite, argument);
            }

            var imported = argument.GlobalContext.AssemblyDefinition.MainModule.ImportReference(constructorCallSite.ConstructorInfo);
            argument.ILProcessor.Emit(OpCodes.Newobj, imported);
            return null;
        }

        protected override object VisitConstant(ConstantCallSite constantCallSite, CecilResolverMethodBuilderContext argument)
        {
            switch (constantCallSite.DefaultValue)
            {
                default:
                    break;
            }

            argument.ILProcessor.Emit(OpCodes.Ldnull);
            return null;
        }

        protected override object VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, CecilResolverMethodBuilderContext argument)
        {
            argument.ILProcessor.Emit(OpCodes.Ldnull);
            return null;
        }

        protected override object VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, CecilResolverMethodBuilderContext argument)
        {
            argument.ILProcessor.Emit(OpCodes.Ldnull);
            return null;
        }

        protected override object VisitIEnumerable(IEnumerableCallSite enumerableCallSite, CecilResolverMethodBuilderContext argument)
        {
            // var array = new ItemType[];
            // array[0] = [Create argument0];
            // array[1] = [Create argument1];
            // ...

            var imported = argument.GlobalContext.AssemblyDefinition.MainModule.ImportReference(enumerableCallSite.ItemType);
            argument.ILProcessor.Emit(OpCodes.Ldc_I4, enumerableCallSite.ServiceCallSites.Length);
            argument.ILProcessor.Emit(OpCodes.Newarr, imported);
            for (int i = 0; i < enumerableCallSite.ServiceCallSites.Length; i++)
            {
                // duplicate array
                argument.ILProcessor.Emit(OpCodes.Dup);
                // push index
                argument.ILProcessor.Emit(OpCodes.Ldc_I4, i);
                // create parameter
                VisitCallSite(enumerableCallSite.ServiceCallSites[i], argument);
                // store
                argument.ILProcessor.Emit(OpCodes.Stelem_Ref);
            }

            return null;
        }

        static MethodReference MakeGeneric(MethodReference method, TypeReference declaringType)
        {
            var reference = new MethodReference(method.Name, method.ReturnType, declaringType);
            reference.CallingConvention = MethodCallingConvention.Generic;
            foreach (ParameterDefinition parameter in method.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            return reference;
        }

        protected override object VisitFactory(FactoryCallSite factoryCallSite, CecilResolverMethodBuilderContext argument)
        {
            argument.ILProcessor.Emit(OpCodes.Ldnull);
            return null;
        }

        protected override object VisitDisposeCache(ServiceCallSite callSite, CecilResolverMethodBuilderContext argument)
        {
            return base.VisitDisposeCache(callSite, argument);
        }

        protected override object VisitCallSite(ServiceCallSite callSite, CecilResolverMethodBuilderContext argument)
        {
            if (!argument.GlobalContext.Resolvers.ContainsKey(callSite.ServiceType))
            {
                argument.GlobalContext.ResolvedServices.Enqueue(callSite.ServiceType);
            }
            return base.VisitCallSite(callSite, argument);
        }

        protected override object VisitRootCache(ServiceCallSite callSite, CecilResolverMethodBuilderContext argument)
        {
            var field = GetSingletonCacheField(argument.GlobalContext, callSite.Cache.Key);
            argument.ILProcessor.Emit(OpCodes.Ldarg_0);
            argument.ILProcessor.Emit(OpCodes.Ldfld, field);

            argument.ILProcessor.Emit(OpCodes.Ldnull);
            argument.ILProcessor.Emit(OpCodes.Ceq);

            var nopPlaceholder = Instruction.Create(OpCodes.Nop);
            argument.ILProcessor.Append(nopPlaceholder);

            argument.ILProcessor.Emit(OpCodes.Ldarg_0);

            VisitCallSiteMain(callSite, argument);

            argument.ILProcessor.Emit(OpCodes.Stfld, field);

            var nop = Instruction.Create(OpCodes.Nop);
            argument.ILProcessor.Replace(nopPlaceholder, Instruction.Create(OpCodes.Brfalse, nop));

            argument.ILProcessor.Append(nop);
            argument.ILProcessor.Emit(OpCodes.Ldarg_0);
            argument.ILProcessor.Emit(OpCodes.Ldfld, field);

            return null;
        }

        private FieldDefinition GetSingletonCacheField(CecilResolverBuilderContext argument, ServiceCacheKey key)
        {
            var d = argument.SingletonCaches;
            if (!d.TryGetValue(key, out var field))
            {
                var imported = argument.AssemblyDefinition.MainModule.ImportReference(key.Type);
                field = new FieldDefinition("Cached" + key.Type.Name + "_" + key.Slot, FieldAttributes.Private, imported);
                argument.RootScopeTypeDefinition.Fields.Add(field);
                d[key] = field;
            }
            return field;
        }

        internal struct CecilResolverMethodBuilderContext
        {
            public CecilResolverBuilderContext GlobalContext { get; set; }
            public MethodDefinition ResolverMethod { get; set; }
            public ILProcessor ILProcessor { get; set; }
        }
    }
}
