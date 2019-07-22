using System;
using Microsoft.Extensions.DependencyInjection;

namespace CompileTimeDI
{
    class MyClass
    {

    }
    class Program
    {
        static void Main(string[] args)
        {
            ServiceCollection c = new ServiceCollection();
            c.AddTransient<MyClass>();

            c.Emit("5.dll");
        }
    }
}
