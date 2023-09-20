﻿using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<Microsoft.Azure.Functions.Worker.Sdk.Analyzers.RegistrationInASPNetIntegration, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<Microsoft.Azure.Functions.Worker.Sdk.Analyzers.RegistrationInASPNetIntegration>;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace Sdk.Analyzers.Tests
{
    public class RegistrationAspNetIntegrationTests
    {
        private const string ExpectedRegistrationMethod = "ConfigureFunctionsWebApplication";

        [Fact]
        public async Task ASPNetIntegration_MissingRegistration_Diagnostics_Expected()
        {
            string testCode = @"
                using System.Linq;
                using System.Threading.Tasks;
                using Microsoft.Azure.Functions.Worker;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;
                using Microsoft.Extensions.Logging;

                namespace AspNetIntegration
                {
                    class Program
                    {
                        static void Main(string[] args)
                        {
                            var host = new HostBuilder()
                                .ConfigureFunctionsWorkerDefaults()
                                .Build();

                            host.Run();
                        }
                    }
                }";

            var test = new AnalyzerTest
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net60.WithPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.19.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Sdk", "1.14.1"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs", "6.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "1.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Abstractions", "5.0.0"),
                    new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "6.0.0")
                    )),

                TestCode = testCode
            };

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                            .WithArguments(ExpectedRegistrationMethod));

            await test.RunAsync();
        }

        [Fact]
        public async Task NotASPNetIntegration_Diagnostics_NotExpected()
        {
            string testCode = @"
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;

                namespace SampleApp
                {
                    public class Program
                    {
                        public static void Main()
                        {
                            var host = new HostBuilder()
                                .ConfigureFunctionsWorkerDefaults()
                                .Build();

                            host.Run();
                        }
                    }
                }";

            var test = new AnalyzerTest
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.18.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Sdk", "1.13.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs", "6.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Abstractions", "1.3.0"))),

                TestCode = testCode
            };

            // test.ExpectedDiagnostics is an empty collection.

            await test.RunAsync();
        }

        [Fact]
        public async Task ASPNetIntegration_WithRegistration_Diagnostics_NotExpected()
        {
            string testCode = @"
                using System.Linq;
                using System.Threading.Tasks;
                using Microsoft.Azure.Functions.Worker;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;
                using Microsoft.Extensions.Logging;

                namespace AspNetIntegration
                {
                    class Program
                    {
                        static void Main(string[] args)
                        {

                            //<docsnippet_aspnet_registration>
                            var host = new HostBuilder()
                                .ConfigureFunctionsWebApplication()
                                .Build();

                            host.Run();
                            //</docsnippet_aspnet_registration>
                        }
                    }
                }";

            var test = new AnalyzerTest
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net60.WithPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.19.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Sdk", "1.14.1"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs", "6.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "1.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Abstractions", "5.0.0"),
                    new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "6.0.0")
                    )),

                TestCode = testCode
            };

            // test.ExpectedDiagnostics is an empty collection.

            await test.RunAsync();
        }

        [Fact]
        public async Task ASPNetIntegration_WithMiddleWare_WithRegistration_Diagnostics_NotExpected()
        {
            string testCode = @"
                using System.Linq;
                using System.Threading.Tasks;
                using Microsoft.Azure.Functions.Worker;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;
                using Microsoft.Extensions.Logging;

                namespace AspNetIntegration
                {
                    class Program
                    {
                        static void Main(string[] args)
                        {
                            #if ENABLE_MIDDLEWARE
                                var host = new HostBuilder()
                                    .ConfigureFunctionsWebApplication(builder =>
                                    {
                                        // can still register middleware and use this extension method the same way
                                        // .ConfigureFunctionsWorkerDefaults() is used
                                        builder.UseWhen<RoutingMiddleware>((context)=>
                                        {
                                            // We want to use this middleware only for http trigger invocations.
                                            return context.FunctionDefinition.InputBindings.Values
                                                            .First(a => a.Type.EndsWith(""Trigger"")).Type == ""httpTrigger"";
                                        });
                                    })
                                    .Build();
                                host.Run();
                            #else
                                //<docsnippet_aspnet_registration>
                                var host = new HostBuilder()
                                    .ConfigureFunctionsWebApplication()
                                    .Build();

                                host.Run();
                                //</docsnippet_aspnet_registration>
                            #endif
                        }
                    }
                }";

            var test = new AnalyzerTest
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net60.WithPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.19.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Sdk", "1.14.1"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs", "6.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "1.0.0"),
                    new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.Abstractions", "5.0.0"),
                    new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "6.0.0")
                    )),

                TestCode = testCode
            };

            // test.ExpectedDiagnostics is an empty collection.

            await test.RunAsync();
        }
    }
}
