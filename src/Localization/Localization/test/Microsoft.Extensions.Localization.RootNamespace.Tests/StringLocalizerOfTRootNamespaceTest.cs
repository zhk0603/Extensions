// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using LocalizationTest.Abc.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Localization.RootNamespace.Tests
{
    public class StringLocalizerOfTRootNamespaceTest
    {
        [Fact]
        public void RootNamespace_WithFolder()
        {
            var locOptions = new LocalizationOptions
            {
                ResourcesPath = "Resources"
            };
            var options = new Mock<IOptions<LocalizationOptions>>();
            options.Setup(o => o.Value).Returns(locOptions);
            var factory = new ResourceManagerStringLocalizerFactory(options.Object, NullLoggerFactory.Instance);

            var valuesLoc = factory.Create(typeof(ValuesController));
            Assert.Equal("ValFromResource", valuesLoc["String1"]);
        }

        [Fact]
        public void RootNamespace_WithoutFolder()
        {
            var locOptions = new LocalizationOptions
            {
                ResourcesPath = null
            };
            var options = new Mock<IOptions<LocalizationOptions>>();
            options.Setup(o => o.Value).Returns(locOptions);

            var factory = new ResourceManagerStringLocalizerFactory(options.Object, NullLoggerFactory.Instance);

            var valuesLoc = factory.Create(typeof(ValuesController));
            Assert.Equal("ValFromResourceWithoutFolder", valuesLoc["String1"]);
        }

        [Fact]
        public void OutsideRootNamespace_WithFolder()
        {
            var locOptions = new LocalizationOptions
            {
                ResourcesPath = "Resources"
            };
            var options = new Mock<IOptions<LocalizationOptions>>();
            options.Setup(o => o.Value).Returns(locOptions);
            var factory = new ResourceManagerStringLocalizerFactory(options.Object, NullLoggerFactory.Instance);

            var valuesLoc = factory.Create(typeof(OutsideRootNamespace.Controllers.OutsideRootControllers));
            Assert.Equal("ValFromOutsideRootnamespaceWithFolder", valuesLoc["String1"]);
        }

        [Fact]
        public void OutsideRootNamespace_WithoutFolder()
        {
            var locOptions = new LocalizationOptions
            {
                ResourcesPath = null
            };
            var options = new Mock<IOptions<LocalizationOptions>>();
            options.Setup(o => o.Value).Returns(locOptions);
            var factory = new ResourceManagerStringLocalizerFactory(options.Object, NullLoggerFactory.Instance);

            var valuesLoc = (ResourceManagerStringLocalizer)factory.Create(typeof(OutsideRootNamespace.Controllers.OutsideRootControllers));

            Assert.Equal("ValFromOutsideRootnamespaceWithoutFolder", valuesLoc["String1"]);
        }
    }
}
