// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.DotNet.Analyzers.Async.Tests
{
    public class AsyncAnalyzerTests
    {
        private AsyncDiagnosticRunner Runner = new AsyncDiagnosticRunner(new AsyncMethodAnalyzer());

        [Theory]
        [InlineData("task.GetAwaiter().GetResult()")]
        [InlineData("task.Wait()")]
        [InlineData("var r = taskT.Result")]
        public void MEL0001IsProducedInAsyncMethods(string code)
        {
            var diagnostic = Assert.Single(GetDiagnostics(FormatAsyncMethod(code)));
            Assert.Equal("ASYNC0001", diagnostic.Id);
        }

        [Theory]
        [InlineData("task.GetAwaiter().GetResult()")]
        [InlineData("task.Wait()")]
        [InlineData("var r = taskT.Result")]
        public void MEL0001IsNotProducedInNonAsyncMethods(string code)
        {
            Assert.Empty(GetDiagnostics(FormatMethod(code)));
        }

        private Diagnostic[] GetDiagnostics(string code, params string[] additionalEnabledDiagnostics)
        {
            return Runner.GetDiagnosticsAsync(code, additionalEnabledDiagnostics).Result;
        }

        private static string FormatMethod(string statement)
        {
            return $@"
using System.Threading.Tasks;
public class Program
{{
    static Task task;
    static Task<int> taskT;
    public static Task Main()
    {{
        {statement};
        return task;
    }}
}}
";
        }

        private static string FormatAsyncMethod(string statement)
        {
            return $@"
using System.Threading.Tasks;
public class Program
{{
    static Task task;
    static Task<int> taskT;
    public static async Task Main()
    {{
        {statement};
    }}
}}
";
        }
    }
}
