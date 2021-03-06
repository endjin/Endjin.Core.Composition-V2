namespace Endjin.Core.Installers
{
    #region Using directives

    using Endjin.Core.Container;
    using System;
    using System.Linq;
    using System.Reflection;

    #endregion Using directives

    /// <summary>
    /// Base class for installer which install services implementing a particular most-derived interface
    /// which provide a static property which determines the name against which they should be registered
    /// in the container
    /// </summary>
    /// <typeparam name="T">The most-derived type of the interface identifying the components to be installed</typeparam>
    /// <typeparam name="TMarker">A type within the assembly to be probed</typeparam>
    public abstract class NamedTypeInstallerBase<T, TMarker> : IInstaller
    {
        private readonly string nameProperty;

        protected NamedTypeInstallerBase(string nameProperty)
        {
            if (nameProperty == null)
            {
                throw new ArgumentNullException("nameProperty");
            }

            this.nameProperty = nameProperty;
        }

        public void Install(IContainer container)
        {
            container.Register(
                AllTypes.FromAssemblyContaining<TMarker>().BasedOn<T>().WithService(Interfaces.FirstInterface).
                    AsSingletonIf(
                        r => r.ComponentType.GetTypeInfo().GetCustomAttributes(typeof(SingletonAttribute), true).Any()).
                    AsTransientIf(
                        r => !r.ComponentType.GetTypeInfo().GetCustomAttributes(typeof(SingletonAttribute), true).Any())
                    .Named(c => this.GetName(c.ComponentType)));
        }

        private string GetName(Type type)
        {
            var property = type.GetRuntimeProperties().FirstOrDefault(p => p.Name == this.nameProperty);
            if (property != null)
            {
                return property.GetValue(null) as string;
            }
            var field = type.GetRuntimeFields().FirstOrDefault(p => p.Name == this.nameProperty);
            return field.GetValue(null) as string;
        }
    }
}