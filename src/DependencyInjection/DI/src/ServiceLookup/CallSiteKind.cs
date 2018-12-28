namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal enum CallSiteKind
    {
        Factory,

        Constructor,

        Constant,

        IEnumerable,

        ServiceProvider,

        Scope,

        Transient,

        ServiceScopeFactory,

        Singleton,

        ServiceActivatorFactory
    }
}
