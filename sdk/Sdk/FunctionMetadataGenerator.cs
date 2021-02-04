﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Microsoft.Azure.Functions.Worker.Sdk
{
    internal class FunctionMetadataGenerator
    {
        private const string BindingType = "Microsoft.Azure.Functions.Worker.Extensions.Abstractions.BindingAttribute";
        private const string OutputBindingType = "Microsoft.Azure.Functions.Worker.Extensions.Abstractions.OutputBindingAttribute";
        private const string FunctionNameType = "Microsoft.Azure.Functions.Worker.Extensions.Abstractions.FunctionNameAttribute";
        private const string ExtensionsInformationType = "Microsoft.Azure.Functions.Worker.Extensions.Abstractions.ExtensionInformationAttribute";

        private readonly IndentableLogger _logger;

        // TODO: Verify that we don't need to allow
        // same extensions of different versions. Picking the last version for now.
        // We can also just add all the versions of extensions and then let the
        // build pick the one it likes.
        private readonly IDictionary<string, string> _extensions;

        public FunctionMetadataGenerator()
            : this((l, m) => { })
        {
            _extensions = new Dictionary<string, string>();
        }

        public FunctionMetadataGenerator(Action<TraceLevel, string> log)
        {
            _logger = new IndentableLogger(log);
            _extensions = new Dictionary<string, string>();
        }

        public IDictionary<string, string> Extensions
        {
            get
            {
                return _extensions;
            }
        }

        public IEnumerable<SdkFunctionMetadata> GenerateFunctionMetadata(string assemblyPath, IEnumerable<string> referencePaths)
        {
            string sourcePath = Path.GetDirectoryName(assemblyPath);
            string[] targetAssemblies = Directory.GetFiles(sourcePath, "*.dll");

            var functions = new List<SdkFunctionMetadata>();

            _logger.LogMessage($"Found { targetAssemblies.Length} assemblies to evaluate in '{sourcePath}':");

            foreach (var path in targetAssemblies)
            {
                using (_logger.Indent())
                {
                    _logger.LogMessage($"{Path.GetFileName(path)}");

                    using (_logger.Indent())
                    {
                        try
                        {
                            BaseAssemblyResolver resolver = new DefaultAssemblyResolver();

                            foreach (string referencePath in referencePaths.Select(p => Path.GetDirectoryName(p)).Distinct())
                            {
                                resolver.AddSearchDirectory(referencePath);
                            }

                            resolver.AddSearchDirectory(Path.GetDirectoryName(path));

                            ReaderParameters readerParams = new ReaderParameters { AssemblyResolver = resolver };

                            var moduleDefintion = ModuleDefinition.ReadModule(path, readerParams);

                            functions.AddRange(GenerateFunctionMetadata(moduleDefintion));
                        }
                        catch (BadImageFormatException)
                        {
                            _logger.LogMessage($"Skipping file '{Path.GetFileName(path)}' because of a {nameof(BadImageFormatException)}.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Could not evaluate '{Path.GetFileName(path)}' for functions metadata. Exception message: {ex.ToString()}");
                        }
                    }
                }
            }

            return functions;
        }

        internal IEnumerable<SdkFunctionMetadata> GenerateFunctionMetadata(ModuleDefinition module)
        {
            var functions = new List<SdkFunctionMetadata>();

            foreach (TypeDefinition type in module.Types)
            {
                var functionsResult = GenerateFunctionMetadata(type).ToArray();
                if (functionsResult.Any())
                {
                    _logger.LogMessage($"Found {functionsResult.Length} functions in '{type.GetReflectionFullName()}'.");
                }

                functions.AddRange(functionsResult);
            }

            return functions;
        }

        internal IEnumerable<SdkFunctionMetadata> GenerateFunctionMetadata(TypeDefinition type)
        {
            var functions = new List<SdkFunctionMetadata>();

            foreach (MethodDefinition method in type.Methods)
            {
                AddFunctionMetadataIfFunction(functions, method);
            }

            return functions;
        }

        private void AddFunctionMetadataIfFunction(IList<SdkFunctionMetadata> functions, MethodDefinition method)
        {
            if (TryCreateFunctionMetadata(method, out SdkFunctionMetadata? metadata)
                && metadata != null)
            {
                var allBindings = CreateBindingMetadataAndAddExtensions(method);

                foreach(var binding in allBindings)
                {
                    metadata.Bindings.Add(binding);
                }

                functions.Add(metadata);
            }
        }

        private bool TryCreateFunctionMetadata(MethodDefinition method, out SdkFunctionMetadata? function)
        {
            function = null;

            foreach (CustomAttribute attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == FunctionNameType)
                {
                    string functionName = attribute.ConstructorArguments.SingleOrDefault().Value.ToString();

                    if (string.IsNullOrEmpty(functionName))
                    {
                        continue;
                    }

                    TypeDefinition declaringType = method.DeclaringType;

                    string actualMethodName = method.Name;
                    string declaryingTypeName = declaringType.GetReflectionFullName();
                    string assemblyName = declaringType.Module.Assembly.Name.Name;

                    function = CreateSdkFunctionMetadata(functionName, actualMethodName, declaryingTypeName, assemblyName);

                    return true;
                }
            }

            return false;
        }

        private SdkFunctionMetadata CreateSdkFunctionMetadata(string functionName, string actualMethodName, string declaringTypeName, string assemblyName)
        {
            var function = new SdkFunctionMetadata
            {
                Name = functionName,
                ScriptFile = $"{assemblyName}.dll",
                EntryPoint = $"{declaringTypeName}.{actualMethodName}",
                Language = "dotnet-isolated"
            };

            function.Properties["IsCodeless"] = false;

            return function;
        }

        private IEnumerable<ExpandoObject> CreateBindingMetadataAndAddExtensions(MethodDefinition method)
        {
            var bindingMetadata = new List<ExpandoObject>();

            AddInputTriggerBindingsAndExtensions(bindingMetadata, method);
            AddOutputBindingsAndExtensions(bindingMetadata, method);

            return bindingMetadata;
        }

        private void AddOutputBindingsAndExtensions(IList<ExpandoObject> bindingMetadata, MethodDefinition method)
        {
            foreach (CustomAttribute methodAttribute in method.CustomAttributes)
            {
                if (IsOutputBindingType(methodAttribute))
                {
                    AddOutputBindingMetadata(bindingMetadata, methodAttribute);
                    AddExtensionInfo(_extensions, methodAttribute);
                }
            }
        }

        private void AddInputTriggerBindingsAndExtensions(IList<ExpandoObject> bindingMetadata, MethodDefinition method)
        {
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                foreach (CustomAttribute parameterAttribute in parameter.CustomAttributes)
                {
                    if (IsFunctionBindingType(parameterAttribute))
                    {
                        AddBindingMetadata(bindingMetadata, parameterAttribute, parameter.Name);
                        AddExtensionInfo(_extensions, parameterAttribute);
                    }
                }
            }
        }

        private static void AddOutputBindingMetadata(IList<ExpandoObject> bindingMetadata, CustomAttribute attribute)
        {
            AddBindingMetadata(bindingMetadata, attribute, parameterName: null);
        }

        private static void AddBindingMetadata(IList<ExpandoObject> bindingMetadata, CustomAttribute attribute, string? parameterName)
        {
            string bindingType = GetBindingType(attribute);

            ExpandoObject binding = BuildBindingMetadataFromAttribute(attribute, bindingType, parameterName);
            bindingMetadata.Add(binding);

            // TODO: Fix $return detection
            // auto-add a return type for http for now
            AddHttpOutputBindingIfHttp(bindingMetadata, bindingType);
        }

        private static ExpandoObject BuildBindingMetadataFromAttribute(CustomAttribute attribute, string bindingType, string? parameterName)
        {
            ExpandoObject binding = new ExpandoObject();

            var bindingDict = (IDictionary<string, object>)binding;

            if (!string.IsNullOrEmpty(parameterName))
            {
                bindingDict["Name"] = parameterName!;
            }

            bindingDict["Type"] = bindingType;
            bindingDict["Direction"] = GetBindingDirection(attribute);

            foreach (var property in attribute.GetAllDefinedProperties())
            {
                bindingDict.Add(property.Key, property.Value);
            }

            return binding;
        }

        private static string GetBindingType(CustomAttribute attribute)
        {
            var attributeType = attribute.AttributeType.Name;

            // TODO: fix this if we continue to use "<>EventAttribute" (questionable)
            // TODO: Should "webjob type" be a property of the "worker types" and come from there?
            return attributeType
                    .Replace("TriggerAttribute", "Trigger")
                    .Replace("InputAttribute", string.Empty)
                    .Replace("OutputAttribute", string.Empty);
        }

        private static void AddHttpOutputBindingIfHttp(IList<ExpandoObject> bindingMetadata, string bindingType)
        {
            if (string.Equals(bindingType, "httptrigger", StringComparison.OrdinalIgnoreCase))
            {
                IDictionary<string, object> returnBinding = new ExpandoObject();
                returnBinding["Name"] = "$return";
                returnBinding["Type"] = "http";
                returnBinding["Direction"] = "Out";

                bindingMetadata.Add((ExpandoObject)returnBinding);
            }
        }

        private static void AddExtensionInfo(IDictionary<string, string> extensions, CustomAttribute attribute)
        {
            AssemblyDefinition extensionAssemblyDefintion = attribute.AttributeType.Resolve().Module.Assembly;

            foreach (var assemblyAttribute in extensionAssemblyDefintion.CustomAttributes)
            {
                if (assemblyAttribute.AttributeType.FullName == ExtensionsInformationType)
                {
                    string extensionName = assemblyAttribute.ConstructorArguments[0].Value.ToString();
                    string extensionVersion = assemblyAttribute.ConstructorArguments[1].Value.ToString();

                    extensions[extensionName] = extensionVersion;

                    // Only 1 extension per library
                    return;
                }
            }
        }

        private static string GetBindingDirection(CustomAttribute attribute)
        {
            if (IsOutputBindingType(attribute))
            {
                return "Out";
            }

            return "In";
        }

        private static bool IsOutputBindingType(CustomAttribute attribute)
        {
            return TryGetBaseAttributeType(attribute, OutputBindingType, out _);
        }

        private static bool IsFunctionBindingType(CustomAttribute attribute)
        {
            return TryGetBaseAttributeType(attribute, BindingType, out _);
        }

        private static bool TryGetBaseAttributeType(CustomAttribute attribute, string baseType, out TypeReference? baseTypeRef)
        {
            baseTypeRef = attribute.AttributeType?.Resolve()?.BaseType;

            while (baseTypeRef != null)
            {
                if (baseTypeRef.FullName == baseType)
                {
                    return true;
                }

                baseTypeRef = baseTypeRef.Resolve().BaseType;
            }

            return false;
        }
    }
}
