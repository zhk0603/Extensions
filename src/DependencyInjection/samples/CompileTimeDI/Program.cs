using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Extensions.DependencyInjection;

namespace CompileTimeDI
{
    class MyClass
    {
        public MyClass(MyOtherClass c, IEnumerable<SomeOtherType> types)
        {

        }
    }
    class SomeOtherType
    {

    }
    class OtherType
    {

    }
    class Program
    {
        static void Main(string[] args)
        {
            ServiceCollection c = new ServiceCollection();
            c.AddSingleton<SomeOtherType>(new SomeOtherType());
            c.AddTransient<OtherType>(p=> new OtherType());
            c.AddTransient<MyClass>();
            c.AddSingleton<MyOtherClass>();
            var provider = c.EmitAndUse("5.dll");

            var a = provider.GetService<MyClass>();
        }
    }
}
