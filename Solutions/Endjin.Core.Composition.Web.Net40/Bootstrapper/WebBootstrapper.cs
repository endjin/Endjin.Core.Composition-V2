﻿namespace Endjin.Core.Container
{
    #region Using statements

    using Endjin.Core.Composition;
    using Endjin.Core.ResourceRegistration.Contracts;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Web;

    #endregion

    public class WebBootstrapper : IBootstrapper
    {

        public Task<IEnumerable<IInstaller>> GetInstallersAsync()
        {
            return InstallerHelpers.GetInstallersAsync(AssemblyHelpers.GetAssemblyList(HttpRuntime.BinDirectory));
        }

        public Task RegisterResourcesAsync()
        {
            return Task.Factory.StartNew(() =>
                {
                    var assemblies = AssemblyHelpers.GetAssemblyList(HttpRuntime.BinDirectory);
                    var registrars = ApplicationServiceLocator.Container.ResolveAll<IResourceRegistrar>();
                    var registry = ApplicationServiceLocator.Container.Resolve<IResourceRegistry>();
                    foreach (var assembly in assemblies)
                    {
                        foreach (var registrar in registrars)
                        {
                            registrar.RegisterResources(registry, assembly);
                        }
                    }
                });
        }
    }
}